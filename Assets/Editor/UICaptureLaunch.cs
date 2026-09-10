// =============================================================================
// UICaptureLaunch -- editor launch hooks for the UICaptureMode UI screenshot harness.
// -----------------------------------------------------------------------------
// Two entry points live here:
//
//   RunCapture()          -- LEGACY Play-mode drive. Sets the UICaptureMode
//                            SessionState flag and enters Play mode; the runtime
//                            MonoBehaviour then boots a scene, opens each router
//                            panel, and ScreenCaptures. This ONLY works with a live
//                            interactive editor / a graphics player -- in
//                            `-batchmode -quit -executeMethod` the method returns
//                            immediately and Unity quits BEFORE Play ever ticks, so
//                            it produces ZERO pngs. Kept for the menu item only.
//
//   RunCaptureHeadless()  -- NEW, RELIABLE headless path (owner directive:
//                            catch visual bugs BEFORE builds ship). A fully
//                            SYNCHRONOUS edit-mode render: it builds the real
//                            code-built uGUI panel (starting with the founding /
//                            Echo unlock card that carries the just-fixed
//                            text/button overlap), switches its canvas to a
//                            camera, renders to a RenderTexture, and writes a PNG --
//                            all inside the one executeMethod call, so `-quit`
//                            takes effect only AFTER the pngs are on disk. No Play
//                            mode, no domain-reload race, no owner playtest.
//
// INVOKE (headless -- the wrapper passes -batchmode -quit and NO -nographics, so a
// real GPU renders real pixels):
//   powershell -File .\run-unity-method.ps1 `
//     -Method DeNelle.Editor.UICaptureLaunch.RunCaptureHeadless -LogName ui-capture.log
//
// OUTPUT: Builds/ui-capture/<PanelName>_<w>x<h>.png  +  three DISTINCT marker lines
// the caller greps:
//     UI_CAPTURE_OK <count>                 -- non-blank frames written
//     UI_CAPTURE_FIDELITY_OK <n> builds     -- every panel was BUILT at the size it
//                                              was shot at (or _DEGRADED, an error)
//     UI_GEOMETRY_OK <n> canvases           -- numeric layout assertions passed
//                                              (or UI_GEOMETRY_FAIL x<n>, an error)
//     UI_GLYPH_OK <clean>/<checked> panels  -- WO-1630: every measurable label drew every
//                                              printable character it was given (or
//                                              UI_GLYPH_FAIL x<n>, an error). Its own marker,
//                                              not folded into the geometry one: that marker
//                                              also covers text-off-plate, so a reader could
//                                              not tell from it whether the glyph class
//                                              specifically was clean. Carries `labels=<n>`,
//                                              `baselined=<n>` and `unproved=<n>` on BOTH paths:
//                                              how many labels were measured, how many findings
//                                              are the WO-1636 known debt, and how many stood
//                                              down because no font resolved. `labels=0` over any
//                                              number of panels is a FAIL, because zero findings
//                                              from zero measurements reads exactly like
//                                              everything fitting. Any finding that is NOT a
//                                              baseline entry reds -- including a new one on a
//                                              panel that already carries entries.
//     UI_ENDSTATE_FIT_OK <n> banners        -- WO-952: no `body rows COMPRESSED to fit`
//                                              fired AND the RESOLVED band stack measures
//                                              >= 0.995 of the content's own demand
//                                              (or UI_ENDSTATE_FIT_FAIL x<n>, an error).
//                                              Carries the numbers on BOTH paths.
//     UI_CAPTURE_HEAD <sha> <branch> dirty=<bool>
//                                           -- WO-1080 PROVENANCE. Printed FIRST, before a
//                                              single pixel is shot, so even a run that
//                                              throws still says which tree it measured.
//                                              `dirty=true` means tracked files under
//                                              Assets/ were uncommitted: there is no commit
//                                              for a later reader to diff against, so that
//                                              log MAY NOT BE CITED by a layout ticket.
//                                              (or UI_CAPTURE_PROVENANCE_FAIL, an error --
//                                              deliberately NOT a UI_CAPTURE_HEAD* suffix,
//                                              so a grep for the good marker cannot match it.)
//     UI_CAPTURE_STAMP head=.. targets=.. pngs=.. touchFindings=..
//                                           -- WO-1080. The run's OWN totals on the SAME line
//                                              as the sha, so a ticket quoting
//                                              "from UI_TOUCH_FAIL x43" can be checked against
//                                              the log it claims to come from. Printed LAST,
//                                              after every Report* has counted.
//
// =============================================================================
//  WO-1080 -- WHY A CAPTURE STAMPS THE COMMIT AND NOT JUST A DATE. READ ONCE.
// -----------------------------------------------------------------------------
//  Four layout tickets (WO-1075/1076/1077/1078) were minted from ONE aged log,
//  `Builds/wo1060-capture.log`, describing a tree that had moved on. WO-1076 was
//  reopened against a panel fixed three days earlier and cost a seat a morning.
//
//  ⛔ A CAPTURE LOG'S FILE DATE IS NOT EVIDENCE OF THE TREE IT MEASURED. That log's
//  mtime is 2026-08-23; the fix it fails to contain landed 2026-08-21. It is NEWER
//  than the commit it does not have -- so an mtime comparison is defeated by the very
//  case that motivated it. Only the COMMIT identifies the tree.
//
//  The resolver lives in `Assets/Editor/Regression/CaptureProvenanceRegression.cs`
//  (assembly DeNelle.EditorRegression) and NOT beside this file, because the assembly
//  reference runs DeNelle.Editor -> DeNelle.EditorRegression one way only; putting it
//  there is what lets an oracle prove it still resolves. See that file's banner.
//
// =============================================================================
//  THE FIDELITY HOLE THIS FILE USED TO HAVE (fixed 2026-08-05) -- READ THIS
// -----------------------------------------------------------------------------
//  Until today every panel was BUILT ONCE and then RE-SCALED per shot: the
//  capture flipped the canvas to ScreenSpaceCamera and called ApplyScreenSpaceScale,
//  which rewrites ONLY `canvas.scaleFactor`. It never resized the root canvas rect
//  and never moved `Screen.width`/`Screen.height`.
//
//  But panels compute their ZONE GEOMETRY AT BUILD TIME from
//  `ElarionUiKit.PostScaleCanvasHeight`, which reads `Screen.*` (and falls back to a
//  hard-coded 1920 when Screen is unusable). So EVERY png a run wrote shared ONE
//  geometry -- the editor process's -- and the "1920x1080" / "2340x1080" in the
//  filenames were LABELS, NOT LAYOUTS. Font point size reproduced; zone geometry did
//  not. That is precisely how the founding Echo card passed green at two "sizes" all
//  night while, on the device, its caption rendered entirely off the black plate.
//
//  THE FIX, two halves:
//    (1) BUILD PER TARGET SIZE. Every capture is now wrapped in ForEachTarget, which
//        runs the WHOLE build->shoot->teardown cycle once per target. Nothing is
//        built once and re-labelled.
//    (2) MOVE THE SURFACE BEFORE THE BUILD. CaptureSurfaceScope sets the resolution
//        the kit resolves geometry against, so PostScaleCanvasHeight returns the TARGET
//        value while the panel is being constructed. The scope VERIFIES the move by
//        reading the surface back; if it did not take, it does NOT pretend -- it logs
//        loudly and the run reports UI_CAPTURE_FIDELITY_DEGRADED.
//
//  WHY IT IS NOT THE GAME VIEW ANY MORE (2026-08-05 evening, from CAPTURED DATA).
//  The first cut of (2) drove the editor's main game view (the GameViewSizes /
//  SizeSelectionCallback reflection recipe) and hoped Screen.* would follow. It does
//  not, and the run proved it: Builds/ui-capture-rail.log:475 records
//  "batchmode=True, graphicsDevice=Direct3D11 ... screen=640x480" -- a REAL D3D11
//  device, so this is not the -nographics case -- and :489 records the reflection
//  SUCCEEDING ("game view accepted the size but Screen still reads 640x480").
//  UI_CAPTURE_FIDELITY_DEGRADED 38/38 was the whole run. Root cause: `-batchmode`
//  builds no editor window layout, so there is no GameView GUIView for Screen.* to
//  mirror and it stays on the 640x480 offscreen default no matter what the size list
//  says. Dropping -nographics cannot help -- that run already had a graphics device.
//  So the harness stopped depending on editor internals for the load-bearing move and
//  drives ElarionUiKit's injectable surface instead (default = Screen.*, override =
//  editor-only). The game-view move is still ATTEMPTED, because in an interactive
//  editor it really does move Screen.* and that also covers any panel still reading
//  Screen directly -- but nothing depends on it.
//
//  And because eyes-only review is not a defence, AuditGeometry now asserts the
//  layout NUMERICALLY on every captured canvas (see its banner).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;   // lore-fragments.json parse (WO-795 lore capture)
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;
using DeNelle.Core.UI;    // PanelManager / PanelHandle (lore-modal arbiter teardown)
using DeNelle.Core.Diagnostics;   // FlowTrace / ITraceSink (WO-952 EndState fit tap)
using DeNelle.Core.State; // deterministic Manage destination capture fixture
using DeNelle.Core.Jobs;  // ObsidianQueueState for deterministic Manage capture
using DeNelle.Core.Catalog; // authoritative structure fixture for build-preview evidence
using DeNelle.Core.Economy; // harvest-overflow worst-case capture fixture
using DeNelle.Core.Quests;   // QuestDef/QuestStage/QuestReward (rumor-board worst-case fixture)
using DeNelle.Dungeons;   // LoreReadingModal, LoreReadRequest, LoreFragmentSet (WO-795)
using DeNelle.Village;    // EchoUnlockDialogue, EchoRosterCatalog, EchoRosterEntry, Tower, BuildMenu
using DeNelle.Village.Hero; // RumorBoardPanel, RumorBoardVM, IRumorBoardBackend (WO-810 board capture)
using DeNelle.Village.UI; // TowerManagerPanel, PlacedTowerListVM (WO-795)
using DeNelle.Village.Talents; // HeroSkillTreePanelMvvm (tree + hot-swap rail capture)
using DeNelle.Village.Crafting; // disclosure/confirmation modal capture
using DeNelle.Village.Buildings.Progression; // PlacedUpgradeKey - the ONE composer of a placed-structure job key (WO-1422)
using DeNelle.Village.Monetization; // Daily Chest production modal capture
using DeNelle.Village.Feedback; // HonestFeedbackPanel proof capture

namespace DeNelle.Editor
{
    /// <summary>Editor entries for the UI-capture harness (legacy Play-mode drive +
    /// the reliable synchronous edit-mode render).</summary>
    public static class UICaptureLaunch
    {
        private const string OutDir = "Builds/ui-capture/";

        // ---------------------------------------------------------------------
        //  BLANK GUARD (2026-08-04). UI_CAPTURE_OK <n> is a PRE-SHIP GATE, so the
        //  number has to mean real pixels. It did not: every render was written and
        //  counted regardless of what was in it, so a run with no graphics device
        //  produced a full green count over flat black frames.
        //
        //  That is not hypothetical -- it is exactly how the owner's UI_REVIEW went
        //  empty. The AutoPilot fleet's own capture writer carries the same
        //  soft-fail ("a -nographics fleet writes a blank frame, never an error"),
        //  and a default-mode fleet run at 21:21 on 2026-08-04 overwrote 35 real
        //  review shots with 33150-byte black PNGs. A blank that looks like a
        //  capture is worse than an absent one: MISSING gets chased, blank gets
        //  reviewed. So every frame is measured BEFORE it is written; a flat one is
        //  refused, logged as an error, and counted as a FAILURE.
        // ---------------------------------------------------------------------
        private const int BlankSampleStride = 13;       // ~190k samples at 2340x1080
        private const int BlankMinDistinctBuckets = 3;  // 4-bit-per-channel quantisation
        private const float BlankMinInkFraction = 0.002f;

        // ---------------------------------------------------------------------
        //  CAPTURE TARGETS (2026-08-05).
        //
        //  2670x1200 is the Solana SEEKER'S REAL SURFACE. It had NEVER been shot by
        //  anything in this repo. The 2340x1080 entry below was only ever a harness
        //  size -- tools' run-autopilot-fleet.ps1 still (wrongly) describes it as the
        //  Seeker's exact screen; fix it there too. 1920x1080 is kept as the
        //  desktop/reference landscape.
        //
        //  Each target drives a FULL build->shoot->teardown cycle (ForEachTarget), so
        //  the geometry in the png is the geometry that resolution really produces.
        // ---------------------------------------------------------------------
        private struct CaptureTarget
        {
            public readonly int W;
            public readonly int H;
            public readonly string Tag;
            public CaptureTarget(int w, int h) { W = w; H = h; Tag = w + "x" + h; }
        }

        private static readonly CaptureTarget[] LandscapeTargets =
        {
            new CaptureTarget(1920, 1080),   // desktop / reference landscape
            new CaptureTarget(2340, 1080),   // common tall-phone landscape (NOT the Seeker)
            new CaptureTarget(2670, 1200),   // THE SEEKER'S REAL SURFACE
        };

        // The Night Market redesign is reviewed at the supplied composition sizes as well as the
        // real Seeker surface. Each size still receives a fresh build; these are not rescaled shots.
        private static readonly CaptureTarget[] NightMarketTargets =
        {
            new CaptureTarget(800, 360),
            new CaptureTarget(915, 412),
            new CaptureTarget(1280, 720),
            new CaptureTarget(2670, 1200),
        };

        // Fidelity bookkeeping (reported as UI_CAPTURE_FIDELITY_OK / _DEGRADED).
        private static int _fidelityOk;
        private static int _fidelityDegraded;
        private static readonly HashSet<string> _fidelityReasons = new HashSet<string>(StringComparer.Ordinal);

        // Screen.* itself NEVER moves in batchmode (proven -- see the file banner). Tracked
        // separately from fidelity so the report can name that residual honestly instead of
        // letting a reader assume every Screen.* reader in the tree followed the target.
        private static int _screenStuckBuilds;
        private static string _screenStuckAt;

        // The divergence proof: two different ASPECTS must resolve DIFFERENT zone rects, or the
        // run is back to one geometry wearing several filenames. Non-null == proven.
        private static string _geoMoveProof;
        private static string _geoMoveFailure;

        /// <summary>
        /// Run one capture body ONCE PER TARGET SIZE, with the editor's game view driven to
        /// that size FIRST so the panel is BUILT against it (not built once and re-labelled).
        /// The body owns its own build + teardown, exactly as it did before.
        /// </summary>
        private static int ForEachTarget(string panelName, Func<CaptureTarget, int> buildAndShoot)
        {
            return ForEachTarget(panelName, LandscapeTargets, buildAndShoot);
        }

        private static int ForEachTarget(string panelName, CaptureTarget[] targets,
                                         Func<CaptureTarget, int> buildAndShoot)
        {
            int total = 0;
            if (targets == null || buildAndShoot == null) return 0;
            foreach (var target in targets)
            {
                using (new CaptureSurfaceScope(target, panelName))
                {
                    try
                    {
                        total += buildAndShoot(target);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[UICap-HL] " + panelName + " @" + target.Tag + " threw: " + e);
                    }
                }
            }
            return total;
        }

        // ---------------------------------------------------------------------
        //  CaptureSurfaceScope -- move the geometry the panel BUILDS against.
        //
        //  Panels resolve their zones through ElarionUiKit.PostScaleCanvasHeight on the
        //  frame they are constructed. Nothing this harness does to the canvas AFTER the
        //  build can change those zones, so the only honest fix is to move the value the
        //  build READS.
        //
        //  (1) THE LOAD-BEARING MOVE: ElarionUiKit.SetSurfaceOverride. The kit's surface
        //      defaults to Screen.* (shipped behaviour, unchanged) and is overridable
        //      ONLY in the editor. No UnityEditor internals, so a Unity upgrade cannot
        //      silently take this away -- which is the whole reason it is not the game
        //      view any more (see the file banner for the captured proof that the game
        //      view CANNOT move Screen.* in batchmode).
        //
        //  (2) BEST EFFORT: still drive the editor's main game view too. In an
        //      INTERACTIVE editor that genuinely moves Screen.*, which additionally
        //      covers any panel that still reads Screen directly. In batchmode it is a
        //      no-op and nothing here depends on it.
        //
        //  VERIFIED, not assumed: the ctor reads the effective surface back. A scope
        //  that could not move it does NOT pretend -- it records a reason and the run
        //  ends on UI_CAPTURE_FIDELITY_DEGRADED, i.e. the harness declares itself
        //  scale-only rather than shipping a green run over one geometry wearing three
        //  filenames. Screen.* is read back too and reported separately, so nobody
        //  mistakes "the kit surface moved" for "the process's screen moved".
        // ---------------------------------------------------------------------
        private sealed class CaptureSurfaceScope : IDisposable
        {
            private static bool _probed;
            private static Type _sizesType, _groupType, _sizeType, _sizeTypeEnum, _gameViewType;
            private static object _sizes;              // GameViewSizes.instance
            private static object _group;              // its current GameViewSizeGroup
            private static EditorWindow _gameView;
            private static string _probeFailure;

            private int _prevIndex;
            private bool _restore;

            public CaptureSurfaceScope(CaptureTarget target, string panelName)
            {
                _prevIndex = -1;
                _restore = false;

                // (1) The load-bearing move: what PostScaleCanvasHeight reads at BUILD time.
                ElarionUiKit.SetSurfaceOverride(target.W, target.H);

                // (2) Best effort: the editor's own game view (moves Screen.* interactively only).
                try
                {
                    Probe();
                    if (_probeFailure == null)
                    {
                        _prevIndex = GetSelectedIndex();
                        if (Select(target.W, target.H)) _restore = _prevIndex >= 0;
                    }
                }
                catch (Exception e)
                {
                    _probeFailure = _probeFailure ?? (e.GetType().Name + ": " + e.Message);
                }

                // VERIFY -- never trust a set, read it back. Screen first, for the record:
                // in batchmode it never moves, and the run must say so rather than let a
                // reader assume every Screen.* reader in the tree followed the target.
                int sw = Screen.width, sh = Screen.height;
                bool screenMoved = sw == target.W && sh == target.H;
                if (!screenMoved)
                {
                    _screenStuckAt = sw + "x" + sh;
                    _screenStuckBuilds++;
                }

                // The EFFECTIVE surface -- the value the kit will actually resolve zones from.
                int ew = ElarionUiKit.SurfaceWidth, eh = ElarionUiKit.SurfaceHeight;
                if (ew == target.W && eh == target.H)
                {
                    _fidelityOk++;
                    return;
                }

                _fidelityDegraded++;
                string why = "ElarionUiKit surface would not move: asked for " + target.Tag +
                             ", it reports " + ew + "x" + eh +
                             (_probeFailure != null ? " (game view path also failed: " + _probeFailure + ")" : "");
                if (_fidelityReasons.Add(why))
                {
                    Debug.LogError("[UICap-HL] GEOMETRY FIDELITY LOST for " + target.Tag +
                                   " (panel " + panelName + "): " + why +
                                   ". ElarionUiKit.PostScaleCanvasHeight will resolve this panel's zones " +
                                   "against THAT geometry, not " + target.Tag + ". The png is then " +
                                   "SCALE-ACCURATE ONLY -- font size reproduces, zone geometry does NOT. " +
                                   "Treat its filename as a label, not a layout.");
                }
            }

            public void Dispose()
            {
                // FIRST and unconditional: a leaked override would mis-size every panel built
                // later in this editor session (including the owner's own play-mode UI).
                ElarionUiKit.ClearSurfaceOverride();
                try
                {
                    if (_restore && _prevIndex >= 0) SelectIndex(_prevIndex);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[UICap-HL] game view size restore failed (harmless): " + e.Message);
                }
            }

            private static void Probe()
            {
                if (_probed) return;
                _probed = true;
                try
                {
                    var edAsm = typeof(EditorWindow).Assembly;
                    _sizesType = edAsm.GetType("UnityEditor.GameViewSizes");
                    _sizeType = edAsm.GetType("UnityEditor.GameViewSize");
                    _sizeTypeEnum = edAsm.GetType("UnityEditor.GameViewSizeType");
                    _gameViewType = edAsm.GetType("UnityEditor.GameView");
                    if (_sizesType == null || _sizeType == null || _sizeTypeEnum == null || _gameViewType == null)
                    {
                        _probeFailure = "UnityEditor internals not found (GameViewSizes/GameViewSize/" +
                                        "GameViewSizeType/GameView) -- this Unity version moved them";
                        return;
                    }

                    var singleton = typeof(ScriptableSingleton<>).MakeGenericType(_sizesType);
                    var instProp = singleton.GetProperty("instance",
                        BindingFlags.Public | BindingFlags.Static);
                    _sizes = instProp != null ? instProp.GetValue(null, null) : null;
                    if (_sizes == null) { _probeFailure = "GameViewSizes.instance was null"; return; }

                    var groupProp = _sizesType.GetProperty("currentGroup",
                        BindingFlags.Public | BindingFlags.Instance);
                    _group = groupProp != null ? groupProp.GetValue(_sizes, null) : null;
                    if (_group == null) { _probeFailure = "GameViewSizes.currentGroup was null"; return; }
                    _groupType = _group.GetType();

                    // An EXISTING game view is preferred; GetWindow can be hostile in batchmode.
                    var open = Resources.FindObjectsOfTypeAll(_gameViewType);
                    if (open != null && open.Length > 0) _gameView = open[0] as EditorWindow;
                    if (_gameView == null)
                    {
                        try { _gameView = EditorWindow.GetWindow(_gameViewType, false, "Game", false); }
                        catch (Exception e) { _probeFailure = "no GameView and GetWindow threw (" + e.GetType().Name + ": " + e.Message + ")"; return; }
                    }
                    if (_gameView == null) { _probeFailure = "no GameView window exists and one could not be created"; return; }
                }
                catch (Exception e)
                {
                    _probeFailure = e.GetType().Name + ": " + e.Message;
                }
            }

            /// <summary>Index of the group's FixedResolution entry for w x h, adding it if absent.</summary>
            private static int IndexOf(int w, int h)
            {
                var totalM = _groupType.GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance);
                var getM = _groupType.GetMethod("GetGameViewSize",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                if (totalM == null || getM == null) return -1;

                var wProp = _sizeType.GetProperty("width", BindingFlags.Public | BindingFlags.Instance);
                var hProp = _sizeType.GetProperty("height", BindingFlags.Public | BindingFlags.Instance);
                if (wProp == null || hProp == null) return -1;

                int total = (int)totalM.Invoke(_group, null);
                for (int i = 0; i < total; i++)
                {
                    object s = getM.Invoke(_group, new object[] { i });
                    if (s == null) continue;
                    if ((int)wProp.GetValue(s, null) == w && (int)hProp.GetValue(s, null) == h) return i;
                }

                // Not present -> author it (FixedResolution, so the view is exactly w x h).
                var ctor = _sizeType.GetConstructor(new[] { _sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                var addM = _groupType.GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance);
                if (ctor == null || addM == null) return -1;
                object created = ctor.Invoke(new object[]
                {
                    Enum.Parse(_sizeTypeEnum, "FixedResolution"), w, h, "UICap " + w + "x" + h,
                });
                addM.Invoke(_group, new[] { created });

                total = (int)totalM.Invoke(_group, null);
                for (int i = 0; i < total; i++)
                {
                    object s = getM.Invoke(_group, new object[] { i });
                    if (s == null) continue;
                    if ((int)wProp.GetValue(s, null) == w && (int)hProp.GetValue(s, null) == h) return i;
                }
                return -1;
            }

            private static int GetSelectedIndex()
            {
                var p = _gameViewType.GetProperty("selectedSizeIndex",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p == null || _gameView == null) return -1;
                try { return (int)p.GetValue(_gameView, null); }
                catch { return -1; }
            }

            private static bool Select(int w, int h)
            {
                int idx = IndexOf(w, h);
                if (idx < 0)
                {
                    _probeFailure = _probeFailure ?? ("could not find or author a " + w + "x" + h +
                                                      " FixedResolution game view size");
                    return false;
                }
                return SelectIndex(idx);
            }

            private static bool SelectIndex(int idx)
            {
                if (_gameView == null) return false;
                var m = _gameViewType.GetMethod("SizeSelectionCallback",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null)
                {
                    _probeFailure = _probeFailure ?? "GameView.SizeSelectionCallback not found";
                    return false;
                }
                m.Invoke(_gameView, new object[] { idx, null });
                _gameView.Repaint();
                // The size is applied on the view's next layout; force it now so the
                // Screen.* read in the ctor is the POST-change value, not the stale one.
                try { InternalEditorUtility_RepaintAllViews(); } catch { }
                return true;
            }

            private static void InternalEditorUtility_RepaintAllViews()
            {
                var t = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.InternalEditorUtility");
                var m = t != null ? t.GetMethod("RepaintAllViews", BindingFlags.Public | BindingFlags.Static) : null;
                if (m != null) m.Invoke(null, null);
            }
        }

        // ---------------------------------------------------------------------
        //  LEGACY -- Play-mode drive (menu item only; not headless-reliable).
        // ---------------------------------------------------------------------
        [MenuItem("Defenders/UI/Capture UI Panels")]
        public static void RunCapture()
        {
            // Same key UICaptureMode reads at boot (kept as a public const there).
            SessionState.SetBool(DeNelle.Diagnostics.UICaptureMode.EditorRequestKey, true);
            Debug.Log("[UICap] capture requested -> entering Play mode (graphics run = real pixels; " +
                      "-nographics = blank frames + drive log). NOTE: does NOT work in -batchmode -quit; " +
                      "use RunCaptureHeadless for headless.");
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        // ---------------------------------------------------------------------
        //  NEW -- reliable synchronous edit-mode render (headless).
        // ---------------------------------------------------------------------
        /// <summary>
        /// Headless, batchmode-safe UI capture. Renders each supported code-built panel
        /// to a PNG under <c>Builds/ui-capture/</c> WITHOUT entering Play mode. Prints
        /// every saved path and a final <c>UI_CAPTURE_OK &lt;count&gt;</c> marker.
        /// Callable via <c>-executeMethod DeNelle.Editor.UICaptureLaunch.RunCaptureHeadless</c>.
        /// </summary>
        public static void RunCaptureHeadless()
        {
            int count = 0;
            _fidelityOk = 0;
            _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _screenStuckBuilds = 0;
            _screenStuckAt = null;
            _geoMoveProof = null;
            _geoMoveFailure = null;
            _geoFailures.Clear();
            _geoCanvasesChecked = 0;
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _touchPanelsChecked = 0;
            _touchPanelsClean = 0;
            _endStateFits.Clear();
            _endStateFitFailures.Clear();

            // WO-1080: stamp the tree BEFORE anything is shot. Deliberately outside the try
            // below -- a run that throws mid-capture still leaves a log that says which
            // commit it was measuring, which is the one fact a later reader cannot recover.
            var head = ReportCaptureProvenance();

            try
            {
                Directory.CreateDirectory(OutDir);
                Debug.Log("[UICap-HL] headless UI capture start (batchmode=" + Application.isBatchMode +
                          ", graphicsDevice=" + SystemInfo.graphicsDeviceType +
                          ", screen=" + Screen.width + "x" + Screen.height +
                          ", out=" + Path.GetFullPath(OutDir) + ")");

                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    Debug.LogWarning("[UICap-HL] NO graphics device (looks like -nographics) -- pngs will be " +
                                     "BLANK. Re-run WITHOUT -nographics for real pixels.");
                }

                // BEFORE any panel is shot: prove the surface move actually changes resolved
                // geometry. If it does not, every png below shares one layout and the run is
                // worthless -- so this decides the fidelity marker (see ReportFidelity).
                ProveGeometryMoves();

                count += CaptureFoundingEchoCard();
                count += CapturePauseMenu();
                count += CaptureEchoRoster();
                count += CaptureEchoCard();          // WO-852: the resource-picker layout
                count += CaptureHelpMenu();
                count += CaptureDailyQuestHud();
                count += CaptureLoreReadingModal();
                count += CaptureTowerManagerPanel();
                count += CaptureBuildMenuUpgradeTower();
                count += CaptureRumorBoard();
                // Realm Map is deliberately retired from every public navigation surface.
                // Keep its dedicated forensic helper below for development, but do not emit
                // it as current public-reskin evidence (same policy as the retired Raids face).
                count += CaptureNightMarketStore();  // UI-001 / WO-1060: THE MONEY SCREEN. Was not
                                                     // in this list, so the geometry oracle was
                                                     // structurally blind to the one screen that
                                                     // takes money -- see CaptureNightMarketStore.
                count += CaptureHeroSkillTree();
                // The standalone CoC queue rail was retired from the HUD; Manage owns the
                // reachable queue presentation and has separate live-state captures. Keep the
                // forensic helper, but do not count obsolete chrome as public reskin evidence.
                count += CaptureBuildGhostChips();   // WO-1010 P1: chips on the ghost
                count += CapturePaletteCollapsed();  // WO-1010 P2: dock open + collapsed w/ restore tab
                count += CaptureEndStateWaveClear(); // WO-952: the wave-clear banner's fit, MEASURED
                count += CaptureDialogueOptions(); // WO-1030 DialogueView options state (2-opt + 4-opt worst case); WO-1031 re-pointed off the deleted pet_engage builder

                // RAID PILLAR (2026-08-16). Sixteen cases covered everything EXCEPT the one
                // pillar the owner asked to verify from screenshots. All three are defensive:
                // a raid screen that refuses to open logs and returns 0, it never throws the
                // run away.
                count += CaptureRaidSelection();     // the grid that was hard-refusing to open
                count += CaptureRaidDeploy();        // the pre-raid deploy screen (never shot before)
                // WO-1286: the conditional Raids bar face is retired; Raids is a stable Journey
                // card. Keep the legacy helper below for forensic comparison, but never emit it
                // as current UI evidence.
                count += CaptureMaintenanceBanner(); // WO-1243: the operator seal, as the player reads it
                count += CaptureHeroSelect();        // WO-1248: carousel rotate control, words fully readable
                count += CapturePlayerDecks();       // WO-1286: Realm/Hero/Journey mobile card workspaces
                count += CaptureManageWorkspace();   // WO-1286: Manage category launcher
                count += CaptureBuildCollections();  // WO-1286: Build category card launcher

                Debug.Log("[UICap-HL] done -> " + Path.GetFullPath(OutDir));
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] capture run threw: " + e);
            }

            // Three DISTINCT markers (CLAUDE.md §8: one marker per entry point, never a
            // shared string -- that is how a 22-case pass once read as a full-suite pass).
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();   // WO-1060: UI_TOUCH_OK <clean>/<checked> panels
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            ReportEndStateFit();

            // The marker a headless caller greps to confirm the run produced pixels.
            Debug.Log("UI_CAPTURE_OK " + count);

            // WO-1080: LAST, because it carries every Report*'s totals. This is the line a
            // reviewer diffs a ticket's quoted baseline against.
            ReportCaptureStamp(head, count);
        }

        /// <summary>Focused visual-review entry point for the landscape Night Market work order.</summary>
        public static void RunNightMarketCapture()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = 0;
            _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _geoCanvasesChecked = 0;
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _touchPanelsChecked = 0;
            _touchPanelsClean = 0;
            ProveGeometryMoves();
            int count = CaptureNightMarketStore();
            Debug.Log("NIGHT_MARKET_CAPTURE_OK " + count + "/" + NightMarketTargets.Length + " frames");
        }

        /// <summary>WO-1286 scoped visual gate: only the five migrated navigation workspaces.</summary>
        public static void RunNavigationCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = 0;
            _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _screenStuckBuilds = 0;
            _screenStuckAt = null;
            _geoMoveProof = null;
            _geoMoveFailure = null;
            _geoFailures.Clear();
            _geoCanvasesChecked = 0;
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _touchPanelsChecked = 0;
            _touchPanelsClean = 0;

            ProveGeometryMoves();
            int count = CapturePlayerDecks() + CaptureManageWorkspace() + CaptureBuildCollections();
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.

            const int expected = 15; // five workspaces x three landscape device targets
            if (count == expected && _fidelityDegraded == 0 && _geoFailures.Count == 0 &&
                _touchFailures.Count == 0 && _touchPanelsChecked == expected)
                Debug.Log("NAVIGATION_CAPTURE_OK " + count + "/" + expected +
                          " frames; geometry=clean; touch=clean");
            else
                Debug.LogError("NAVIGATION_CAPTURE_FAIL frames=" + count + "/" + expected +
                               " fidelityDegraded=" + _fidelityDegraded +
                               " geometryFindings=" + _geoFailures.Count +
                               " touchFindings=" + _touchFailures.Count +
                               " measured=" + _touchPanelsChecked);
        }

        // ---------------------------------------------------------------------
        //  WO-1080 provenance markers.
        // ---------------------------------------------------------------------
        /// <summary>
        /// Emit <c>UI_CAPTURE_HEAD &lt;sha&gt; &lt;branch&gt; dirty=&lt;bool&gt;</c>. Written by
        /// the CAPTURE, never by a human remembering a second command -- CLAUDE.md §16 records
        /// that a step whose remedy is "someone remembers" is not a gate.
        /// </summary>
        private static DeNelle.Editor.Regression.CaptureProvenance.Head ReportCaptureProvenance()
        {
            var head = default(DeNelle.Editor.Regression.CaptureProvenance.Head);
            try
            {
                head = DeNelle.Editor.Regression.CaptureProvenance.Resolve();
            }
            catch (Exception e)
            {
                Debug.LogError(DeNelle.Editor.Regression.CaptureProvenance.FailMarker +
                               " provenance resolve threw: " + e);
                return head;
            }

            string line = DeNelle.Editor.Regression.CaptureProvenance.FormatHeadLine(head);
            if (line == null)
            {
                // NOT a UI_CAPTURE_HEAD* suffix on purpose: a grep for the good marker must
                // never match this line and read an unidentified tree as an identified one.
                Debug.LogError(DeNelle.Editor.Regression.CaptureProvenance.FailMarker +
                               " -- this run cannot say which commit it measured, so NO layout or " +
                               "touch ticket may be minted from its log (WO-1080). Reason: " +
                               (head.Failure ?? "unstated"));
                return head;
            }

            Debug.Log(line);

            if (head.Dirty)
            {
                Debug.LogWarning("[UICap-HL] WO-1080: the working tree is DIRTY under Assets/" +
                                 (head.DirtyMeasured ? "" : " (dirtiness could NOT be measured, so it is " +
                                  "reported dirty -- unknown must never read as clean)") +
                                 ". This log records geometry that exists in NO commit, so a later " +
                                 "reader has nothing to diff it against: do not cite it in a ticket. " +
                                 "Commit, then re-run the capture.");
            }
            return head;
        }

        /// <summary>
        /// Emit <c>UI_CAPTURE_STAMP ...</c> -- the sha, the DEFAULT landscape target set (a
        /// panel with its own target array, e.g. RumorBoardTargets, is not reflected here), and
        /// this run's OWN marker totals, on one parseable line. WO-1080 R2: a
        /// repo-wide total quoted in a ticket ("drops from UI_TOUCH_FAIL x43") is only
        /// checkable if the log states the total AND the tree it belongs to together.
        /// </summary>
        private static void ReportCaptureStamp(DeNelle.Editor.Regression.CaptureProvenance.Head head, int pngs)
        {
            try
            {
                var targets = new System.Text.StringBuilder();
                for (int i = 0; i < LandscapeTargets.Length; i++)
                {
                    if (i > 0) targets.Append(',');
                    targets.Append(LandscapeTargets[i].Tag);
                }

                string sha = head.Resolved ? head.Sha : "unknown";
                string branch = head.Resolved ? head.Branch : "unknown";
                // An UNRESOLVED head is a `default(Head)`, whose Dirty field is false. Printing
                // that as dirty=false would report "clean and citable" about a run that could not
                // name its own tree at all -- the precise inversion this ticket exists to stop.
                string dirty = head.Resolved ? (head.Dirty ? "true" : "false") : "unknown";

                Debug.Log(DeNelle.Editor.Regression.CaptureProvenance.StampMarker +
                          " head=" + sha +
                          " branch=" + branch +
                          " dirty=" + dirty +
                          // 'T' and 'Z' escaped: unescaped they are at the mercy of custom-format
                          // specifier lookup, and this line must never throw its own run away.
                          " utc=" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") +
                          " targets=" + targets +
                          " pngs=" + pngs +
                          " panelBuilds=" + (_fidelityOk + _fidelityDegraded) +
                          " canvases=" + _geoCanvasesChecked +
                          " geometryFindings=" + _geoFailures.Count +
                          " touchPanels=" + _touchPanelsChecked +
                          " touchClean=" + _touchPanelsClean +
                          " touchFindings=" + _touchFailures.Count +
                          // WO-1630: the glyph tallies ride the SAME stamp line as the sha,
                          // for the same reason every other Report* total does -- so a ticket
                          // quoting a glyph FAIL count can be checked against the log it says
                          // it came from. glyphLabels=0 with panels>0 means nothing was measured.
                          " glyphPanels=" + _glyphPanelsChecked +
                          " glyphLabels=" + _glyphLabelsMeasured +
                          " glyphFindings=" + _glyphLabelFindings +
                          " glyphBaselined=" + _glyphBaselinedCount +
                          " glyphUnproved=" + _glyphUnmeasuredCount);
            }
            catch (Exception e)
            {
                // Never let the stamp throw away a run that produced real pngs.
                Debug.LogWarning("[UICap-HL] WO-1080 stamp line failed: " + e.Message);
            }
        }

        /// <summary>Did every panel get BUILT at the size it was SHOT at?</summary>
        private static void ReportFidelity()
        {
            int total = _fidelityOk + _fidelityDegraded;

            // The residual, stated plainly. The kit surface moved; the PROCESS's screen did not
            // (batchmode has no game view). Everything resolving through ElarionUiKit is
            // therefore target-accurate; a panel that reads Screen.* DIRECTLY at build time is
            // not. Naming it is the difference between a known limit and a fresh silent lie.
            string residual = _screenStuckBuilds > 0
                ? " RESIDUAL: Screen.width/height itself never moved (stuck at " + _screenStuckAt +
                  " across " + _screenStuckBuilds + " builds) -- `-batchmode` builds no game view for " +
                  "it to mirror, with or without a graphics device. Zones resolved through " +
                  "ElarionUiKit (PostScaleCanvasHeight / SurfaceWidth / SurfaceHeight) ARE the " +
                  "target's; any panel still reading Screen.* directly at build time is not, and " +
                  "must be routed through the kit surface before its shot can be trusted."
                : "";

            if (total == 0)
            {
                Debug.LogError("UI_CAPTURE_FIDELITY_DEGRADED 0/0 builds -- no capture ran at all" + residual);
                return;
            }

            // The divergence proof gates the whole run: if two different aspects resolve the SAME
            // zone rects then every png shares one geometry, whatever the per-build checks said.
            if (_geoMoveProof == null)
            {
                Debug.LogError("UI_CAPTURE_FIDELITY_DEGRADED " + total + "/" + total +
                               " builds -- the harness could NOT prove that changing the target " +
                               "changes the resolved layout, so every png in this run must be treated " +
                               "as SCALE-ACCURATE ONLY (font size reproduces, zone geometry does NOT; " +
                               "the resolution in the filename is a LABEL, not a layout). Reason: " +
                               (_geoMoveFailure ?? "the aspect-divergence proof did not run") + residual);
                return;
            }

            if (_fidelityDegraded == 0)
            {
                Debug.Log("UI_CAPTURE_FIDELITY_OK " + _fidelityOk + " builds -- every panel was " +
                          "constructed with the kit surface at its target resolution, so the zone " +
                          "geometry in each png is that resolution's real geometry. PROOF: " +
                          _geoMoveProof + residual);
                return;
            }

            Debug.LogError("UI_CAPTURE_FIDELITY_DEGRADED " + _fidelityDegraded + "/" + total +
                           " builds -- the surface would not move to the target, so those " +
                           "pngs are SCALE-ACCURATE ONLY (font size reproduces, zone geometry does " +
                           "NOT; the resolution in the filename is a LABEL, not a layout). Reasons: " +
                           string.Join(" | ", new List<string>(_fidelityReasons).ToArray()) + residual);
        }

        // =====================================================================
        //  ProveGeometryMoves -- the run's own proof that the fix is working.
        // ---------------------------------------------------------------------
        //  A capture harness can claim any resolution it likes in a filename. The only
        //  thing that makes the claim MEAN something is that two targets with different
        //  ASPECTS resolve to DIFFERENT zone rects. Before today they did not -- one
        //  geometry wore three filenames all night and a caption rendering off its plate
        //  was invisible.
        //
        //  So the run measures it, on the real kit path: build the real Obsidian modal
        //  under each of the two most-different aspects in the matrix (today 1920x1080,
        //  aspect 1.778, and the Seeker's 2670x1200, aspect 2.225) and compare the
        //  resolved layout.body zone. Identical => the whole run is scale-only and
        //  ReportFidelity says so at ERROR severity. This is a FAILURE, never a warning:
        //  a warning is indistinguishable from the silence that shipped the defect.
        //
        //  Computed from the zone's RESOLVED anchors and PostScaleCanvasHeight rather than
        //  a live rect -- the overlay canvas's own rect is still the editor's 640x480
        //  (nothing can move that in batchmode), and it is not what the panel's fraction
        //  anchors resolve against anyway. Both axes are expressed in canvas-HEIGHT units
        //  so that every number in the proof is one the KIT produced (see MeasureZones).
        // =====================================================================
        private static readonly Vector2 ProbePanelMin = new Vector2(0.22f, 0.10f);
        private static readonly Vector2 ProbePanelMax = new Vector2(0.78f, 0.90f);

        private struct ZoneProbe
        {
            public string Tag;
            public float CanvasH;
            public Rect Body;
        }

        private static void ProveGeometryMoves()
        {
            _geoMoveProof = null;
            _geoMoveFailure = null;

            // The two most-different aspects in the matrix (never a hard-coded pair -- if the
            // matrix changes, the proof follows it).
            CaptureTarget lo = default(CaptureTarget), hi = default(CaptureTarget);
            float loAr = float.MaxValue, hiAr = float.MinValue;
            foreach (var t in LandscapeTargets)
            {
                float ar = (float)t.W / Mathf.Max(1, t.H);
                if (ar < loAr) { loAr = ar; lo = t; }
                if (ar > hiAr) { hiAr = ar; hi = t; }
            }
            if (Mathf.Abs(hiAr - loAr) < 0.01f)
            {
                _geoMoveFailure = "the capture matrix carries only ONE aspect ratio, so nothing in " +
                                  "this run can demonstrate that geometry follows the target. Keep at " +
                                  "least one 16:9-class target and the Seeker's 2670x1200 (2.225).";
                Debug.LogError("[UICap-HL] " + _geoMoveFailure);
                return;
            }

            ZoneProbe a, b;
            if (!MeasureZones(lo, out a) || !MeasureZones(hi, out b))
            {
                _geoMoveFailure = "the zone probe could not be measured at both aspects (see the " +
                                  "[UICap-HL] error above), so the run cannot claim geometry moved";
                return;
            }

            bool moved = Mathf.Abs(a.CanvasH - b.CanvasH) > 0.5f || RectDiffers(a.Body, b.Body, 0.5f);
            if (!moved)
            {
                _geoMoveFailure = "aspect " + loAr.ToString("0.###") + " (" + a.Tag + ") and aspect " +
                                  hiAr.ToString("0.###") + " (" + b.Tag + ") resolved the IDENTICAL " +
                                  "layout.body zone " + RectStr(a.Body) + " at canvas height " +
                                  a.CanvasH.ToString("0.#") + " -- i.e. the surface move is not " +
                                  "reaching ElarionUiKit.PostScaleCanvasHeight and every png in this " +
                                  "run shares one geometry, exactly as before the 2026-08-05 fix";
                Debug.LogError("[UICap-HL] GEOMETRY DID NOT MOVE: " + _geoMoveFailure);
                return;
            }

            _geoMoveProof = a.Tag + " (aspect " + loAr.ToString("0.###") + ") resolves layout.body " +
                            RectStr(a.Body) + " at canvas height " + a.CanvasH.ToString("0.#") +
                            " ref px, while " + b.Tag + " (aspect " + hiAr.ToString("0.###") +
                            ") resolves " + RectStr(b.Body) + " at " + b.CanvasH.ToString("0.#") +
                            " (rects in canvas-height units, all kit-derived) -- different targets, " +
                            "genuinely different layouts";
            Debug.Log("[UICap-HL] geometry-moves proof: " + _geoMoveProof);
        }

        /// <summary>Build the real kit modal under <paramref name="target"/> and measure its
        /// resolved body zone in kit reference px. Returns false (loudly) if it cannot.</summary>
        private static bool MeasureZones(CaptureTarget target, out ZoneProbe probe)
        {
            probe = default(ZoneProbe);
            ElarionUiKit.ObsidianModal modal = null;
            try
            {
                ElarionUiKit.SetSurfaceOverride(target.W, target.H);
                if (ElarionUiKit.SurfaceWidth != target.W || ElarionUiKit.SurfaceHeight != target.H)
                {
                    Debug.LogError("[UICap-HL] zone probe: ElarionUiKit surface refused " + target.Tag +
                                   " (reports " + ElarionUiKit.SurfaceWidth + "x" +
                                   ElarionUiKit.SurfaceHeight + "). The injectable surface is the ONE " +
                                   "lever this harness has left -- without it every shot is scale-only.");
                    return false;
                }

                modal = ElarionUiKit.BuildObsidianModal("~UICapZoneProbe", "Zone Probe",
                    ProbePanelMin, ProbePanelMax, null, 100);
                if (modal == null || modal.chrome == null || modal.chrome.content == null
                    || modal.chrome.layout == null || modal.chrome.layout.body == null)
                {
                    Debug.LogError("[UICap-HL] zone probe: BuildObsidianModal produced no layout.body " +
                                   "at " + target.Tag + " -- the probe measures the kit's real chrome, " +
                                   "so a null here means the kit path itself did not build.");
                    return false;
                }

                float canvasH = ElarionUiKit.PostScaleCanvasHeight(modal.chrome.content.transform);

                // EVERY number below comes from the KIT (this height + the zone's resolved
                // anchors). The x axis is deliberately expressed in canvas-HEIGHT units rather
                // than a width derived from the target's own aspect: a self-derived width would
                // make the two rects differ even if the kit had ignored the surface completely,
                // i.e. a proof that proves itself. This way the rects can only diverge because
                // the kit's own build-time geometry diverged.
                float canvasW = canvasH;

                // The body zone's resolved rect: its anchors within the panel, the panel's
                // anchors within the canvas, the canvas in reference units.
                var body = modal.chrome.layout.body;
                float px0 = ProbePanelMin.x * canvasW, px1 = ProbePanelMax.x * canvasW;
                float py0 = ProbePanelMin.y * canvasH, py1 = ProbePanelMax.y * canvasH;
                float pw = px1 - px0, ph = py1 - py0;
                probe = new ZoneProbe
                {
                    Tag = target.Tag,
                    CanvasH = canvasH,
                    Body = new Rect(px0 + body.anchorMin.x * pw,
                                    py0 + body.anchorMin.y * ph,
                                    (body.anchorMax.x - body.anchorMin.x) * pw,
                                    (body.anchorMax.y - body.anchorMin.y) * ph),
                };
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] zone probe threw at " + target.Tag + ": " + e);
                return false;
            }
            finally
            {
                if (modal != null && modal.canvas != null)
                    UnityEngine.Object.DestroyImmediate(modal.canvas);
                ElarionUiKit.ClearSurfaceOverride();
            }
        }

        private static bool RectDiffers(Rect a, Rect b, float eps)
        {
            return Mathf.Abs(a.xMin - b.xMin) > eps || Mathf.Abs(a.yMin - b.yMin) > eps
                || Mathf.Abs(a.width - b.width) > eps || Mathf.Abs(a.height - b.height) > eps;
        }

        // ---------------------------------------------------------------------
        //  Panel: the founding / Echo unlock card (EchoUnlockDialogue) at its
        //  LONGEST copy -- the founding echo Aldwin. This is the panel with the
        //  just-fixed text/button overlap the owner cares about. We render the
        //  sole acknowledgement state at EVERY target (including Seeker 2670x1200),
        //  so any overlap shows as it does on device.
        //
        //  BUILT ONCE PER TARGET (2026-08-05). This card is the exact panel the old
        //  build-once/re-scale harness lied about: it was "captured at two sizes" and
        //  green all night while its caption rendered off the black plate on device,
        //  because both pngs carried the SAME zone geometry.
        // ---------------------------------------------------------------------
        private static int CaptureFoundingEchoCard()
        {
            return ForEachTarget("EchoUnlockDialogue", CaptureFoundingEchoCardOnce);
        }

        private static int CaptureFoundingEchoCardOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject canvasGo = null;
            EchoUnlockDialogue dlg = null;

            try
            {
                // A pre-seeded EventSystem so EchoUnlockDialogue.EnsureEventSystem no-ops
                // (its DontDestroyOnLoad path warns harmlessly in edit mode; avoid the noise).
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                // Founding spirit == count 1 == Aldwin (long founding + lore copy).
                EchoRosterEntry entry = EchoRosterCatalog.ByCount(1);
                if (entry == null)
                {
                    Debug.LogWarning("[UICap-HL] EchoRosterCatalog.ByCount(1) returned null -- founding card skipped.");
                    return 0;
                }

                // Build the REAL beat via its own public entry (data-driven, guarded).
                // OWNER RULING 2026-08-05 ("it should just simply be one screen"): the WO-831
                // EMERGENCE state is GONE -- its headline, arrival line, artwork and fade are
                // folded into the single awakening card. So there is no _emergenceCanvas to
                // capture and no OnEmergenceContinue advance to drive; Show() lands directly on
                // the one acknowledgement screen. Lore remains in the roster.
                bool shown = EchoUnlockDialogue.Show(entry, 1);
                dlg = UnityEngine.Object.FindAnyObjectByType<EchoUnlockDialogue>();
                if (dlg == null)
                {
                    Debug.LogWarning("[UICap-HL] EchoUnlockDialogue instance not found after Show(shown=" +
                                     shown + ") -- founding card skipped.");
                    return 0;
                }

                canvasGo = GetPrivateGameObject(dlg, "_canvas");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] EchoUnlockDialogue._canvas was null -- card did not build; skipped.");
                    return saved;
                }

                // -- SOLE acknowledgement state --
                if (RenderCanvasToPng(canvasGo,
                    OutDir + "EchoUnlockDialogue_Aldwin_acknowledge_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] founding echo card capture threw: " + e);
            }
            finally
            {
                // Edit-mode teardown MUST be DestroyImmediate (the card's own Close uses
                // runtime Destroy, which errors in edit mode).
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (dlg != null && dlg.gameObject != null) UnityEngine.Object.DestroyImmediate(dlg.gameObject);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: DialogueView in its OPTIONS state (WO-1030: the longest option
        //  was sliced by the panel edge in landscape; the option band is now
        //  reserved FIRST and the text well scrolls). Shot at every landscape
        //  target (including the Seeker's real 2670x1200) in two flavors:
        //    * a 2-option node -- the common case, which must fit scroll-free;
        //    * a 4-option node -- the many-option worst case that must scroll with
        //      a visible affordance, never silently clip.
        //
        //  WO-1031: these used to shoot PetTaskController.BuildEngageDef("ice-wolf")
        //  (speaker "Frost"). That prompt is DELETED -- the guide wolf is Echo #1,
        //  Aldwin, and tasking lives in the Echo tab. The capture is now driven by
        //  SYNTHETIC probe defs, because what it proves is the SHARED DialogueView
        //  layout (canon reference implementation, UI_BLINK_TEMPLATE_CANON.md sec. 8)
        //  which every conversation in the game depends on -- so the WO-1030 fix
        //  keeps its screenshot coverage after the pet prompt's removal.
        //
        //  DialogueView lives in DeNelle.HUD, which DeNelle.Editor does NOT
        //  reference, so the view is resolved by reflection (like PauseController).
        //  Its OnEnable/OnDisable are invoked explicitly: edit mode never calls
        //  MonoBehaviour lifecycle on AddComponent, and the Opened subscription is
        //  how the view builds its panel when DialogueService.PlayDef fires.
        // ---------------------------------------------------------------------
        private static int CaptureDialogueOptions()
        {
            return ForEachTarget("DialogueOptions", CaptureDialogueOptionsOnce);
        }

        private static int CaptureDialogueOptionsOnce(CaptureTarget target)
        {
            int saved = 0;
            saved += CaptureDialogueDefOnce(target, BuildOptionProbeDef(2),
                "DialogueOptions_2opt");
            saved += CaptureDialogueDefOnce(target, BuildOptionProbeDef(4),
                "DialogueOptions_4opt");
            return saved;
        }

        private static DeNelle.Core.Dialogue.DialogueDef BuildCompactHelperProbeDef()
        {
            var def = new DeNelle.Core.Dialogue.DialogueDef
            {
                Id = "uicap_dialogue_compact_helper",
                StartNode = "root",
            };
            def.Nodes.Add(new DeNelle.Core.Dialogue.DialogueNode
            {
                Id = "root",
                Lines = new List<DeNelle.Core.Dialogue.DialogueLine>
                {
                    new DeNelle.Core.Dialogue.DialogueLine
                    {
                        Speaker = "Aldwin",
                        Text = "I can farm. I can mend. Put me to work, Keeper.",
                    },
                },
            });
            return def;
        }

        /// <summary>An n-option probe node (WO-1030 acceptance: 2 options fit scroll-free;
        /// a 4-option node either fits or scrolls with a visible affordance). One builder so
        /// the two shots differ ONLY by option count. Speaker is a catalog speaker, never an
        /// invented name (WO-1031: no species -> name table exists any more).</summary>
        private static DeNelle.Core.Dialogue.DialogueDef BuildOptionProbeDef(int optionCount)
        {
            string[] labels =
            {
                "Show me the rumor board.",
                "Gather resources",
                "Stand watch at the gate",
                "Rest by the Heart of Elarion",
            };
            var def = new DeNelle.Core.Dialogue.DialogueDef
            {
                Id = "uicap_dialogue_" + optionCount + "opt",
                StartNode = "root",
            };
            var options = new List<DeNelle.Core.Dialogue.DialogueOption>();
            for (int i = 0; i < optionCount && i < labels.Length; i++)
                options.Add(new DeNelle.Core.Dialogue.DialogueOption { Text = labels[i], Goto = "" });

            def.Nodes.Add(new DeNelle.Core.Dialogue.DialogueNode
            {
                Id = "root",
                Lines = new List<DeNelle.Core.Dialogue.DialogueLine>
                {
                    new DeNelle.Core.Dialogue.DialogueLine
                    {
                        Speaker = "Your Echo",
                        Text = "Keeper, the option-layout probe: every choice below must stay reachable.",
                    },
                },
                Options = options,
            });
            return def;
        }

        private static int CaptureDialogueDefOnce(CaptureTarget target,
            DeNelle.Core.Dialogue.DialogueDef def, string shotName,
            bool advanceToOptions = true)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject viewGo = null;
            GameObject uiGo = null;
            Component view = null;
            try
            {
                // Every probe must own a clean modal slot. Earlier capture families can leave
                // their panel registered even after their canvas is destroyed; DialogueView
                // correctly suppresses itself in that state, which used to produce six blank
                // PNG attempts while the aggregate UI_CAPTURE_OK marker still went green.
                DeNelle.Core.UI.PanelManager.CloseAll();
                Type viewType = ResolveType("DeNelle.HUD.DialogueView");
                if (viewType == null)
                {
                    Debug.LogWarning("[UICap-HL] DeNelle.HUD.DialogueView type not found -- " +
                                     shotName + " skipped.");
                    return 0;
                }
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                viewGo = new GameObject("~UICapDialogueView");
                view = viewGo.AddComponent(viewType);
                // Edit mode calls NO lifecycle on AddComponent -- wire the Opened subscription
                // the way play mode would, so PlayDef below reaches this view.
                InvokePrivate(view, "OnEnable");

                if (!DeNelle.Core.Dialogue.DialogueService.PlayDef(def))
                {
                    Debug.LogWarning("[UICap-HL] DialogueService.PlayDef refused the def -- " +
                                     shotName + " skipped.");
                    return 0;
                }

                // The prompt is line -> OPTIONS; advance once so the CHOICE LIST (the WO-1030
                // defect surface) is the state in the png.
                var vm = DeNelle.Core.Dialogue.DialogueService.ActiveVm;
                if (advanceToOptions && vm != null && !vm.ShowingOptions) vm.Advance();
                if (advanceToOptions && (vm == null || !vm.ShowingOptions))
                {
                    Debug.LogWarning("[UICap-HL] dialogue VM never reached ShowingOptions -- " +
                                     shotName + " captures the non-options state instead.");
                }

                uiGo = GetPrivateGameObject(view, "_ui");
                if (uiGo == null)
                {
                    Debug.LogWarning("[UICap-HL] DialogueView._ui was null -- panel did not build; " +
                                     shotName + " skipped.");
                    return saved;
                }
                if (RenderCanvasToPng(uiGo, OutDir + shotName + "_" + target.Tag + ".png",
                                      target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] " + shotName + " capture threw: " + e);
            }
            finally
            {
                // Teardown WITHOUT the view's runtime Destroy path (Destroy errors in edit
                // mode): null the private _ui first so the view's close handler skips it, close
                // the VM (unbinds handlers + releases the panel arbiter via NotifyClosed), then
                // detach the static Opened subscription and DestroyImmediate the orphans.
                try
                {
                    if (view != null) SetPrivateField(view, "_ui", null);
                    DeNelle.Core.Dialogue.DialogueService.Stop();
                    if (view != null) InvokePrivate(view, "OnDisable");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[UICap-HL] dialogue capture teardown: " + e.Message);
                }
                if (uiGo != null) UnityEngine.Object.DestroyImmediate(uiGo);
                if (viewGo != null) UnityEngine.Object.DestroyImmediate(viewGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }
            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the Pause overlay (PauseController) -- the code-built kit modal
        //  (FrameOptions): Resume / Settings / Quit to Title over a scrim. The owner
        //  reported the option buttons "stacked". We build the REAL modal headless
        //  (attaching a bare SettingsController so the WORST case -- all three option
        //  buttons -- renders) and shoot it at two mobile-landscape resolutions so any
        //  overlap shows exactly as on device.
        //
        //  PauseController lives in the DeNelle.Settings assembly, which DeNelle.Editor
        //  does NOT reference, so every touch is via reflection (type-load + private
        //  field/method access) -- no compile-time dependency, no asmdef change.
        // ---------------------------------------------------------------------
        // ---------------------------------------------------------------------
        //  Panel: WO-1243 the operator maintenance banner, as a player actually
        //  reads it.
        //
        //  WHY THIS SHOT IS BUILT THE WAY IT IS: the tempting version hands
        //  ObjectiveBannerUi a hand-typed "MAINTENANCE ON RAIDS" string and
        //  photographs that. It would look identical and prove NOTHING -- it
        //  would only prove that a banner can render a literal I just typed.
        //
        //  So this drives the REAL path end to end: a server-shaped payload goes
        //  in through MaintenanceCatalog.ApplyPayload -- the same seam the live
        //  /api/maintenance response goes through -- and the text on screen is
        //  whatever the ONE producer -- MaintenanceCatalog.LineFor, reached
        //  through BuildLines, which is the call MaintenanceBannerDriver makes --
        //  decides to say about it. If the catalog stops sealing, mis-names an
        //  area, or returns empty, the shot changes or the capture fails. That is
        //  the difference between evidence and decoration.
        //
        //  Two areas are sealed on purpose (raiding + store) because the roll is
        //  a MULTI-line surface and a single-seal shot would hide a bug in the
        //  joining. `server` is deliberately NOT sealed here: it outranks the
        //  others and collapses the roll to one line, which is a different shot.
        //
        //  The catalog is a static singleton, so it is RESTORED in finally --
        //  leaking a sealed state into later captures would seal panels shot
        //  after this one and read as a mysterious unrelated failure.
        // ---------------------------------------------------------------------
        private static int CaptureMaintenanceBanner()
        {
            return ForEachTarget("MaintenanceBanner", CaptureMaintenanceBannerOnce);
        }

        private static int CaptureMaintenanceBannerOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject canvasGo = null;
            GameObject tempEventSystem = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                // A payload shaped exactly like the live GET /api/maintenance body.
                const string payload =
                    "{\"ok\":true,\"version\":1,\"readOk\":true,\"areas\":{" +
                    "\"farming\":{\"closed\":false}," +
                    "\"raiding\":{\"closed\":true,\"closedBy\":\"owner\"," +
                        "\"message\":\"Raids are closed while we fix the reward payout. Back shortly.\"}," +
                    "\"arena\":{\"closed\":false}," +
                    "\"dungeons\":{\"closed\":false}," +
                    "\"store\":{\"closed\":true,\"closedBy\":\"owner\"," +
                        "\"message\":\"The store is closed. No purchase can be charged while it is.\"}," +
                    "\"server\":{\"closed\":false}}}";

                if (!DeNelle.Core.Ops.MaintenanceCatalog.ApplyPayload(payload, "ui-capture"))
                {
                    Debug.LogError("[UICap-HL] MaintenanceCatalog REFUSED a well-formed payload -- " +
                                   "banner capture cannot proceed. This is a real defect, not a skip.");
                    return 0;
                }

                // Take the line from MaintenanceBannerDriver, which is the object that
                // actually feeds ObjectiveBannerUi at runtime. WO-1245 collapsed the two
                // producers this comment used to warn about: the driver no longer formats
                // anything of its own, it calls MaintenanceCatalog.BuildLines, and that is
                // also what MaintenanceTogglesRegression asserts. Driving the DRIVER (not
                // BuildLines directly) keeps this shot end-to-end: if the driver ever
                // stops calling the single producer, this capture changes with it.
                var driverGo = new GameObject("~UICapMaintDriver");
                string text;
                try
                {
                    var driver = driverGo.AddComponent<DeNelle.Core.Ops.MaintenanceBannerDriver>();
                    InvokePrivate(driver, "RebuildLines");
                    var lines = GetPrivateFieldValue(driver, "_lines") as System.Collections.Generic.List<string>;
                    text = (lines != null && lines.Count > 0) ? lines[0] : null;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(driverGo);
                }

                // Assert the producer actually produced. A blank or unsealed string
                // would still photograph fine as an empty plate, and an empty plate
                // that nobody checks is exactly how a broken gate ships green.
                if (string.IsNullOrWhiteSpace(text) || text.IndexOf("MAINTENANCE ON", StringComparison.Ordinal) < 0)
                {
                    Debug.LogError("[UICap-HL] the driver produced no maintenance line after two areas " +
                                   "were sealed -- got: '" + (text ?? "<null>") + "'. Refusing to shoot a " +
                                   "blank plate and call it proof.");
                    return 0;
                }

                // ObjectiveBannerUi.Show() cannot be called here: its Ensure() calls
                // DontDestroyOnLoad, which THROWS in edit mode. Same constraint the
                // pause-menu capture works around. So build the same object Ensure
                // builds, minus that one call, and drive the same private members
                // Show drives -- the widget under the shot is still the real one.
                canvasGo = new GameObject("ObjectiveBanner");
                var banner = canvasGo.AddComponent<DeNelle.Core.UI.ObjectiveBannerUi>();
                InvokePrivate(banner, "Build");

                SetPrivateField(banner, "_visible", true);
                // WO-1245 defect 2: the plate is NoWrap+Ellipsis by default (correct for
                // the tutorial objectives it was built for), which cut the operator's
                // message at about 40 characters -- "MAINTENANCE ON RAIDS - Raids are
                // closed whil...". Show(..., wrap:true) is the opt-in the driver passes;
                // Show() cannot be called here (DontDestroyOnLoad throws in edit mode) so
                // the same field is set directly. WITHOUT THIS LINE the capture would keep
                // photographing the truncation and calling it proof.
                SetPrivateField(banner, "_wrap", true);
                SetPrivateField(banner, "_baseText", text);
                SetPrivateField(banner, "_count", 0);
                SetPrivateField(banner, "_done", 0);
                InvokePrivate(banner, "RefreshLabel");

                // Build() sets the CanvasGroup to alpha 0 and Update() eases it in.
                // Update never ticks in edit mode, so without this the shot is a
                // FULLY TRANSPARENT banner -- a blank plate that still writes a png
                // and still counts. Force the alpha the fade would have reached, and
                // assert it, because "the capture ran" is not "the capture showed
                // something".
                var group = canvasGo.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    Debug.LogError("[UICap-HL] ObjectiveBanner has no CanvasGroup after Build -- " +
                                   "the banner did not construct; refusing to shoot.");
                    return 0;
                }
                group.alpha = 1f;

                canvasGo.SetActive(true);

                if (RenderCanvasToPng(canvasGo, OutDir + "MaintenanceBanner_" + target.Tag + ".png",
                    target.W, target.H)) saved++;

                Debug.Log("[UICap-HL] maintenance banner line under shot: " + text);
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] maintenance banner capture threw: " + e);
            }
            finally
            {
                // Restore FIRST -- a leaked seal would close areas for every panel
                // shot after this one.
                try { DeNelle.Core.Ops.MaintenanceCatalog.Clear(); }
                catch (Exception ce) { Debug.LogError("[UICap-HL] could not clear MaintenanceCatalog: " + ce); }

                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the hero-select carousel (WO-1248). The owner saw "Pr..." where
        //  the rotate control meant "Previous". Screenshots are primary evidence
        //  for visual defects (WO-1245 passed every marker while still truncating).
        //  No capture existed; this one builds the REAL HeroSelectController tree
        //  in edit mode at Seeker landscape AND portrait.
        // ---------------------------------------------------------------------
        private static readonly CaptureTarget[] HeroSelectTargets =
        {
            new CaptureTarget(1920, 1080),
            new CaptureTarget(2670, 1200),   // Seeker landscape - the owner's device
            new CaptureTarget(1080, 1920),   // portrait - the 0.068-lane smoking gun
        };

        private static int CaptureHeroSelect()
        {
            return ForEachTarget("HeroSelect", HeroSelectTargets, CaptureHeroSelectOnce);
        }

        private static int CaptureHeroSelectOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            object ctrl = null;

            try
            {
                Type heroSelectType = ResolveType("DeNelle.Onboarding.HeroSelectController");
                if (heroSelectType == null)
                {
                    Debug.LogWarning("[UICap-HL] HeroSelectController type not found -- hero-select capture skipped.");
                    return 0;
                }

                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                // Inactive so OnEnable (which may SceneRouter.GoCastle on a save that
                // already has a hero) never runs. We drive BuildScreen directly.
                hostGo = new GameObject("~UICapHeroSelect");
                hostGo.SetActive(false);
                ctrl = hostGo.AddComponent(heroSelectType);
                SetPrivateField(ctrl, "_skipWhenIntroComplete", false);
                InvokePrivate(ctrl, "BuildScreen");

                canvasGo = GetPrivateFieldValue(ctrl, "_canvas") as GameObject;
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] HeroSelectController._canvas null after BuildScreen -- skipped.");
                    return 0;
                }

                canvasGo.SetActive(true);

                if (RenderCanvasToPng(canvasGo, OutDir + "HeroSelect_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] hero-select capture threw: " + e);
            }
            finally
            {
                // Null the field FIRST so OnDisable does not call runtime Destroy()
                // on the canvas (illegal in edit mode -- same contract as pause-menu).
                if (ctrl != null) SetPrivateField(ctrl, "_canvas", null);
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        private static int CapturePauseMenu()
        {
            return ForEachTarget("PauseMenu", CapturePauseMenuOnce);
        }

        /// <summary>Fast visual iteration entry point for the approved pause reference.</summary>
        public static void RunPauseCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CapturePauseMenu();
            if (count == 3) Debug.Log("PAUSE_CAPTURE_OK 3/3");
            else Debug.LogError("PAUSE_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Fast visual iteration entry point for the combat Item picker.</summary>
        public static void RunCombatItemPickerCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("CombatItemPicker", CaptureCombatItemPickerOnce);
            if (count == 3) Debug.Log("COMBAT_ITEM_PICKER_CAPTURE_OK 3/3");
            else Debug.LogError("COMBAT_ITEM_PICKER_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Fast visual iteration entry point for the full Settings surface.</summary>
        public static void RunSettingsCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("Settings", CaptureSettingsOnce);
            if (count == 3) Debug.Log("SETTINGS_CAPTURE_OK 3/3");
            else Debug.LogError("SETTINGS_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused proof for the player-facing honest-feedback panel, including the
        /// Seeker's exact 2670x1200 landscape surface.</summary>
        public static void RunHonestFeedbackCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("HonestFeedback", CaptureHonestFeedbackOnce);
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (count == 3 && _geoFailures.Count == 0 && _touchFailures.Count == 0)
                Debug.Log("HONEST_FEEDBACK_CAPTURE_OK 3/3; geometry=clean; touch=clean");
            else
                Debug.LogError("HONEST_FEEDBACK_CAPTURE_FAIL frames=" + count +
                    "/3 geometry=" + _geoFailures.Count + " touch=" + _touchFailures.Count);
        }

        private static int CaptureHonestFeedbackOnce(CaptureTarget target)
        {
            GameObject host = null;
            GameObject canvas = null;
            try
            {
                PanelManager.CloseAll();
                host = new GameObject("~UICapHonestFeedback");
                var panel = host.AddComponent<HonestFeedbackPanel>();
                InvokePrivate(panel, "EnsureBuilt");
                var modal = GetPrivateFieldValue(panel, "_modal") as ElarionUiKit.ObsidianModal;
                canvas = modal != null ? modal.canvas : null;
                if (canvas != null) canvas.SetActive(true);
                return canvas != null && RenderCanvasToPng(canvas,
                    OutDir + "HonestFeedback_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] HonestFeedback @" + target.Tag + " threw: " + e);
                return 0;
            }
            finally
            {
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                PanelManager.CloseAll();
            }
        }

        /// <summary>Fast visual iteration for the live operator-maintenance status strip.</summary>
        public static void RunMaintenanceBannerCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureMaintenanceBanner();
            if (count == 3) Debug.Log("MAINTENANCE_BANNER_CAPTURE_OK 3/3");
            else Debug.LogError("MAINTENANCE_BANNER_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Fast visual iteration for the production Monthly Ledger route.</summary>
        public static void RunMonthlyLedgerCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;

            int count = CaptureReflectedSecondary(
                "MonthlyLedger", "DeNelle.Wallet.MonthlyLedgerPanel", "OnEnable", null, true);
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (count == 3 && _fidelityDegraded == 0 && _geoFailures.Count == 0 &&
                _touchFailures.Count == 0)
                Debug.Log("MONTHLY_LEDGER_CAPTURE_OK 3/3; fidelity=clean; geometry=clean; touch=clean");
            else
                Debug.LogError("MONTHLY_LEDGER_CAPTURE_FAIL frames=" + count + "/3 fidelity=" +
                    _fidelityDegraded + " geometry=" + _geoFailures.Count + " touch=" +
                    _touchFailures.Count);
        }

        /// <summary>
        /// Matched adaptive-HUD evidence: the real kit is built once per target and forced through
        /// calm-town and active-battle postures on the same canvas. This proves persistent anchor
        /// stability and catches posture-specific geometry/touch regressions.
        /// </summary>
        public static void RunAdaptiveHudCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("AdaptiveHud", CaptureAdaptiveHudOnce);
            if (count == 9) Debug.Log("ADAPTIVE_HUD_CAPTURE_OK 9/9");
            else Debug.LogError("ADAPTIVE_HUD_CAPTURE_FAIL " + count + "/9");
        }

        /// <summary>Focused proof that a one-line tutorial/townsfolk helper uses the compact
        /// composition while retaining the full dialogue interaction contract.</summary>
        public static void RunCompactDialogueCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("CompactDialogue", target =>
                CaptureDialogueDefOnce(target, BuildCompactHelperProbeDef(),
                    "DialogueCompact_Aldwin", advanceToOptions: false));
            bool interaction = VerifyDialogueFrameTapAndCombatTruce();
            if (count == 3 && interaction) Debug.Log("COMPACT_DIALOGUE_CAPTURE_OK 3/3 + INTERACTION_OK");
            else Debug.LogError("COMPACT_DIALOGUE_CAPTURE_FAIL " + count + "/3 interaction=" + interaction);
        }

        /// <summary>Focused two/four-choice dialogue evidence. Kept separate from the broad
        /// registry so a prose/choice balance correction can be iterated without rebuilding all
        /// public surfaces, while still exercising the production DialogueView and modal arbiter.</summary>
        public static void RunDialogueOptionsCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureDialogueOptions();
            if (count == 6) Debug.Log("DIALOGUE_OPTIONS_CAPTURE_OK 6/6");
            else Debug.LogError("DIALOGUE_OPTIONS_CAPTURE_FAIL " + count + "/6");
        }

        private static bool VerifyDialogueFrameTapAndCombatTruce()
        {
            GameObject viewGo = null;
            Component view = null;
            Func<bool> combatProbe = null;
            bool combat = false;
            try
            {
                Type viewType = ResolveType("DeNelle.HUD.DialogueView");
                if (viewType == null) return false;
                viewGo = new GameObject("~UICapDialogueInteraction");
                view = viewGo.AddComponent(viewType);
                InvokePrivate(view, "OnEnable");

                var def = new DeNelle.Core.Dialogue.DialogueDef
                {
                    Id = "uicap_dialogue_interaction",
                    StartNode = "root",
                };
                def.Nodes.Add(new DeNelle.Core.Dialogue.DialogueNode
                {
                    Id = "root",
                    Lines = new List<DeNelle.Core.Dialogue.DialogueLine>
                    {
                        new DeNelle.Core.Dialogue.DialogueLine { Speaker = "Aldwin", Text = "First helper line." },
                        new DeNelle.Core.Dialogue.DialogueLine { Speaker = "Aldwin", Text = "Second helper line." },
                    },
                });
                if (!DeNelle.Core.Dialogue.DialogueService.PlayDef(def)) return false;
                var vm = DeNelle.Core.Dialogue.DialogueService.ActiveVm;
                if (vm == null || vm.Text != "First helper line.") return false;

                // Bypass only the opening debounce; invoke the Button attached to the FRAME,
                // proving the modal surface itself owns advancement.
                SetPrivateField(view, "_openedAt", -1000f);
                var box = GetPrivateFieldValue(view, "_box") as RectTransform;
                var frameButton = box != null ? box.GetComponent<Button>() : null;
                if (frameButton == null) return false;
                frameButton.onClick.Invoke();
                if (vm.Text != "Second helper line." || !vm.IsOpen) return false;

                combatProbe = () => combat;
                DeNelle.Core.Combat.BattleLock.RegisterProbe(combatProbe);
                combat = true;
                // This is the production hostile-posture path. The dialogue close callback must
                // convert it into a truce rather than fire Ended/advance the tutorial.
                PanelManager.CloseAll();
                var ui = GetPrivateGameObject(view, "_ui");
                if (ui == null || ui.activeSelf || !vm.IsOpen || vm.Text != "Second helper line.") return false;

                combat = false;
                InvokePrivate(view, "TickCombatTruce");
                if (!ui.activeSelf || !vm.IsOpen || vm.Text != "Second helper line.") return false;
                Debug.Log("DIALOGUE_INTERACTION_OK frame-tap advanced; combat hid/paused; post-combat resumed same line");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] dialogue interaction contract threw: " + e);
                return false;
            }
            finally
            {
                if (combatProbe != null) DeNelle.Core.Combat.BattleLock.UnregisterProbe(combatProbe);
                try
                {
                    if (view != null) SetPrivateField(view, "_ui", null);
                    DeNelle.Core.Dialogue.DialogueService.Stop();
                    if (view != null) InvokePrivate(view, "OnDisable");
                }
                catch { }
                PanelManager.CloseAll();
                if (viewGo != null) UnityEngine.Object.DestroyImmediate(viewGo);
            }
        }

        private static int CaptureFoundingChoiceOnce(CaptureTarget target)
        {
            GameObject host = null;
            GameObject canvas = null;
            Component controller = null;
            try
            {
                Type type = ResolveType("DeNelle.Onboarding.FoundingChoiceController");
                if (type == null)
                {
                    Debug.LogWarning("[UICap-HL] FoundingChoiceController type not found -- skipped.");
                    return 0;
                }
                host = new GameObject("~UICapFoundingChoice");
                controller = host.AddComponent(type);
                InvokePrivate(controller, "Build");
                canvas = GetPrivateGameObject(controller, "_canvas");
                if (canvas == null)
                {
                    Debug.LogWarning("[UICap-HL] FoundingChoiceController._canvas was null -- skipped.");
                    return 0;
                }
                return RenderCanvasToPng(canvas,
                    OutDir + "FoundingChoice_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] founding choice capture threw: " + e);
                return 0;
            }
            finally
            {
                // Avoid the runtime Destroy path in edit-mode capture; tear down the owned
                // canvas and host immediately after the pixels are committed.
                if (controller != null) SetPrivateField(controller, "_canvas", null);
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>Focused three-ratio evidence for the mandatory fresh-save founding choice.</summary>
        public static void RunFoundingChoiceCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("FoundingChoice", CaptureFoundingChoiceOnce);
            if (count == 3) Debug.Log("FOUNDING_CHOICE_CAPTURE_OK 3/3");
            else Debug.LogError("FOUNDING_CHOICE_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused evidence loop for the Hero navigation family.</summary>
        public static void RunHeroFamilyCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("HeroEquipment", CaptureHeroEquipmentOnce) +
                        CaptureHeroSkillTree();
            if (count == 9) Debug.Log("HERO_FAMILY_CAPTURE_OK 9/9");
            else Debug.LogError("HERO_FAMILY_CAPTURE_FAIL " + count + "/9");
        }

        /// <summary>Focused proof for the first-build category launcher.</summary>
        public static void RunBuildCollectionsCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureBuildCollections();
            if (count == 3) Debug.Log("BUILD_COLLECTIONS_CAPTURE_OK 3/3");
            else Debug.LogError("BUILD_COLLECTIONS_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused proof for the build placement confirmation/orientation modal.</summary>
        public static void RunBuildPreviewCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("BuildPreview", CaptureBuildPreviewOnce);
            if (count == 3) Debug.Log("BUILD_PREVIEW_CAPTURE_OK 3/3");
            else Debug.LogError("BUILD_PREVIEW_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused proof for the highest-risk player warning: random jewel re-polish.</summary>
        public static void RunJewelPolishConfirmCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("JewelPolishConfirm", CaptureJewelPolishConfirmOnce);
            if (count == 3) Debug.Log("JEWEL_POLISH_CONFIRM_CAPTURE_OK 3/3");
            else Debug.LogError("JEWEL_POLISH_CONFIRM_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>
        /// WO-1600 - the JEWELER DISCOVERED card, which had NEVER been shot.
        /// <para>The owner's device frame (Logs/device/seeker-shots/Screenshot_20260907-132324.png,
        /// build 2026.09.07.359651) is the only picture that has ever existed of this modal, and it
        /// showed the copy sitting outside the plate and OPEN CRAFTING: JEWELER printed on top of the
        /// modal's own CLOSE. A card with no capture entry is a card whose layout is proven by the
        /// owner's eyes, which is the arrangement §14 exists to end. Marker
        /// <c>JEWELER_DISCOVERY_CAPTURE_OK 3/3</c>; the Seeker's real 2670x1200 surface is one of the
        /// three targets, so the acceptance ("body inside the plate at 2670x1200") is judged on the
        /// PNG this writes.</para>
        /// </summary>
        public static void RunJewelerDiscoveryCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("JewelerDiscovery", CaptureJewelerDiscoveryOnce);
            if (count == 3) Debug.Log("JEWELER_DISCOVERY_CAPTURE_OK 3/3");
            else Debug.LogError("JEWELER_DISCOVERY_CAPTURE_FAIL " + count + "/3");
        }

        private static int CaptureJewelerDiscoveryOnce(CaptureTarget target)
        {
            GameObject host = null, stateHost = null, eventSystem = null, canvas = null;
            GameStateService priorState = null;
            GameState stateFixture = null;
            Component ftue = null;
            try
            {
                PanelManager.CloseAll();
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    eventSystem = new GameObject("~UICapEventSystem");
                    eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }
                Type type = ResolveType("DeNelle.Village.Crafting.JewelerDiscoveryFtue");
                if (type == null) throw new TypeLoadException("DeNelle.Village.Crafting.JewelerDiscoveryFtue");

                // The SAME earned-history fixture the registered Jeweler panel shot uses: the card's
                // gate is JewelerProgression.IsUnlocked = HasEverAcquired(RoughStoneId).
                priorState = GameStateService.Instance;
                stateFixture = ScriptableObject.CreateInstance<GameState>();
                stateFixture.MarkEverAcquired(DungeonExclusiveItems.RoughStoneId);
                stateHost = new GameObject("~UICapJewelerDiscoveryState");
                if (!InstallCaptureState(stateHost.AddComponent<GameStateService>(), stateFixture))
                    throw new InvalidOperationException("Jeweler discovery progression fixture unavailable");

                host = new GameObject("~UICapJewelerDiscovery");
                ftue = host.AddComponent(type);
                // Present(), NOT TryPresent(). WO-1600's gate admits the card only in a home hub
                // with a chosen hero, and the capture scene is neither - so TryPresent would
                // correctly refuse and this entry would shoot nothing. Present() is the build step
                // that gate guards; shooting it proves the card the player actually gets in town,
                // without the capture having to defeat (or silently re-implement) the gate.
                InvokePrivate(ftue, "Present");
                var modal = GetPrivateFieldValue(ftue, "_modal") as ElarionUiKit.ObsidianModal;
                canvas = modal != null ? modal.canvas : null;
                if (canvas == null) throw new InvalidOperationException("Jeweler discovery built no canvas");
                Canvas.ForceUpdateCanvases();
                return RenderCanvasToPng(canvas,
                    OutDir + "JewelerDiscovery_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] JewelerDiscovery capture threw: " + e);
                return 0;
            }
            finally
            {
                // ORDER MATTERS: tear the canvas down with DestroyImmediate FIRST, then Close().
                // Close() calls UnityEngine.Object.Destroy, which is deferred and ERRORS in edit
                // mode ("Destroy may not be called from edit mode") - a predictable red line in a
                // gate log that a reader would then have to rule out by hand. With the canvas
                // already gone Close() skips that branch and still does the work only it can:
                // dispose the WorldHold and notify PanelManager.
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (ftue != null) InvokePrivate(ftue, "Close");
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (stateHost != null)
                {
                    RestoreCaptureState(priorState);
                    UnityEngine.Object.DestroyImmediate(stateHost);
                }
                if (stateFixture != null) UnityEngine.Object.DestroyImmediate(stateFixture);
                if (eventSystem != null) UnityEngine.Object.DestroyImmediate(eventSystem);
                PanelManager.CloseAll();
            }
        }

        /// <summary>Focused two-state proof for normal loading and first-run connection recovery.</summary>
        public static void RunLoadingOverlayCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("LoadingOverlay", CaptureLoadingOverlayOnce);
            if (count == 6) Debug.Log("LOADING_OVERLAY_CAPTURE_OK 6/6");
            else Debug.LogError("LOADING_OVERLAY_CAPTURE_FAIL " + count + "/6");
        }

        /// <summary>Focused three-ratio proof for the real offline-haul acknowledgement.</summary>
        public static void RunWelcomeBackCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            // WO-1408 -- TWO fixtures, because the ticket's acceptance is about a row that is only
            // there when it is true. The first is the long-standing worst-case haul with NO doors
            // (the "COLLECT alone, no empty rows" half); the second carries a finished job, a
            // recorded attack and a ready army, so the doors and the ready band are on screen.
            int count = ForEachTarget("WelcomeBack", CaptureWelcomeBackOnce) +
                        ForEachTarget("WelcomeBackDoors", CaptureWelcomeBackDoorsOnce);
            if (count == 6) Debug.Log("WELCOME_BACK_CAPTURE_OK 6/6");
            else Debug.LogError("WELCOME_BACK_CAPTURE_FAIL " + count + "/6");
        }

        /// <summary>
        /// Focused proof for the remaining shared system-modal family. These use the
        /// production builders and worst-case copy, not screenshot-only replicas.
        /// </summary>
        public static void RunSystemModalCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("SystemModals", CaptureSystemModalsOnce);
            if (count == 12) Debug.Log("SYSTEM_MODAL_CAPTURE_OK 12/12");
            else Debug.LogError("SYSTEM_MODAL_CAPTURE_FAIL " + count + "/12");
        }

        private static int CaptureSystemModalsOnce(CaptureTarget target)
        {
            int saved = 0;
            PanelManager.CloseAll();
            saved += CaptureBuiltModal<AdConsentPanel>("AdConsent", target, "Build");
            saved += CaptureBuiltModal<OfflineOptInPanel>("OfflineOptIn", target, "Build");
            saved += CaptureBuiltModal<DailyChestController>("DailyChest", target, "Build");

            GameObject host = null;
            GameObject canvas = null;
            try
            {
                host = new GameObject("~UICapHarvestOverflow");
                var panel = host.AddComponent<HarvestOverflowModal>();
                // (!) THE OWNER'S OWN STATE, NOT A TOY ONE (2026-09-07 01:14, build 358872,
                // Logs/device/screens/owner-screen-20260907-011426.png). The old fixture was two
                // small rows with short container names, which is exactly why the capture never
                // showed the two defects she hit: THREE full plates, and the long container names
                // ("Lumberyard", "Stoneyard") whose chips ellipsized to "UPGRADE LUMBER..." and
                // "UPGRADE STONEYA...". SIX statuses over THREE resources on purpose - that is the
                // shape that produced her "+3 more" line, so the capture proves the merge as well
                // as the fit. Figures reconcile to the WELCOME BACK frame one minute earlier:
                // waiting = 40,972 wood / 21,843 iron / 45,257 stone.
                const string Collectors = HarvestOverflowModal.CollectorSource;
                const string Silo = HarvestOverflowModal.SiloSource;
                var rows = new List<BankOverflowStatus>
                {
                    new BankOverflowStatus
                    {
                        Available = true, Resource = BankResource.Wood,
                        ResourceName = "Wood", ContainerName = "Lumberyard",
                        Requested = 15142, Granted = 2906, Lost = 12236,
                        Current = 23094, Max = 26000, Source = Collectors
                    },
                    new BankOverflowStatus
                    {
                        Available = true, Resource = BankResource.Iron,
                        ResourceName = "Iron", ContainerName = "Foundry",
                        Requested = 7570, Granted = 1535, Lost = 6035,
                        Current = 8465, Max = 10000, Source = Collectors
                    },
                    new BankOverflowStatus
                    {
                        Available = true, Resource = BankResource.Food,
                        ResourceName = "Stone", ContainerName = "Stoneyard",
                        Requested = 30932, Granted = 0, Lost = 30932,
                        Current = 3000, Max = 3000, Source = Collectors
                    },
                    new BankOverflowStatus
                    {
                        Available = true, Resource = BankResource.Wood,
                        ResourceName = "Wood", ContainerName = "Lumberyard",
                        Requested = 28736, Granted = 0, Lost = 28736,
                        Current = 26000, Max = 26000, Source = Silo
                    },
                    new BankOverflowStatus
                    {
                        Available = true, Resource = BankResource.Iron,
                        ResourceName = "Iron", ContainerName = "Foundry",
                        Requested = 15808, Granted = 0, Lost = 15808,
                        Current = 10000, Max = 10000, Source = Silo
                    },
                    new BankOverflowStatus
                    {
                        Available = true, Resource = BankResource.Food,
                        ResourceName = "Stone", ContainerName = "Stoneyard",
                        Requested = 14325, Granted = 0, Lost = 14325,
                        Current = 3000, Max = 3000, Source = Silo
                    }
                };
                InvokePrivate(panel, "Open", rows);
                var modal = GetPrivateFieldValue(panel, "_modal") as ElarionUiKit.ObsidianModal;
                canvas = modal != null ? modal.canvas : null;
                if (canvas != null && RenderCanvasToPng(canvas,
                    OutDir + "HarvestOverflow_" + target.Tag + ".png", target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] HarvestOverflow @" + target.Tag + " threw: " + e);
            }
            finally
            {
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                PanelManager.CloseAll();
            }
            return saved;
        }

        private static int CaptureBuiltModal<T>(string fileStem, CaptureTarget target, string buildMethod)
            where T : MonoBehaviour
        {
            GameObject host = null;
            GameObject canvas = null;
            try
            {
                host = new GameObject("~UICap" + fileStem);
                var panel = host.AddComponent<T>();
                InvokePrivate(panel, buildMethod);
                var modal = GetPrivateFieldValue(panel, "_modal") as ElarionUiKit.ObsidianModal;
                canvas = modal != null ? modal.canvas : null;
                return canvas != null && RenderCanvasToPng(canvas,
                    OutDir + fileStem + "_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] " + fileStem + " @" + target.Tag + " threw: " + e);
                return 0;
            }
            finally
            {
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                PanelManager.CloseAll();
            }
        }

        private static int CaptureWelcomeBackOnce(CaptureTarget target)
        {
            WelcomeBackPopup popup = null;
            GameObject canvas = null;
            try
            {
                var result = new OfflineHarvestResult
                {
                    AwaySeconds = 9.5 * 3600.0,
                    WasCapped = true,
                    AetherCrystals = 240,
                    Food = 610,
                    Iron = 480,
                    Wood = 920,
                    Mend = new EchoMendReport
                    {
                        Repairs = 2,
                        HealthFraction = 0.35f,
                        SpentWood = 120,
                        SpentIron = 80,
                        StalledResource = "Wood"
                    }
                };
                WelcomeBackPopup.Show(result);
                popup = UnityEngine.Object.FindAnyObjectByType<WelcomeBackPopup>();
                var modal = popup != null
                    ? GetPrivateFieldValue(popup, "_modal") as ElarionUiKit.ObsidianModal : null;
                canvas = modal != null ? modal.canvas : null;
                return canvas != null && RenderCanvasToPng(canvas,
                    OutDir + "WelcomeBack_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            finally
            {
                if (popup != null) InvokePrivate(popup, "Dismiss");
                else if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>
        /// WO-1408 -- the away report WITH its next doors: a finished job (Manage), a recorded
        /// attack (Defence Report) and a ready army (RAID beside COLLECT).
        /// <para>The posture rail is driven directly because the doors read LIVE signals at build
        /// time, not claim time -- the reveal is deferred until a hub scene is active, so a claim
        /// -time snapshot would be a boot default. Driving the real setters is what makes this a
        /// capture of the production decision rather than of a replica.</para>
        /// </summary>
        private static int CaptureWelcomeBackDoorsOnce(CaptureTarget target)
        {
            WelcomeBackPopup popup = null;
            GameObject canvas = null;
            // The posture rail is STATIC and every later capture in this batchmode run reads it.
            // Snapshot before, restore in the finally -- an un-restored "army 3/10, Heartfire 3/3"
            // would silently re-pose every screen captured after this one.
            bool wasRaidCapable = DeNelle.Core.HudModel.PostureSignals.RaidCapable;
            var wasRaidLock = DeNelle.Core.HudModel.PostureSignals.RaidLock;
            int wasArmyUsed = DeNelle.Core.HudModel.PostureSignals.ArmyFillUsed;
            int wasArmyCap = DeNelle.Core.HudModel.PostureSignals.ArmyFillCap;
            int wasHfLit = DeNelle.Core.HudModel.PostureSignals.HeartfireLit;
            int wasHfMax = DeNelle.Core.HudModel.PostureSignals.HeartfireMax;
            double wasHfNext = DeNelle.Core.HudModel.PostureSignals.HeartfireSecondsToNext;
            try
            {
                DeNelle.Core.HudModel.PostureSignals.SetRaidCapable(true);
                DeNelle.Core.HudModel.PostureSignals.SetArmyFill(3, 10);
                DeNelle.Core.HudModel.PostureSignals.SetHeartfire(3, 3, 0.0);

                var result = new OfflineHarvestResult
                {
                    AwaySeconds = 6.0 * 3600.0 + 12.0 * 60.0,
                    Wood = 1200,
                    Iron = 340,
                    AetherCrystals = 12,
                    AttackCount = 1,
                    AttackBreachName = "North Gate",
                    AttackOutcomeWord = "BREACHED",
                };
                result.CompletedJobs.Add(new OfflineHarvestResult.OfflineJobLine
                { Verb = "TRAIN", Label = "Footman x1", FinishedUnixMs = 1_000.0 });
                result.CompletedJobs.Add(new OfflineHarvestResult.OfflineJobLine
                { Verb = "UPGRADE", Label = "Arcane Spire L2", FinishedUnixMs = 2_000.0 });

                WelcomeBackPopup.Show(result);
                popup = UnityEngine.Object.FindAnyObjectByType<WelcomeBackPopup>();
                var modal = popup != null
                    ? GetPrivateFieldValue(popup, "_modal") as ElarionUiKit.ObsidianModal : null;
                canvas = modal != null ? modal.canvas : null;
                return canvas != null && RenderCanvasToPng(canvas,
                    OutDir + "WelcomeBackDoors_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            finally
            {
                if (popup != null) InvokePrivate(popup, "Dismiss");
                else if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                DeNelle.Core.HudModel.PostureSignals.SetRaidCapable(wasRaidCapable, wasRaidLock);
                DeNelle.Core.HudModel.PostureSignals.SetArmyFill(wasArmyUsed, wasArmyCap);
                DeNelle.Core.HudModel.PostureSignals.SetHeartfire(wasHfLit, wasHfMax, wasHfNext);
            }
        }

        /// <summary>Focused three-ratio proof for the player-facing title and login gate.</summary>
        public static void RunFrontDoorCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _loginCaptureStem = "Login";
            int count = ForEachTarget("Title", CaptureTitleOnce) +
                        ForEachTarget("Login", CaptureLoginOnce);
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
                                   // WO-1644: this path was the odd one out -- Title/Login were
                                   // measured by AuditGeometry and the verdict thrown away.
            if (count == 6) Debug.Log("FRONT_DOOR_CAPTURE_OK 6/6");
            else Debug.LogError("FRONT_DOOR_CAPTURE_FAIL " + count + "/6");
        }

        /// <summary>
        /// Builds the production LoginPanelController through its editor-only
        /// presentation seam with GOOGLE_PLAY wording. This is visual evidence,
        /// not a substitute for scanning the eventual AAB.
        /// </summary>
        public static void RunGooglePlayLoginCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = 0;
            _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _screenStuckBuilds = 0;
            _screenStuckAt = null;
            _geoMoveProof = null;
            _geoMoveFailure = null;
            _geoFailures.Clear();
            _geoCanvasesChecked = 0;
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _touchPanelsChecked = 0;
            _touchPanelsClean = 0;
            ProveGeometryMoves();

            Type loginType = ResolveType("DeNelle.Onboarding.LoginPanelController");
            PropertyInfo overrideProperty = loginType?.GetProperty(
                "EditorGooglePlayPresentationOverride",
                BindingFlags.Public | BindingFlags.Static);
            if (overrideProperty == null)
            {
                Debug.LogError("GOOGLE_PLAY_LOGIN_CAPTURE_FAIL 0/3; production presentation seam missing");
                return;
            }

            int count = 0;
            _loginCaptureStem = "GooglePlayLogin";
            try
            {
                overrideProperty.SetValue(null, (bool?)true);
                count = ForEachTarget("GooglePlayLogin", CaptureLoginOnce);
            }
            finally
            {
                overrideProperty.SetValue(null, null);
                _loginCaptureStem = "Login";
            }

            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            bool clean = count == LandscapeTargets.Length
                         && _fidelityDegraded == 0
                         && _geoMoveProof != null
                         && _geoMoveFailure == null
                         && _geoCanvasesChecked == LandscapeTargets.Length
                         && _geoFailures.Count == 0
                         && _touchPanelsChecked == LandscapeTargets.Length
                         && _touchPanelsClean == _touchPanelsChecked
                         && _touchFailures.Count == 0;
            if (clean)
                Debug.Log("GOOGLE_PLAY_LOGIN_CAPTURE_OK 3/3; fidelity=clean; geometry=clean; touch=clean");
            else
                Debug.LogError("GOOGLE_PLAY_LOGIN_CAPTURE_FAIL " + count + "/3; fidelityDegraded=" +
                               _fidelityDegraded + "; geometryFailures=" + _geoFailures.Count +
                               "; touchFailures=" + _touchFailures.Count);
        }

        /// <summary>Focused profile/hero-selection proof after front-door migration.</summary>
        public static void RunHeroSelectCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureHeroSelect();
            if (count == 3) Debug.Log("HERO_SELECT_CAPTURE_OK 3/3");
            else Debug.LogError("HERO_SELECT_CAPTURE_FAIL " + count + "/3");
        }

        private static int CaptureTitleOnce(CaptureTarget target)
        {
            GameObject host = null, canvas = null;
            try
            {
                host = new GameObject("~UICapTitle");
                var type = ResolveType("DeNelle.Onboarding.TitleController");
                if (type == null) return 0;
                var controller = host.AddComponent(type) as MonoBehaviour;
                InvokePrivate(controller, "BuildTitleMenu");
                canvas = GetPrivateGameObject(controller, "_canvas");
                if (canvas == null) return 0;
                Canvas.ForceUpdateCanvases();
                return RenderCanvasToPng(canvas,
                    OutDir + "Title_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] Title capture threw: " + e);
                return 0;
            }
            finally
            {
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static string _loginCaptureStem = "Login";

        private static int CaptureLoginOnce(CaptureTarget target)
        {
            GameObject host = null, canvas = null;
            try
            {
                PanelManager.CloseAll();
                host = new GameObject("~UICapLogin");
                var type = ResolveType("DeNelle.Onboarding.LoginPanelController");
                if (type == null) return 0;
                var controller = host.AddComponent(type) as MonoBehaviour;
                InvokePrivate(controller, "Build");
                canvas = GetPrivateGameObject(controller, "_canvas");
                if (canvas == null) return 0;
                Canvas.ForceUpdateCanvases();
                return RenderCanvasToPng(canvas,
                    OutDir + _loginCaptureStem + "_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] Login capture threw: " + e);
                return 0;
            }
            finally
            {
                PanelManager.CloseAll();
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static int CaptureLoadingOverlayOnce(CaptureTarget target)
        {
            int saved = 0;
            LoadingOverlay normal = null, barrier = null;
            try
            {
                var normalGo = new GameObject("~UICapLoadingOverlay");
                normal = normalGo.AddComponent<LoadingOverlay>();
                typeof(LoadingOverlay).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(normal, new object[] { "Preparing your realm...", null });
                var barFill = GetPrivateFieldValue(normal, "_barFill") as RectTransform;
                if (barFill != null) barFill.anchorMax = new Vector2(0.62f, 1f);
                if (normal != null && RenderCanvasToPng(normal.gameObject,
                    OutDir + "LoadingOverlay_" + target.Tag + ".png", target.W, target.H)) saved++;
                if (normal != null) UnityEngine.Object.DestroyImmediate(normal.gameObject);
                normal = null;

                var barrierGo = new GameObject("~UICapConnectionRequired");
                barrier = barrierGo.AddComponent<LoadingOverlay>();
                typeof(LoadingOverlay).GetField("_connectionRequired",
                    BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(barrier, true);
                typeof(LoadingOverlay).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(barrier, new object[] {
                        "Internet is needed once to prepare game content. Your connection may still be working for other apps.",
                        "RETRY CONNECTION" });
                if (barrier != null && RenderCanvasToPng(barrier.gameObject,
                    OutDir + "ConnectionRequired_" + target.Tag + ".png", target.W, target.H)) saved++;
                return saved;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] LoadingOverlay capture threw: " + e);
                return saved;
            }
            finally
            {
                if (normal != null) UnityEngine.Object.DestroyImmediate(normal.gameObject);
                if (barrier != null) UnityEngine.Object.DestroyImmediate(barrier.gameObject);
            }
        }

        private static int CaptureJewelPolishConfirmOnce(CaptureTarget target)
        {
            try
            {
                PanelManager.CloseAll();
                bool opened = JewelPolishConfirmPanel.Show(
                    DungeonExclusiveItems.EmberCrystalId, () => { });
                if (!opened) return 0;
                var canvasField = typeof(JewelPolishConfirmPanel).GetField("s_canvas",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var canvas = canvasField != null ? canvasField.GetValue(null) as GameObject : null;
                if (canvas == null) return 0;
                Canvas.ForceUpdateCanvases();
                return RenderCanvasToPng(canvas,
                    OutDir + "JewelPolishConfirm_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] JewelPolishConfirm capture threw: " + e);
                return 0;
            }
            finally
            {
                typeof(JewelPolishConfirmPanel).GetMethod("CloseCancelled",
                    BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
                PanelManager.CloseAll();
            }
        }

        private static int CaptureBuildPreviewOnce(CaptureTarget target)
        {
            GameObject host = null;
            try
            {
                typeof(CatalogBootstrap).GetMethod("Register",
                    BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
                CatalogEntry entry = null;
                var all = CatalogRegistry.All();
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && !string.IsNullOrEmpty(all[i].displayName))
                    { entry = all[i]; break; }
                entry ??= new CatalogEntry { id = "capture-tower", displayName = "Watchtower" };

                host = new GameObject("~UICapBuildPreview");
                var modal = host.AddComponent<BuildPreviewModal>();
                modal.Show(entry, _ => { }, () => { });
                var previewCamera = GetPrivateFieldValue(modal, "_previewCam") as Camera;
                if (previewCamera != null) previewCamera.Render();
                Canvas.ForceUpdateCanvases();
                return RenderCanvasToPng(host,
                    OutDir + "BuildPreview_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] BuildPreview capture threw: " + e);
                return 0;
            }
            finally
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>Focused three-ratio proof for the shared Realm/Hero/Journey card deck.</summary>
        public static void RunPlayerDeckCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CapturePlayerDecks();
            if (count == 9) Debug.Log("PLAYER_DECK_CAPTURE_OK 9/9");
            else Debug.LogError("PLAYER_DECK_CAPTURE_FAIL " + count + "/9");
        }

        /// <summary>Focused three-ratio proof for the Journey raid camp selector.</summary>
        public static void RunRaidSelectionCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureRaidSelection();
            if (count == 3) Debug.Log("RAID_SELECTION_CAPTURE_OK 3/3");
            else Debug.LogError("RAID_SELECTION_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused two-page, three-ratio proof for the Journey quest board.</summary>
        public static void RunRumorBoardCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureRumorBoard();
            if (count == 6) Debug.Log("RUMOR_BOARD_CAPTURE_OK 6/6");
            else Debug.LogError("RUMOR_BOARD_CAPTURE_FAIL " + count + "/6");
        }

        /// <summary>Focused three-ratio proof for both reachable Echo surfaces.</summary>
        public static void RunEchoFamilyCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureEchoRosterPanel() + CaptureEchoCard();
            if (count == 6) Debug.Log("ECHO_FAMILY_CAPTURE_OK 6/6");
            else Debug.LogError("ECHO_FAMILY_CAPTURE_FAIL " + count + "/6");
        }

        /// <summary>Focused three-ratio proof for the one-action Echo unlock acknowledgement.</summary>
        public static void RunEchoUnlockCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureFoundingEchoCard();
            if (count == 3) Debug.Log("ECHO_UNLOCK_CAPTURE_OK 3/3");
            else Debug.LogError("ECHO_UNLOCK_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused three-ratio proof for the persistent Echo HUD chip.</summary>
        public static void RunEchoChipCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureEchoRoster();
            if (count == 3) Debug.Log("ECHO_CHIP_CAPTURE_OK 3/3");
            else Debug.LogError("ECHO_CHIP_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused three-ratio proof for the one FTUE skip affordance.</summary>
        public static void RunTutorialSkipCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = ForEachTarget("TutorialSkip", CaptureTutorialSkipOnce);
            if (count == 3) Debug.Log("TUTORIAL_SKIP_CAPTURE_OK 3/3");
            else Debug.LogError("TUTORIAL_SKIP_CAPTURE_FAIL " + count + "/3");
        }

        /// <summary>Focused three-ratio proof for the public Bag workspace.</summary>
        public static void RunBagCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureBag();
            if (count == 3) Debug.Log("BAG_CAPTURE_OK 3/3");
            else Debug.LogError("BAG_CAPTURE_FAIL " + count + "/3");
        }

        // RETIRED 2026-09-06 (WO-1430): RunRealmGoldStoreCaptureHeadless and
        // RunRealmGoldStorePurchaseCaptureHeadless photographed the legacy ShopPanel, which was
        // DELETED as doorless in that change (PanelDoorRegression [panel-door-is-harness-only]: only
        // this file and AutoPilotDriver ever constructed it, and a harness that AddComponents a panel
        // so it can be photographed is not a door). The gold-currency storefront the player really
        // opens is PanelId.PartyShop -> PartyShopPanelMvvm; its proof is
        // RunPartyShopPopulatedCaptureHeadless below.
        // ⚠ COVERAGE LOST, recorded rather than hidden: RealmStorePurchase was the ONLY headless
        // assertion that a buy moves gold by exactly the total price and inventory by exactly the
        // quantity. PartyShopVM exposes no Quantity/TotalPrice, so it could not be re-pointed; an
        // equivalent PartyShop purchase proof is a follow-up, not a silent deletion.

        /// <summary>Focused three-ratio proof for Hero Skills/Talents.</summary>
        public static void RunHeroSkillTreeCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            int count = CaptureHeroSkillTree() +
                ForEachTarget("HeroSkillTreeLv2", CaptureHeroSkillTreeLv2Once) +
                ForEachTarget("HeroSkillTreeStates", CaptureHeroSkillTreeStatesOnce);
            // WO-1601: 12, not 9 - the Lv2 one-point board joins the proof. The owner's defect
            // frame (Screenshot_20260907-132616.png) is a BRAND NEW hero, and that is the tree at
            // its SMALLEST population; the 999-Wisdom states fixture below could never photograph
            // it, so the clipped-edge board had no headless evidence at all.
            if (count == 12) Debug.Log("HERO_SKILL_TREE_CAPTURE_OK 12/12");
            else Debug.LogError("HERO_SKILL_TREE_CAPTURE_FAIL " + count + "/12");
        }

        /// <summary>
        /// WO-1601 - THE LV2, ONE-POINT BOARD: the tree exactly as a new hero meets it.
        ///
        /// The owner's defect frame is a fresh hero ("WISDOM 2 - next point at Level 3"), and at
        /// that population the calm frontier is a handful of seats spread over the full lane
        /// axis - which is precisely the board whose outer medallions the mask sliced, and the
        /// board the graph-well bezel painted a band across. Every existing Skills capture opens
        /// on either the ambient save or a 999-Wisdom fixture, so neither could see it.
        ///
        /// Reversible, exactly like the states fixture: the talent PlayerPref is cleared and
        /// restored, the Wisdom service is a temporary object, and a live service means SKIP
        /// rather than clobber.
        /// </summary>
        private static int CaptureHeroSkillTreeLv2Once(CaptureTarget target)
        {
            const string pref = DeNelle.Core.State.GameStateService.TalentPrefKey;
            bool hadPref = PlayerPrefs.HasKey(pref);
            string oldPref = hadPref ? PlayerPrefs.GetString(pref) : null;
            GameObject eventSystem = null, serviceGo = null, heroGo = null, hostGo = null, canvasGo = null;
            HeroSkillTreePanelMvvm panel = null;
            int saved = 0;
            try
            {
                if (WisdomCurrencyService.Instance != null)
                {
                    Debug.LogWarning("[UICap-HL] live Wisdom service present; Lv2 skill-tree fixture skipped.");
                    return 0;
                }
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    eventSystem = new GameObject("~UICapEventSystem");
                    eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }
                PlayerPrefs.DeleteKey(pref);            // nothing unlocked - a brand new hero
                serviceGo = new GameObject("~UICapWisdomLv2");
                var wisdom = serviceGo.AddComponent<WisdomCurrencyService>();
                wisdom.ResetForNewGame();
                wisdom.Grant(2);                        // the owner's frame reads WISDOM 2

                heroGo = new GameObject("~UICapSkillHeroLv2");
                heroGo.tag = "Player";
                heroGo.AddComponent<AssignableSkillBar>();

                hostGo = new GameObject("~UICapHeroSkillTreeLv2");
                panel = hostGo.AddComponent<HeroSkillTreePanelMvvm>();
                panel.Open();
                canvasGo = GetPrivateGameObject(panel, "_ui");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] HeroSkillTreePanelMvvm._ui null after Lv2 Open -- skipped.");
                    return 0;
                }
                if (RenderCanvasToPng(canvasGo,
                    OutDir + "HeroSkillTree_Lv2_" + target.Tag + ".png", target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] hero skill tree Lv2 capture threw: " + e);
            }
            finally
            {
                if (panel != null)
                {
                    var handle = GetPrivateFieldValue(panel, "_panelHandle") as PanelHandle;
                    if (handle != null) PanelManager.NotifyClosed(handle);
                }
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (heroGo != null) UnityEngine.Object.DestroyImmediate(heroGo);
                if (serviceGo != null) UnityEngine.Object.DestroyImmediate(serviceGo);
                if (hadPref) PlayerPrefs.SetString(pref, oldPref); else PlayerPrefs.DeleteKey(pref);
                PlayerPrefs.Save();
                if (eventSystem != null) UnityEngine.Object.DestroyImmediate(eventSystem);
            }
            return saved;
        }

        private static int CaptureHeroSkillTreeStatesOnce(CaptureTarget target)
        {
            const string pref = DeNelle.Core.State.GameStateService.TalentPrefKey;
            string skillPref = AssignableSkillBar.PrefsKeyFor(AbilityCatalog.DefaultClass);
            bool hadPref = PlayerPrefs.HasKey(pref);
            string oldPref = hadPref ? PlayerPrefs.GetString(pref) : null;
            bool hadSkillPref = PlayerPrefs.HasKey(skillPref);
            string oldSkillPref = hadSkillPref ? PlayerPrefs.GetString(skillPref) : null;
            GameObject eventSystem = null, serviceGo = null, heroGo = null, hostGo = null, canvasGo = null;
            HeroSkillTreePanelMvvm panel = null;
            int saved = 0;
            try
            {
                if (WisdomCurrencyService.Instance != null)
                {
                    Debug.LogWarning("[UICap-HL] live Wisdom service present; reversible skill-state fixture skipped.");
                    return 0;
                }
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    eventSystem = new GameObject("~UICapEventSystem");
                    eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }
                PlayerPrefs.DeleteKey(pref);
                serviceGo = new GameObject("~UICapWisdom");
                var wisdom = serviceGo.AddComponent<WisdomCurrencyService>();
                wisdom.ResetForNewGame();
                wisdom.Grant(999);

                heroGo = new GameObject("~UICapSkillHero");
                heroGo.tag = "Player";
                heroGo.AddComponent<AssignableSkillBar>();

                hostGo = new GameObject("~UICapHeroSkillStates");
                panel = hostGo.AddComponent<HeroSkillTreePanelMvvm>();
                panel.Open();
                canvasGo = GetPrivateGameObject(panel, "_ui");
                var vm = GetPrivateFieldValue(panel, "_vm") as HeroSkillTreeVM;
                if (canvasGo == null || vm == null) return 0;

                SkillNodeVM? live = null;
                foreach (var track in vm.Tracks)
                    foreach (var seat in track.Nodes)
                        if (seat.State == SkillNodeState.Next || seat.State == SkillNodeState.Available ||
                            seat.State == SkillNodeState.Inert) { live = seat.Node; break; }
                if (!live.HasValue)
                    foreach (var track in vm.Tracks)
                        if (track.Nodes.Count > 0) { live = track.Nodes[0].Node; break; }
                if (!live.HasValue) return 0;

                vm.Select(live.Value.Id);
                if (RenderCanvasToPng(canvasGo,
                    OutDir + "HeroSkillTree_Popup_" + target.Tag + ".png", target.W, target.H)) saved++;

                // Prefer an honestly owned active. If this catalog has no live reachable active,
                // populate the class-valid bar directly to exercise the slot presentation only;
                // the popup capture above remains truthful about the talent's actual state.
                SkillNodeVM? active = null;
                for (int pass = 0; pass < 8 && !active.HasValue; pass++)
                {
                    bool advanced = false;
                    foreach (var node in vm.Nodes)
                    {
                        if (node.Owned && node.Kind == SkillNodeKind.Skill && node.EffectLive &&
                            !string.IsNullOrEmpty(node.AbilityId)) { active = node; break; }
                        if (node.CanUnlock && node.EffectLive && wisdom.Unlock(node.Id)) advanced = true;
                    }
                    if (!advanced && !active.HasValue) break;
                }
                if (!active.HasValue)
                    foreach (var node in vm.Nodes)
                        if (node.Kind == SkillNodeKind.Skill && !string.IsNullOrEmpty(node.AbilityId) &&
                            AbilityCatalog.FindById(node.AbilityId) != null &&
                            AbilityCatalog.IsUsableByClass(node.AbilityId, AbilityCatalog.DefaultClass))
                        { active = node; break; }
                string assignedId = active.HasValue ? active.Value.AbilityId : null;
                if (string.IsNullOrEmpty(assignedId))
                {
                    var defaults = AbilityCatalog.GetLoadout(AbilityCatalog.DefaultClass);
                    if (defaults.Count > 0) assignedId = defaults[0].Id;
                }
                if (!string.IsNullOrEmpty(assignedId))
                {
                    bool assigned = AssignableSkillBarAccess.Assign(0, assignedId);
                    if (!assigned)
                        Debug.LogWarning("[UICap-HL] could not populate assigned skill-state fixture: " +
                                         assignedId);
                }
                vm.ClearSelection();
                if (RenderCanvasToPng(canvasGo,
                    OutDir + "HeroSkillTree_Assigned_" + target.Tag + ".png", target.W, target.H)) saved++;
            }
            catch (Exception e) { Debug.LogError("[UICap-HL] skill state capture threw: " + e); }
            finally
            {
                if (panel != null)
                {
                    var handle = GetPrivateFieldValue(panel, "_panelHandle") as PanelHandle;
                    if (handle != null) PanelManager.NotifyClosed(handle);
                }
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (heroGo != null) UnityEngine.Object.DestroyImmediate(heroGo);
                if (serviceGo != null) UnityEngine.Object.DestroyImmediate(serviceGo);
                if (hadPref) PlayerPrefs.SetString(pref, oldPref); else PlayerPrefs.DeleteKey(pref);
                if (hadSkillPref) PlayerPrefs.SetString(skillPref, oldSkillPref);
                else PlayerPrefs.DeleteKey(skillPref);
                PlayerPrefs.Save();
                if (eventSystem != null) UnityEngine.Object.DestroyImmediate(eventSystem);
            }
            return saved;
        }

        /// <summary>
        /// Populated Forge proof for the party equipment storefront. Unlike the generic
        /// secondary-route inventory this creates a real player/economy/inventory context and
        /// selects a real catalog row, so the party rail, merchandise, preview, price and actions
        /// must all render their exercised state.
        /// </summary>
        public static void RunPartyShopPopulatedCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;

            int frames = ForEachTarget("PartyShopPopulated", CapturePartyShopPopulatedOnce);
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (frames == 3 && _geoFailures.Count == 0 && _touchFailures.Count == 0)
                Debug.Log("PARTY_SHOP_POPULATED_CAPTURE_OK 3/3 frames; real Forge stock selected; touch=clean");
            else
                Debug.LogError("PARTY_SHOP_POPULATED_CAPTURE_FAIL frames=" + frames + "/3 geometry=" +
                    _geoFailures.Count + " touch=" + _touchFailures.Count);
        }

        private static int CapturePartyShopPopulatedOnce(CaptureTarget target)
        {
            GameStateService priorState = GameStateService.Instance;
            VillageInventory priorInventory = VillageInventory.Instance;
            EconomyService priorEconomy = EconomyService.Instance;
            GameObject stateHost = null, inventoryHost = null, economyHost = null, hero = null, panelHost = null, canvas = null;
            GameState fixture = null;
            PartyShopPanelMvvm panel = null;
            try
            {
                PanelManager.CloseAll();
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.GearInventory = new Dictionary<string, int>();
                var wallet = fixture.Resources;
                wallet.Coins = 500;
                fixture.Resources = wallet;
                stateHost = new GameObject("~UICapPartyShopState");
                if (!InstallCaptureState(stateHost.AddComponent<GameStateService>(), fixture))
                    throw new InvalidOperationException("GameStateService capture seam unavailable");
                inventoryHost = new GameObject("~UICapPartyShopInventory");
                InstallCaptureVillageInventory(inventoryHost.AddComponent<VillageInventory>());
                economyHost = new GameObject("~UICapPartyShopEconomy");
                InstallCaptureEconomy(economyHost.AddComponent<EconomyService>());

                hero = new GameObject("~UICapPartyShopHero");
                hero.tag = "Player";
                hero.AddComponent<GearLoadout>();
                hero.AddComponent<HeroAbilities>().SetHeroClass("knight");

                panelHost = new GameObject("~UICapPartyShopPanel");
                panel = panelHost.AddComponent<PartyShopPanelMvvm>();
                panel.Open("forge", "THE FORGE");
                var vm = GetPrivateFieldValue(panel, "_vm") as PartyShopVM;
                if (vm == null || vm.Items == null || vm.Items.Count == 0)
                    throw new InvalidOperationException("Forge built without populated production stock");
                vm.Select(vm.Items[0].Id);
                canvas = GetPrivateGameObject(panel, "_ui");
                if (canvas == null) throw new InvalidOperationException("Party Shop built no public canvas");
                Canvas.ForceUpdateCanvases();
                return RenderCanvasToPng(canvas, OutDir + "PartyShopPopulated_" + target.Tag + ".png",
                    target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-PartyShop] populated capture threw: " + e);
                return 0;
            }
            finally
            {
                try { if (panel != null) InvokePrivate(panel, "Close"); } catch { }
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (panelHost != null) UnityEngine.Object.DestroyImmediate(panelHost);
                if (hero != null) UnityEngine.Object.DestroyImmediate(hero);
                RestoreCaptureEconomy(priorEconomy);
                if (economyHost != null) UnityEngine.Object.DestroyImmediate(economyHost);
                RestoreCaptureVillageInventory(priorInventory);
                if (inventoryHost != null) UnityEngine.Object.DestroyImmediate(inventoryHost);
                RestoreCaptureState(priorState);
                if (stateHost != null) UnityEngine.Object.DestroyImmediate(stateHost);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                PanelManager.CloseAll();
            }
        }

        /// <summary>Focused current-state proof for the approved Night Market handoff.</summary>
        public static void RunNightMarketCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = 0;
            _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _screenStuckBuilds = 0;
            _screenStuckAt = null;
            _geoMoveProof = null;
            _geoMoveFailure = null;
            _geoFailures.Clear();
            _geoCanvasesChecked = 0;
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _touchPanelsChecked = 0;
            _touchPanelsClean = 0;
            ProveGeometryMoves();
            int count = CaptureNightMarketStore();
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            bool clean = count == NightMarketTargets.Length
                         && _fidelityDegraded == 0
                         && _geoMoveProof != null
                         && _geoMoveFailure == null
                         && _geoCanvasesChecked == NightMarketTargets.Length
                         && _geoFailures.Count == 0
                         && _touchPanelsChecked == NightMarketTargets.Length
                         && _touchPanelsClean == _touchPanelsChecked
                         && _touchFailures.Count == 0;
            if (clean)
                Debug.Log("NIGHT_MARKET_CAPTURE_OK " + count + "/" + NightMarketTargets.Length +
                          "; fidelity=clean; geometry=clean; touch=clean");
            else
                Debug.LogError("NIGHT_MARKET_CAPTURE_FAIL " + count + "/" + NightMarketTargets.Length +
                               "; fidelityDegraded=" + _fidelityDegraded +
                               "; geometryFailures=" + _geoFailures.Count +
                               "; touchFailures=" + _touchFailures.Count);
        }

        private static int CaptureHeroEquipmentOnce(CaptureTarget target)
        {
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            GameObject heroFixture = null;
            GameObject inventoryFixture = null;
            EquipmentPanel panel = null;
            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }
                PanelManager.CloseAll();

                // Truthful representative state: the production panel discovers its target from
                // the Player-tagged hero and the VM unions currently equipped gear into owned
                // inventory. This exercises identity, vitals, a filled slot, item selection, and
                // the contextual REMOVE state without hard-coding any display values in the View.
                heroFixture = new GameObject("Sylas Swift");
                heroFixture.tag = "Player";
                var loadout = heroFixture.AddComponent<GearLoadout>();
                var abilities = heroFixture.AddComponent<HeroAbilities>();
                abilities.SetHeroClass("ranger");
                heroFixture.AddComponent<HeroHealth>();
                heroFixture.AddComponent<HeroProgression>();
                var weapon = GearCatalog.BestWeapon("ranger", 2);
                var armor = GearCatalog.BestArmor("ranger", 2);
                if (weapon != null) loadout.EquipWeaponById(weapon.id);
                if (armor != null) loadout.EquipArmorById(armor.id);
                inventoryFixture = new GameObject("~UICapVillageInventory");
                var inventory = inventoryFixture.AddComponent<DeNelle.Village.Crafting.VillageInventory>();
                // Ordinary MonoBehaviour Awake is not guaranteed for a non-ExecuteAlways component
                // in this synchronous edit-mode harness. Invoke it so VillageInventory.Instance is
                // the exact production singleton EquipVM.CreateDefault resolves.
                InvokePrivate(inventory, "Awake");
                var candidate = GearCatalog.FindWeapon("ranger_arrow_fire");
                if (candidate != null && candidate.id != weapon?.id) inventory.Add(candidate.id, 1);

                hostGo = new GameObject("~UICapHeroEquipment");
                panel = hostGo.AddComponent<EquipmentPanel>();
                panel.Open();
                canvasGo = GetPrivateGameObject(panel, "_ui");
                if (canvasGo == null) return 0;

                int saved = 0;
                if (RenderCanvasToPng(canvasGo,
                    OutDir + "HeroEquipment_Compare_" + target.Tag + ".png", target.W, target.H)) saved++;

                // The approved contract has two distinct contextual-action states. Select the
                // item that is actually equipped through EquipVM (not by mutating view text), then
                // capture REMOVE as independent evidence beside the candidate/EQUIP comparison.
                object vm = GetPrivateFieldValue(panel, "_vm");
                MethodInfo selectItem = vm?.GetType().GetMethod("SelectItem",
                    BindingFlags.Public | BindingFlags.Instance);
                if (selectItem != null && weapon != null)
                {
                    selectItem.Invoke(vm, new object[] { weapon.id });
                    Canvas.ForceUpdateCanvases();
                    if (RenderCanvasToPng(canvasGo,
                        OutDir + "HeroEquipment_Equipped_" + target.Tag + ".png", target.W, target.H)) saved++;
                }
                return saved;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] hero equipment capture threw: " + e);
                return 0;
            }
            finally
            {
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (inventoryFixture != null) UnityEngine.Object.DestroyImmediate(inventoryFixture);
                if (heroFixture != null) UnityEngine.Object.DestroyImmediate(heroFixture);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
                PanelManager.CloseAll();
            }
        }

        private static int CaptureAdaptiveHudOnce(CaptureTarget target)
        {
            GameObject tempEventSystem = null;
            GameObject ownerGo = null;
            GameObject hostGo = null;
            object hudModelFixture = null;
            Type coreServicesType = null;
            try
            {
                Type ownerType = ResolveType("DeNelle.HUD.VillageHudController");
                Type kitType = ResolveType("DeNelle.HUD.Kit.HudKitController");
                Type postureType = ResolveType("DeNelle.HUD.Kit.HudPosture");
                if (ownerType == null || kitType == null || postureType == null) return 0;
                coreServicesType = ResolveType("DeNelle.Core.CoreServices");
                Type hudModelType = ResolveType("DeNelle.Core.HudModel.HudModel");
                if (coreServicesType != null && hudModelType != null)
                {
                    hudModelFixture = Activator.CreateInstance(hudModelType);
                    coreServicesType.GetMethod("RegisterHudModel", BindingFlags.Public | BindingFlags.Static)
                        ?.Invoke(null, new[] { hudModelFixture });
                }
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                ownerGo = new GameObject("~UICapAdaptiveHudOwner");
                Component owner = ownerGo.AddComponent(ownerType);
                var create = kitType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                Component kit = create != null ? create.Invoke(null, new object[] { owner }) as Component : null;
                if (kit == null) return 0;
                hostGo = kit.gameObject;

                object models = GetPrivateFieldValue(kit, "_models");
                object wave = models?.GetType().GetProperty("Wave",
                    BindingFlags.Public | BindingFlags.Instance)?.GetValue(models);
                MethodInfo setWave = wave?.GetType().GetMethod("Set", BindingFlags.Public | BindingFlags.Instance);
                Type wavePhaseType = wave?.GetType().GetProperty("Phase")?.PropertyType;
                object vitals = models?.GetType().GetProperty("HeroVitals",
                    BindingFlags.Public | BindingFlags.Instance)?.GetValue(models);
                vitals?.GetType().GetMethod("Set", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(vitals, new object[] { 92, 120, 38, 60, 240, 500, 2, "knight", 0,
                        38f, 60f, "Focus" });
                object economy = models?.GetType().GetProperty("Economy",
                    BindingFlags.Public | BindingFlags.Instance)?.GetValue(models);
                economy?.GetType().GetMethod("Set", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(economy, new object[] { 227, 480, 320, 260, 18 });
                object world = models?.GetType().GetProperty("World",
                    BindingFlags.Public | BindingFlags.Instance)?.GetValue(models);
                world?.GetType().GetMethod("SetMetrics", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(world, new object[] { 840, 1000, 0.84f, 4, 12, 6, 3.5f, 2, 0,
                        2, 4, "2 of 4 wards lit" });

                var apply = kitType.GetMethod("ApplyPosture", BindingFlags.NonPublic | BindingFlags.Instance);
                if (apply == null) return 0;

                int saved = 0;
                object peaceful = Enum.Parse(postureType, "CalmTown");
                if (setWave != null && wavePhaseType != null)
                    setWave.Invoke(wave, new object[] { Enum.Parse(wavePhaseType, "Countdown"),
                        2, 20, 849f, true, "", 18, 18, "" });
                kitType.GetMethod("SetStartWaveAvailable", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(kit, new object[] { true });
                apply.Invoke(kit, new[] { peaceful });
                // The edit-mode capture scene has no hub name, so the production scene gate
                // correctly hides HeartStatus. Reveal that already-bound widget here to capture
                // the approved hub state rather than treating a harness limitation as UI truth.
                var allHudTransforms = hostGo.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allHudTransforms.Length; i++)
                    if (allHudTransforms[i].name == "Widget_heartStatus")
                        allHudTransforms[i].gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                if (RenderCanvasToPng(hostGo,
                    OutDir + "AdaptiveHudPeaceful_" + target.Tag + ".png", target.W, target.H)) saved++;

                // The collapsed HUD shot cannot prove the gear drawer's material, row spacing,
                // or obsolete-route cleanup. Capture the same peaceful posture with the actual
                // shared SlideDock expanded so this player-facing state is a visual gate too.
                object slideDock = GetPrivateFieldValue(kit, "_slideDock");
                slideDock?.GetType().GetMethod("SetExpanded", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(slideDock, new object[] { true });
                Canvas.ForceUpdateCanvases();
                if (RenderCanvasToPng(hostGo,
                    OutDir + "AdaptiveHudGearOpen_" + target.Tag + ".png", target.W, target.H)) saved++;
                slideDock?.GetType().GetMethod("SetExpanded", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(slideDock, new object[] { false });

                object combat = Enum.Parse(postureType, "HostileActiveBattle");
                if (setWave != null && wavePhaseType != null)
                    setWave.Invoke(wave, new object[] { Enum.Parse(wavePhaseType, "Active"),
                        2, 20, 0f, false, "", 18, 18, "" });
                apply.Invoke(kit, new[] { combat });
                allHudTransforms = hostGo.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allHudTransforms.Length; i++)
                    if (allHudTransforms[i].name == "Widget_heartStatus")
                        allHudTransforms[i].gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                if (RenderCanvasToPng(hostGo,
                    OutDir + "AdaptiveHudCombat_" + target.Tag + ".png", target.W, target.H)) saved++;
                return saved;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] adaptive HUD capture threw: " + e);
                return 0;
            }
            finally
            {
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (ownerGo != null) UnityEngine.Object.DestroyImmediate(ownerGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
                if (coreServicesType != null && hudModelFixture != null)
                    coreServicesType.GetMethod("UnregisterHudModel", BindingFlags.Public | BindingFlags.Static)
                        ?.Invoke(null, new[] { hudModelFixture });
            }
        }

        private static int CaptureSettingsOnce(CaptureTarget target)
        {
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            Component controller = null;
            try
            {
                Type controllerType = ResolveType("DeNelle.Settings.SettingsController");
                if (controllerType == null) return 0;
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapSettings");
                controller = hostGo.AddComponent(controllerType);
                InvokePrivate(controller, "EnsureBuilt");
                object modal = GetPrivateFieldValue(controller, "_modal");
                canvasGo = modal != null ? GetFieldValue(modal, "canvas") as GameObject : null;
                if (canvasGo == null) return 0;
                canvasGo.SetActive(true);
                return RenderCanvasToPng(canvasGo,
                    OutDir + "Settings_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] Settings capture threw: " + e);
                return 0;
            }
            finally
            {
                if (controller != null) SetPrivateField(controller, "_modal", null);
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }
        }

        private static int CaptureCombatItemPickerOnce(CaptureTarget target)
        {
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            Component controller = null;
            try
            {
                Type controllerType = ResolveType("DeNelle.HUD.Kit.HudKitController");
                if (controllerType == null) return 0;
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapCombatItemPicker");
                controller = hostGo.AddComponent(controllerType);
                InvokePrivate(controller, "OpenItemPicker");
                object modal = GetPrivateFieldValue(controller, "_itemPicker");
                canvasGo = modal != null ? GetFieldValue(modal, "canvas") as GameObject : null;
                if (canvasGo == null) return 0;
                return RenderCanvasToPng(canvasGo,
                    OutDir + "CombatItemPicker_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] combat Item picker capture threw: " + e);
                return 0;
            }
            finally
            {
                if (controller != null) InvokePrivate(controller, "CloseItemPicker");
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }
        }

        private static int CapturePauseMenuOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject pauseGo = null;
            GameObject settingsGo = null;
            GameObject canvasGo = null;

            try
            {
                Type pauseType = ResolveType("DeNelle.Settings.PauseController");
                if (pauseType == null)
                {
                    Debug.LogWarning("[UICap-HL] PauseController type not found -- pause menu capture skipped.");
                    return 0;
                }

                // A pre-seeded EventSystem so kit UI construction never warns in edit mode.
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                pauseGo = new GameObject("~UICapPause");
                var pause = pauseGo.AddComponent(pauseType);

                // Attach a bare SettingsController so the Settings button (the 3-button worst case)
                // builds. Guarded: if it cannot attach, the menu still builds with Resume + Quit.
                Type settingsType = ResolveType("DeNelle.Settings.SettingsController");
                if (settingsType != null)
                {
                    try
                    {
                        settingsGo = new GameObject("~UICapSettings");
                        var settings = settingsGo.AddComponent(settingsType);
                        SetPrivateField(pause, "_settings", settings);
                    }
                    catch (Exception se)
                    {
                        Debug.LogWarning("[UICap-HL] could not attach SettingsController (2-button capture): " + se.Message);
                    }
                }

                // Build the REAL modal. EnsureBuilt is private; it constructs _modal and leaves the
                // canvas INACTIVE. Pause() is deliberately NOT called, so there is no Time.timeScale
                // freeze and no PanelManager.NotifyOpened side effect -- a pure, static build.
                InvokePrivate(pause, "EnsureBuilt");

                object modal = GetPrivateFieldValue(pause, "_modal");
                if (modal == null)
                {
                    Debug.LogWarning("[UICap-HL] PauseController._modal null after EnsureBuilt -- pause menu did not build; skipped.");
                    return 0;
                }
                canvasGo = GetFieldValue(modal, "canvas") as GameObject;
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] Pause modal canvas null -- pause menu build produced no canvas; skipped.");
                    return 0;
                }

                canvasGo.SetActive(true);   // EnsureBuilt builds it hidden; show it for the shot

                if (RenderCanvasToPng(canvasGo, OutDir + "PauseMenu_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] pause menu capture threw: " + e);
            }
            finally
            {
                // Destroy the canvas FIRST so PauseController.OnDestroy sees _modal.canvas == null and
                // never calls runtime Destroy() (illegal in edit mode -- same contract as the card).
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (settingsGo != null) UnityEngine.Object.DestroyImmediate(settingsGo);
                if (pauseGo != null) UnityEngine.Object.DestroyImmediate(pauseGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the persistent Echo pip + "Pets" HUD button overlay
        //  (EchoUnlockFeedback). WO 2026-07-24 moved this button from bottom-centre
        //  (ate centre real estate) to the RIGHT screen edge, vertically centred, at
        //  a >=112px square touch target -- this shot is the felt-verify that it now
        //  hugs the right edge and did not drift back to centre.
        //
        //  We capture the pip+button OVERLAY canvas, NOT the full EchoRosterView modal
        //  that the button opens: EchoRosterView.OpenPanel pulls heavy runtime deps
        //  (EchoRosterVM.CreateDefault over EchoService state, PanelManager.NotifyOpened
        //  side effects, a DontDestroyOnLoad host) that do not stand up cleanly in a
        //  synchronous edit-mode render. The pip+button canvas is precisely the chrome
        //  this WO repositioned, so it is the right thing to screenshot. (Per WO: do
        //  NOT force EchoRoster headlessly.)
        //
        //  EchoUnlockFeedback lives in DeNelle.Village (already referenced here). Its
        //  Start() does NOT run on AddComponent in edit mode, so we drive the same two
        //  private builders Start calls -- BuildPip (creates _pipCanvas) then
        //  BuildPetBoxButton (adds the right-edge Pets button onto it).
        // ---------------------------------------------------------------------
        private static int CaptureTutorialSkipOnce(CaptureTarget target)
        {
            TutorialSkipUi skip = null;
            try
            {
                TutorialSkipUi.Show(() => { });
                skip = UnityEngine.Object.FindAnyObjectByType<TutorialSkipUi>();
                if (skip == null) return 0;
                var group = skip.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
                return RenderCanvasToPng(skip.gameObject,
                    OutDir + "TutorialSkip_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] tutorial Skip capture threw: " + e);
                return 0;
            }
            finally
            {
                TutorialSkipUi.Hide();
                if (skip != null) UnityEngine.Object.DestroyImmediate(skip.gameObject);
            }
        }

        private static int CaptureEchoRoster()
        {
            return ForEachTarget("EchoPetButton", CaptureEchoRosterOnce);
        }

        private static int CaptureEchoRosterOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject pipCanvas = null;

            try
            {
                // Pre-seed an EventSystem so BuildPetBoxButton's EnsureEventSystem no-ops
                // (its DontDestroyOnLoad path warns harmlessly in edit mode; avoid the noise).
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapEchoFeedback");
                var feedback = hostGo.AddComponent<EchoUnlockFeedback>();

                // Build the pip, then the Pets button onto its canvas (the two private methods
                // Start would call). Both are guarded/font-safe kit construction -- no play mode.
                InvokePrivate(feedback, "BuildPip");
                InvokePrivate(feedback, "BuildPetBoxButton");

                pipCanvas = GetPrivateGameObject(feedback, "_pipCanvas");
                if (pipCanvas == null)
                {
                    Debug.LogWarning("[UICap-HL] EchoUnlockFeedback._pipCanvas null after BuildPip -- pip/Pets capture skipped.");
                    return 0;
                }

                pipCanvas.SetActive(true);   // scene-gate never ran (no Start); ensure it is visible

                if (RenderCanvasToPng(pipCanvas, OutDir + "EchoPetButton_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] echo pip/Pets button capture threw: " + e);
            }
            finally
            {
                // Edit-mode teardown MUST be DestroyImmediate (same contract as the card/pause shots).
                if (pipCanvas != null) UnityEngine.Object.DestroyImmediate(pipCanvas);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the Help/Settings modal (HelpMenu) -- WO-795 wave 2 wrapped its
        //  button column in a masked ScrollRect well so the rows can never spill
        //  into the kit's shared Close band. Editor compiles carry the
        //  DEVELOPMENT_BUILD/UNITY_EDITOR rows too (Dev Tools + the dev grant),
        //  so this shot renders the WORST-case row count the well must clip.
        //
        //  HelpMenu lives in the DeNelle.HUD assembly, which DeNelle.Editor does
        //  NOT reference, so every touch is via reflection (the CapturePauseMenu
        //  recipe). Awake never runs on an edit-mode AddComponent, so no
        //  PanelManager registration leaks from this capture.
        // ---------------------------------------------------------------------
        private static int CaptureHelpMenu()
        {
            return ForEachTarget("HelpMenu", CaptureHelpMenuOnce);
        }

        private static int CaptureHelpMenuOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;

            try
            {
                Type helpType = ResolveType("DeNelle.HUD.HelpMenu");
                if (helpType == null)
                {
                    Debug.LogWarning("[UICap-HL] HelpMenu skipped -- DeNelle.HUD.HelpMenu type not found.");
                    return 0;
                }

                // Pre-seed an EventSystem so kit UI construction never warns in edit mode.
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapHelpMenu");
                var help = hostGo.AddComponent(helpType);

                // Build the REAL modal. EnsureBuilt is private; it constructs _modal
                // (frame + scroll well + rows + toast) and leaves the canvas INACTIVE.
                // SetOpen/ToggleOverlay are deliberately NOT called -- no PanelManager
                // NotifyOpened side effect, a pure static build (pause-menu contract).
                InvokePrivate(help, "EnsureBuilt");

                object modal = GetPrivateFieldValue(help, "_modal");
                if (modal == null)
                {
                    Debug.LogWarning("[UICap-HL] HelpMenu._modal null after EnsureBuilt -- help menu skipped.");
                    return 0;
                }
                canvasGo = GetFieldValue(modal, "canvas") as GameObject;
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] HelpMenu modal canvas null -- help menu build produced no canvas; skipped.");
                    return 0;
                }

                canvasGo.SetActive(true);   // EnsureBuilt builds it hidden; show it for the shot

                if (RenderCanvasToPng(canvasGo, OutDir + "HelpMenu_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] help menu capture threw: " + e);
            }
            finally
            {
                // Destroy the canvas FIRST so HelpMenu.OnDestroy (if it ever ran) sees a
                // dead canvas and never calls runtime Destroy (edit-mode teardown contract).
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the Daily Quests master-detail card (DailyQuestHud) -- WO-795
        //  wave 2 rebuilt the quest list as a ScrollRect well (Viewport drag
        //  catcher + RectMask2D, fixed-height LayoutElement rows) so a deep quest
        //  log scrolls instead of truncating.
        //
        //  DeNelle.HUD is unreferenced -> reflection (CapturePauseMenu recipe).
        //  DailyQuestService.Instance only exists in Play mode (RuntimeInitialize
        //  bootstrap), so the VM resolves an EMPTY set headless and the shot
        //  carries the honest empty state (frame + wells + "No daily quests
        //  today."). We deliberately do NOT reflect-seed the service: standing it
        //  up would need a private static Instance injection AND writes today's
        //  rolled set into the owner's editor PlayerPrefs -- a real side effect,
        //  not a cheap fake. The WO-795 well itself is built by EnsureBuilt
        //  regardless of data, so its geometry IS in the shot.
        // ---------------------------------------------------------------------
        private static int CaptureDailyQuestHud()
        {
            return ForEachTarget("DailyQuestHud", CaptureDailyQuestHudOnce);
        }

        private static int CaptureDailyQuestHudOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;

            try
            {
                Type hudType = ResolveType("DeNelle.HUD.DailyQuestHud");
                if (hudType == null)
                {
                    Debug.LogWarning("[UICap-HL] DailyQuestHud skipped -- DeNelle.HUD.DailyQuestHud type not found.");
                    return 0;
                }

                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapDailyQuestHud");
                var hud = hostGo.AddComponent(hudType);

                // EnsureBuilt creates the VM + canvas + FrameQuest chrome + the WO-795
                // scroll well, then hides the chrome root. Repaint (normally driven by
                // OnEnable, which never runs in edit mode) paints the list/detail wells.
                // ClearZone preserves the kit's ZoneBacking by name, so the first paint
                // makes zero runtime-Destroy calls -- edit-mode safe.
                InvokePrivate(hud, "EnsureBuilt");
                InvokePrivate(hud, "Repaint");

                canvasGo = GetPrivateGameObject(hud, "_canvas");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] DailyQuestHud._canvas null after EnsureBuilt -- daily quests skipped.");
                    return 0;
                }

                // EnsureBuilt hides the CHROME ROOT (not the canvas); show it for the shot.
                object chrome = GetPrivateFieldValue(hud, "_chrome");
                var chromeRoot = GetFieldValue(chrome, "root") as GameObject;
                if (chromeRoot != null) chromeRoot.SetActive(true);

                if (RenderCanvasToPng(canvasGo, OutDir + "DailyQuestHud_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] daily quest hud capture threw: " + e);
            }
            finally
            {
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the lore-stone reading modal (LoreReadingModal, DeNelle.Dungeons
        //  -- a referenced assembly, so this capture is reflection-light). WO-795
        //  wave 2 gave the body a vertical ScrollRect well so long lore scrolls at
        //  a FIXED readable size (owner law: reflow, never shrink fonts).
        //
        //  WORST CASE by construction: we parse the canon lore-fragments.json from
        //  StreamingAssets and pick the fragment with the LONGEST total body --
        //  today that is journal-4, but the pick tracks the data as canon grows.
        //  Show() is the panel's own public entry and runs Build synchronously
        //  (no lifecycle dependency), so the REAL modal renders the canon prose
        //  verbatim. Teardown NotifyCloses the arbiter handle by hand because the
        //  panel's own Close() uses runtime Destroy (illegal in edit mode).
        // ---------------------------------------------------------------------
        private static int CaptureLoreReadingModal()
        {
            return ForEachTarget("LoreReadingModal", CaptureLoreReadingModalOnce);
        }

        private static int CaptureLoreReadingModalOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            LoreReadingModal modal = null;
            GameObject canvasGo = null;

            try
            {
                string jsonPath = Path.Combine(Application.streamingAssetsPath,
                    "Data/Canonical/lore-fragments.json");
                if (!File.Exists(jsonPath))
                {
                    Debug.LogWarning("[UICap-HL] LoreReadingModal skipped -- lore-fragments.json not found at "
                                     + jsonPath);
                    return 0;
                }

                LoreFragmentSet set = JsonConvert.DeserializeObject<LoreFragmentSet>(
                    File.ReadAllText(jsonPath));
                if (set == null || set.Fragments == null || set.Fragments.Count == 0)
                {
                    Debug.LogWarning("[UICap-HL] LoreReadingModal skipped -- lore-fragments.json parsed empty.");
                    return 0;
                }

                // Longest total body = the worst case the scroll well must carry.
                LoreFragment worst = null;
                int worstLen = -1;
                foreach (var f in set.Fragments)
                {
                    if (f == null || f.Body == null || f.Body.Length == 0) continue;
                    int len = 0;
                    foreach (var p in f.Body) len += p != null ? p.Length : 0;
                    if (len > worstLen) { worstLen = len; worst = f; }
                }
                if (worst == null)
                {
                    Debug.LogWarning("[UICap-HL] LoreReadingModal skipped -- no fragment carries a body.");
                    return 0;
                }

                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                LoreReadingModal.Show(new LoreReadRequest
                {
                    LoreStoneId = worst.Id,
                    Title = worst.Title,
                    Body = worst.Body,
                });
                modal = UnityEngine.Object.FindAnyObjectByType<LoreReadingModal>();
                if (modal == null)
                {
                    Debug.LogWarning("[UICap-HL] LoreReadingModal instance not found after Show -- lore capture skipped.");
                    return 0;
                }

                canvasGo = GetPrivateGameObject(modal, "_canvas");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] LoreReadingModal._canvas null -- modal did not build; skipped.");
                    return 0;
                }

                Debug.Log("[UICap-HL] lore worst-case fragment = '" + worst.Id + "' (" + worstLen + " chars).");
                if (RenderCanvasToPng(canvasGo, OutDir + "LoreReadingModal_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] lore reading modal capture threw: " + e);
            }
            finally
            {
                // Build() registered + NotifyOpened the "LoreReading" arbiter handle; clear
                // it by hand (the panel's own Close() would runtime-Destroy -- edit-illegal).
                try
                {
                    if (modal != null)
                    {
                        var handle = GetPrivateFieldValue(modal, "_handle") as PanelHandle;
                        if (handle != null) PanelManager.NotifyClosed(handle);
                    }
                }
                catch (Exception pe)
                {
                    Debug.LogWarning("[UICap-HL] lore modal arbiter release failed (harmless): " + pe.Message);
                }
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (modal != null && modal.gameObject != null) UnityEngine.Object.DestroyImmediate(modal.gameObject);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the tower manager (TowerManagerPanel, DeNelle.Village.UI --
        //  referenced, so direct types + private-field reflection only). WO-795
        //  wave 2 moved the tower rows into a persistent ScrollRect well so an
        //  unbounded tower list scrolls instead of truncating at the old 0.24
        //  floor. We spawn EIGHT stub towers (below) so the rows OVERFLOW the
        //  well -- the clip is the thing this shot verifies.
        //
        //  Refresh() clears the body's non-well children with runtime Destroy()
        //  (edit-illegal), so those children (the kit ZoneBacking plate, plus the
        //  footer text on the no-frame fallback path) are PARKED outside the body
        //  for the call and restored at their original sibling index after --
        //  zero Destroy noise, true chrome in the shot. NO row is selected: row
        //  selection spawns the in-world marker via CreatePrimitive + runtime
        //  Destroy on its collider, which edit mode forbids.
        // ---------------------------------------------------------------------
        private static int CaptureTowerManagerPanel()
        {
            return ForEachTarget("TowerManagerPanel", CaptureTowerManagerPanelOnce);
        }

        private static int CaptureTowerManagerPanelOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            GameObject[] stubTowers = null;
            PlacedTowerListVM vm = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                stubTowers = CreateStubTowers(8);

                hostGo = new GameObject("~UICapTowerManager");
                var panel = hostGo.AddComponent<TowerManagerPanel>();

                // Awake never ran (edit mode), so build the modal + inject the VM the
                // panel's Awake would have created. CreateDefault's FindObjectsByType
                // poll picks the stub towers up like real ones.
                InvokePrivate(panel, "EnsureBuilt");
                vm = PlacedTowerListVM.CreateDefault(null);
                SetPrivateField(panel, "_vm", vm);

                // Park non-well body children across Refresh (see banner).
                var bodyHost = GetPrivateFieldValue(panel, "_bodyHost") as Transform;
                var wellGo = GetPrivateFieldValue(panel, "_listViewport") as GameObject;
                var parked = new List<KeyValuePair<Transform, int>>();
                if (bodyHost != null)
                {
                    for (int i = 0; i < bodyHost.childCount; i++)
                    {
                        var ch = bodyHost.GetChild(i);
                        if (ch == null || (wellGo != null && ch.gameObject == wellGo)) continue;
                        parked.Add(new KeyValuePair<Transform, int>(ch, i));
                    }
                    foreach (var kv in parked) kv.Key.SetParent(null, false);
                }
                try
                {
                    InvokePrivate(panel, "Refresh");
                }
                finally
                {
                    foreach (var kv in parked)
                    {
                        if (kv.Key == null || bodyHost == null) continue;
                        kv.Key.SetParent(bodyHost, false);
                        kv.Key.SetSiblingIndex(kv.Value);
                    }
                }

                object modal = GetPrivateFieldValue(panel, "_modal");
                canvasGo = GetFieldValue(modal, "canvas") as GameObject;
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] TowerManagerPanel modal canvas null -- tower manager skipped.");
                    return 0;
                }

                canvasGo.SetActive(true);   // EnsureBuilt builds it hidden; show it for the shot

                if (RenderCanvasToPng(canvasGo, OutDir + "TowerManagerPanel_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] tower manager capture threw: " + e);
            }
            finally
            {
                if (vm != null) vm.Dispose();
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                DestroyStubTowers(stubTowers);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the BuildMenu UPGRADE TOWER screen (DeNelle.Village -- direct
        //  types). WO-795 wave 2 put the placed-tower list into a ScrollRect well
        //  above the fixed info band (UpgradeInfoTop); the info block + Upgrade
        //  CTA stay OUTSIDE the well. Worst case rendered here: eight stub tower
        //  rows overflowing the well PLUS the first tower SELECTED, so the cost
        //  rows / result line / Upgrade button all render below the list.
        //
        //  RenderUpgradeTower is invoked DIRECTLY instead of the public Render():
        //  Render()'s screen-switch clear uses runtime Destroy() on the body
        //  children (edit-illegal), and on a fresh build there is nothing to
        //  clear anyway. Known delta: the "Crystals: N" readout Render() tops
        //  every screen with is absent from this shot (it lives in Render, not
        //  RenderUpgradeTower). The VM is built through its public injectable
        //  ctor (null economy -> the standalone 500-crystal fallback), so no
        //  service/scene context is needed -- BuildModeController is NOT touched.
        // ---------------------------------------------------------------------
        private static int CaptureBuildMenuUpgradeTower()
        {
            return ForEachTarget("BuildMenuUpgradeTower", CaptureBuildMenuUpgradeTowerOnce);
        }

        private static int CaptureBuildMenuUpgradeTowerOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            GameObject[] stubTowers = null;
            BuildMenuVM vm = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                stubTowers = CreateStubTowers(8);

                // 2026-08-04: the stubs used to be bare GameObject + AddComponent<Tower>, i.e.
                // towers with NO TowerData. That is the state a tower is in while the crew is
                // still raising it -- it has no level, no stats and no upgrade price -- so the
                // shot showed an un-authored tower rather than the upgrade screen. Seed the REAL
                // shipped ArcherTower asset (authored 100/220 upgrade costs) so the capture
                // exercises the priced path with real numbers. _data is set directly instead of
                // through Tower.Initialize because Initialize instantiates the level visual and
                // wires combat -- scene side effects this throwaway host must not create.
                string[] seedIds = { "Towers/ArcherTower", "Towers/FrostTower", "Towers/MageTower" };
                int seeded = 0;
                for (int i = 0; i < stubTowers.Length; i++)
                {
                    var seedData = Resources.Load<DeNelle.Core.Data.TowerData>(seedIds[i % seedIds.Length]);
                    if (seedData == null) continue;
                    SetPrivateField(stubTowers[i].GetComponent<Tower>(), "_data", seedData);
                    seeded++;
                }
                if (seeded == 0)
                    Debug.LogWarning("[UICap-HL] no TowerData under Resources/Towers -- the upgrade-tower " +
                                     "shot will render the still-under-construction state.");

                hostGo = new GameObject("~UICapBuildMenu");
                var menu = hostGo.AddComponent<BuildMenu>();

                InvokePrivate(menu, "EnsureBuilt");

                // Public injectable ctor: (economy, towers, wallRepair, fallbackCrystals,
                // onClose). A FUNDED ledger is injected so the shot shows the affordable CTA;
                // with a null economy the VM correctly reports 0 wood/iron on hand and the
                // button would read its (equally real) "Not enough Wood" state instead.
                vm = new BuildMenuVM(new CaptureLedger(400), PlacedTowerListVM.CreateDefault(null), null, 500, null);
                SetPrivateField(menu, "_vm", vm);

                // Select the first stub tower BEFORE the single render pass so the
                // selected-row highlight + upgrade info block are in the shot.
                SetPrivateField(menu, "_selectedTowerForUpgrade",
                    stubTowers[0].GetComponent<Tower>());

                InvokePrivate(menu, "RenderUpgradeTower");

                object modal = GetPrivateFieldValue(menu, "_modal");
                canvasGo = GetFieldValue(modal, "canvas") as GameObject;
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] BuildMenu modal canvas null -- upgrade-tower screen skipped.");
                    return 0;
                }

                canvasGo.SetActive(true);   // EnsureBuilt builds it hidden; show it for the shot

                if (RenderCanvasToPng(canvasGo, OutDir + "BuildMenuUpgradeTower_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] build menu upgrade-tower capture threw: " + e);
            }
            finally
            {
                if (vm != null) vm.Dispose();
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                DestroyStubTowers(stubTowers);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: Brom's Rumor Board (RumorBoardPanel, DeNelle.Village.Hero --
        //  referenced, so direct types + private-field reflection only).
        //
        //  WO-1192 v3 rebuilt it as THREE SELF-CONTAINED RUMOR POSTERS paged three
        //  at a time (no tabs, no detail pane, no In-Progress, no selection step),
        //  so this shot covers what the two failing 2026-08-25/26 captures could
        //  not survive: three columns of fixed-pixel bands at every landscape
        //  aspect, with the widest possible reward row on poster one.
        //
        //  Open() is the panel's ONLY build entry: it registers the arbiter handle,
        //  creates the LIVE VM (RumorBoardVM.CreateDefault) and paints once. For a
        //  DETERMINISTIC worst case we then swap in a VM built over the panel's own
        //  injectable backend seam (IRumorBoardBackend).
        //
        //  EDIT-SAFE REPAINT: Repaint()'s clear calls runtime Destroy on the FIRST
        //  paint's children (edit-illegal), so we DestroyImmediate the poster row's
        //  children ourselves before invoking Repaint -- the repaint then runs
        //  Destroy-free (tower-manager parking recipe, applied as a pre-clear).
        //
        //  ⛔ A HARNESS PHOTOGRAPHS THE PANEL; IT NEVER RE-AUTHORS IT. The old
        //  portrait anchor re-assert is gone for good: it carried a private copy of
        //  a retired literal, wrote it back after Open() and before AuditGeometry,
        //  and manufactured 18 phantom findings while concealing 2 real ones.
        // ---------------------------------------------------------------------
        //  ⛔ LANDSCAPE ONLY (owner ruling 2026-08-26, recorded in WO-1192): the game is
        //  landscape and portrait work is out of scope. The two PORTRAIT targets are DELETED
        //  rather than left in: the v3 board has exactly one layout, so a portrait shot would
        //  photograph a composition nobody designed and then report its findings as defects -
        //  which is how a finished ticket gets re-opened by its own harness.
        private static readonly CaptureTarget[] RumorBoardTargets =
        {
            new CaptureTarget(1920, 1080),
            new CaptureTarget(2340, 1080),
            new CaptureTarget(2670, 1200),   // Seeker landscape
        };

        private static int CaptureRumorBoard()
        {
            return ForEachTarget("RumorBoard", RumorBoardTargets, CaptureRumorBoardOnce);
        }

        private static int CaptureRumorBoardOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            RumorBoardPanel panel = null;
            RumorBoardVM worstVm = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapRumorBoard");
                panel = hostGo.AddComponent<RumorBoardPanel>();

                // The real build path (chrome + head row + three posters + status).
                panel.Open();

                canvasGo = GetPrivateGameObject(panel, "_ui");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] RumorBoardPanel._ui null after Open -- rumor board skipped.");
                    return 0;
                }

                // Swap the live VM for the worst-case VM (injectable backend seam).
                var liveVm = GetPrivateFieldValue(panel, "_vm") as RumorBoardVM;
                if (liveVm != null) liveVm.Dispose();   // also detaches the panel's Changed -> Repaint hook
                worstVm = new RumorBoardVM(new WorstCaseRumorBackend(), null);
                SetPrivateField(panel, "_vm", worstVm);

                // Pre-clear the first paint with DestroyImmediate so the repaint below makes
                // zero runtime-Destroy calls (edit-mode contract). WO-1192 v3: the ONE rebuilt
                // container is the poster row - there is no list content root and no detail CTA
                // any more, because there is no list and no detail pane.
                var posterHost = GetPrivateFieldValue(panel, "_posterHost") as RectTransform;
                if (posterHost != null)
                {
                    for (int i = posterHost.childCount - 1; i >= 0; i--)
                    {
                        var ch = posterHost.GetChild(i);
                        if (ch != null) UnityEngine.Object.DestroyImmediate(ch.gameObject);
                    }
                }

                InvokePrivate(panel, "Repaint");   // page one: three posters at their widest

                if (RenderCanvasToPng(canvasGo, OutDir + "RumorBoard_" + target.Tag + ".png",
                    target.W, target.H)) saved++;

                // WO-1192 acceptance 4: Next > WRAPS. Page two is captured so the wrap is
                // photographed rather than asserted - the fixture's 14 rumors make five pages
                // with a deliberately SHORT last one, which is the page a fixed 3-up layout
                // would break on.
                worstVm.NextPage();
                InvokePrivate(panel, "Repaint");
                if (RenderCanvasToPng(canvasGo, OutDir + "RumorBoard_page2_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] rumor board capture threw: " + e);
            }
            finally
            {
                // Open() registered + NotifyOpened the arbiter handle; clear it by hand
                // (the panel's own Close() uses runtime Destroy -- edit-illegal).
                try
                {
                    if (panel != null)
                    {
                        var handle = GetPrivateFieldValue(panel, "_handle") as PanelHandle;
                        if (handle != null) PanelManager.NotifyClosed(handle);
                    }
                }
                catch (Exception pe)
                {
                    Debug.LogWarning("[UICap-HL] rumor board arbiter release failed (harmless): " + pe.Message);
                }
                if (worstVm != null) worstVm.Dispose();
                // Canvas FIRST so any later OnDestroy sees a dead _ui (edit-mode teardown contract).
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // =====================================================================
        //  Panel: THE NIGHT MARKET (PackStore) -- UI-001 / WO-1060.
        // ---------------------------------------------------------------------
        //  ⛔ WHY THIS ENTRY EXISTS, AND WHY IT IS THE FIRST THING UI-001'S REBUILD
        //  LANDS. The enumeration above is a HAND-WRITTEN list, and the money screen
        //  was not on it. Everything downstream of the list -- AuditGeometry's four
        //  numeric rules, the fidelity report, the pngs a human opens -- was therefore
        //  STRUCTURALLY BLIND to the one screen that takes money, no matter how green
        //  the markers read.
        //
        //  What that blindness cost, measured, on 2026-08-22: the owner's own device
        //  frames showed Folk's Thanks priced "20 SKR" when the real price is 120 --
        //  the leading digit occluded by an overlapping card -- and Ingot Crate "6 SKR"
        //  against a real 36. A WRONG PRICE ON THE MONEY SCREEN, found by eye, days
        //  late, after a fully green run. AuditGeometry rule 3 (BUTTON OVER TEXT) sees
        //  exactly that occlusion, and rule 4 sees the inflated FREE-band OPEN slabs.
        //  The oracle did not lack the ASSERTS; it lacked the CANVAS.
        //
        //  So this is deliberately NOT a new oracle -- adding one would leave the next
        //  panel just as invisible. It adds the store to the set the existing oracle
        //  already walks, which is WO-1060 section 4's rule: "a panel that can be
        //  captured can be measured", never a hand-maintained second list.
        //
        //  BUILT, NOT FAKED. Awake() + EnsureBuilt() + Render() are the real build path
        //  (kit modal -> bands -> spotlight -> trust strip), driven per target size by
        //  ForEachTarget so the geometry measured is the geometry that resolution really
        //  produces. Awake() constructs a WalletService and a PackStoreVM; with no live
        //  GameStateService in edit mode both resolve to their empty defaults, which is
        //  the honest first-run state -- rail closed, no wallet bound, "Coming soon" in
        //  the CTA slot. That is one of the six required UI-001 review frames, free.
        //
        //  Each step is INDIVIDUALLY guarded: a store that refuses to open must log and
        //  return 0, never throw the whole capture run away (the raid-screen contract).
        // =====================================================================
        private static int CaptureNightMarketStore()
        {
            return ForEachTarget("NightMarket", NightMarketTargets, CaptureNightMarketStoreOnce);
        }

        private static int CaptureNightMarketStoreOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            DeNelle.Wallet.PackStore store = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                // WO-1060 Assert A: clear the ring so any growth recorded below is THIS panel's.
                ElarionUiKit.ClearClampGrowths();
                var wordmarkSprite = Resources.Load<Sprite>("UI/NightMarket/night-market-wordmark");
                var wordmarkTexture = Resources.Load<Texture2D>("UI/NightMarket/night-market-wordmark");
                Debug.Log("[UICap-HL] NightMarket art probe sprite=" +
                          (wordmarkSprite != null ? wordmarkSprite.name : "null") +
                          " texture=" + (wordmarkTexture != null ? wordmarkTexture.name : "null"));

                // Left ACTIVE (the RealmMap pattern). Edit mode does not call Awake/OnEnable on a
                // plain MonoBehaviour, so there is no race to dodge -- and an INACTIVE host would
                // make any StartCoroutine on the build path throw instead of building.
                hostGo = new GameObject("~UICapNightMarket");
                store = hostGo.AddComponent<DeNelle.Wallet.PackStore>();

                // Awake() is not called on an edit-mode AddComponent -- drive it by hand, guarded,
                // because it is the only thing that creates the VM the render path reads.
                try { InvokePrivate(store, "Awake"); }
                catch (Exception ae)
                {
                    Debug.LogWarning("[UICap-HL] Night Market Awake threw (continuing to build): " + ae.Message);
                }

                InvokePrivate(store, "EnsureBuilt");

                var modal = GetPrivateFieldValue(store, "_modal") as ElarionUiKit.ObsidianModal;
                canvasGo = modal != null ? modal.canvas : null;
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] PackStore._modal.canvas null after EnsureBuilt -- " +
                                     "Night Market skipped (the store cannot draw; that is itself the defect).");
                    return 0;
                }

                canvasGo.SetActive(true);       // EnsureBuilt leaves it hidden for OnEnable to show
                int artCount = 0;
                foreach (var tr in canvasGo.GetComponentsInChildren<Transform>(true))
                {
                    if (!tr.name.StartsWith("art-", StringComparison.Ordinal)) continue;
                    artCount++;
                    var rt = tr as RectTransform;
                    Debug.Log("[UICap-HL] NightMarket composed art " + tr.name +
                              " active=" + tr.gameObject.activeInHierarchy +
                              " rect=" + (rt != null ? rt.rect.ToString() : "n/a") +
                              " sibling=" + tr.GetSiblingIndex());
                }
                Debug.Log("[UICap-HL] NightMarket composed art count=" + artCount);

                // Render() fills the priced bands from PackCatalog. Guarded separately: a catalogue
                // failure must still leave the CHROME shot, because "the store opened empty" and
                // "the store did not open" are different defects and the png must tell them apart.
                try { store.Render(); }
                catch (Exception re)
                {
                    Debug.LogWarning("[UICap-HL] Night Market Render threw (shooting the chrome anyway): " + re.Message);
                }

                if (RenderCanvasToPng(canvasGo, OutDir + "NightMarket_" + target.Tag + ".png",
                    target.W, target.H)) saved++;

                // The runtime half of Assert A. In edit mode ClampMinTouch's guard MonoBehaviour
                // never gets a LateUpdate, so this is expected to stay empty -- the gate-time
                // assert is AuditGeometry rule 4, which measures the AUTHORED band instead. Printed
                // anyway so a play-mode or device run through this path is not silently discarded.
                var growths = ElarionUiKit.ClampGrowths;
                for (int i = 0; i < growths.Count; i++)
                    Debug.LogError("[touch-oracle] FAIL NightMarket@" + target.Tag + " " + growths[i]);
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] night market capture threw: " + e);
            }
            finally
            {
                // Awake() registered the panel with the arbiter; release it by hand -- the store's
                // own CloseStore path uses runtime Destroy, which is edit-illegal.
                try
                {
                    var handle = GetPrivateFieldValue(store, "_panelHandle") as PanelHandle;
                    if (handle != null) PanelManager.NotifyClosed(handle);
                }
                catch (Exception pe)
                {
                    Debug.LogWarning("[UICap-HL] night market arbiter release failed (harmless): " + pe.Message);
                }
                // Canvas FIRST so any later OnDestroy sees a dead _modal (edit-mode teardown contract).
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: the Realm Map parchment overworld (WO-826). Open() builds the
        //  real code-built panel from the dual-copy realm-map.json; with no
        //  GameStateService alive in edit mode the VM's live source reads a
        //  fresh save (BestWave 0, empty ledger), so the shot shows exactly the
        //  acceptance state: home Elarion selected + every region as LOCKED fog.
        //  Open() makes zero runtime-Destroy calls on a first build (empty node
        //  host, no CTA yet), so no DestroyImmediate pre-clear is needed.
        // ---------------------------------------------------------------------
        private static int CaptureRealmMap()
        {
            return ForEachTarget("RealmMap", CaptureRealmMapOnce);
        }

        private static int CaptureRealmMapOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            RealmMapPanel panel = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapRealmMap");
                panel = hostGo.AddComponent<RealmMapPanel>();

                // The real build path (parchment plate + nodes + detail + disabled Travel CTA).
                panel.Open();

                canvasGo = GetPrivateGameObject(panel, "_ui");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] RealmMapPanel._ui null after Open -- realm map skipped.");
                    return 0;
                }

                // Landscape at every capture target -- and now genuinely BUILT at each one
                // (the panel used to be authored once under the editor's own Screen).
                if (RenderCanvasToPng(canvasGo, OutDir + "RealmMap_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] realm map capture threw: " + e);
            }
            finally
            {
                // Open() registered + NotifyOpened the arbiter handle; clear it by hand
                // (the panel's own Close() uses runtime Destroy -- edit-illegal).
                try
                {
                    if (panel != null)
                    {
                        var handle = GetPrivateFieldValue(panel, "_handle") as PanelHandle;
                        if (handle != null) PanelManager.NotifyClosed(handle);
                    }
                }
                catch (Exception pe)
                {
                    Debug.LogWarning("[UICap-HL] realm map arbiter release failed (harmless): " + pe.Message);
                }
                // Canvas FIRST so any later OnDestroy sees a dead _ui (edit-mode teardown contract).
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Hero talent graph + persistent three-slot hot-swap rail. This is built
        //  from the real live-class VM; the edit-mode fallback is Knight, matching
        //  first-run behavior when no gameplay GameState exists.
        // ---------------------------------------------------------------------
        private static int CaptureHeroSkillTree()
        {
            return ForEachTarget("HeroSkillTree", CaptureHeroSkillTreeOnce);
        }

        private static int CaptureHeroSkillTreeOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject canvasGo = null;
            HeroSkillTreePanelMvvm panel = null;
            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }
                hostGo = new GameObject("~UICapHeroSkillTree");
                panel = hostGo.AddComponent<HeroSkillTreePanelMvvm>();
                panel.Open();
                canvasGo = GetPrivateGameObject(panel, "_ui");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] HeroSkillTreePanelMvvm._ui null after Open -- skipped.");
                    return 0;
                }
                if (RenderCanvasToPng(canvasGo,
                    OutDir + "HeroSkillTree_" + target.Tag + ".png", target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] hero skill tree capture threw: " + e);
            }
            finally
            {
                try
                {
                    if (panel != null)
                    {
                        var handle = GetPrivateFieldValue(panel, "_panelHandle") as PanelHandle;
                        if (handle != null) PanelManager.NotifyClosed(handle);
                    }
                }
                catch (Exception pe)
                {
                    Debug.LogWarning("[UICap-HL] hero skill tree arbiter release failed: " + pe.Message);
                }
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }
            return saved;
        }

        // ---------------------------------------------------------------------
        //  Worst-case rumor-board backend: the deterministic fixture behind the
        //  RumorBoard shots. 15 rumors overflow the WO-795 list well; the
        //  pre-selected rumor carries the longest hook (= the detail body's
        //  variable prose) plus a full rewards row (crystals/food/magic + items).
        // ---------------------------------------------------------------------
        private sealed class WorstCaseRumorBackend : IRumorBoardBackend
        {
            public const string LongestBodyId = "uicap_rumor_longest";

            private readonly List<QuestDef> _defs = new List<QuestDef>();
            private readonly HashSet<string> _activeIds = new HashSet<string>();

            public WorstCaseRumorBackend()
            {
                // WO-1192 v3: the board OFFERS work, so the worst case is the widest PAGE OF
                // THREE, not a master-detail selection. Poster 1 carries the longest title AND
                // the longest letter AND every reward part, so the page is at its widest and its
                // tallest at once.
                _defs.Add(MakeRumor(LongestBodyId,
                    "The Long Letter from the Drowned Archive of Old Elarion", "endgame",
                    "Brom unfolds a letter soaked through and dried twice over. The archivist of the "
                    + "drowned wing writes that the shelves beneath the reservoir have begun to sing at "
                    + "dusk, a low note that loosens the mortar and wakes the lantern eels. She asks for "
                    + "a steady hand to carry the sealed ledger past the flooded stair, to count the "
                    + "black candles left burning in the reading room nobody has entered since the "
                    + "founding, and to bring back whichever page the water refuses to touch. The road "
                    + "there crosses both gates, the old orchard, and the culvert the masons swear was "
                    + "never on any drawing of the village.",
                    220, 90, 45, "relic_drowned_ledger"));

                // Two more on page one, so the shot is a full three-poster board.
                for (int i = 1; i <= 2; i++)
                {
                    _defs.Add(MakeRumor("uicap_rumor_watch" + i,
                        "Standing Watch Over the Western Fields - Part " + i + " of 2", i == 1 ? "story" : "gear",
                        "Hold the western fields until the lantern wardens return from the ridge.",
                        40, 20, 0, null));
                }

                // WO-1521: ONE quest already underway. It IS on the board now, as an ACTIVE row
                // carrying its current objective and a GO TO door - the exclusion this fixture
                // used to prove is retired (owner report 2026-09-06: an accepted quest that
                // vanishes from every surface is why "no idea how or what to do to complete it").
                const string activeId = "uicap_rumor_underway";
                _defs.Add(MakeRumor(activeId, "Already Underway - the active row", "story",
                    "Carry the sealed ledger to the archivist waiting at the western gate.",
                    10, 0, 0, null));
                _activeIds.Add(activeId);

                // Eleven more available rumors -> 14 offered, i.e. FIVE pages of three with a
                // deliberately SHORT last page, so Next > has to wrap off a two-poster page.
                for (int i = 1; i <= 11; i++)
                {
                    _defs.Add(MakeRumor("uicap_rumor_avail" + i,
                        "Rumor of the " + Ordinal(i) + " Bell That Rings Itself", (i % 3 == 0) ? "gear" : "story",
                        "Track down why the " + Ordinal(i).ToLowerInvariant()
                        + " bell rings with nobody on the rope, and quiet it before nightfall.",
                        10 + i, i, 0, null));
                }
            }

            private static string Ordinal(int i)
            {
                string[] names = { "First", "Second", "Third", "Fourth", "Fifth", "Sixth",
                                   "Seventh", "Eighth", "Ninth", "Tenth", "Eleventh" };
                return i >= 1 && i <= names.Length ? names[i - 1] : i.ToString();
            }

            private static QuestDef MakeRumor(string id, string title, string type, string hook,
                                              int crystals, int food, int magic, string itemId)
            {
                var def = new QuestDef { Id = id, Title = title, Type = type };
                var lines = new List<QuestRewardLine>();
                // Capture fixture: worst-case reward slab - include XP so the chip is exercised.
                lines.Add(new QuestRewardLine { Kind = QuestRewardLine.KindXp, Amount = 400 });
                if (crystals > 0) lines.Add(new QuestRewardLine { Kind = QuestRewardLine.KindCrystals, Amount = crystals });
                if (food > 0) lines.Add(new QuestRewardLine { Kind = QuestRewardLine.KindFood, Amount = food });
                if (magic > 0) lines.Add(new QuestRewardLine { Kind = QuestRewardLine.KindMagic, Amount = magic });
                if (!string.IsNullOrEmpty(itemId))
                    lines.Add(new QuestRewardLine { Kind = QuestRewardLine.KindItem, Id = itemId });
                def.Stages.Add(new QuestStage
                {
                    StageId = id + "_s1",
                    ObjectiveText = hook,
                    Reward = lines,
                });
                return def;
            }

            // -- IRumorBoardBackend ------------------------------------------------
            public IReadOnlyList<QuestDef> Catalog => _defs;
            public bool Ready => true;
            public bool IsActive(string id) => id != null && _activeIds.Contains(id);
            public bool IsCompleted(string id) => false;
            public void StartQuest(string id) { }
            // The capture fixture is deliberately ALL-NEW: every poster wears its NEW chip, so
            // the shot proves the chip's geometry rather than depending on this machine's
            // PlayerPrefs (which is what the live backend reads).
            public bool HasSeen(string id) => false;
            public void MarkSeen(string id) { }

            // -- WO-1521 seams: the capture fixture carries ONE claimable daily, so every
            //    RumorBoard shot proves the three row kinds (Claim / Go To / Accept) at once.
            private readonly List<DailyQuestInstance> _dailies = new List<DailyQuestInstance>
            {
                new DailyQuestInstance
                {
                    Id = "uicap_daily_claimable", TemplateId = "combat.clear-waves", Slot = "combat",
                    Target = 3, Progress = 3, Completed = true, ClaimedAtUnix = 0,
                    Label = "Clear {target} waves at the western gate",
                },
            };

            public IReadOnlyList<DailyQuestInstance> Dailies => _dailies;
            public DailyQuestSlotReward DailyReward(string slot) => new DailyQuestSlotReward
            { Slot = slot, RewardCrystals = 120, RewardWisdom = 15, RewardRandomItem = true };
            public bool ClaimDaily(string dailyQuestId) => false;   // a capture never mutates state
            public string ActiveObjective(string questId) =>
                "Carry the sealed ledger past the flooded stair before the lantern eels wake.";
            public bool GoTo(string questId) => false;
            public void Track(string questId) { }

            public event Action Changed { add { } remove { } }
        }

        // ---------------------------------------------------------------------
        //  Stub towers for the two tower-list captures: bare GameObjects carrying
        //  a REAL Tower component. Tower has no RequireComponent/Reset/OnValidate
        //  and its stat getters are null-guarded (no TowerData -> level 1, rng 0,
        //  dmg 0), so an un-awoken edit-mode instance is inert -- but it IS what
        //  FindObjectsByType<Tower> returns, so both panels list it through their
        //  real VM path. Eight of them overflow the WO-795 wells on purpose: the
        //  clip-not-spill behaviour is exactly what the screenshots verify.
        // ---------------------------------------------------------------------
        /// <summary>
        /// A read-only, evenly-funded ledger for capture runs. It exists only so a shot can show
        /// the affordable branch of a priced screen; it never pretends to have spent anything
        /// (TrySpend debits its own fields and nothing else), and no shipped code constructs it.
        /// </summary>
        private sealed class CaptureLedger : DeNelle.Village.IEconomy
        {
            public CaptureLedger(int each)
            {
                Coins = each; Wood = each; Iron = each; Food = each; Crystals = each;
            }

            public int Coins { get; private set; }
            public int Wood { get; private set; }
            public int Iron { get; private set; }
            public int Food { get; private set; }
            public int Crystals { get; private set; }

            public event Action<DeNelle.Village.ResourceSnapshot> OnChanged;

            public bool CanAfford(DeNelle.Village.ResourceCost cost)
                => Wood >= cost.Wood && Food >= cost.Food && Iron >= cost.Iron
                   && Crystals >= cost.Crystals && Coins >= cost.Coins;

            public bool TrySpend(DeNelle.Village.ResourceCost cost)
            {
                if (!CanAfford(cost)) return false;
                Wood -= cost.Wood; Food -= cost.Food; Iron -= cost.Iron;
                Crystals -= cost.Crystals; Coins -= cost.Coins;
                OnChanged?.Invoke(new DeNelle.Village.ResourceSnapshot(Wood, Food, Iron, Crystals));
                return true;
            }

            public DeNelle.Village.ResourceCost Grant(DeNelle.Village.ResourceCost amount)
            {
                Wood += amount.Wood; Food += amount.Food; Iron += amount.Iron;
                Crystals += amount.Crystals; Coins += amount.Coins;
                OnChanged?.Invoke(new DeNelle.Village.ResourceSnapshot(Wood, Food, Iron, Crystals));
                // Uncapped fake ledger: every requested unit lands, so applied == requested.
                return amount;
            }
        }

        private static GameObject[] CreateStubTowers(int count)
        {
            string[] flavors = { "Flame", "Ice", "Aether", "Stone" };
            var towers = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                towers[i] = new GameObject("Tower_" + flavors[i % flavors.Length] + (i + 1));
                towers[i].AddComponent<Tower>();
            }
            return towers;
        }

        private static void DestroyStubTowers(GameObject[] towers)
        {
            if (towers == null) return;
            foreach (var t in towers)
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
        }

        // ---------------------------------------------------------------------
        //  Panel: the ECHO SELECT CARD (EchoCardView) -- the WO-830 resource picker.
        //  WO-852: this card had NO capture coverage, which is exactly why its
        //  fraction-band / sub-touch-floor overlap ("only the bottom chip is
        //  tappable") reached the owner. The card must show its info block AND
        //  touch-floor chips with no overlap at BOTH mobile-landscape aspects.
        //
        //  Driven WITHOUT OpenFor on purpose: OpenFor registers a PanelHandle with
        //  the static PanelManager whose Close callback would outlive this
        //  DestroyImmediate'd host and poison later captures. We seed the VM +
        //  open flag and call the view's own private Build/Refresh instead, so the
        //  REAL layout code runs against no global state.
        // ---------------------------------------------------------------------
        private static int CaptureEchoCard()
        {
            return ForEachTarget("EchoCard", CaptureEchoCardOnce);
        }

        private sealed class CaptureEchoWorkforce : IEchoWorkforce
        {
            public bool Available => true;
            public int EchoCount => 3;
            public int MaxEchoes => 6;
            public int WavesPerEcho => 3;
            public int WavesUntilNextEcho => 2;
            public float NextEchoProgress => 0.34f;
            public double GlobalHarvestMultiplier => 1.45d;
            public float FillFraction => 0.42f;
            public int PendingCollect => 180;
            public float CollectorMaxFill => 0.62f;
            public int CollectAll() => 180;
            public event Action Changed { add { } remove { } }
            public event Action<int> EchoUnlocked { add { } remove { } }
        }

        private static int CaptureEchoRosterPanel()
        {
            return ForEachTarget("EchoRoster", CaptureEchoRosterPanelOnce);
        }

        private static int CaptureEchoRosterPanelOnce(CaptureTarget target)
        {
            GameObject hostGo = null;
            GameObject modal = null;
            EchoRosterVM vm = null;
            try
            {
                hostGo = new GameObject("~UICapEchoRoster");
                var view = hostGo.AddComponent<EchoRosterView>();
                vm = new EchoRosterVM(new CaptureEchoWorkforce(), _ => { }, null);
                SetPrivateField(view, "_vm", vm);
                SetPrivateField(view, "_open", true);
                InvokePrivate(view, "Build");
                modal = GetPrivateGameObject(view, "_modal");
                if (modal == null)
                {
                    Debug.LogWarning("[UICap-HL] EchoRosterView._modal null after Build -- roster skipped.");
                    return 0;
                }
                modal.SetActive(true);
                return RenderCanvasToPng(modal, OutDir + "EchoRoster_" + target.Tag + ".png",
                    target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] echo roster capture threw: " + e);
                return 0;
            }
            finally
            {
                vm?.Dispose();
                if (modal != null) UnityEngine.Object.DestroyImmediate(modal);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
            }
        }

        private static int CaptureEchoCardOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject hostGo = null;
            GameObject modal = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                hostGo = new GameObject("~UICapEchoCard");
                var view = hostGo.AddComponent<EchoCardView>();

                // Echo index 0 = the founding spirit (longest name + the affinity note row,
                // i.e. the WORST-case row budget the picker has to seat).
                SetPrivateField(view, "_vm", new EchoCardVM(0));
                SetPrivateField(view, "_open", true);
                InvokePrivate(view, "Build");

                modal = GetPrivateGameObject(view, "_modal");
                if (modal == null)
                {
                    Debug.LogWarning("[UICap-HL] EchoCardView._modal null after Build -- echo card capture skipped.");
                    return 0;
                }
                modal.SetActive(true);
                InvokePrivate(view, "Refresh");

                if (RenderCanvasToPng(modal, OutDir + "EchoCard_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] echo card capture threw: " + e);
            }
            finally
            {
                // Edit-mode teardown MUST be DestroyImmediate (same contract as the other shots).
                if (modal != null) UnityEngine.Object.DestroyImmediate(modal);
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // NOTE: SetPrivateField already exists further down this file (the SettingsController
        // fixture helper) - the WO-852 capture reuses it rather than declaring a second copy
        // (CS0111 duplicate-member, caught by the compile gate 2026-08-02).

        // ---------------------------------------------------------------------
        //  Render a uGUI canvas subtree to a PNG in EDIT mode (no Play).
        //  ScreenSpaceOverlay canvases render straight to the backbuffer and cannot
        //  be captured off a camera, so we flip the canvas to ScreenSpaceCamera,
        //  point a throwaway camera at a RenderTexture, force a synchronous layout +
        //  TMP mesh rebuild, render, and read the pixels back.
        // ---------------------------------------------------------------------
        // ---------------------------------------------------------------------
        //  Panel: the WO-864 queue CARD RAIL, in BOTH of its hosts, driven by a
        //  synthetic ObsidianQueueGate snapshot that reproduces the owner's live
        //  2026-08-03 Seeker screen exactly: 1 of 2 builders busy on
        //  tower_arcane_spire with 193s left (so the idle slot MUST draw a visible
        //  "FREE" card), 3 identical footman trains queued (so they MUST collapse
        //  to one card with an x3 badge), and an idle Research channel.
        //
        //  Landscape-only, at every capture target. CORRECTION (2026-08-05): the old
        //  banner here described the second harness size as the device resolution. It
        //  is not -- the Seeker's real surface is 2670x1200, which nothing in this
        //  repo had ever shot. (The same wrong claim is still in tools'
        //  run-autopilot-fleet.ps1 -- fix it there too.)
        // ---------------------------------------------------------------------
        private static int CaptureQueueRail()
        {
            return ForEachTarget("QueueCardRail", CaptureQueueRailOnce);
        }

        private static int CaptureQueueRailOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject canvasGo = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                PublishQueueFixture();

                canvasGo = new GameObject("~UICapQueueRail", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 4000;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);   // HudAreasHost's scaler
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                // A dark backdrop so the rail's own plate/contrast reads honestly in the PNG.
                var bg = new GameObject("Backdrop", typeof(Image));
                bg.transform.SetParent(canvasGo.transform, false);
                var bgrt = (RectTransform)bg.transform;
                bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
                bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
                bg.GetComponent<Image>().color = new Color(0.13f, 0.15f, 0.12f, 1f);

                // (1) THE ALWAYS-ON HUD SURFACE — the exact HudArea.QueueStatus band
                //     geometry (0.780-0.995 x, 0.530-0.865 y) the owner is looking at.
                var band = MakeAreaMount(canvasGo.transform, "Area_QueueStatus",
                    new Vector2(0.780f, 0.530f), new Vector2(0.995f, 0.865f));
                var chipBand = MakePixelBand(band, "ChipBand", 0f, ElarionUiKit.MinTouchPx, 4f);
                ElarionUiKit.BuildObsidianButton(chipBand, "Builders 1/2 | Training 1",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    Vector2.zero, Vector2.one, null);
                var railMount = MakePixelBand(band, "QueueRailMount",
                    ElarionUiKit.MinTouchPx + 6f, 240f, 2f);
                QueueRailView.Build(railMount, DeNelle.Core.Jobs.ChannelId.Builder,
                    QueueRailView.Options.Default);

                // (2) THE MODAL SURFACE — three stacked rails, one per channel, in the
                //     Work Queue modal's body footprint. Same component, so any visual
                //     disagreement between the two surfaces would show up side by side.
                var modal = MakeAreaMount(canvasGo.transform, "Area_WorkQueueBody",
                    new Vector2(0.29f, 0.21f), new Vector2(0.71f, 0.79f));
                var modalBg = new GameObject("ModalPlate", typeof(Image));
                modalBg.transform.SetParent(modal, false);
                var mrt = (RectTransform)modalBg.transform;
                mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
                mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
                modalBg.GetComponent<Image>().color = new Color(0.035f, 0.033f, 0.038f, 0.98f);

                float railH = QueueRailView.HeightOf(QueueRailView.Options.Default);
                float y = 8f;
                foreach (DeNelle.Core.Jobs.ChannelId ch in new[]
                {
                    DeNelle.Core.Jobs.ChannelId.Builder,
                    DeNelle.Core.Jobs.ChannelId.Train,
                    DeNelle.Core.Jobs.ChannelId.Research,
                })
                {
                    var head = MakePixelBand(modal, "Head_" + ch, y, 60f, 8f);
                    var lbl = head.gameObject.AddComponent<TextMeshProUGUI>();
                    ElarionUiKit.EnsureFont(lbl);
                    var st = ObsidianQueueGate.Status;
                    lbl.text = ObsidianQueueGate.WorkQueueStatus.LabelOf(ch) + "   " +
                               st.BusyOf(ch) + "/" + st.SlotsOf(ch) + " busy";
                    lbl.fontSize = ElarionUi.FontLabel;
                    lbl.color = ElarionUi.Gilt;
                    lbl.fontStyle = FontStyles.Bold;
                    lbl.alignment = TextAlignmentOptions.MidlineLeft;
                    y += 60f;

                    var mount = MakePixelBand(modal, "Rail_" + ch, y, railH, 8f);
                    QueueRailView.Build(mount, ch, QueueRailView.Options.Default);
                    y += railH + 10f;
                }

                Canvas.ForceUpdateCanvases();
                if (RenderCanvasToPng(canvasGo, OutDir + "QueueCardRail_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] queue rail capture threw: " + e);
            }
            finally
            {
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }
            return saved;
        }

        // The owner's live screen, as data. Publishing through the REAL Core seam means
        // the capture exercises the same path the game does -- no view-only fixture.
        private static void PublishQueueFixture()
        {
            var s = new ObsidianQueueGate.WorkQueueStatus
            {
                Available = true,
                BuilderBusy = 1, BuilderSlots = 2, BuilderQueued = 1,
                TrainBusy = 1, TrainSlots = 2, TrainQueued = 3,
                ResearchBusy = 0, ResearchSlots = 1, ResearchQueued = 0,
                SoonestRemainingSec = 193,
                Entries = new[]
                {
                    new ObsidianQueueGate.QueueEntry
                    {
                        Label = "Arcane Spire", Verb = "BUILD", JobId = "tower_arcane_spire@15_7",
                        RemainingSec = 193, Queued = false, StackCount = 1,
                    },
                    new ObsidianQueueGate.QueueEntry
                    {
                        Label = "Stone Wall -> L2", Verb = "UPGRADE", JobId = "wall_stone@3_4",
                        TargetTier = 2, RemainingSec = -1, Queued = true, StackCount = 1,
                    },
                },
                TrainEntries = new[]
                {
                    new ObsidianQueueGate.QueueEntry
                    {
                        Label = "Footman", Verb = "TRAIN", JobId = "barracks-train:troop-footman:a1",
                        IconRole = RpgUiCatalog.RoleIcons, IconKey = "icon_sword",
                        RemainingSec = 42, Queued = false, StackCount = 1,
                    },
                    new ObsidianQueueGate.QueueEntry
                    {
                        Label = "Footman", Verb = "TRAIN", JobId = "barracks-train:troop-footman:b1",
                        IconRole = RpgUiCatalog.RoleIcons, IconKey = "icon_sword",
                        RemainingSec = -1, Queued = true, StackCount = 3,
                    },
                },
                ResearchEntries = Array.Empty<ObsidianQueueGate.QueueEntry>(),
            };
            ObsidianQueueGate.PublishStatus(s);
        }

        private static RectTransform MakeAreaMount(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static RectTransform MakePixelBand(RectTransform parent, string name,
            float yFromTopPx, float heightPx, float insetX)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-insetX * 2f, heightPx);
            rt.anchoredPosition = new Vector2(0f, -yFromTopPx);
            return rt;
        }

        /// <summary>
        /// True when the frame carries no picture: fewer than a handful of distinct
        /// (4-bit quantised) colours, or almost every pixel identical to the dominant one.
        /// A real Obsidian panel -- plate, gold trim, antialiased text -- clears this by an
        /// order of magnitude; an all-black no-graphics frame scores exactly 1 bucket and
        /// 0 ink. Sampled on a stride, so the check costs microseconds.
        /// </summary>
        private static bool IsBlank(Texture2D tex, out string measure)
        {
            measure = "unmeasured";
            if (tex == null) return true;

            Color32[] px;
            try { px = tex.GetPixels32(); }
            catch (Exception e) { measure = "GetPixels32 threw: " + e.Message; return true; }
            if (px == null || px.Length == 0) { measure = "no pixels"; return true; }

            var buckets = new Dictionary<int, int>();
            int sampled = 0;
            for (int i = 0; i < px.Length; i += BlankSampleStride)
            {
                var p = px[i];
                int key = ((p.r >> 4) << 8) | ((p.g >> 4) << 4) | (p.b >> 4);
                buckets.TryGetValue(key, out int n);
                buckets[key] = n + 1;
                sampled++;
            }
            if (sampled == 0) { measure = "no samples"; return true; }

            int dominant = 0;
            foreach (var kv in buckets) if (kv.Value > dominant) dominant = kv.Value;
            float ink = 1f - (dominant / (float)sampled);

            measure = "distinct=" + buckets.Count + " ink=" + ink.ToString("F4") +
                      " (floors " + BlankMinDistinctBuckets + " / " + BlankMinInkFraction.ToString("F4") + ")";
            return buckets.Count < BlankMinDistinctBuckets || ink < BlankMinInkFraction;
        }

        // =====================================================================
        //  WO-1010 P1 — the chips that replaced the build intent bar
        // =====================================================================
        /// <summary>
        /// Shoot the Build HUD's ghost controls in the four states the WO's acceptance
        /// criteria name: valid, blocked, clamped against a screen corner, and with the nudge
        /// pad toggled on. The clamp shot is the one that matters most -- chips that walk
        /// off-screen when the ghost nears an edge make a building unplaceable, and that is a
        /// failure the OLD bottom-edge bar could not have had, so it is new risk this redesign
        /// introduced and must be photographed rather than reasoned about.
        ///
        /// Positioning is invoked EXPLICITLY via LayoutGhostControlsNow() because
        /// MonoBehaviour ticks do not run in edit mode -- without that call this would
        /// photograph the chips parked at the canvas centre in all four shots and prove
        /// nothing at all.
        /// </summary>
        private static int CaptureBuildGhostChips()
        {
            return ForEachTarget("BuildGhostChips", target =>
            {
                int saved = 0;
                GameObject hostGo = null;
                try
                {
                    var hud = DeNelle.Village.BuildHudController.Create(null,
                        null, null, null, null, null);
                    if (hud == null)
                    {
                        Debug.LogWarning("[UICap-HL] BuildHudController.Create returned null -- build chips skipped.");
                        return 0;
                    }
                    hostGo = hud.gameObject;

                    var canvasTf = hostGo.transform.Find("BuildHudCanvas");
                    if (canvasTf == null)
                    {
                        Debug.LogWarning("[UICap-HL] BuildHudCanvas not found under the HUD host -- build chips skipped.");
                        return 0;
                    }
                    GameObject canvasGo = canvasTf.gameObject;

                    hud.Show();
                    hud.SetState(DeNelle.Village.BuildHudState.Placing);

                    // =========================================================
                    //  WO-1478 -- THE PILL READS THE LIVE CATALOG, NEVER A LITERAL.
                    //  This line used to hand SetPlacingLabel a hand-written cost string
                    //  mixing wood + iron + crystals. The authored Arcane Spire row has
                    //  never held that basket (it is iron only), and the SHAPE itself --
                    //  all three resources in one basket -- is what WO-947 /
                    //  CostBasketSeparationRegression forbids outright. So the harness
                    //  photographed a price the game cannot charge, and BOTH independent
                    //  UI reviewers then quoted the fiction back as evidence.
                    //  A capture that paints numbers the game does not use is not a
                    //  capture of the game.
                    //  The label now comes from the SAME seam the ghost paints with in
                    //  play: the hydrated catalog row -> StructureCardVM.PlacementSummaryFor
                    //  -> the one shared CostFormat. Retune the row and the capture moves
                    //  with it; no literal is left here to rot. The only string that
                    //  remains is the catalog ID being photographed.
                    // =========================================================
                    const string ghostEntryId = "tower_arcane_spire";
                    HydrateCatalogForCapture();
                    var ghostEntry = DeNelle.Core.Catalog.CatalogRegistry.Get(ghostEntryId);
                    if (ghostEntry == null)
                    {
                        Debug.LogError("[UICap-HL] catalog row '" + ghostEntryId + "' absent after hydration -- " +
                                       "the ghost pill would have to INVENT a cost, so the case is skipped rather " +
                                       "than photographing a fiction (WO-1478).");
                        return 0;
                    }
                    string ghostCostWords = DeNelle.Village.StructureCardVM
                        .PlacementSummaryFor(ghostEntry).CostWords;
                    if (string.IsNullOrEmpty(ghostCostWords))
                    {
                        // Not a fallback to a literal -- an EMPTY price slot is the authored
                        // behaviour while a first-build freebie is live (WO-1010 D20). Logged so
                        // a costless pill in the PNG reads as the rule, not as a broken capture.
                        Debug.LogWarning("[UICap-HL] '" + ghostEntryId + "' priced EMPTY -- the first-build " +
                                         "freebie is live for this row, so the pill shows no cost by design.");
                    }
                    hud.SetPlacingLabel(ghostEntry.displayName, ghostCostWords);
                    Debug.Log("[UICap-HL] ghost pill from the LIVE catalog row '" + ghostEntryId + "': name='" +
                              ghostEntry.displayName + "' cost='" + ghostCostWords + "'");

                    // (1) VALID — chips near the middle of the field, OK chip affirmative.
                    hud.TrackGhost(new Vector2(target.W * 0.5f, target.H * 0.55f), true, null);
                    hud.LayoutGhostControlsNow();
                    if (RenderCanvasToPng(canvasGo, OutDir + "BuildGhostChips_valid_" + target.Tag + ".png",
                        target.W, target.H)) saved++;

                    // (2) BLOCKED — the refusal must be READABLE, not just red.
                    hud.TrackGhost(new Vector2(target.W * 0.5f, target.H * 0.55f), false, "Not enough Wood");
                    hud.LayoutGhostControlsNow();
                    // WO-942 gap 2: the D17 SPRITE path's invalid verdict had no assertion
                    // anywhere. The worded refusal on the PILL is photographed by this shot, but
                    // the confirm chip's own invalid state is a colour/alpha + interactable flip
                    // that a PNG cannot be trusted to prove (the owner is red/green colourblind;
                    // "did the check-mark dim" is exactly the judgement a screenshot should not
                    // be asked for). Measure it instead — see AssertConfirmChipInvalid.
                    AssertConfirmChipInvalid(canvasGo, target.Tag);
                    if (RenderCanvasToPng(canvasGo, OutDir + "BuildGhostChips_blocked_" + target.Tag + ".png",
                        target.W, target.H)) saved++;

                    // (3) EDGE — ghost jammed into the bottom-right corner. Every chip must
                    //     still be fully on-screen and still be tappable.
                    hud.TrackGhost(new Vector2(target.W - 2f, 2f), true, null);
                    hud.LayoutGhostControlsNow();
                    string edgePath = OutDir + "BuildGhostChips_edgeclamp_" + target.Tag + ".png";
                    if (RenderCanvasToPng(canvasGo, edgePath, target.W, target.H)) saved++;

                    // (4) NUDGE PAD — it follows STATE, with no toggle for the player to find
                    //     (owner ruling 2026-08-09: "it should be smart... user should not need
                    //     to do anything"). The assertion is inverted from the old one: a
                    //     surviving toggle button would now be the defect, so its ABSENCE is
                    //     what gets checked.
                    //
                    // ── WO-942 gap 1: THIS CASE USED TO PROVE NOTHING. ───────────────────
                    // It photographed BYTE-IDENTICAL to (3) at all three sizes (88555 / 108491 /
                    // 118402 — the identical-file-size tell, UI_PLAYBOOK §13.1), for TWO reasons
                    // that both had to be fixed:
                    //   a. it never moved the ghost, so it re-shot (3)'s corner-clamped frame; and
                    //   b. the pad's visibility is the BRAIN's per-frame verdict, delivered via
                    //      SetNudgePadAllowed from an Update that DOES NOT RUN IN EDIT MODE — so
                    //      the stick built by SetState(Placing) was still SetActive(false) and
                    //      the shot contained no stick at all.
                    // Both are now driven EXPLICITLY, exactly as LayoutGhostControlsNow() already
                    // is and for the identical reason. A capture that cannot draw the thing it
                    // captures launders an unverified state as verified (§13).
                    hud.TrackGhost(new Vector2(target.W * 0.28f, target.H * 0.5f), true, null);
                    hud.LayoutGhostControlsNow();
                    hud.SetNudgePadAllowed(true);

                    if (canvasGo.transform.Find("BuildNudgePadToggle") != null)
                        Debug.LogWarning("[UICap-HL] a BuildNudgePadToggle still exists -- the '+' toggle was " +
                                         "RETIRED; the pad follows placement state. REAL gap, not noise.");
                    AssertNudgePadOnScreen(canvasGo, target.Tag, target.W, target.H);

                    string padPath = OutDir + "BuildGhostChips_padon_" + target.Tag + ".png";
                    if (RenderCanvasToPng(canvasGo, padPath, target.W, target.H)) saved++;
                    AssertShotsDiffer(edgePath, padPath, "BuildGhostChips edgeclamp vs padon", target.Tag);
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] build ghost chips capture threw: " + e);
                }
                finally
                {
                    if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                }
                return saved;
            });
        }

        // =====================================================================
        //  WO-942 — the assertions that close the two capture-case gaps
        // =====================================================================

        /// <summary>
        /// WO-942 gap 2 — the D17 SPRITE path's INVALID verdict, MEASURED.
        ///
        /// D17 gave the confirm verb a check-mark sprite, and a check-mark has no word to flip.
        /// So <c>BuildHudController</c> renders the invalid state as DIM + DISABLED: the icon
        /// Image goes to alpha 0.35 and the Button goes non-interactable, with the WORDED reason
        /// staying on the pill. Both halves of that are unphotographable in practice — an alpha
        /// change on a small glyph is precisely the judgement a PNG (and a red/green colourblind
        /// reader) should never be asked to make — and until now NOTHING measured them. This is
        /// the measurement. On the ASCII fallback path the chip flips its WORD instead, so that
        /// is what gets asserted there.
        ///
        /// Warn-only, matching every other assertion in this harness: it prints a REAL-gap line
        /// the capture judge reads, and never aborts a capture run mid-way.
        /// </summary>
        private static void AssertConfirmChipInvalid(GameObject canvasGo, string tag)
        {
            if (canvasGo == null) return;

            Transform chip = FindDescendant(canvasGo.transform, "OkChip");
            if (chip == null)
            {
                Debug.LogWarning("[UICap-HL] " + tag + ": no 'OkChip' under the build HUD canvas -- the confirm " +
                                 "verb was renamed or removed, so the D17 invalid verdict is unmeasured. " +
                                 "REAL gap, not noise.");
                return;
            }

            var btn = chip.GetComponent<Button>();
            if (btn == null)
                Debug.LogWarning("[UICap-HL] " + tag + ": 'OkChip' carries no Button -- confirm cannot be " +
                                 "disabled at all while the placement is invalid. REAL gap, not noise.");
            else if (btn.interactable)
                Debug.LogWarning("[UICap-HL] " + tag + ": 'OkChip' is still INTERACTABLE while the ghost is " +
                                 "invalid -- the player can commit a refused placement. REAL gap, not noise.");

            // Sprite path: the glyph Image dims. ASCII fallback: the label flips OK -> No.
            Transform icon = FindDescendant(chip, "ChipIcon");
            var iconImg = icon != null ? icon.GetComponent<Image>() : null;
            if (iconImg != null)
            {
                const float WantAlpha = 0.35f;   // BuildHudController: new Color(1,1,1,0.35f)
                if (Mathf.Abs(iconImg.color.a - WantAlpha) > 0.02f)
                    Debug.LogWarning("[UICap-HL] " + tag + ": D17 sprite path -- 'OkChip/ChipIcon' alpha is " +
                                     iconImg.color.a.ToString("F2") + ", expected " + WantAlpha.ToString("F2") +
                                     " for the invalid verdict. The check-mark reads as ENABLED while the " +
                                     "placement is refused. REAL gap, not noise.");
                else
                    Debug.Log("[UICap-HL] " + tag + ": D17 invalid verdict OK (sprite path: icon alpha " +
                              iconImg.color.a.ToString("F2") + ", confirm non-interactable).");
                return;
            }

            var label = chip.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
                Debug.LogWarning("[UICap-HL] " + tag + ": 'OkChip' has neither a ChipIcon nor a label -- the " +
                                 "confirm verb renders nothing at all. REAL gap, not noise.");
            // WO-1411 RE-POINTED 2026-09-06: the expected word was 'No', from the retired D17
            // ASCII fallback. The rail's verbs are now kit WORD buttons (PLACE / ROTATE /
            // CANCEL) and the confirm's blocked state flips its word to BLOCKED — so this is
            // the same measurement (the refusal must be readable WITHOUT hue), pointed at the
            // word the screen actually carries. The ChipIcon branch above is now unreachable on
            // a built rail; it is left standing so a future glyph experiment is still measured.
            else if (!string.Equals(label.text, "BLOCKED", StringComparison.Ordinal))
                Debug.LogWarning("[UICap-HL] " + tag + ": worded-verb path -- 'OkChip' label reads '" +
                                 label.text + "', expected 'BLOCKED' for the invalid verdict. The refusal would " +
                                 "rest on COLOUR ALONE, which the owner cannot see. REAL gap, not noise.");
            else
                Debug.Log("[UICap-HL] " + tag + ": invalid verdict OK (worded path: label 'BLOCKED', " +
                          "confirm non-interactable).");
        }

        /// <summary>
        /// WO-942 gap 1 — the nudge stick is ACTUALLY UP and ACTUALLY ON-SCREEN before the padon
        /// shot is taken. Edit-mode safe: the pad's visibility has just been driven explicitly by
        /// <c>SetNudgePadAllowed(true)</c>, because the brain's per-frame verdict never ticks here.
        /// An off-screen or hidden pad means the shot photographs a stick the player does not have.
        /// </summary>
        private static void AssertNudgePadOnScreen(GameObject canvasGo, string tag, int w, int h)
        {
            if (canvasGo == null) return;

            var pad = canvasGo.transform.Find("BuildNudgePad");
            if (pad == null || !pad.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[UICap-HL] " + tag + ": BuildNudgePad is absent/hidden while PLACING -- the " +
                                 "player has no way to nudge a piece and no toggle to summon one, and the " +
                                 "padon shot photographs nothing. REAL gap, not noise.");
                return;
            }

            // The host stretches the whole canvas; the STICK is the widget that has to be reachable.
            Transform stick = FindDescendant(pad, "AnalogStick") ?? FindDescendant(pad, "VirtualDPad");
            var root = canvasGo.GetComponent<RectTransform>();
            var srt = stick as RectTransform;
            if (stick == null || srt == null)
            {
                Debug.LogWarning("[UICap-HL] " + tag + ": BuildNudgePad is active but carries NO stick/d-pad " +
                                 "widget -- ElarionUiKit.BuildAnalogStick and the WO-611 d-pad fallback BOTH " +
                                 "failed to construct. REAL gap, not noise.");
                return;
            }

            if (!TryRectInRoot(srt, root, out Rect r))
            {
                Debug.LogWarning("[UICap-HL] " + tag + ": nudge stick rect could not be measured.");
                return;
            }

            Rect canvasRect = root.rect;
            bool inside = r.xMin >= canvasRect.xMin - GeoContainSlackPx && r.xMax <= canvasRect.xMax + GeoContainSlackPx
                       && r.yMin >= canvasRect.yMin - GeoContainSlackPx && r.yMax <= canvasRect.yMax + GeoContainSlackPx;
            if (!inside)
                Debug.LogWarning("[UICap-HL] " + tag + ": nudge stick rect (" + r + ") is NOT fully inside the " +
                                 canvasRect + " canvas at " + w + "x" + h + " -- part of the only pixel-nudge " +
                                 "control is unreachable. REAL gap, not noise.");
            else
                Debug.Log("[UICap-HL] " + tag + ": nudge stick UP and fully on-screen (" + stick.name +
                          ", " + r.width.ToString("F0") + "x" + r.height.ToString("F0") + " ref px).");
        }

        /// <summary>
        /// WO-942 gap 1 — the identical-file-size tell (UI_PLAYBOOK §13.1) turned into an
        /// assertion. Two capture cases that are meant to photograph DIFFERENT states and come out
        /// byte-identical did not photograph two states; one of them proved nothing, and a green
        /// run then launders that unverified state as verified. Byte length is the cheap, exact
        /// signal — PNGs of two genuinely different frames are never the same length.
        /// </summary>
        private static void AssertShotsDiffer(string pathA, string pathB, string what, string tag)
        {
            try
            {
                if (!File.Exists(pathA) || !File.Exists(pathB)) return;   // a missing shot is reported elsewhere
                long a = new FileInfo(pathA).Length;
                long b = new FileInfo(pathB).Length;
                if (a == b)
                    Debug.LogWarning("[UICap-HL] " + tag + ": " + what + " produced BYTE-IDENTICAL shots (" + a +
                                     " bytes each) -- the two cases photographed the SAME frame, so one of them " +
                                     "proves nothing (UI_PLAYBOOK 13.1). REAL gap, not noise.");
                else
                    Debug.Log("[UICap-HL] " + tag + ": " + what + " differ (" + a + " vs " + b + " bytes).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UICap-HL] " + tag + ": shot-difference check threw: " + e.Message);
            }
        }

        /// <summary>First descendant named <paramref name="name"/> (inactive included), or null.</summary>
        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && string.Equals(all[i].name, name, StringComparison.Ordinal)) return all[i];
            return null;
        }

        // =====================================================================
        //  WO-1010 P2 — the collapsed dock and its "^ Buildings (n)" way back
        // =====================================================================
        /// <summary>
        /// Shoot the palette dock in BOTH states: the open carousel, and collapsed-for-placing
        /// where the ONLY chrome left standing must be the restore tab.
        ///
        /// The collapsed shot is the point of this case. Collapse() hides every dock
        /// background so the field is clear, and until P2 that left NO route back to the
        /// carousel — so what this photograph has to establish is a negative plus a positive:
        /// the dock really is gone, AND exactly one labelled door remains. Neither half is
        /// visible to a compile gate or a data oracle.
        /// </summary>
        /// <summary>Shape of Data/Canonical/structures-catalog.json (mirrors the economy oracle's).</summary>
        private sealed class CaptureStructuresFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<DeNelle.Core.Catalog.CatalogEntry> Entries
                = new List<DeNelle.Core.Catalog.CatalogEntry>();
        }

        /// <summary>
        /// Populate CatalogRegistry from the canonical structures file so the palette has real
        /// cards to draw. Idempotent, and it registers ONLY ids that are absent, so it can never
        /// stomp a row a different capture case set up. Reads through CanonicalJson — the same
        /// source of truth the game and the economy oracle use — rather than a hand-rolled path,
        /// so a capture can never show cards the shipped catalog does not contain.
        /// </summary>
        private static void HydrateCatalogForCapture()
        {
            try
            {
                if (DeNelle.Core.Catalog.CatalogRegistry.Count > 0) return;

                string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/structures-catalog.json");
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogWarning("[UICap-HL] structures-catalog.json unreadable -- palette cards cannot be " +
                                     "captured; the dock will render empty and prove nothing about cards.");
                    return;
                }

                var settings = new JsonSerializerSettings
                {
                    Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                var file = JsonConvert.DeserializeObject<CaptureStructuresFile>(json, settings);
                if (file == null || file.Entries == null || file.Entries.Count == 0)
                {
                    Debug.LogWarning("[UICap-HL] structures-catalog.json deserialized to 0 entries -- no cards to capture.");
                    return;
                }

                int n = 0;
                foreach (var e in file.Entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.id)) continue;
                    if (DeNelle.Core.Catalog.CatalogRegistry.Get(e.id) == null)
                    { DeNelle.Core.Catalog.CatalogRegistry.Register(e); n++; }
                }
                Debug.Log("[UICap-HL] hydrated CatalogRegistry with " + n + " entry(ies) for the palette capture");
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] catalog hydration threw: " + e);
            }
        }

        private static int CapturePaletteCollapsed()
        {
            return ForEachTarget("BuildPaletteDock", target =>
            {
                int saved = 0;
                GameObject hostGo = null;
                GameObject canvasGo = null;
                try
                {
                    // The registry is populated at RUNTIME by the game's bootstrap, which never
                    // runs in an edit-mode capture — the first version of this case shot a dock
                    // reading "No buildables registered" and still reported green, which would
                    // have made the cards look verified when nothing about them was. Hydrate
                    // from the same canonical file the economy oracle uses.
                    HydrateCatalogForCapture();

                    hostGo = new GameObject("BuildPaletteUI_Capture");
                    var palette = hostGo.AddComponent<DeNelle.Village.BuildPaletteUI>();
                    palette.Configure(DeNelle.Core.Catalog.BuildType.Town);
                    palette.Show();

                    var canvasTf = hostGo.transform.Find("BuildPaletteCanvas");
                    canvasGo = canvasTf != null ? canvasTf.gameObject : null;
                    if (canvasGo == null)
                    {
                        // ElarionUiKit.BuildModalCanvas parents the canvas at the SCENE ROOT,
                        // not under the palette host — so this scan is the normal path, not a
                        // fallback. That distinction caused a real bug: destroying only the
                        // host leaked the canvas, canvases ACCUMULATED across the three target
                        // sizes, and this scan then returned target 1's stale, already-COLLAPSED
                        // canvas. The 2340 and 2670 "open" shots were byte-identical to their
                        // own collapsed shots and showed nothing but the restore tab. The
                        // capture reported green the whole time. Take the NEWEST match, and
                        // destroy it explicitly below.
                        foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                            if (c != null && c.gameObject.name == "BuildPaletteCanvas") canvasGo = c.gameObject;
                    }
                    if (canvasGo == null)
                    {
                        Debug.LogWarning("[UICap-HL] BuildPaletteCanvas not found -- palette dock capture skipped.");
                        return 0;
                    }

                    // (1) OPEN — tabs + card carousel, the state the player picks from.
                    if (RenderCanvasToPng(canvasGo, OutDir + "BuildPaletteDock_open_" + target.Tag + ".png",
                        target.W, target.H)) saved++;

                    // (2) COLLAPSED — dock chrome gone, restore tab standing.
                    palette.Collapse("Arcane Spire");
                    if (RenderCanvasToPng(canvasGo, OutDir + "BuildPaletteDock_collapsed_" + target.Tag + ".png",
                        target.W, target.H)) saved++;

                    // The tab is the WHOLE point of the collapsed state; if it is absent the
                    // png would look like a correct empty field and hide a dead end.
                    var tab = canvasGo.transform.Find("BuildPaletteRestoreTab");
                    if (tab == null || !tab.gameObject.activeInHierarchy)
                        Debug.LogWarning("[UICap-HL] collapsed palette has NO active BuildPaletteRestoreTab -- " +
                                         "the player would have no way back to the carousel. REAL gap, not noise.");
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] palette dock capture threw: " + e);
                }
                finally
                {
                    // Destroy the ROOT canvas too, not just the host — see the scan comment
                    // above. Sweep by name so a canvas from an earlier target that somehow
                    // survived cannot poison the next one either.
                    if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
                    if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                    foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                        if (c != null && c.gameObject.name == "BuildPaletteCanvas")
                            UnityEngine.Object.DestroyImmediate(c.gameObject);
                }
                return saved;
            });
        }

        // =====================================================================
        //  WO-952 -- the EndState (wave-clear) banner, WITH EYES AND WITH A NUMBER.
        // ---------------------------------------------------------------------
        //  THE DEFECT THIS EXISTS FOR (F8 daemon, 2026-08-10, captured TWICE in one
        //  session on the desktop exe -- capture-20260810-102345.md and seq 2268):
        //
        //    [Flow:EndState] body rows COMPRESSED to fit: need=276px well=249px scale=0.9
        //    - the panel hit its screen-height clamp; every band is now below its own
        //      content size
        //
        //  The geometry half of WO-952 landed on 2026-08-10 (EndStateView.Bind's owned
        //  compact solve). It shipped SOURCE-REASONED: no capture case covered the
        //  EndState at all, so nothing in the harness could have told us if it worked --
        //  and "no capture case" is indistinguishable from "the case passes".
        //
        //  WHAT THIS ASSERTS, and why it is not a hollow assertion
        //  (INSTRUMENTATION_STANDARD 1.4b -- a trace that cannot report failure is a bug):
        //    (1) The banner's OWN instrumentation is TAPPED for the duration of the build
        //        (FlowTrace.Sink swap, restored in a finally). If the `COMPRESSED to fit`
        //        Fail line fires, this run FAILS -- the absence of that line is the
        //        acceptance signal the WO asked for, and it is now checked, not hoped for.
        //    (2) It is NOT trusted on its own. The SETTLED layout is MEASURED: the resolved
        //        body-well rect and the resolved band stack, both read back off the real
        //        RectTransforms in kit reference px, and the compression factor is
        //        RECOMPUTED from them (extent / need) instead of read off the view's own
        //        arithmetic. A pass prints all four numbers; a run where the numbers cannot
        //        be obtained is reported as a FAILURE, never as silence.
        //  So this case fails loudly in three distinct ways: the trace fired, the measured
        //  fit was short, or nothing could be measured at all.
        //
        //  FIXTURES, not the live factory. EndStateVM.FromWaveClear(n) reads the live wall
        //  damage ledger / wallet / repair controller, none of which stand up in a
        //  synchronous edit-mode render -- and a fixture is what pins the WORST case anyway.
        //  Two are built: the Repair-All banner (a CTA + a full 4-row damage report -- the
        //  exact shape that broke, because a CTA-carrying banner kept the stale close-band
        //  reservation) and the plain no-CTA banner (the path that never broke, so a
        //  regression in it would otherwise be silent).
        // =====================================================================

        /// <summary>Per-case measured fit record (all lengths in kit reference px).</summary>
        private struct EndStateFit
        {
            public string Label;
            public float NeedPx;          // the view's own traced content demand
            public float WellPx;          // MEASURED resolved body-well height
            public float ExtentPx;        // MEASURED first-band top -> last-band bottom
            public int Bands;
            public float MeasuredScale;   // ExtentPx / NeedPx -- recomputed, not read
            public float UnitFactor;      // measured root px per kit reference px (~1.0)
            public bool TracedCompression;
        }

        private static readonly List<EndStateFit> _endStateFits = new List<EndStateFit>();
        private static readonly List<string> _endStateFitFailures = new List<string>();

        /// <summary>Compression is real below this (the view's own threshold, same rationale:
        /// a self-fitting solve lands on target within float residue, a real clamp lands far
        /// below -- the captured defect measured 0.9).</summary>
        private const float EndStateFitFloor = 0.995f;

        /// <summary>Optional probe run on the settled, camera-space layout inside
        /// <see cref="RenderCanvasToPng"/> (canvas, label, w, h).</summary>
        private static Action<GameObject, string, int, int> _settledProbe;

        // Per-case hand-off between the trace tap and the settled-layout probe.
        private static float _endStateNeedPx;
        private static bool _endStateSawCompression;

        /// <summary>Forwards every FlowTrace line to the real sink AND keeps the
        /// [Flow:EndState] ones, so the panel's own instrumentation becomes this
        /// harness's evidence instead of scrolling past in a log nobody greps.</summary>
        private sealed class EndStateTraceTap : ITraceSink
        {
            private readonly ITraceSink _inner;
            public readonly List<string> Lines = new List<string>();

            public EndStateTraceTap(ITraceSink inner) { _inner = inner; }

            private void Keep(string line)
            {
                if (!string.IsNullOrEmpty(line) && line.IndexOf("[Flow:EndState]", StringComparison.Ordinal) >= 0)
                    Lines.Add(line);
            }

            public void Info(string line)  { Keep(line); if (_inner != null) _inner.Info(line); }
            public void Warn(string line)  { Keep(line); if (_inner != null) _inner.Warn(line); }
            public void Error(string line) { Keep(line); if (_inner != null) _inner.Error(line); }
        }

        private static int CaptureEndStateWaveClear()
        {
            return ForEachTarget("EndStateWaveClear", CaptureEndStateWaveClearOnce);
        }

        private static int CaptureEndStateWaveClearOnce(CaptureTarget target)
        {
            int saved = 0;
            saved += CaptureOneEndStateBanner(target, "EndStateWaveClear_repairAll", true);
            saved += CaptureOneEndStateBanner(target, "EndStateWaveClear_plain", false);
            return saved;
        }

        /// <summary>Build ONE real compact EndState banner, tap its trace, shoot it, and
        /// measure its settled fit. <paramref name="withCta"/> selects the Repair-All
        /// (CTA-carrying) shape -- the one the 2026-08-10 capture caught.</summary>
        private static int CaptureOneEndStateBanner(CaptureTarget target, string caseName, bool withCta)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            EndStateView view = null;
            GameObject canvasGo = null;
            ITraceSink prevSink = FlowTrace.Sink;
            var tap = new EndStateTraceTap(prevSink);
            string label = caseName + "_" + target.Tag;

            _endStateNeedPx = 0f;
            _endStateSawCompression = false;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                FlowTrace.Sink = tap;
                view = EndStateView.Show(BuildWaveClearFixture(withCta));
                FlowTrace.Sink = prevSink;

                if (view == null)
                {
                    RecordEndStateFitFailure(label + ": EndStateView.Show returned null -- the banner did " +
                        "not build, so this run measured NOTHING about its fit (an unbuilt panel is a " +
                        "failure of the case, not a pass by absence).");
                    return 0;
                }
                canvasGo = view.gameObject;

                // The view's own numbers, harvested from its instrumentation. `need=` is printed
                // by BOTH the owned compact solve's Step and the fit Fail/Step lines, so it is
                // present on every path a compact banner can take.
                _endStateNeedPx = ParseNumberAfter(tap.Lines, "need=");
                _endStateSawCompression = ContainsLine(tap.Lines, "body rows COMPRESSED to fit");

                // The reveal is a coroutine, and coroutines never tick in an edit-mode render --
                // so every CanvasGroup is still parked at its start-of-tween alpha 0 and the frame
                // would be blank. Stamp the tween's FINISHED state (what the player sees a beat
                // later); geometry is untouched by this, only opacity.
                foreach (var cg in canvasGo.GetComponentsInChildren<CanvasGroup>(true))
                    if (cg != null) cg.alpha = 1f;

                _settledProbe = MeasureEndStateFit;
                if (RenderCanvasToPng(canvasGo, OutDir + label + ".png", target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] end-state banner capture threw: " + e);
                RecordEndStateFitFailure(label + ": the capture threw before it could measure the fit (" +
                    e.GetType().Name + ": " + e.Message + ")");
            }
            finally
            {
                _settledProbe = null;
                FlowTrace.Sink = prevSink;
                // Edit-mode teardown MUST be DestroyImmediate (the view's own Close uses runtime
                // Destroy). EndStateView.OnDestroy unsubscribes sceneLoaded and clears the
                // end-state posture signal, so nothing leaks into the editor session.
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        /// <summary>The worst-case wave-clear banner as DATA. Row/label shapes mirror
        /// EndStateVM.FromWaveClear's own output (rewards + a damage report, capped at its
        /// CompactMaxSpoilRows = 4); the live factory is not callable headlessly because it
        /// reads the wall-damage ledger and the wallet.</summary>
        private static EndStateVM BuildWaveClearFixture(bool withCta)
        {
            var vm = new EndStateVM
            {
                Kind = EndStateKind.WaveResults,
                Title = "Wave 7 Cleared!",
                Subtitle = "The wave broke against your walls.\nThe north gate took the worst of it.",
                Compact = true,
                PrimaryLabel = null,      // compact banners auto-dismiss; no primary CTA
                PrimaryRoute = "dismiss",
                AutoDismissSeconds = 0f,  // no coroutine to leave pending in edit mode
                Stars = -1,
                TimeSeconds = -1f,
            };

            vm.Spoils.Add(new SpoilRowVM { Label = "Wood", Amount = "+240" });
            vm.Spoils.Add(new SpoilRowVM { Label = "Iron", Amount = "+85" });
            if (withCta)
            {
                // The CTA path: a full damage report (4 rows = the banner's hard cap) under a
                // Repair-All button. This is the shape that produced need=276px / well=249px.
                vm.Spoils.Add(new SpoilRowVM { Label = "North Gate", Amount = "DESTROYED, looted 120" });
                vm.Spoils.Add(new SpoilRowVM { Label = "Wall x3", Amount = "damaged" });
                vm.CtaLabel = "Repair All - 120 wood, 40 iron";
                vm.CtaEnabled = true;
                vm.CtaRoute = "repair-all";
            }
            return vm;
        }

        /// <summary>MEASURE the banner's settled fit off its real RectTransforms and RECOMPUTE
        /// the compression factor. Runs inside RenderCanvasToPng (camera-space, post-rebuild),
        /// which is the only place a rect read is in kit reference px.
        ///
        /// The band stack's measured extent is, by construction, need x scale: BuildBody lays
        /// bands top-down at (px * scale) with (BandGapPx * scale) between them, so
        /// first-band-top -> last-band-bottom == (sum px + gaps) * scale == need * scale. So
        /// extent / need IS the compression factor -- derived from resolved geometry, with no
        /// dependence on the view's own arithmetic. Anything that cannot be measured is
        /// recorded as a FAILURE: silence is not a pass.</summary>
        private static void MeasureEndStateFit(GameObject canvasGo, string label, int w, int h)
        {
            RectTransform root = canvasGo != null ? canvasGo.GetComponent<RectTransform>() : null;
            if (root == null)
            {
                RecordEndStateFitFailure(label + ": no root RectTransform on the captured canvas -- nothing measurable.");
                return;
            }

            RectTransform well = null;
            foreach (var rt in canvasGo.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt != null && rt.gameObject.name == "Zone_RewardWell") { well = rt; break; }
            }
            if (well == null)
            {
                RecordEndStateFitFailure(label + ": the banner built no 'Zone_RewardWell' -- EndStateView's " +
                    "body well is gone or renamed, so this gate is measuring nothing and must not read as green.");
                return;
            }

            var bandRects = new List<Rect>();
            for (int i = 0; i < well.childCount; i++)
            {
                var child = well.GetChild(i) as RectTransform;
                if (child == null || child.gameObject.name != "Band") continue;
                if (TryRectInRoot(child, root, out Rect br)) bandRects.Add(br);
            }
            if (bandRects.Count == 0)
            {
                RecordEndStateFitFailure(label + ": zero 'Band' rows resolved inside the body well -- the " +
                    "banner rendered no content bands, so its fit is unproven (and the png is empty of rows).");
                return;
            }
            if (!TryRectInRoot(well, root, out Rect wellRect))
            {
                RecordEndStateFitFailure(label + ": the body well resolved to a degenerate rect -- no measurement possible.");
                return;
            }

            float top = float.MinValue, bottom = float.MaxValue;
            float outsideBy = float.MinValue;
            foreach (var br in bandRects)
            {
                if (br.yMax > top) top = br.yMax;
                if (br.yMin < bottom) bottom = br.yMin;
                outsideBy = Mathf.Max(outsideBy, OutsideBy(br, wellRect));
            }
            float extentPx = top - bottom;

            // Root-space px per kit reference px. Expected ~1.0 (the kit's own reference space);
            // measured rather than assumed, because `need` is a KIT number and the extent is a
            // MEASURED one -- comparing them across a silently different unit is exactly the kind
            // of hidden mismatch that makes a green gate meaningless.
            float kitH = ElarionUiKit.PostScaleCanvasHeight(canvasGo.transform);
            float unit = kitH > 1f ? root.rect.height / kitH : 0f;
            if (unit <= 0.01f) unit = 1f;

            float needRootPx = _endStateNeedPx * unit;
            float measuredScale = needRootPx > 1f ? extentPx / needRootPx : 0f;

            var fit = new EndStateFit
            {
                Label = label,
                NeedPx = _endStateNeedPx,
                WellPx = wellRect.height / Mathf.Max(0.0001f, unit),
                ExtentPx = extentPx / Mathf.Max(0.0001f, unit),
                Bands = bandRects.Count,
                MeasuredScale = measuredScale,
                UnitFactor = unit,
                TracedCompression = _endStateSawCompression,
            };
            _endStateFits.Add(fit);

            string numbers = "need=" + fit.NeedPx.ToString("0") + "px (traced) well=" +
                             fit.WellPx.ToString("0") + "px (MEASURED) bands=" + fit.Bands +
                             " stack=" + fit.ExtentPx.ToString("0") + "px (MEASURED) -> measured scale=" +
                             fit.MeasuredScale.ToString("0.###") + " [root px per ref px = " +
                             unit.ToString("0.###") + "]";

            if (_endStateNeedPx <= 1f)
            {
                RecordEndStateFitFailure(label + ": the banner printed NO `need=` line, so there is no content " +
                    "demand to measure the well against. " + numbers + ". Treat this case as unproven, not passing.");
                return;
            }
            if (fit.TracedCompression)
            {
                RecordEndStateFitFailure(label + ": the panel's own net fired `body rows COMPRESSED to fit` -- " +
                    "every band is below its own content size. " + numbers);
                return;
            }
            if (measuredScale < EndStateFitFloor)
            {
                RecordEndStateFitFailure(label + ": MEASURED compression -- the resolved band stack is only " +
                    measuredScale.ToString("0.###") + " of the content's demand. " + numbers);
                return;
            }
            if (outsideBy > GeoContainSlackPx)
            {
                RecordEndStateFitFailure(label + ": a body band resolves " + outsideBy.ToString("0.#") +
                    "px OUTSIDE the body well it is laid into. " + numbers);
                return;
            }

            Debug.Log("[UICap-ENDSTATE] " + label + " fits: " + numbers);
        }

        private static void RecordEndStateFitFailure(string message)
        {
            _endStateFitFailures.Add(message);
            Debug.LogError("[UICap-ENDSTATE] " + message);
        }

        /// <summary>First number following <paramref name="token"/> in any of the captured lines
        /// (e.g. "need=276px" -> 276). Returns 0 when the token never appeared.</summary>
        private static float ParseNumberAfter(List<string> lines, string token)
        {
            if (lines == null || string.IsNullOrEmpty(token)) return 0f;
            foreach (var line in lines)
            {
                if (line == null) continue;
                int i = line.IndexOf(token, StringComparison.Ordinal);
                if (i < 0) continue;
                i += token.Length;
                int start = i;
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == '-')) i++;
                if (i > start && float.TryParse(line.Substring(start, i - start),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return v;
            }
            return 0f;
        }

        private static bool ContainsLine(List<string> lines, string needle)
        {
            if (lines == null) return false;
            foreach (var line in lines)
                if (line != null && line.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>The WO-952 acceptance marker. DISTINCT from UI_CAPTURE_OK / UI_GEOMETRY_OK
        /// (CLAUDE.md 8: one marker per entry point -- a shared string is how a partial pass once
        /// read as a full one), and it always carries the MEASUREMENTS, never a bare "ok".</summary>
        private static void ReportEndStateFit()
        {
            if (_endStateFits.Count == 0 && _endStateFitFailures.Count == 0)
            {
                Debug.LogError("UI_ENDSTATE_FIT_FAIL x0 -- ZERO end-state banners were measured this run, so " +
                               "nothing here proves the WO-952 wave-clear fit. An absent case is not a passing case.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var f in _endStateFits)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(f.Label).Append(" need=").Append(f.NeedPx.ToString("0"))
                  .Append("px well=").Append(f.WellPx.ToString("0"))
                  .Append("px stack=").Append(f.ExtentPx.ToString("0"))
                  .Append("px bands=").Append(f.Bands)
                  .Append(" scale=").Append(f.MeasuredScale.ToString("0.###"));
            }

            if (_endStateFitFailures.Count > 0)
            {
                Debug.LogError("UI_ENDSTATE_FIT_FAIL x" + _endStateFitFailures.Count + " failure(s) over " +
                               _endStateFits.Count + " measured case(s) -- see the " +
                               "[UICap-ENDSTATE] lines above. Measured: " +
                               (sb.Length > 0 ? sb.ToString() : "(nothing measurable)"));
                return;
            }

            Debug.Log("UI_ENDSTATE_FIT_OK " + _endStateFits.Count + " banners -- no `body rows COMPRESSED to " +
                      "fit` fired and the RESOLVED band stack measures at least " +
                      EndStateFitFloor.ToString("0.###") + " of the content's own demand in every case. " +
                      "Measured: " + sb);
        }

        private static bool RenderCanvasToPng(GameObject canvasGo, string path, int w, int h)
        {
            if (canvasGo == null) return false;

            var canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[UICap-HL] " + path + " -- no Canvas on target; skipped.");
                return false;
            }

            RenderMode prevMode = canvas.renderMode;
            Camera prevCam = canvas.worldCamera;
            float prevPlane = canvas.planeDistance;

            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                // Throwaway camera -> RenderTexture at the requested resolution.
                camGo = new GameObject("~UICapCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.03f;
                cam.farClipPlane = 1000f;
                cam.cullingMask = ~0;   // see every layer (the UI may be on any layer)

                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                rt.Create();
                cam.targetTexture = rt;

                // Flip the overlay canvas to camera-space so the camera can capture it.
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f;

                // CanvasScaler.Update does NOT run in a synchronous edit-mode call, so set
                // the ScaleWithScreenSize scale factor by hand (mirrors the scaler math) --
                // the card's fraction anchors reflow regardless, but font point size needs it
                // so overlap reproduces exactly as on device.
                ApplyScreenSpaceScale(canvas, w, h);

                // Force a full synchronous layout + graphic + TMP rebuild (twice: TMP
                // auto-size for the flavor block can need a second pass to settle).
                for (int pass = 0; pass < 2; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    var rootRt = canvasGo.GetComponent<RectTransform>();
                    if (rootRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
                    foreach (var t in canvasGo.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (t != null) t.ForceMeshUpdate();
                    }
                    Canvas.ForceUpdateCanvases();
                }

                // THE NUMERIC GEOMETRY GATE. Runs on the SETTLED layout, before the pixels
                // exist -- eyes-on review is not a defence and never was (CLAUDE.md §12).
                AuditGeometry(canvasGo, Path.GetFileNameWithoutExtension(path), w, h);

                // A PANEL-SPECIFIC measurement on the SAME settled, camera-space layout the
                // audit just ran on. This is the only point in the run where a rect read is
                // in kit reference px (an overlay canvas's own rect is still the editor's
                // 640x480 in batchmode -- see the file banner), so any probe that has to
                // compare a RESOLVED rect against an authored px budget must run HERE.
                // Set by the capture that needs it, cleared in its finally; guarded, because a
                // throwing probe must never cost the run its screenshot.
                if (_settledProbe != null)
                {
                    try { _settledProbe(canvasGo, Path.GetFileNameWithoutExtension(path), w, h); }
                    catch (Exception pe) { Debug.LogError("[UICap-HL] settled-layout probe threw: " + pe); }
                }

                // Render (SRP-correct path first; fall back to legacy Camera.Render).
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) cam.SubmitRenderRequest(req);
                else cam.Render();

                // Read the pixels back.
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                tex.Apply(false);

                // THE BLANK GUARD: measure the pixels before shipping them. A flat frame is
                // the no-graphics failure mode, not a screenshot -- refuse to write it, so a
                // reviewer sees an honest MISSING instead of a convincing empty rectangle,
                // and UI_CAPTURE_OK cannot be inflated by frames with nothing in them.
                if (IsBlank(tex, out string measure))
                {
                    Debug.LogError("[UICap-HL] BLANK RENDER (not written): " + path +
                                   " -- " + measure + ". Counted as a FAILURE, not a shot.");
                    return false;
                }

                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    Debug.LogWarning("[UICap-HL] " + path + " -- EncodeToPNG produced no bytes; skipped.");
                    return false;
                }

                File.WriteAllBytes(path, png);
                Debug.Log("[UICap-HL] saved " + w + "x" + h + " -> " + Path.GetFullPath(path) +
                          " (" + png.Length + " bytes, " + measure + ")");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] render " + path + " threw: " + e);
                return false;
            }
            finally
            {
                // Restore the canvas so a reused card renders identically on the next call.
                RenderTexture.active = prevActive;
                if (canvas != null)
                {
                    canvas.renderMode = prevMode;
                    canvas.worldCamera = prevCam;
                    canvas.planeDistance = prevPlane;
                }
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            }
        }

        // =====================================================================
        //  AuditGeometry -- the numeric layout gate (2026-08-05).
        // ---------------------------------------------------------------------
        //  A screenshot only catches a layout defect if a HUMAN opens it and sees the
        //  defect. That is not a gate; it is a hope. Two RCAs on 2026-08-05 landed on
        //  the same finding -- the harness was structurally blind, and green runs
        //  shipped broken panels to the owner.
        //
        //  So every captured canvas is now MEASURED on its settled layout. All rects
        //  are converted into ROOT-CANVAS LOCAL SPACE, which is the kit's reference-px
        //  space -- the same units MinTouchPx and the zone fractions are authored in --
        //  so every number in a failure message is directly comparable to the source.
        //
        //  RULE 1 [text-off-plate]  A TMP_Text under a kit Zone_Body must be fully
        //     inside that body's ZoneBacking rect. The ZoneBacking IS the black plate
        //     (ElarionUiKit ~line 690: ZoneBacking(layout.body, ObsidianFill)), and
        //     "the caption fell off the black" is the exact founding-Echo-card defect.
        //     Text under a RectMask2D/Mask is SKIPPED: masked content is clipped by
        //     construction and cannot visibly spill.
        //
        //  RULE 2 [button-overlap]  Two SIBLING Buttons must not overlap. Sibling
        //     buttons are bands laid in one host, and an overlap there is the
        //     "options stacked" / "only the bottom chip is tappable" defect.
        //
        //  RULE 3 [button-over-text]  A VISIBLE Button must not overlap a TMP_Text it
        //     does not own. Its own label is a descendant, so it is excluded; INVISIBLE
        //     buttons (no targetGraphic, or alpha < 0.05) are excluded too -- those are
        //     hit-area/scrim overlays and cannot collide visually with anything.
        //
        //  RULE 4 [sub-touch-floor]  A kit button (one carrying the ClampMinTouch guard)
        //     whose AUTHORED band resolves under ElarionUiKit.MinTouchPx. The guard grows
        //     it in LateUpdate, which never runs in an edit-mode capture -- so what this
        //     harness measures IS the pre-grow authored size. That is the point: the
        //     sub-floor band is the DEFECT SIGNATURE; the symmetric growth is only its
        //     consequence, and by the time you can see the growth the neighbour is
        //     already overlapped.
        //
        //  Elements that are clipped and NOT fully inside their clipper are skipped by
        //  rules 2 and 3: a scrolled-out row's pixel adjacency is unknowable.
        // =====================================================================
        private static readonly List<string> _geoFailures = new List<string>();
        private static int _geoCanvasesChecked;

        /// <summary>Containment slack, reference px. Sub-pixel seams are not defects.</summary>
        private const float GeoContainSlackPx = 1.5f;
        /// <summary>Cap on printed failure lines (all are still counted in the marker).</summary>
        private const int GeoMaxPrintedLines = 60;

        private static void AuditGeometry(GameObject canvasGo, string label, int w, int h)
        {
            RectTransform root = canvasGo != null ? canvasGo.GetComponent<RectTransform>() : null;
            if (root == null) return;

            _geoCanvasesChecked++;
            var fails = new List<string>();
            var crossFails = new List<string>();   // WO-1060 Assert B, cross-parent half (touch marker only)
            var glyphFails = new List<string>();        // WO-1630 Assert C (glyph marker only)
            var glyphUnmeasured = new List<string>();   // WO-1630 Assert C stand-down (glyph marker only)
            // WO-1630: labels Assert C actually MEASURED on this panel. Zero findings and zero
            // measured is NOT a clean panel -- it is a panel nothing looked at, and the two must
            // never print the same line. Stays 0 if the audit throws before Assert C runs.
            int glyphLabelsMeasured = 0;
            string at = " [" + label + " @" + w + "x" + h + "]";

            try
            {
                TMP_Text[] texts = canvasGo.GetComponentsInChildren<TMP_Text>(false);
                Button[] buttons = canvasGo.GetComponentsInChildren<Button>(false);

                if (!TryRectInRoot(root, root, out Rect canvasRect)) canvasRect = new Rect(0f, 0f, w, h);
                float canvasArea = Mathf.Max(1f, canvasRect.width * canvasRect.height);

                // ---- RULE 1: text must stay on its panel's black plate -------------
                foreach (var t in texts)
                {
                    if (t == null || !t.enabled || !t.gameObject.activeInHierarchy) continue;
                    if (string.IsNullOrEmpty(t.text) || t.color.a < 0.05f) continue;
                    var trt = t.transform as RectTransform;
                    if (!TryRectInRoot(trt, root, out Rect tr)) continue;

                    RectTransform body = ZoneBodyAbove(t.transform);
                    if (body == null) continue;                                  // header/footer/close copy
                    if (NearestClipper(t.transform, body.transform) != null) continue;   // masked well

                    RectTransform plate = PlateOf(body);
                    if (!TryRectInRoot(plate, root, out Rect pr)) continue;

                    float over = OutsideBy(tr, pr);
                    if (over > GeoContainSlackPx)
                        fails.Add("TEXT OFF PLATE" + at + " '" + PathOf(t.transform, canvasGo.transform) +
                                  "' (\"" + Snippet(t.text) + "\") overflows its layout.body ZoneBacking by " +
                                  over.ToString("0.#") + " ref px -- text " + RectStr(tr) +
                                  " vs plate " + RectStr(pr) +
                                  ". This is the founding-Echo-card defect: copy rendered off the black.");
                }

                // ---- RULES 2, 3, 4: DELEGATED TO DeNelle.Core.UI.LayoutOracle ------
                //
                //  ⛔ THESE THREE RULES USED TO LIVE HERE, AND THAT IS WHY NOBODY HAD EVER
                //  SEEN THEM GO RED. They ran only on the headless screenshot path, and
                //  nothing could reach them from a regression suite (DeNelle.Editor
                //  references DeNelle.EditorRegression, so the reverse reference is a
                //  cycle). WO-1060 section 6.2 requires the oracle be PROVEN red before
                //  anyone trusts it green, so the rules moved to Core where BOTH callers
                //  share one implementation:
                //      this harness       -> every captured panel, three aspects
                //      UiTouchClampRegression -> synthetic canvases with authored defects
                //  Two oracles disagreeing about the same canvas would have been worse
                //  than none; there is exactly one, and it is LayoutOracle.Audit.
                //
                //  ROUTING IS UNCHANGED, deliberately. SIBLING overlaps stay in `fails`
                //  -- that is the pre-existing UI_GEOMETRY gate, byte-for-byte. The NEW
                //  cross-parent pairs go to `crossFails`, which feeds ONLY the WO-1060
                //  touch marker, because section 5 is explicit: four panels are known-bad
                //  today and a widened assert wired straight into a live gate would turn
                //  every commit red and be suppressed within the week.
                //  WO-1630: Assert C's two kinds are branched out FIRST and go to NEITHER
                //  bucket. Falling through to `fails` would put glyph survival into the
                //  pre-existing geometry gate on its very first run, with nobody having
                //  ever measured how many labels truncate across the captured set -- the
                //  exact "a widened assert wired straight into a live gate turns every
                //  commit red and is suppressed within the week" outcome §5 above forbids.
                //  It gets its own tally and its own marker instead; see ReportGlyphOracle.
                foreach (var f in LayoutOracle.Audit(canvasGo, label, w, h, out glyphLabelsMeasured))
                {
                    if (f.Kind == LayoutOracle.FindingKind.TextTruncated)
                        glyphFails.Add(f.Message);   // WO-1630 Assert C
                    else if (f.Kind == LayoutOracle.FindingKind.TextUnmeasured)
                        glyphUnmeasured.Add(f.Message);   // WO-1630 Assert C, stand-down half
                    else if (f.Kind == LayoutOracle.FindingKind.ButtonsOverlap && !f.SameParent)
                        crossFails.Add(f.Message);   // WO-1060 Assert B, cross-parent half
                    else
                        fails.Add(f.Message);        // unchanged gate
                }
            }
            catch (Exception e)
            {
                fails.Add("GEOMETRY AUDIT THREW" + at + " " + e.GetType().Name + ": " + e.Message);
                // WO-1630: the throw is already a geometry FAIL, but Assert C must not be able
                // to count this panel as measured-and-clean. It asserted nothing here.
                glyphUnmeasured.Add("TEXT UNMEASURED" + at + " the audit THREW before Assert C " +
                                    "could finish (" + e.GetType().Name + ") -- no label on this " +
                                    "panel had its glyph survival proved this run.");
            }

            _geoFailures.AddRange(fails);

            // WO-1060: the CLAMP/OVERLAP subset gets its own tally so it can carry its own marker.
            // Classified by each rule's own message prefix rather than by a parallel list, so a new
            // rule cannot be added to one bucket and silently forgotten in the other.
            _touchPanelsChecked++;
            bool clean = true;
            bool baselined = IsTouchBaselined(label);
            for (int i = 0; i < fails.Count; i++)
            {
                string f = fails[i];
                if (f.StartsWith("BUTTONS OVERLAP", StringComparison.Ordinal) ||       // Assert B
                    f.StartsWith("BUTTON OVER TEXT", StringComparison.Ordinal) ||      // Assert B (occlusion)
                    f.StartsWith("SUB-TOUCH-FLOOR BAND", StringComparison.Ordinal))    // Assert A
                {
                    clean = false;
                    if (!baselined) _touchFailures.Add(f);
                }
            }
            for (int i = 0; i < crossFails.Count; i++)
            {
                clean = false;
                if (!baselined) _touchFailures.Add(crossFails[i]);
            }
            if (clean) _touchPanelsClean++;
            else if (baselined)
                Debug.LogWarning("[touch-oracle] BASELINED (known debt, still red) " + label +
                                 " -- this panel's WO removes its own allow-list entry when it lands.");

            // WO-1630: Assert C's own tally, kept apart from the touch one by KIND rather than
            // by message prefix. The touch classifier above reads prefixes because its three
            // rules already share the `fails` list; Assert C never enters that list, so a
            // prefix test here would be a second, weaker copy of a routing decision the
            // `foreach` above already made. Do NOT add these prefixes to the touch classifier.
            _glyphPanelsChecked++;
            _glyphLabelsMeasured += glyphLabelsMeasured;
            _glyphLabelFindings += glyphFails.Count;
            _glyphUnmeasuredCount += glyphUnmeasured.Count;
            for (int i = 0; i < glyphUnmeasured.Count; i++) _glyphUnproved.Add(glyphUnmeasured[i]);
            // WO-1636: split the panel's findings into KNOWN DEBT and NEW. A baselined finding
            // is warned about and kept out of the marker; anything else reds, including a NEW
            // finding on a panel that already carries entries -- the baseline is keyed per
            // finding precisely so that stays true.
            int baselinedHere = 0;
            var freshFails = new List<string>();
            for (int i = 0; i < glyphFails.Count; i++)
            {
                if (IsGlyphBaselined(label, glyphFails[i])) baselinedHere++;
                else freshFails.Add(glyphFails[i]);
            }
            _glyphBaselinedCount += baselinedHere;

            // ONE COMPACT LINE PER PANEL, ALWAYS. The per-finding lines below are capped at
            // GeoMaxPrintedLines -- the seeding run hit that cap and 8 findings were never read,
            // which is exactly why this line exists and is never capped.
            if (glyphFails.Count > 0)
                _glyphPanelTally.Add(label + " @" + w + "x" + h + ": " + glyphFails.Count +
                                     " truncated label(s) (" + baselinedHere + " baselined, " +
                                     freshFails.Count + " new)");

            if (freshFails.Count == 0) _glyphPanelsClean++;
            else
                for (int i = 0; i < freshFails.Count; i++) _glyphFailures.Add(freshFails[i]);
        }

        // ---------------------------------------------------------------------
        //  WO-1060 section 5 -- THE BASELINE ALLOW-LIST, AND ITS EXPIRY RULE.
        //
        //  Four panels are known-bad TODAY. Turning the oracle on red would block
        //  every commit, and a gate that blocks everything gets switched off, not
        //  fixed. So these four are reported as WARNINGS and excluded from the
        //  marker -- and NOTHING ELSE IS.
        //
        //  ⛔ THIS LIST MAY ONLY EVER SHRINK. Each fix deletes its own entry in the
        //  same commit; adding an entry requires an owner ruling. When the last one
        //  goes, DELETE THE MECHANISM -- an empty suppression list is an invitation
        //  to add to it.
        //
        //  ⚠ NightMarket is deliberately NOT here. It is the panel this oracle was
        //  extended for; it must be able to go RED, or adding it to the capture set
        //  bought nothing.
        // ---------------------------------------------------------------------
        private static readonly string[] TouchBaseline =
        {
            "ArmyMuster",    // WO-1056 -- slot-chip-0 grown 4.5x on H
            // "ManageScreen" -- ENTRY DELETED BY WO-1058 (2026-08-23), per this list's own
            // shrink-only rule. The cited defect is gone at the source: Cancel no longer lives in
            // the 0.76-0.98 primary band on ANY row type (it moved into the evenly-split secondary
            // cluster at 0.455-0.72), so the panel has nothing left to suppress.
            // ⚠ NOTE FOR THE NEXT SEAT: there is still no Capture*ManageScreen* entry point, so
            // this entry was suppressing nothing measurable either way. Adding ManageScreen to the
            // capture set is what would actually PROVE the fix; until then the proof is the
            // [Flow:Manage] "row bands:" line plus eyes-on the PNGs.
            "EquipDrawer",   // the 2026-08-22 screenshots
        };

        private static bool IsTouchBaselined(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            for (int i = 0; i < TouchBaseline.Length; i++)
                if (label.IndexOf(TouchBaseline[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ---------------------------------------------------------------------
        //  WO-1630 / WO-1636 -- THE GLYPH BASELINE. SEPARATE LIST, SEPARATE LAWS.
        //
        //  Seeded 2026-09-10 from TWO live runs of Assert C, and it took two because the
        //  first could not print everything it found:
        //    Builds/wave3-capture2   -- the FULL capture. 91 panel builds, 876 labels
        //                               measured, 0 unproved, 68 truncations over 21 panel
        //                               builds. It PRINTED 60 of the 68: GeoMaxPrintedLines
        //                               caps printed lines, the per-panel tally is uncapped
        //                               and read 68, and the run's own trailing line said
        //                               "... and 8 more".
        //    Builds/wave3-navcapture -- the NAVIGATION capture, run to READ THE OTHER 8
        //                               rather than to raise the cap. 15 panel builds, 144
        //                               labels, 19 findings of which 11 were already listed
        //                               here -- so the 8 new ones are exactly the 8 the full
        //                               run could not print, corroborated twice over.
        //  ⛔ THE CAP WAS NOT RAISED, AND MUST NOT BE. Widening the reading is what closed
        //  the gap; widening the cap would only have moved the ceiling for the next run.
        //  Every entry below is a measurement,
        //  never a guess: it carries the panel build, the label's hierarchy path and
        //  the counts the oracle actually read, and the comment under it carries the
        //  copy, the font, the autosize band and the overflow/wrap mode -- enough to
        //  author the fix without re-running anything.
        //
        //  ⛔ THIS LIST MAY ONLY EVER SHRINK. Each fix deletes its own entries in the
        //  same commit; adding an entry requires an owner ruling. When the last one
        //  goes, DELETE THE MECHANISM -- an empty suppression list is an invitation
        //  to add to it. Every line names WO-1636, the umbrella ticket that owns the
        //  68 and whose sub-fixes delete these lines one panel family at a time.
        //
        //  ⛔ IT IS KEYED PER FINDING, NOT PER PANEL, AND THAT IS THE WHOLE DESIGN.
        //  TouchBaseline suppresses a whole panel by name; doing that here would mean
        //  a NEW caption cut on NightMarket -- which already owns 21 entries -- lands
        //  silently under an existing suppression, which is how a baseline turns into
        //  a blindfold. A finding is baselined ONLY when the panel build, the exact
        //  hierarchy path AND the exact drawn-of-printable counts all match. Change
        //  the copy, change the band, or cut one more glyph, and it reds.
        //
        //  ⛔ NOT ADDED TO TouchBaseline, which is shrink-only by owner ruling and
        //  belongs to a different rule; growing it would violate its own header.
        //
        // ---------------------------------------------------------------------
        //  ⚠ SHRUNK 2026-09-10, 68 -> 4, AND EVERY DELETION IS A MEASUREMENT, NOT A TIDY-UP.
        //  Two runs on `dev` proved 64 of the 68 no longer match a live finding:
        //    Builds/wave3-capture10   -- the FULL capture. 91 panel builds, 875 labels read,
        //                               0 unproved, 4 findings -- ALL FOUR baselined, 0 new.
        //                               Its stamp's own glyphBaselined field reads 4.
        //    Builds/wave3-navcapture6 -- the NAVIGATION capture, re-run over the same
        //                               workspaces: 15 panel builds, 3 findings, 3 baselined,
        //                               0 new. The Realm/Manage survivors, corroborated twice.
        //  The per-panel tally lines name the four surviving panel builds, one finding each:
        //  EndStateWaveClear_repairAll_1920x1080, RealmWorkspace_1920x1080,
        //  ManageWorkspace_2340x1080, ManageWorkspace_2670x1200.
        //
        //  ⚠ WHICH RealmWorkspace_1920x1080 ENTRY SURVIVED WAS PROVEN BY ELIMINATION, NOT
        //  READ OFF THE RUN -- the tally names the PANEL, never the entry, because a baselined
        //  finding is deliberately not printed. Say so rather than implying the log named it.
        //  The elimination: its four DeckCardPurpose_* siblings share ONE authoring site
        //  (PlayerDeckWorkspace.BuildCard), and RealmWorkspace_2340x1080 and _2670x1200 print
        //  NO tally line at all on capture10 -- zero findings -- so that site cleared on every
        //  captured aspect; the DeckCard_The Night Market TITLE was explicitly DECLINED by
        //  WO-1636's Remainder sub-lane and taken by no other lane. It is the one left.
        //
        //  ⚠ capture10's stamp reads head=5a65a78319 dirty=true -- it measured a DIRTY tree,
        //  not a commit. This shrink makes the list AGREE with what that run measured; the next
        //  capture is the proof it still holds at HEAD, and it should read baselined=4.
        private static readonly string[] GlyphBaseline =
        {
            // ---- EndStateWaveClear_repairAll_1920x1080 (1) ----
            "EndStateWaveClear_repairAll_1920x1080|ObsidianPanel/PanelContent/Zone_Body/Zone_RewardWell/Band/SpoilCell2/SpoilRow/Label|17 of 19",
            //   "DESTROYED, looted 120" at font 30 [30..50, enabled=True] overflow=Ellipsis wrap=NoWrap -- WO-1636
            // ---- RealmWorkspace_1920x1080 (1) ----
            "RealmWorkspace_1920x1080|ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_The Night Market/Label|12 of 14",
            //   "THE NIGHT MARKET" at font 30 [30..40, enabled=True] overflow=Ellipsis wrap=NoWrap -- WO-1636
            // ---- ManageWorkspace_2340x1080 (1) ----
            "ManageWorkspace_2340x1080|ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageCategoryGrid/ManageCard_ARMY/Label|11 of 14",
            //   "BUILD A BARRACKS" at font 30 [30..40, enabled=True] overflow=Ellipsis wrap=NoWrap -- WO-1636 (nav capture)
            // ---- ManageWorkspace_2670x1200 (1) ----
            "ManageWorkspace_2670x1200|ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageCategoryGrid/ManageCard_ARMY/Label|11 of 14",
            //   "BUILD A BARRACKS" at font 30 [30..40, enabled=True] overflow=Ellipsis wrap=NoWrap -- WO-1636 (nav capture)
        };

        /// <summary>True when this exact finding was measured on the seeding run and is carried
        /// as known debt. Matched on all THREE fields -- panel build, hierarchy path, counts --
        /// so a new cut, a moved label or a changed string is never absorbed by a neighbour's
        /// entry.</summary>
        private static bool IsGlyphBaselined(string label, string message)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(message)) return false;
            for (int i = 0; i < GlyphBaseline.Length; i++)
            {
                string e = GlyphBaseline[i];
                int a = e.IndexOf('|');
                if (a <= 0) continue;
                int b = e.IndexOf('|', a + 1);
                if (b <= a) continue;
                if (!string.Equals(label, e.Substring(0, a), StringComparison.Ordinal)) continue;
                string path = e.Substring(a + 1, b - a - 1);
                if (message.IndexOf("'" + path + "'", StringComparison.Ordinal) < 0) continue;
                string counts = e.Substring(b + 1);
                if (message.IndexOf(" draws " + counts + " printable", StringComparison.Ordinal) < 0) continue;
                return true;
            }
            return false;
        }

        // =====================================================================
        //  WO-1060 -- THE CLAMP / OVERLAP ORACLE'S OWN MARKER.
        // ---------------------------------------------------------------------
        //  ⛔ A DISTINCT MARKER, NOT A SHARED STRING (CLAUDE.md 8). UI_GEOMETRY_OK
        //  also covers text-off-plate, so a reader could not tell from it whether
        //  the touch/overlap class specifically was clean. That is the same defect
        //  that once let a 22-case suite's pass read as the full suite's pass.
        //
        //  Judge by THIS MARKER on a FRESH log, never by the exit code -- this
        //  repo's runners exit 0 on refusals and FAILs. Marker absent on a fresh
        //  log is a FAILURE, not an unknown.
        // =====================================================================
        private static readonly List<string> _touchFailures = new List<string>();
        private static int _touchPanelsChecked;
        private static int _touchPanelsClean;

        private static void ReportTouchOracle()
        {
            if (_touchPanelsChecked == 0)
            {
                Debug.LogError("UI_TOUCH_FAIL x0 -- ZERO panels were measured, so the clamp/overlap " +
                               "oracle proved nothing this run.");
                return;
            }
            if (_touchFailures.Count == 0)
            {
                // <clean>/<checked> counts PANEL BUILDS, not distinct panels: ForEachTarget builds
                // and measures every panel once per aspect (1920x1080 / 2340x1080 / 2670x1200), and
                // a band that clears 112 at one aspect can fall under it at another -- so each build
                // is its own assertion and each one is counted.
                Debug.Log("UI_TOUCH_OK " + _touchPanelsClean + "/" + _touchPanelsChecked + " panels -- " +
                          "no control authored under MinTouchPx(" + ElarionUiKit.MinTouchPx.ToString("0.#") +
                          ") so the clamp had nothing to rescue, and no two interactive rects intersect.");
                return;
            }

            int shown = Mathf.Min(_touchFailures.Count, GeoMaxPrintedLines);
            for (int i = 0; i < shown; i++) Debug.LogError("[touch-oracle] " + _touchFailures[i]);
            if (_touchFailures.Count > shown)
                Debug.LogError("[touch-oracle] ... and " + (_touchFailures.Count - shown) + " more");

            Debug.LogError("UI_TOUCH_FAIL x" + _touchFailures.Count + " over " + _touchPanelsChecked +
                           " panels (" + _touchPanelsClean + " clean) -- each line names the panel, the " +
                           "control and the numbers. Author the band above the floor; do not rely on the clamp.");
        }

        // =====================================================================
        //  WO-1630 -- ASSERT C'S OWN MARKER: DID THE GLYPHS SURVIVE?
        // ---------------------------------------------------------------------
        //  ⛔ A DISTINCT MARKER, AND THE PRECEDENT IS TWENTY LINES ABOVE. The
        //  clamp/overlap class was given its own marker rather than folded into
        //  the geometry one, because the geometry marker also covers
        //  text-off-plate and a reader could not tell from it whether the
        //  touch/overlap class specifically was clean. Folding glyph survival
        //  into either existing tally repeats exactly that -- the same defect
        //  that once let a small suite's pass read as the full suite's pass.
        //
        //  The emitted names are UI_GLYPH_OK / UI_GLYPH_FAIL and this method is
        //  their ONE definition site. Judge by the marker on a FRESH log, never
        //  by the exit code -- this repo's runners exit 0 on refusals and FAILs,
        //  and marker-absent on a fresh log is a FAILURE, not an unknown.
        //
        //  ⚠ THREE OUTCOMES, NOT TWO, AND THE THIRD IS WHY THIS RULE CAN BE
        //  TRUSTED. "Nothing measured" and "everything fit" are the SAME clean
        //  line to a grep unless the reporter separates them, so:
        //    * zero panels                       -> FAIL, nothing was proved
        //    * panels measured, none proved      -> FAIL, no font resolved at all
        //    * findings                          -> FAIL, each line names the label
        //    * clean                             -> OK, carrying the unproved count
        //  A run with unproved labels still prints OK when the rest fit, but the
        //  count travels ON the marker line so it can never be read as silence.
        // =====================================================================
        private static readonly List<string> _glyphFailures = new List<string>();
        private static readonly List<string> _glyphPanelTally = new List<string>();
        private static readonly List<string> _glyphUnproved = new List<string>();
        private static int _glyphPanelsChecked;
        private static int _glyphPanelsClean;
        private static int _glyphLabelFindings;
        private static int _glyphUnmeasuredCount;
        private static int _glyphLabelsMeasured;
        private static int _glyphBaselinedCount;

        /// <summary>Clear Assert C's tallies. Called beside every existing
        /// <c>_touchFailures.Clear()</c> rather than from inside the reporter: every entry
        /// point in this file READS the tallies AFTER its Report* calls (the pass/fail
        /// verdicts and the capture stamp both do), so a self-clearing reporter would make
        /// the glyph counts unreadable at precisely the place a ticket's quoted numbers get
        /// checked against the log they claim to come from.</summary>
        private static void ResetGlyphOracle()
        {
            _glyphFailures.Clear();
            _glyphPanelTally.Clear();
            _glyphUnproved.Clear();
            _glyphPanelsChecked = 0;
            _glyphPanelsClean = 0;
            _glyphLabelFindings = 0;
            _glyphUnmeasuredCount = 0;
            _glyphLabelsMeasured = 0;
            _glyphBaselinedCount = 0;
        }

        private static void ReportGlyphOracle()
        {
            if (_glyphPanelsChecked == 0)
            {
                Debug.LogError("UI_GLYPH_FAIL x0 -- ZERO panels were measured, so the glyph-survival " +
                               "oracle proved nothing this run. A clean geometry marker beside this " +
                               "line still means no rule looked inside a single label.");
                return;
            }
            if (_glyphLabelsMeasured == 0)
            {
                // ⛔ THE BRANCH THAT MAKES THE CLEAN LINE MEAN ANYTHING. Panels were visited and NOT
                // ONE label was measured, which returns zero findings and is byte-identical, to a
                // grep, to a run in which everything fit. The two causes are separated because the
                // fix is different for each.
                if (_glyphUnmeasuredCount > 0)
                    Debug.LogError("UI_GLYPH_FAIL x0 labels -- " + _glyphPanelsChecked + " panels were " +
                                   "visited and NOT ONE label could be measured (" + _glyphUnmeasuredCount +
                                   " stand-downs: TMP produced no textInfo, i.e. no font resolved). This " +
                                   "run proves NOTHING about glyph survival and MAY NOT be cited as clean.");
                else
                    Debug.LogError("UI_GLYPH_FAIL x0 labels -- " + _glyphPanelsChecked + " panels were " +
                                   "visited and Assert C's exclusions skipped EVERY label on all of them, " +
                                   "with no stand-downs recorded. A predicate that silently excludes every " +
                                   "control reports a clean run and looks identical to a healthy one; that " +
                                   "is the blindness this rule exists to end, so it is a FAIL, not silence.");
                int stood = Mathf.Min(_glyphUnproved.Count, GeoMaxPrintedLines);
                for (int i = 0; i < stood; i++) Debug.LogError("[glyph-oracle] " + _glyphUnproved[i]);
                return;
            }

            for (int i = 0; i < _glyphPanelTally.Count; i++)
                Debug.LogWarning("[glyph-oracle] " + _glyphPanelTally[i]);
            int unprovedShown = Mathf.Min(_glyphUnproved.Count, GeoMaxPrintedLines);
            for (int i = 0; i < unprovedShown; i++)
                Debug.LogWarning("[glyph-oracle] NOT PROVED " + _glyphUnproved[i]);

            if (_glyphFailures.Count == 0)
            {
                Debug.Log("UI_GLYPH_OK " + _glyphPanelsClean + "/" + _glyphPanelsChecked +
                          " panels labels=" + _glyphLabelsMeasured +
                          " baselined=" + _glyphBaselinedCount +
                          " unproved=" + _glyphUnmeasuredCount +
                          " -- every measurable label " +
                          "drew every printable character it was given. <clean>/<checked> counts PANEL " +
                          "BUILDS, not distinct panels: each aspect is its own assertion, because a " +
                          "caption that fits at one aspect can lose its last word at another.");
                return;
            }

            int shown = Mathf.Min(_glyphFailures.Count, GeoMaxPrintedLines);
            for (int i = 0; i < shown; i++) Debug.LogError("[glyph-oracle] " + _glyphFailures[i]);
            if (_glyphFailures.Count > shown)
                Debug.LogError("[glyph-oracle] ... and " + (_glyphFailures.Count - shown) +
                               " more (the per-panel tally lines above are NOT capped)");

            // ⛔ THE x-NUMBER IS THE NEW FINDINGS, NOT EVERY FINDING. A headline that counted
            // baselined debt too would name a number with no printed lines under it -- 68 in a
            // run whose only red is 8 -- and a marker whose count does not match its own evidence
            // is the defect this whole rule exists to end. The total still travels, labelled.
            Debug.LogError("UI_GLYPH_FAIL x" + _glyphFailures.Count + " NEW over " + _glyphPanelsChecked +
                           " panels (" + _glyphPanelsClean + " clean, labels=" + _glyphLabelsMeasured +
                           ", baselined=" + _glyphBaselinedCount +
                           " of " + _glyphLabelFindings + " found" +
                           ", unproved=" + _glyphUnmeasuredCount +
                           ") -- each line names the label, both counts, the rect and the font. A rect " +
                           "in the right place whose words were cut away is invisible to every other " +
                           "rule on this path.");
        }

        private static void ReportGeometry()
        {
            if (_geoCanvasesChecked == 0)
            {
                Debug.LogError("UI_GEOMETRY_FAIL x0 -- ZERO canvases were measured, so the geometry gate " +
                               "proved nothing this run (a green UI_CAPTURE_OK without it is the old blindness).");
                return;
            }
            if (_geoFailures.Count == 0)
            {
                Debug.Log("UI_GEOMETRY_OK " + _geoCanvasesChecked + " canvases -- no text off its plate, " +
                          "no overlapping sibling buttons, no button on foreign text, no authored band " +
                          "under the " + ElarionUiKit.MinTouchPx.ToString("0.#") + " px touch floor");
                return;
            }

            int shown = Mathf.Min(_geoFailures.Count, GeoMaxPrintedLines);
            for (int i = 0; i < shown; i++) Debug.LogError("[UICap-GEO] " + _geoFailures[i]);
            if (_geoFailures.Count > shown)
                Debug.LogError("[UICap-GEO] ... and " + (_geoFailures.Count - shown) + " more");

            Debug.LogError("UI_GEOMETRY_FAIL x" + _geoFailures.Count + " over " + _geoCanvasesChecked +
                           " canvases -- see the [UICap-GEO] lines above; each names the panel, the " +
                           "element and the numbers.");
        }

        // ---------------------------------------------------------------------
        //  Geometry helpers (all measurements in ROOT-CANVAS local = reference px).
        // ---------------------------------------------------------------------
        private static bool TryRectInRoot(RectTransform rt, RectTransform root, out Rect r)
        {
            r = default(Rect);
            if (rt == null || root == null) return false;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = root.InverseTransformPoint(c[i]);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            if (float.IsNaN(minX) || float.IsNaN(minY) || float.IsInfinity(maxX) || float.IsInfinity(maxY))
                return false;
            r = new Rect(minX, minY, maxX - minX, maxY - minY);
            return r.width > 0.5f && r.height > 0.5f;
        }

        /// <summary>How far <paramref name="inner"/> pokes outside <paramref name="outer"/> (px; &lt;=0 = contained).</summary>
        private static float OutsideBy(Rect inner, Rect outer)
        {
            float left = outer.xMin - inner.xMin;
            float right = inner.xMax - outer.xMax;
            float bottom = outer.yMin - inner.yMin;
            float top = inner.yMax - outer.yMax;
            return Mathf.Max(Mathf.Max(left, right), Mathf.Max(bottom, top));
        }


        /// <summary>The kit body zone above <paramref name="t"/>, if any (ElarionUiKit names it Zone_Body).</summary>
        private static RectTransform ZoneBodyAbove(Transform t)
        {
            for (Transform p = t != null ? t.parent : null; p != null; p = p.parent)
                if (string.Equals(p.name, "Zone_Body", StringComparison.Ordinal)) return p as RectTransform;
            return null;
        }

        /// <summary>The black plate of a body zone: its ZoneBacking child, else the zone itself.</summary>
        private static RectTransform PlateOf(RectTransform body)
        {
            if (body == null) return null;
            for (int i = 0; i < body.childCount; i++)
            {
                Transform ch = body.GetChild(i);
                if (ch != null && string.Equals(ch.name, "ZoneBacking", StringComparison.Ordinal))
                    return ch as RectTransform;
            }
            return body;
        }

        /// <summary>Nearest masking ancestor between <paramref name="t"/> and <paramref name="stopAt"/> (exclusive).</summary>
        private static RectTransform NearestClipper(Transform t, Transform stopAt)
        {
            for (Transform p = t != null ? t.parent : null; p != null; p = p.parent)
            {
                if (p == stopAt) break;
                if (p.GetComponent<RectMask2D>() != null || p.GetComponent<Mask>() != null)
                    return p as RectTransform;
            }
            return null;
        }

        // NOTE: Overlaps / ButtonUsable / ClippedOut / HasVisibleGraphic / HasMinTouchGuard /
        // IsDescendantOf and the pad+scrim constants MOVED to DeNelle.Core.UI.LayoutOracle
        // (WO-1060). They are not re-declared here: two copies of a predicate are two oracles
        // waiting to disagree about the same canvas. Call LayoutOracle's public helpers.

        private static string PathOf(Transform t, Transform stopAt)
        {
            if (t == null) return "<null>";
            string s = t.name;
            for (Transform p = t.parent; p != null && p != stopAt; p = p.parent)
                s = p.name + "/" + s;
            return s;
        }

        private static string RectStr(Rect r)
        {
            return "(x " + r.xMin.ToString("0.#") + ".." + r.xMax.ToString("0.#") +
                   ", y " + r.yMin.ToString("0.#") + ".." + r.yMax.ToString("0.#") + ")";
        }

        private static string Snippet(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length <= 48 ? s : s.Substring(0, 45) + "...";
        }

        /// <summary>Set <c>canvas.scaleFactor</c> to what a ScaleWithScreenSize +
        /// MatchWidthOrHeight CanvasScaler would compute for a w x h target -- the scaler's
        /// own Update does not run in a synchronous edit-mode call.</summary>
        private static void ApplyScreenSpaceScale(Canvas canvas, int w, int h)
        {
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (scaler == null) return;

            Vector2 refRes = scaler.referenceResolution;
            float refW = refRes.x > 1f ? refRes.x : 1080f;
            float refH = refRes.y > 1f ? refRes.y : 1920f;
            float match = Mathf.Clamp01(scaler.matchWidthOrHeight);

            float logW = Mathf.Log(w / refW, 2f);
            float logH = Mathf.Log(h / refH, 2f);
            float logWeighted = Mathf.Lerp(logW, logH, match);
            float sf = Mathf.Pow(2f, logWeighted);
            if (sf <= 0f || float.IsNaN(sf) || float.IsInfinity(sf)) sf = 1f;

            canvas.scaleFactor = sf;
            canvas.referencePixelsPerUnit = scaler.referencePixelsPerUnit > 0f
                ? scaler.referencePixelsPerUnit : 100f;
        }

        // =====================================================================
        //  RAID PILLAR CAPTURES (2026-08-16).
        // ---------------------------------------------------------------------
        //  Sixteen cases shipped before today and not one of them touched raids --
        //  the pillar the owner explicitly asked to verify from screenshots. Three
        //  cases close that hole, each following the established idiom exactly:
        //  ForEachTarget owns the per-resolution build, the body owns its own
        //  DestroyImmediate teardown, and every risky step is inside the try so a
        //  refusing raid screen costs ONE frame, never the remaining sixteen cases.
        //
        //  THE ARMY GATE, and how this harness gets past it honestly.
        //  RaidSelectionScreen.Open() refuses unless ArmyReadiness.Compute says the
        //  army is full. In an edit-mode capture there is no GameStateService, and
        //  Compute's documented stateless rule ("null st/Army publishes READY with
        //  zeros so headless/AutoPilot never false-blocks") returns Ready -- so the
        //  grid opens on the REAL static entry, with the REAL gate evaluated, not
        //  driven around it. RaidTestFlagScope additionally raises ff.raidtest for the
        //  duration as a belt-and-braces: if a future state seeds a partial army into
        //  this process, the capture still gets its frame instead of silently shooting
        //  the drillmaster redirect. The scope RESTORES the previous PlayerPrefs value
        //  (deleting the key when it was unset) so a capture run can never leave the
        //  owner's flags mutated.
        //
        //  TEARDOWN: neither screen's own Close() may be called here -- it routes
        //  through ElarionUiKit.ClosePanelWithFx, which outside play mode calls the
        //  runtime Destroy (edit-illegal). Both bodies release the arbiter handle by
        //  hand and DestroyImmediate the canvas FIRST, so the host's OnDestroy sees a
        //  dead _ui and skips its own Destroy -- the same contract the rumor-board and
        //  realm-map cases already keep.
        // =====================================================================

        /// <summary>
        /// Raise ff.raidtest for the life of a capture and put PlayerPrefs back exactly as it
        /// was found. A harness that leaves a bypass flag ON would silently change how the
        /// owner's next play session gates raids -- the restore is the load-bearing half.
        /// </summary>
        private sealed class RaidTestFlagScope : IDisposable
        {
            private const string Key = "ff.raidtest";
            private readonly int _prev;      // -1 == the key was not set at all

            public RaidTestFlagScope()
            {
                _prev = PlayerPrefs.GetInt(Key, -1);
                PlayerPrefs.SetInt(Key, 1);
                Debug.Log("[UICap-HL] ff.raidtest raised for the raid captures (previous value " +
                          (_prev < 0 ? "UNSET" : _prev.ToString()) + "); it is restored on scope exit. " +
                          "NOTE: headless has no GameState, so ArmyReadiness.Compute already returns " +
                          "READY by its stateless never-false-block rule -- this flag is the safety " +
                          "net, not the thing that opens the grid.");
            }

            public void Dispose()
            {
                try
                {
                    if (_prev < 0) PlayerPrefs.DeleteKey(Key);
                    else PlayerPrefs.SetInt(Key, _prev);
                    PlayerPrefs.Save();
                    Debug.Log("[UICap-HL] ff.raidtest restored to " +
                              (_prev < 0 ? "UNSET (key deleted)" : _prev.ToString()) + ".");
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] ff.raidtest RESTORE FAILED: " + e +
                                   " -- the owner's PlayerPrefs may still carry the bypass flag. " +
                                   "Clear it by hand (PlayerPrefs key 'ff.raidtest').");
                }
            }
        }

        // ---------------------------------------------------------------------
        //  Panel: RaidSelectionScreen -- the grid of raid cards. This is the screen
        //  that was hard-refusing to open, so a frame of it OPEN is the single most
        //  valuable raid picture. Shot through the real static Open(), which builds
        //  the real chrome + the real RaidSelectionVM projection off
        //  SceneConfigCatalog (the three flagship enemy raids, falling back to every
        //  enemy raid). An empty catalog renders the panel's own "No raids available."
        //  empty state -- which is still a true picture and is logged as such.
        // ---------------------------------------------------------------------
        private static int CaptureRaidSelection()
        {
            using (new RaidTestFlagScope())
                return ForEachTarget("RaidSelection", CaptureRaidSelectionOnce);
        }

        private static int CaptureRaidSelectionOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject canvasGo = null;
            RaidSelectionScreen screen = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                int raidCount = 0;
                var probe = RaidSelectionVM.CreateDefault(null);
                if (probe != null)
                {
                    raidCount = probe.Raids != null ? probe.Raids.Count : 0;
                    probe.Dispose();
                }
                if (raidCount == 0)
                {
                    Debug.LogWarning("[UICap-HL] SceneConfigCatalog projected ZERO enemy raids -- the raid " +
                                     "grid will render its 'No raids available.' empty state. The shot is " +
                                     "still honest, but it proves the CHROME opens, not that cards build.");
                }

                // The REAL static entry -- the same call the HUD Raids face reaches through
                // RaidEntryGate, army gate included (see the banner for why it passes headless).
                RaidSelectionScreen.Open();

                // Awake never runs on an edit-mode AddComponent, so the screen's own _instance
                // cache is always null here and Open() authors a fresh host each time; find it.
                screen = UnityEngine.Object.FindAnyObjectByType<RaidSelectionScreen>();
                if (screen == null)
                {
                    Debug.LogWarning("[UICap-HL] RaidSelectionScreen.Open() produced no screen instance -- " +
                                     "the army gate refused (or the host was not created). Raid grid skipped " +
                                     "for " + target.Tag + "; the remaining captures continue.");
                    return 0;
                }

                canvasGo = GetPrivateGameObject(screen, "_ui");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] RaidSelectionScreen._ui null after Open -- the grid did not " +
                                     "build; raid grid skipped for " + target.Tag + ".");
                    return 0;
                }

                if (RenderCanvasToPng(canvasGo, OutDir + "RaidSelection_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] raid selection capture threw: " + e);
            }
            finally
            {
                ReleaseScreenHandle(screen, "raid selection");
                // Canvas FIRST so the host's OnDestroy sees a dead _ui and never calls the
                // runtime Destroy (edit-illegal) -- the shared teardown contract.
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (screen != null && screen.gameObject != null)
                    UnityEngine.Object.DestroyImmediate(screen.gameObject);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        // ---------------------------------------------------------------------
        //  Panel: RaidDeployScreen -- the pre-raid deploy screen (party row, troop
        //  list, scout report, BEGIN ASSAULT). No test and no bot had ever exercised
        //  this path, so the first frame of it is genuinely new information.
        //
        //  HONEST LIMIT, stated rather than dressed up: with no GameStateService in
        //  edit mode RaidDeployVM.CreateDefault resolves a NULL army, so the troop
        //  list renders its "No troops trained yet." empty state and the counts read
        //  zero. That is the real zero-army layout (exactly what a player with an
        //  empty barracks sees) -- it is NOT proof that a populated roster lays out.
        //  Seeding an army would need a live GameStateService, which this harness
        //  deliberately does not stand up.
        // ---------------------------------------------------------------------
        private static int CaptureRaidDeploy()
        {
            using (new RaidTestFlagScope())
                return ForEachTarget("RaidDeploy", CaptureRaidDeployOnce);
        }

        private static int CaptureRaidDeployOnce(CaptureTarget target)
        {
            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject canvasGo = null;
            RaidDeployScreen screen = null;
            RaidSelectionVM vm = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                // The def the player would have tapped: the FIRST card the real grid projects,
                // resolved through the same VM the grid uses (never a hand-built fixture def).
                vm = RaidSelectionVM.CreateDefault(null);
                SceneConfigDef def = null;
                if (vm != null && vm.Raids != null)
                {
                    foreach (var item in vm.Raids)
                    {
                        def = vm.DefFor(item.Id);
                        if (def != null) break;
                    }
                }
                if (def == null)
                {
                    Debug.LogWarning("[UICap-HL] no SceneConfigDef projected for any raid card -- the deploy " +
                                     "screen has nothing to open for; skipped at " + target.Tag + ".");
                    return 0;
                }

                RaidDeployScreen.Open(def);

                screen = UnityEngine.Object.FindAnyObjectByType<RaidDeployScreen>();
                if (screen == null)
                {
                    Debug.LogWarning("[UICap-HL] RaidDeployScreen.Open(def) produced no screen instance -- " +
                                     "deploy screen skipped at " + target.Tag + ".");
                    return 0;
                }

                canvasGo = GetPrivateGameObject(screen, "_ui");
                if (canvasGo == null)
                {
                    Debug.LogWarning("[UICap-HL] RaidDeployScreen._ui null after Open -- the deploy screen " +
                                     "did not build; skipped at " + target.Tag + ".");
                    return 0;
                }

                Debug.Log("[UICap-HL] raid deploy shot is the ZERO-ARMY state (no GameStateService in edit " +
                          "mode -> RaidDeployVM resolves a null army). Read it as the empty-barracks layout, " +
                          "not as proof that a populated troop list fits.");

                if (RenderCanvasToPng(canvasGo, OutDir + "RaidDeploy_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] raid deploy capture threw: " + e);
            }
            finally
            {
                if (vm != null) vm.Dispose();
                ReleaseScreenHandle(screen, "raid deploy");
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (screen != null && screen.gameObject != null)
                    UnityEngine.Object.DestroyImmediate(screen.gameObject);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        /// <summary>Release a raid screen's single-modal arbiter slot by hand. Its own Close()
        /// cannot be used (ClosePanelWithFx -> runtime Destroy is edit-illegal), and a leaked
        /// handle would make the NEXT panel in the run open against a busy arbiter.</summary>
        private static void ReleaseScreenHandle(object screen, string what)
        {
            if (screen == null) return;
            try
            {
                var handle = GetPrivateFieldValue(screen, "_panelHandle") as PanelHandle;
                if (handle != null) PanelManager.NotifyClosed(handle);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UICap-HL] " + what + " arbiter release failed (harmless): " + e.Message);
            }
        }

        // ---------------------------------------------------------------------
        //  Panel: the bottom action bar's RAIDS FACE in its THREE states (WO-1008).
        //  The face used to VANISH with an empty army; it is now visible-and-greyed
        //  and carries the reason in WORDS ("Raids 0/5" / "Raids 3/5") because the
        //  owner is red/green colourblind and a hue tells her nothing. Whether that
        //  reads at a glance is a question ONLY a screenshot can answer -- so all
        //  three states are shot side by side in ONE frame per target, which is what
        //  makes them comparable.
        //
        //  Not a view-only mock: each row is painted from a REAL HudActionBarModel
        //  (the Core applicability model) driven over its ISource seam, so the face
        //  set, the dim decision AND the label text all come from the shipped compute
        //  -- exactly what HudKitController.ApplyRaidsDim renders. The kit builds the
        //  faces (BuildObsidianButton, same style/colour arguments as HudKitController),
        //  so the plate, the font and the touch floor are the shipped ones too.
        // ---------------------------------------------------------------------
        private sealed class FakeBarSource : DeNelle.Core.HudModel.HudActionBarModel.ISource
        {
            public bool TalkAvailable { get; set; }
            public bool RaidCapable { get; set; }
            public bool RaidArmyReady { get; set; }
            public int RaidDeployableSlots { get; set; }
            public int RaidQueuedSlots { get; set; }
            public int RaidCapSlots { get; set; }
            public bool MapUnlocked { get; set; }
            public bool BuildingFocused { get; set; }
        }

        private struct RaidFaceState
        {
            public string Caption;
            public FakeBarSource Source;
        }

        private static RaidFaceState[] BuildRaidFaceStates()
        {
            return new[]
            {
                new RaidFaceState
                {
                    Caption = "LIVE - army full, the face is undimmed and reads 'Raids'",
                    Source = new FakeBarSource
                    {
                        RaidCapable = true, RaidArmyReady = true,
                        RaidDeployableSlots = 5, RaidQueuedSlots = 0, RaidCapSlots = 5,
                    },
                },
                new RaidFaceState
                {
                    Caption = "GREYED / NO TROOPS - barracks built, nothing trained (WO-1008: visible, not hidden)",
                    Source = new FakeBarSource
                    {
                        RaidCapable = true, RaidArmyReady = false,
                        RaidDeployableSlots = 0, RaidQueuedSlots = 0, RaidCapSlots = 5,
                    },
                },
                new RaidFaceState
                {
                    Caption = "GREYED / PARTIAL ARMY - 2 ready + 1 training of 5",
                    Source = new FakeBarSource
                    {
                        RaidCapable = true, RaidArmyReady = false,
                        RaidDeployableSlots = 2, RaidQueuedSlots = 1, RaidCapSlots = 5,
                    },
                },
            };
        }

        // The kit face arguments per id, verbatim from HudKitController's bar build.
        private static string BarFaceWord(DeNelle.Core.HudModel.ActionBarButtonId id)
        {
            switch (id)
            {
                case DeNelle.Core.HudModel.ActionBarButtonId.Build: return "Build";
                case DeNelle.Core.HudModel.ActionBarButtonId.Talk: return "Talk";
                case DeNelle.Core.HudModel.ActionBarButtonId.Bag: return "Bag";
                case DeNelle.Core.HudModel.ActionBarButtonId.Raids:
                    return DeNelle.Core.HudModel.HudActionBarModel.RaidsBaseLabel;
                case DeNelle.Core.HudModel.ActionBarButtonId.Quests: return "Quests";
                case DeNelle.Core.HudModel.ActionBarButtonId.Upgrade: return "Manage";
                default: return id.ToString();
            }
        }

        private static ElarionUiKit.ObsidianButtonColor BarFaceColor(DeNelle.Core.HudModel.ActionBarButtonId id)
        {
            if (id == DeNelle.Core.HudModel.ActionBarButtonId.Build)
                return ElarionUiKit.ObsidianButtonColor.Yellow;
            if (id == DeNelle.Core.HudModel.ActionBarButtonId.Talk)
                return ElarionUiKit.ObsidianButtonColor.Green;
            return ElarionUiKit.ObsidianButtonColor.Gray;
        }

        private static int CaptureRaidsFaceStates()
        {
            return ForEachTarget("RaidsFaceStates", CaptureRaidsFaceStatesOnce);
        }

        private static int CaptureRaidsFaceStatesOnce(CaptureTarget target)
        {
            // HudKitController's own bar geometry, so the slot widths are the shipped ones.
            const float barGap = 0.01f;
            float barSlotW = (1f - barGap * (DeNelle.Core.HudModel.HudActionBarModel.MaxVisibleFaces - 1))
                             / DeNelle.Core.HudModel.HudActionBarModel.MaxVisibleFaces;

            int saved = 0;
            GameObject tempEventSystem = null;
            GameObject canvasGo = null;

            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~UICapEventSystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                canvasGo = new GameObject("~UICapRaidsFace",
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 4000;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);   // HudAreasHost's scaler
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                // Dark backdrop so the greyed plate's contrast reads honestly in the PNG.
                var bg = new GameObject("Backdrop", typeof(Image));
                bg.transform.SetParent(canvasGo.transform, false);
                var bgrt = (RectTransform)bg.transform;
                bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
                bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
                bg.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.12f, 1f);

                var root = (RectTransform)canvasGo.transform;
                var title = MakePixelBand(root, "Title", 8f, 52f, 24f);
                var titleLbl = title.gameObject.AddComponent<TextMeshProUGUI>();
                ElarionUiKit.EnsureFont(titleLbl);
                titleLbl.text = "ACTION BAR - RAIDS FACE, THREE STATES (WO-1008)";
                titleLbl.fontSize = ElarionUi.FontLabel;
                titleLbl.color = ElarionUi.Gilt;
                titleLbl.fontStyle = FontStyles.Bold;
                titleLbl.alignment = TextAlignmentOptions.MidlineLeft;
                titleLbl.raycastTarget = false;

                var states = BuildRaidFaceStates();
                float y = 84f;
                foreach (var state in states)
                {
                    // The REAL Core compute decides the set, the dim and the words.
                    var model = new DeNelle.Core.HudModel.HudActionBarModel(state.Source);
                    model.SetPosture(DeNelle.Core.HudModel.HudActionBarModel.PostureTown);

                    var cap = MakePixelBand(root, "Caption", y, 44f, 24f);
                    var capLbl = cap.gameObject.AddComponent<TextMeshProUGUI>();
                    ElarionUiKit.EnsureFont(capLbl);
                    capLbl.text = state.Caption + "   [model: dim=" + model.RaidsDimmed +
                                  " reason=" + model.RaidsDimReason + " label='" + model.RaidsFaceLabel + "']";
                    capLbl.fontSize = ElarionUi.FontMicro;
                    capLbl.color = ElarionUi.Parchment;
                    capLbl.alignment = TextAlignmentOptions.MidlineLeft;
                    capLbl.raycastTarget = false;
                    y += 50f;

                    var barBand = MakePixelBand(root, "Bar", y, ElarionUiKit.MinTouchPx, 24f);
                    var actives = model.Active;
                    int n = actives != null ? actives.Count : 0;
                    float groupW = n > 0 ? n * barSlotW + (n - 1) * barGap : 0f;
                    float x = (1f - groupW) * 0.5f;
                    for (int i = 0; i < n; i++)
                    {
                        var id = actives[i];
                        bool isRaids = id == DeNelle.Core.HudModel.ActionBarButtonId.Raids;
                        // WO-1008: the Raids face carries the model's STATE label, every other
                        // face its plain word -- exactly what ApplyRaidsDim paints.
                        // WO-1219: and what it paints is now the base WORD plus the model's short
                        // BADGE on a SECOND LINE (the one-line "Raids 0/5" ellipsised to
                        // "Raids ..." on the owner's device, throwing away the numerals that are
                        // the whole colourblind-safe tell). Mirrored here verbatim, FitBlock
                        // included -- a capture that does not paint what the View paints is
                        // worse than no capture.
                        string word = isRaids
                            ? (string.IsNullOrEmpty(model.RaidsFaceBadge)
                                ? DeNelle.Core.HudModel.HudActionBarModel.RaidsBaseLabel
                                : DeNelle.Core.HudModel.HudActionBarModel.RaidsBaseLabel + "\n" + model.RaidsFaceBadge)
                            : BarFaceWord(id);
                        var face = ElarionUiKit.BuildObsidianButton(barBand, word,
                            ElarionUiKit.ObsidianButtonStyle.Style1, BarFaceColor(id),
                            new Vector2(x, 0f), new Vector2(x + barSlotW, 1f), null);
                        if (isRaids && face != null)
                        {
                            var multiLine = face.GetComponentInChildren<TMP_Text>(true);
                            if (multiLine != null) ElarionUiKit.FitBlock(multiLine);
                        }
                        if (isRaids && model.RaidsDimmed && face != null)
                        {
                            // ApplyRaidsDim, verbatim: tint face + label toward Disabled and
                            // NEVER touch interactable (a dimmed tap still reaches the redirect).
                            var faceImg = face.targetGraphic as Image;
                            if (faceImg != null) faceImg.color = ElarionUi.Disabled;
                            var faceLbl = face.GetComponentInChildren<TMP_Text>(true);
                            if (faceLbl != null) faceLbl.color = ElarionUi.Disabled;
                        }
                        x += barSlotW + barGap;
                    }
                    if (n == 0)
                    {
                        Debug.LogWarning("[UICap-HL] raids-face state '" + state.Caption + "' produced an " +
                                         "EMPTY action-bar set -- the row will be blank in the shot.");
                    }
                    y += ElarionUiKit.MinTouchPx + 42f;
                }

                Canvas.ForceUpdateCanvases();
                if (RenderCanvasToPng(canvasGo, OutDir + "RaidsFaceStates_" + target.Tag + ".png",
                    target.W, target.H)) saved++;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] raids face states capture threw: " + e);
            }
            finally
            {
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
            }

            return saved;
        }

        private static int CapturePlayerDecks()
        {
            int saved = 0;
            string[] pages = { "OpenRealm", "OpenHero", "OpenJourney" };
            string[] names = { "RealmWorkspace", "HeroWorkspace", "JourneyWorkspace" };
            for (int p = 0; p < pages.Length; p++)
            {
                int pageIndex = p;
                saved += ForEachTarget(names[p], target => CapturePlayerDeckOnce(
                    target, pages[pageIndex], names[pageIndex]));
            }
            return saved;
        }

        private static int CaptureBag()
        {
            return ForEachTarget("Bag", CaptureBagOnce);
        }

        /// <summary>
        /// Stateful Bag proof: the selected potion is rendered, then the real production Use
        /// command heals a damaged HeroHealth and consumes exactly one persisted inventory unit.
        /// This is intentionally separate from the broad empty-state capture so a pretty empty
        /// shell can never be mistaken for evidence that the player-facing verb works.
        /// </summary>
        public static void RunBagUseCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;

            int frames = ForEachTarget("BagUse", CaptureBagUseOnce);
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (frames == 6 && _fidelityDegraded == 0 && _geoFailures.Count == 0 &&
                _touchFailures.Count == 0)
                Debug.Log("BAG_USE_CAPTURE_OK 6/6 frames; production effect; hp+inventory asserted; touch=clean");
            else
                Debug.LogError("BAG_USE_CAPTURE_FAIL frames=" + frames + "/6 fidelity=" +
                    _fidelityDegraded + " geometry=" + _geoFailures.Count + " touch=" +
                    _touchFailures.Count);
        }

        private static int CaptureBagUseOnce(CaptureTarget target)
        {
            const string potionId = "minor-heal-potion";
            GameStateService priorState = GameStateService.Instance;
            VillageInventory priorInventory = VillageInventory.Instance;
            GameObject stateHost = null, inventoryHost = null, hero = null, panelHost = null, canvas = null;
            GameState fixture = null;
            HeroInventoryController panel = null;
            try
            {
                PanelManager.CloseAll();
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.GearInventory = new Dictionary<string, int> { { potionId, 3 } };
                stateHost = new GameObject("~UICapBagUseState");
                if (!InstallCaptureState(stateHost.AddComponent<GameStateService>(), fixture))
                    throw new InvalidOperationException("GameStateService capture seam unavailable");

                // AddComponent invokes Awake and installs the production singleton over the
                // temporary persisted state. The previous singleton is restored in finally.
                inventoryHost = new GameObject("~UICapBagUseInventory");
                var inventory = inventoryHost.AddComponent<VillageInventory>();
                InstallCaptureVillageInventory(inventory);

                hero = new GameObject("~UICapBagUseHero");
                hero.tag = "Player";
                var health = hero.AddComponent<HeroHealth>();
                SetPrivateFieldValue(health, "_hp", 45f);
                float hpBefore = health.Hp;
                int countBefore = inventory.Get(potionId);

                // The service owns a runtime cooldown dictionary. Clear only the capture id so
                // each independently built ratio starts in the same authoritative ready state.
                ClearConsumableCaptureCooldown(potionId);

                panelHost = new GameObject("~UICapBagUsePanel");
                panel = panelHost.AddComponent<HeroInventoryController>();
                panel.Open();
                var vm = GetPrivateFieldValue(panel, "_vm") as InventoryVM;
                if (vm == null) throw new InvalidOperationException("Bag built without InventoryVM");
                // Exercise the same rail command as the POTIONS tap. Calling SelectTab directly
                // changes VM data but deliberately does not leave the pseudo Gear destination,
                // producing an impossible screenshot (Gear stage + potion detail).
                InvokePrivate(panel, "SelectRail", 5); // HeroInventoryController.RailPotions
                vm.SelectById(potionId);
                Canvas.ForceUpdateCanvases();
                canvas = GetPrivateGameObject(panel, "_ui");
                if (canvas == null) throw new InvalidOperationException("Bag built no public canvas");

                int saved = RenderCanvasToPng(canvas,
                    OutDir + "BagUse_Selected_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
                vm.Use();
                Canvas.ForceUpdateCanvases();
                float hpAfter = health.Hp;
                int countAfter = inventory.Get(potionId);
                if (!(hpAfter > hpBefore) || countAfter != countBefore - 1 ||
                    string.IsNullOrEmpty(vm.Status) || !vm.Status.StartsWith("Used ", StringComparison.Ordinal))
                    throw new InvalidOperationException("production Bag use assertion failed: hp " +
                        hpBefore + "->" + hpAfter + ", count " + countBefore + "->" + countAfter +
                        ", status='" + (vm.Status ?? "<null>") + "'");
                Debug.Log("[UICap-BagUse] production assertion OK target=" + target.Tag +
                    " hp=" + hpBefore + "->" + hpAfter + " count=" + countBefore + "->" + countAfter);
                if (RenderCanvasToPng(canvas,
                    OutDir + "BagUse_Used_" + target.Tag + ".png", target.W, target.H)) saved++;
                return saved;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-BagUse] capture threw: " + e);
                return 0;
            }
            finally
            {
                try { panel?.Close(); } catch { }
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (panelHost != null) UnityEngine.Object.DestroyImmediate(panelHost);
                if (hero != null) UnityEngine.Object.DestroyImmediate(hero);
                RestoreCaptureVillageInventory(priorInventory);
                if (inventoryHost != null) UnityEngine.Object.DestroyImmediate(inventoryHost);
                RestoreCaptureState(priorState);
                if (stateHost != null) UnityEngine.Object.DestroyImmediate(stateHost);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                ClearConsumableCaptureCooldown(potionId);
                PanelManager.CloseAll();
            }
        }

        /// <summary>
        /// Route-census proof for registered public destinations that are intentionally outside
        /// the broad story-state harness. These are the real runtime components and real Open
        /// methods; the helper only supplies the edit-mode lifecycle tick that Unity normally
        /// supplies in play mode.
        /// </summary>
        public static void RunRegisteredSecondaryCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;

            int count = 0;
            count += CaptureReflectedSecondary("Benefactors", "DeNelle.HUD.BenefactorsWallPanel", "Open");
            count += CaptureReflectedSecondary("CosmeticShop", "DeNelle.HUD.CosmeticShopPanel", "OpenOverlay");
            count += CaptureReflectedSecondary("BuildingUpgrade", "DeNelle.Village.Buildings.Progression.BuildingUpgradePanelMvvm", "Open", new object[] { null });
            count += CaptureReflectedSecondary("Workshop", "DeNelle.Village.Crafting.VillageCraftingPanel", "Open");
            count += CaptureReflectedSecondary("PartyShop", "DeNelle.Village.Hero.PartyShopPanelMvvm", "Open", new object[] { null, "VILLAGE SHOP" });
            count += CaptureReflectedSecondary("Alchemy", "DeNelle.Village.Items.CraftingPanelMvvm", "Open");
            count += CaptureReflectedSecondary("Jeweler", "DeNelle.Village.Items.JewelerPanelMvvm", "Open");
            count += CaptureReflectedSecondary("HeroLoadout", "DeNelle.Village.Talents.HeroLoadoutPanelMvvm", "Open");
            count += CaptureReflectedSecondary("DefenseReport", "DeNelle.Village.UI.DefenseReportPanel", "Open");
            count += CaptureReflectedSecondary("GameGuide", "DeNelle.Village.GameGuidePanel", "Open");
            count += CaptureReflectedSecondary("MonthlyLedger", "DeNelle.Wallet.MonthlyLedgerPanel", "OnEnable", null, true);
            // WO-1394: the Season Track gets its first capture the day it gets its first door (the
            // Journey deck's Season card). Same host-free OnEnable recipe as its ledger sibling.
            count += CaptureReflectedSecondary("SeasonTrack", "DeNelle.Wallet.SeasonTrackPanel", "OnEnable", null, true);

            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            const int expected = 36;
            if (count == expected && _fidelityDegraded == 0 && _geoFailures.Count == 0 && _touchFailures.Count == 0)
                Debug.Log("REGISTERED_SECONDARY_CAPTURE_OK 36/36 frames; routes=12; touch=clean");
            else
                Debug.LogError("REGISTERED_SECONDARY_CAPTURE_FAIL frames=" + count + "/" + expected +
                    " fidelity=" + _fidelityDegraded + " geometry=" + _geoFailures.Count +
                    " touch=" + _touchFailures.Count);
        }

        private static int CaptureReflectedSecondary(string shotName, string typeName, string openMethod,
                                                      object[] arguments = null, bool privateMethod = false)
        {
            return ForEachTarget(shotName, target =>
            {
                GameObject eventSystem = null, host = null, canvas = null;
                Component panel = null;
                GameStateService priorState = null;
                GameObject stateHost = null;
                GameState stateFixture = null;
                try
                {
                    PanelManager.CloseAll();
                    if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                    {
                        eventSystem = new GameObject("~UICapEventSystem");
                        eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                    }
                    Type type = ResolveType(typeName);
                    if (type == null) throw new TypeLoadException(typeName);
                    if (shotName == "Jeweler")
                    {
                        priorState = GameStateService.Instance;
                        stateFixture = ScriptableObject.CreateInstance<GameState>();
                        stateFixture.MarkEverAcquired(DungeonExclusiveItems.RoughStoneId);
                        stateHost = new GameObject("~UICapJewelerState");
                        if (!InstallCaptureState(stateHost.AddComponent<GameStateService>(), stateFixture))
                            throw new InvalidOperationException("Jeweler progression fixture unavailable");
                    }
                    host = new GameObject("~UICap" + shotName);
                    panel = host.AddComponent(type);
                    InvokePrivate(panel, "Awake");
                    if (privateMethod)
                        InvokePrivate(panel, openMethod);
                    else
                    {
                        object[] args = arguments ?? Array.Empty<object>();
                        MethodInfo selected = null;
                        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                            if (candidate.Name == openMethod && candidate.GetParameters().Length == args.Length)
                            { selected = candidate; break; }
                        if (selected == null) throw new MissingMethodException(typeName, openMethod);
                        selected.Invoke(panel, args);
                    }
                    Canvas.ForceUpdateCanvases();
                    canvas = GetSecondaryCanvas(panel);
                    if (canvas == null) throw new InvalidOperationException(shotName + " built no public canvas");
                    return RenderCanvasToPng(canvas, OutDir + shotName + "_" + target.Tag + ".png",
                        target.W, target.H) ? 1 : 0;
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] registered secondary '" + shotName + "' threw: " + e);
                    return 0;
                }
                finally
                {
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                    if (host != null) UnityEngine.Object.DestroyImmediate(host);
                    if (stateHost != null)
                    {
                        RestoreCaptureState(priorState);
                        UnityEngine.Object.DestroyImmediate(stateHost);
                    }
                    if (stateFixture != null) UnityEngine.Object.DestroyImmediate(stateFixture);
                    if (eventSystem != null) UnityEngine.Object.DestroyImmediate(eventSystem);
                    PanelManager.CloseAll();
                }
            });
        }

        private static GameObject GetSecondaryCanvas(object panel)
        {
            if (panel == null) return null;
            for (Type t = panel.GetType(); t != null; t = t.BaseType)
            {
                foreach (string fieldName in new[] { "_ui", "_canvas" })
                {
                    var field = t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance |
                        BindingFlags.DeclaredOnly);
                    var direct = field != null ? field.GetValue(panel) as GameObject : null;
                    if (direct != null) return direct;
                }
                var modalField = t.GetField("_modal", BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                object modal = modalField != null ? modalField.GetValue(panel) : null;
                var canvasField = modal?.GetType().GetField("canvas", BindingFlags.Public | BindingFlags.Instance);
                var nested = canvasField != null ? canvasField.GetValue(modal) as GameObject : null;
                if (nested != null) return nested;
            }
            return null;
        }

        private static int CaptureBagOnce(CaptureTarget target)
        {
            GameObject host = null;
            GameObject canvas = null;
            HeroInventoryController panel = null;
            try
            {
                PanelManager.CloseAll();
                host = new GameObject("~UICapBag");
                panel = host.AddComponent<HeroInventoryController>();
                panel.Open();
                Canvas.ForceUpdateCanvases();
                canvas = GetPrivateGameObject(panel, "_ui");
                if (canvas == null)
                {
                    Debug.LogWarning("[UICap-HL] HeroInventoryController built no canvas; Bag skipped.");
                    return 0;
                }
                return RenderCanvasToPng(canvas, OutDir + "Bag_" + target.Tag + ".png",
                    target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] Bag capture threw: " + e);
                return 0;
            }
            finally
            {
                try { panel?.Close(); } catch { }
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                PanelManager.CloseAll();
            }
        }

        private static int CapturePlayerDeckOnce(CaptureTarget target, string openMethod, string shotName)
        {
            GameObject host = null;
            GameObject canvas = null;
            Action captureDoor = () => { };
            bool journeyFixture = string.Equals(shotName, "JourneyWorkspace", StringComparison.Ordinal);
            // Every deck card's Available is PanelRouter.IsRegistered(target), and no real panel
            // registers in this edit-mode fixture, so each destination gets a stand-in door here or
            // its card captures LOCKED. WO-1397: CosmeticShop joins the list for the Hero deck's
            // Wardrobe card - on device CosmeticShopPanelBootstrap spawns the panel in every
            // hero scene (CosmeticShopPanelBootstrap.cs EnsureFirst/SpawnInScene) and its Awake
            // registers PanelId.CosmeticShop, so "available" is what the player actually sees.
            PanelId[] fixtureDoors =
            {
                PanelId.Inventory, PanelId.EquipmentPanel, PanelId.HeroSkillTree, PanelId.HeroLoadout,
                PanelId.CosmeticShop,
                PanelId.RumorBoard, PanelId.RealmMap, PanelId.BattlePass, PanelId.RealmStore,
                PanelId.DefenseReport, PanelId.MonthlyLedger, PanelId.GameGuide
            };
            // WO-1376: the Journey deck's Dungeons card is Available only while the fail-closed
            // status rail says at least one gated portal is open. Edit mode has no cache and no
            // network, so without a fixture the card captures LOCKED every time and the capture
            // could never show the open face. The fixture below is the shape /api/dungeon-status
            // served on 2026-09-05 (5 open / 1 sealed) applied under its own provenance label, and
            // it is CLEARED in finally so no other capture inherits an open table.
            const string dungeonFixture =
                "{\"version\":1,\"dungeons\":{" +
                "\"dg_starter_loop\":{\"status\":\"open\"},\"dg_sunken_vault\":{\"status\":\"open\"}," +
                "\"dg_bonecrypt\":{\"status\":\"open\"},\"dg_ember_deep\":{\"status\":\"open\"}," +
                "\"dg_folks_granary\":{\"status\":\"open\"},\"dg_healers_cottage\":{\"status\":\"sealed\"}}}";
            try
            {
                PanelManager.CloseAll();
                foreach (var id in fixtureDoors) PanelRouter.Register(id, captureDoor);
                DeNelle.Core.World.DungeonStatusCatalog.ApplyPayload(dungeonFixture, "capture-fixture");
                if (journeyFixture)
                {
                    // WO-1404: publish a real cap so the zero-army proof reads "Army 0 / 10",
                    // never the pre-producer sentinel "Army 0 / 0".
                    DeNelle.Core.HudModel.PostureSignals.SetArmyFill(0, 10);
                    DeNelle.Core.HudModel.PostureSignals.SetRaidOpenCampCount(0);
                }
                Type type = ResolveType("DeNelle.HUD.PlayerDeckWorkspace");
                if (type == null)
                {
                    Debug.LogWarning("[UICap-HL] PlayerDeckWorkspace type not found; " + shotName + " skipped.");
                    return 0;
                }
                host = new GameObject("~UICap" + shotName);
                var workspace = host.AddComponent(type);
                InvokePrivate(workspace, "Awake");
                InvokePrivate(workspace, openMethod);
                Canvas.ForceUpdateCanvases();
                canvas = GetPrivateFieldValue(workspace, "_canvas") as GameObject;
                if (canvas == null)
                {
                    Debug.LogWarning("[UICap-HL] " + shotName + " built no canvas; skipped.");
                    return 0;
                }
                return RenderCanvasToPng(canvas, OutDir + shotName + "_" + target.Tag + ".png",
                    target.W, target.H) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[UICap-HL] " + shotName + " capture threw: " + e);
                return 0;
            }
            finally
            {
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                foreach (var id in fixtureDoors) PanelRouter.Unregister(id, captureDoor);
                DeNelle.Core.World.DungeonStatusCatalog.Clear();
                if (journeyFixture)
                {
                    DeNelle.Core.HudModel.PostureSignals.SetArmyFill(0, 0);
                    DeNelle.Core.HudModel.PostureSignals.SetRaidOpenCampCount(0);
                }
                PanelManager.CloseAll();
            }
        }

        private static int CaptureManageWorkspace()
        {
            return ForEachTarget("ManageWorkspace", target =>
            {
                GameObject host = null;
                GameObject canvas = null;
                try
                {
                    PanelManager.CloseAll();
                    host = new GameObject("~UICapManageWorkspace");
                    var panel = host.AddComponent<ManageScreenPanel>();
                    InvokePrivate(panel, "Awake");
                    panel.Open();
                    Canvas.ForceUpdateCanvases();
                    canvas = GetPrivateFieldValue(panel, "_ui") as GameObject;
                    return canvas != null && RenderCanvasToPng(canvas,
                        OutDir + "ManageWorkspace_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] ManageWorkspace capture threw: " + e);
                    return 0;
                }
                finally
                {
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                    if (host != null) UnityEngine.Object.DestroyImmediate(host);
                    PanelManager.CloseAll();
                }
            });
        }

        /// <summary>Focused three-ratio visual gate for the approved Manage launcher.</summary>
        public static void RunManageHubCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;
            int count = CaptureManageWorkspace();
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (count == 3 && _fidelityDegraded == 0 && _geoFailures.Count == 0 && _touchFailures.Count == 0)
                Debug.Log("MANAGE_HUB_CAPTURE_OK " + count + "/3 frames; touch=clean");
            else
                Debug.LogError("MANAGE_HUB_CAPTURE_FAIL frames=" + count + "/3 fidelity=" +
                    _fidelityDegraded + " geometry=" + _geoFailures.Count + " touch=" + _touchFailures.Count);
        }

        /// <summary>F8 2026-08-31 visual gate for the default, drawer-collapsed Defense workspace.</summary>
        public static void RunManageDefenseCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;
            int count = CaptureManageDefense();
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (count == 3 && _fidelityDegraded == 0 && _geoFailures.Count == 0 && _touchFailures.Count == 0)
                Debug.Log("MANAGE_DEFENSE_CAPTURE_OK " + count + "/3 frames; drawer=collapsed; touch=clean");
            else
                Debug.LogError("MANAGE_DEFENSE_CAPTURE_FAIL frames=" + count + "/3 fidelity=" +
                    _fidelityDegraded + " geometry=" + _geoFailures.Count + " touch=" + _touchFailures.Count);
        }

        private static int CaptureManageDefense()
        {
            return ForEachTarget("ManageDefense", target =>
            {
                GameObject host = null;
                GameObject canvas = null;
                try
                {
                    PanelManager.CloseAll();
                    host = new GameObject("~UICapManageDefense");
                    var panel = host.AddComponent<ManageScreenPanel>();
                    InvokePrivate(panel, "Awake");
                    panel.Open("Defense");
                    Canvas.ForceUpdateCanvases();
                    canvas = GetPrivateFieldValue(panel, "_ui") as GameObject;
                    return canvas != null && RenderCanvasToPng(canvas,
                        OutDir + "ManageDefense_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] ManageDefense capture threw: " + e);
                    return 0;
                }
                finally
                {
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                    if (host != null) UnityEngine.Object.DestroyImmediate(host);
                    PanelManager.CloseAll();
                }
            });
        }

        /// <summary>
        /// Focused three-ratio gate for every operational Manage destination. Unlike the empty-town
        /// Defense capture, this installs a throwaway founded-town state so every destination is
        /// genuinely disclosed by the same authoritative BaseLayout rules used at runtime.
        /// The prior singleton is restored after every frame; capture cannot mutate the editor save.
        ///
        /// <para>⛔ WO-1444 (2026-09-06) -- THIS ENTRY POINT CARRIED THE SAME TWO FAULTS AS THE FLOW
        /// MAP, AND THE ANSWER TO "does it share them" IS YES ON BOTH.
        /// <list type="number">
        /// <item>Its four destinations were the LEGACY tabs, and <c>ManageScreenVM.LegacyTabOf</c>
        /// maps BOTH <c>Defense</c> and <c>Buildings</c> to <c>ManageTabId.Build</c> (ruling 4) --
        /// so <c>ManageDefense_*</c> and <c>ManageBuildings_*</c> were the SAME SCREEN under two
        /// names, at all three widths. Six of twelve frames were duplicates.</item>
        /// <item>Its per-width "card grammar" variation wrote <c>_selectedBuildingId</c> /
        /// <c>_selectedDefenseId</c> / <c>_selectedResearchKey</c> by reflection. Nothing reads
        /// those while the workspace owns the well (ManageScreenPanel.RenderList:1974), so the
        /// three widths differed only in resolution -- and <c>SetPrivateField</c> merely WARNS on a
        /// field it cannot find, so the run stayed silent about it.</item>
        /// </list>
        /// It also swept nothing, so a frame it failed to re-take stayed on disk looking current.
        /// The destinations are now the THREE REAL TABS plus the Research school->perks screen (the
        /// count stays 12 because that fourth destination is a real, distinct screen, not a filler),
        /// three ratios of the SAME screen each -- which is what a geometry gate is for. Detail /
        /// card-state coverage lives in the flow map, whose frames now reach it honestly.</para>
        /// </summary>
        public static void RunManageOperationalCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;

            var shots = new[]
            {
                new KeyValuePair<DeNelle.Core.Manage.ManageTabId, bool>(DeNelle.Core.Manage.ManageTabId.Build, false),
                new KeyValuePair<DeNelle.Core.Manage.ManageTabId, bool>(DeNelle.Core.Manage.ManageTabId.Army, false),
                new KeyValuePair<DeNelle.Core.Manage.ManageTabId, bool>(DeNelle.Core.Manage.ManageTabId.Research, false),
                new KeyValuePair<DeNelle.Core.Manage.ManageTabId, bool>(DeNelle.Core.Manage.ManageTabId.Research, true),
            };
            int expected = shots.Length * LandscapeTargets.Length;   // DERIVED, never a constant

            var expectedFiles = new List<string>(expected);
            for (int s = 0; s < shots.Length; s++)
                for (int t = 0; t < LandscapeTargets.Length; t++)
                    expectedFiles.Add(OutDir + ManageOperationalShotName(shots[s].Key, shots[s].Value) +
                                      "_" + LandscapeTargets[t].Tag + ".png");
            BeginCaptureLedger("MANAGE_OPERATIONAL", expectedFiles, RetiredManageOperationalFrames());

            // ⛔ THE SAME OMISSION AS THE FLOW MAP'S, FIXED IN THE SAME BREATH. Both Manage entry
            // points reported fidelity WITHOUT ever running the aspect-divergence proof, so both
            // could only ever print UI_CAPTURE_FIDELITY_DEGRADED n/n with the fallback reason "the
            // aspect-divergence proof did not run". Leaving one of the pair unfixed is how a lane
            // spends the next round re-diagnosing a defect it already closed.
            ProveGeometryMoves();

            int count = 0;
            using (new ManageLastTabPin())
                for (int s = 0; s < shots.Length; s++)
                    count += CaptureManageOperational(shots[s].Key, shots[s].Value);

            int ledger = EndCaptureLedger();

            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (count == expected && ledger == 0 && _fidelityDegraded == 0 &&
                _geoFailures.Count == 0 && _touchFailures.Count == 0)
                Debug.Log("MANAGE_OPERATIONAL_CAPTURE_OK " + count + "/" + expected +
                          " frames; four destinations; touch=clean");
            else
                Debug.LogError("MANAGE_OPERATIONAL_CAPTURE_FAIL frames=" + count + "/" + expected +
                    " ledger=" + ledger + " fidelity=" + _fidelityDegraded +
                    " geometry=" + _geoFailures.Count + " touch=" + _touchFailures.Count);
        }

        /// <summary>The operational shot's stable file stem.</summary>
        private static string ManageOperationalShotName(DeNelle.Core.Manage.ManageTabId tab, bool schoolPerks)
        {
            if (schoolPerks) return "ManageResearchSchool";
            switch (tab)
            {
                case DeNelle.Core.Manage.ManageTabId.Army: return "ManageArmy";
                case DeNelle.Core.Manage.ManageTabId.Research: return "ManageResearch";
                default: return "ManageBuild";
            }
        }

        /// <summary>
        /// Frame names this entry point has RETIRED: <c>ManageBuildings_*</c> named the same screen
        /// as <c>ManageDefense_*</c>, and <c>ManageTroops_*</c> is now <c>ManageArmy_*</c>. Both are
        /// swept and not re-taken, so no png survives under a name whose screen no longer exists.
        ///
        /// <para>⛔ <c>ManageDefense_*</c> IS DELIBERATELY NOT ON THIS LIST, even though this entry
        /// point stopped writing it. <see cref="RunManageDefenseCaptureHeadless"/> (marker
        /// <c>MANAGE_DEFENSE_CAPTURE_OK</c>) is a SEPARATE entry point that owns exactly those three
        /// filenames. A sweep list is a delete list: putting a name on it that another entry point
        /// writes would make this run silently destroy that run's evidence -- which is the same
        /// class of harm the ledger exists to prevent, pointed the other way.</para>
        /// </summary>
        private static IEnumerable<string> RetiredManageOperationalFrames()
        {
            string[] retired = { "ManageBuildings", "ManageTroops" };
            for (int r = 0; r < retired.Length; r++)
                for (int t = 0; t < LandscapeTargets.Length; t++)
                    yield return OutDir + retired[r] + "_" + LandscapeTargets[t].Tag + ".png";
        }

        /// <summary>
        /// Focused evidence for the expanded queue state. Each production line contains two real
        /// running jobs plus one real pending job; the drawer is opened through the same private
        /// command used by its player-facing button before capture.
        /// </summary>
        public static void RunManageLiveQueueCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;

            int count = CaptureManageLiveQueue(ManageTab.Defense) +
                        CaptureManageLiveQueue(ManageTab.Troops) +
                        CaptureManageLiveQueue(ManageTab.Research);
            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (count == 9 && _fidelityDegraded == 0 && _geoFailures.Count == 0 && _touchFailures.Count == 0)
                Debug.Log("MANAGE_LIVE_QUEUE_CAPTURE_OK 9/9 frames; running+pending; touch=clean");
            else
                Debug.LogError("MANAGE_LIVE_QUEUE_CAPTURE_FAIL frames=" + count + "/9 fidelity=" +
                    _fidelityDegraded + " geometry=" + _geoFailures.Count + " touch=" + _touchFailures.Count);
        }

        private static int CaptureManageLiveQueue(ManageTab tab)
        {
            return ForEachTarget("ManageQueue" + tab, target =>
            {
                GameStateService prior = GameStateService.Instance;
                BuildTimerService priorQueue = BuildTimerService.Instance;
                GameObject stateHost = null, queueHost = null, panelHost = null, canvas = null;
                GameState fixture = null;
                try
                {
                    PanelManager.CloseAll();
                    fixture = ScriptableObject.CreateInstance<GameState>();
                    fixture.Onboarded = true;
                    fixture.BarracksLevel = 3;
                    fixture.BaseLayout = new List<PlacedStructureData>
                    {
                        new PlacedStructureData("tower_ground_archer", 3, 7, 0, 1),
                        new PlacedStructureData("barracks", 2, 2, 0, 4),
                        new PlacedStructureData("arcane-tower", 6, 3, 0, 4),
                    };
                    fixture.Wood = fixture.Iron = 100000;
                    var balances = fixture.Resources;
                    balances.Food = balances.Crystals = balances.Coins = 100000;
                    fixture.Resources = balances;
                    fixture.ObsidianQueue = ObsidianQueueState.Empty();
                    fixture.BuildingTiers["barracks"] = 3;
                    fixture.BuildingTiers["arcane-tower"] = 3;
                    fixture.VillageTier = 4;

                    stateHost = new GameObject("~UICapManageQueueState");
                    if (!InstallCaptureState(stateHost.AddComponent<GameStateService>(), fixture))
                        throw new InvalidOperationException("GameStateService capture seam is unavailable");
                    queueHost = new GameObject("~UICapManageQueueService");
                    var queue = queueHost.AddComponent<BuildTimerService>();
                    if (!InstallCaptureQueue(queue))
                        throw new InvalidOperationException("BuildTimerService capture seam is unavailable");
                    SeedManageCaptureQueue(queue);

                    panelHost = new GameObject("~UICapManageQueue" + tab);
                    var panel = panelHost.AddComponent<ManageScreenPanel>();
                    InvokePrivate(panel, "Awake");
                    panel.Open();
                    InvokePrivate(panel, "ShowOperational", tab);
                    InvokePrivate(panel, "ToggleQueueDrawer");
                    Canvas.ForceUpdateCanvases();
                    canvas = GetPrivateFieldValue(panel, "_ui") as GameObject;
                    return canvas != null && RenderCanvasToPng(canvas,
                        OutDir + "ManageQueue" + tab + "_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] ManageQueue" + tab + " capture threw: " + e);
                    return 0;
                }
                finally
                {
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                    if (panelHost != null) UnityEngine.Object.DestroyImmediate(panelHost);
                    RestoreCaptureQueue(priorQueue);
                    if (queueHost != null) UnityEngine.Object.DestroyImmediate(queueHost);
                    RestoreCaptureState(prior);
                    if (stateHost != null) UnityEngine.Object.DestroyImmediate(stateHost);
                    if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                    PanelManager.CloseAll();
                }
            });
        }

        private static int CaptureManageOperational(DeNelle.Core.Manage.ManageTabId tab, bool schoolPerks)
        {
            string shotName = ManageOperationalShotName(tab, schoolPerks);
            return ForEachTarget(shotName, target =>
            {
                GameStateService prior = GameStateService.Instance;
                BuildTimerService priorQueue = BuildTimerService.Instance;
                GameObject stateHost = null;
                GameObject queueHost = null;
                GameState fixture = null;
                GameObject panelHost = null;
                GameObject canvas = null;
                BuildingTierDef gatedTier = null;
                int priorGate = 0;
                bool hydratedCatalog = false;
                try
                {
                    PanelManager.CloseAll();

                    // ⛔ THE REASON THE DEFENCE FRAME WAS EMPTY, AND IT IS NOT THE FIXTURE'S LEVELS.
                    // CatalogBootstrap registers structures from a [RuntimeInitializeOnLoadMethod]
                    // (CatalogBootstrap.cs:96), which never runs in an -executeMethod capture. So
                    // CatalogRegistry.Get(placed.itemId) returned NULL for every BaseLayout row and
                    // BuildDefenseBrowse bailed at ManageScreenVM.cs:821 before it could ever reach
                    // the ceiling test — a level-1 archer under a ceiling of 3 emitted no row.
                    // Seeding more structures without this would have changed nothing.
                    // Idempotent, registers only absent ids, and restored below when WE hydrated.
                    hydratedCatalog = DeNelle.Core.Catalog.CatalogRegistry.Count == 0;
                    HydrateCatalogForCapture();

                    fixture = ScriptableObject.CreateInstance<GameState>();
                    fixture.Onboarded = true;
                    fixture.BarracksLevel = 3;
                    fixture.BaseLayout = new List<PlacedStructureData>
                    {
                        new PlacedStructureData("tower_ground_archer", 3, 7, 0, 1),
                        new PlacedStructureData("barracks", 2, 2, 0, 4),
                        new PlacedStructureData("arcane-tower", 6, 3, 0, 4),
                        new PlacedStructureData("forge", 8, 3, 0, 4),
                        new PlacedStructureData("lumbermill", 10, 3, 0, 4),

                        // WO-1422 lane D: the DEFENCE fixture. Before this the tab had exactly ONE
                        // placed defensive structure, so the frame could not prove a rail, a
                        // per-type tally, a Max card or a six-rung ladder — it proved a sentence.
                        // Every id + ceiling below is read from structures-catalog.json:
                        //   tower_ground_archer :15 maxLevel 3 (:31)   tower_ballista :88 maxLevel 3 (:103)
                        //   tower_catapult      :226 maxLevel 3 (:241) wall_wood      :288 maxLevel 2 (:304)
                        //   lumberyard          :1289 maxLevel 6 (:1309)
                        // A SECOND archer at a HIGHER level: the per-TYPE ruling (3.1) must fold
                        // these two into one rail row whose Level is the LOWEST placed (L1 at 3,7),
                        // so a per-instance rail regresses visibly here.
                        new PlacedStructureData("tower_ground_archer", 5, 9, 0, 2),
                        new PlacedStructureData("tower_ballista", 7, 9, 0, 1),
                        // AT its ceiling (3 of 3) -> the "Max" card with no CTA. The retired paged
                        // path SKIPS a maxed row (ManageScreenVM.cs:828), which is exactly why the
                        // old fixture could never paint one.
                        new PlacedStructureData("tower_catapult", 9, 9, 0, 3),
                        // Mid-climb on the only SIX-rung ladder in the catalog (WO-966 containers).
                        new PlacedStructureData("lumberyard", 11, 9, 0, 3),
                        // Three segments of one wall type: the card must read "3 placed . lowest L1"
                        // and the rail must still show ONE row (the unbounded-rail trap, ruling 3.1).
                        new PlacedStructureData("wall_wood", 13, 9, 0, 1),
                        new PlacedStructureData("wall_wood", 14, 9, 0, 1),
                        new PlacedStructureData("wall_wood", 15, 9, 0, 1),
                    };
                    fixture.Wood = 100000;
                    fixture.Iron = 100000;
                    var balances = fixture.Resources;
                    balances.Food = 100000;
                    balances.Crystals = 100000;
                    balances.Coins = 100000;
                    fixture.Resources = balances;
                    fixture.ObsidianQueue = ObsidianQueueState.Empty();
                    fixture.BuildingTiers["barracks"] = 3;
                    fixture.BuildingTiers["arcane-tower"] = 3;
                    fixture.BuildingTiers["forge"] = BuildingTierCatalog.MaxTier("forge");
                    fixture.BuildingTiers["lumbermill"] = 1;
                    fixture.VillageTier = 4;

                    // WO-1422 lane D: RESEARCH must reach all FOUR state words in ONE frame.
                    // Perk ids read from building-tiers.json (17 perks over 5 buildings; 'farm'
                    // authors none):
                    //   Researched  -> owned outright, below. BuildingPerkService.IsOwned reads
                    //                  GameState.OwnedBuildingPerks for the key "<building>:<perk>"
                    //                  (BuildingPerkService.cs:111-115, Key at :68).
                    //   Researching -> "arcane-tower:arcane-warding-runes", the running Research job
                    //                  seeded in SeedManageCaptureQueue (IsResearching, :122-132).
                    //   Available   -> every barracks perk (BuildingTiers["barracks"] = 3, unlock
                    //                  tiers 1/2/3) and forge's remaining two; Coins = 100000 pays.
                    //   Locked      -> lumbermill sits at tier 1, so its tier 2/3/4 perks fail
                    //                  CanResearch with "Upgrade the building to Tier N first."
                    //                  (BuildingPerkService.cs:181).
                    // This lives on the throwaway fixture GameState, so nothing global is mutated.
                    fixture.OwnedBuildingPerks = new List<string>
                    {
                        "forge:forge-efficient-smelting",
                    };

                    // Capture-only presentation fixtures: production canon currently tops out at
                    // Village Tier 3 requirements, so temporarily raise one real next-tier gate.
                    // The finally block restores the shared definition after every frame.
                    gatedTier = BuildingTierCatalog.TierOf("lumbermill", 2);
                    if (gatedTier == null)
                        throw new InvalidOperationException("Manage Buildings capture requires lumbermill tier 2");
                    priorGate = gatedTier.RequiresVillageTier;
                    gatedTier.RequiresVillageTier = 5;

                    stateHost = new GameObject("~UICapManageState");
                    var stateService = stateHost.AddComponent<GameStateService>();
                    if (!InstallCaptureState(stateService, fixture))
                        throw new InvalidOperationException("GameStateService capture seam is unavailable");

                    queueHost = new GameObject("~UICapManageQueue");
                    var queueService = queueHost.AddComponent<BuildTimerService>();
                    if (!InstallCaptureQueue(queueService))
                        throw new InvalidOperationException("BuildTimerService capture seam is unavailable");

                    // Exercise the real durable queue, not a display-only screenshot prop. Two jobs
                    // occupy each production line and a third is forced through the production
                    // Enqueue path into its pending FIFO. ManageScreenVM then derives every label,
                    // countdown, progress bar and occupancy value from BuildTimerService exactly as
                    // it does in a player session.
                    SeedManageCaptureQueue(queueService);

                    panelHost = new GameObject("~UICap" + shotName);
                    var panel = panelHost.AddComponent<ManageScreenPanel>();
                    InvokePrivate(panel, "Awake");
                    panel.Open();

                    // ⛔ THE MODEL DRIVES THE SCREEN. The old body called ShowOperational with a
                    // LEGACY tab and then wrote a private selection field by reflection; see this
                    // entry point's summary for why neither survived the redesign. EnterTab is the
                    // seam the tab row itself uses, and a refusal is a THROWN frame, never a
                    // silently-substituted one.
                    var vm = GetPrivateFieldValue(panel, "_vm") as ManageScreenVM;
                    if (vm == null)
                        throw new InvalidOperationException("ManageScreenPanel exposed no _vm -- nothing to drive");
                    vm.EnterTab(tab);
                    if (vm.Nav == null || vm.Nav.Tab != tab)
                        throw new InvalidOperationException(
                            "the model refused tab " + tab + " -- this destination is not available in " +
                            "this fixture, so the frame cannot be shot honestly");
                    if (schoolPerks && !TryOpenManageFlowSchool(vm, out string _))
                        throw new InvalidOperationException(
                            "no research school could be opened -- the perks screen cannot be shot honestly");

                    Canvas.ForceUpdateCanvases();
                    canvas = GetPrivateFieldValue(panel, "_ui") as GameObject;
                    return canvas != null && RenderCanvasToPng(canvas,
                        OutDir + shotName + "_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] " + shotName + " capture threw: " + e);
                    return 0;
                }
                finally
                {
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                    if (panelHost != null) UnityEngine.Object.DestroyImmediate(panelHost);
                    RestoreCaptureQueue(priorQueue);
                    if (queueHost != null) UnityEngine.Object.DestroyImmediate(queueHost);
                    RestoreCaptureState(prior);
                    if (stateHost != null) UnityEngine.Object.DestroyImmediate(stateHost);
                    if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                    if (gatedTier != null) gatedTier.RequiresVillageTier = priorGate;
                    // Leave the static registry exactly as this frame found it: we clear ONLY when
                    // this frame is the one that filled an empty registry, so a registry another
                    // capture (or a real bootstrap) owns is never wiped.
                    if (hydratedCatalog) DeNelle.Core.Catalog.CatalogRegistry.Clear();
                    PanelManager.CloseAll();
                }
            });
        }

        private static void SeedManageCaptureQueue(BuildTimerService queue)
        {
            if (queue == null) throw new ArgumentNullException(nameof(queue));

            // WO-1422 (CLI, 2026-09-06): this seeded "tower_ground_archer:7:0" - a COLON shape the live
            // game NEVER produces. PlacedUpgradeKey.Compose is the only composer in the tree
            // (BuildModeController.cs:2451,2529; ManageScreenVM.cs:995,1358) and it emits
            // "<itemId>@<cellX>_<cellZ>". PlacedUpgradeKey.TryParse requires that '@' and rejected the
            // colon form outright, so the capture was exercising a grammar that cannot occur: the
            // BUILDING NOW band could not resolve a name or art, AND the Archer Tower card read
            // "Upgradable" while its own job was running, because HasPlacedBuilderJob matches the key
            // exactly. A fixture that speaks a language the game does not is not a test.
            // The cell MUST match the placement seeded above (3,7) or the key names a tower that is not there.
            queue.Enqueue(JobKind.TowerUpgrade, ChannelId.Builder, PlacedUpgradeKey.Compose("tower_ground_archer", 3, 7), 420d, 2);
            queue.Enqueue(JobKind.Upgrade, ChannelId.Builder, "barracks:2:0", 660d, 4);
            queue.Enqueue(JobKind.Repair, ChannelId.Builder, "gate:4:1", 180d);

            queue.Enqueue(JobKind.TrainTroop, ChannelId.Train, "train:militia:capture-a", 240d);
            queue.Enqueue(JobKind.TrainTroop, ChannelId.Train, "train:archer:capture-b", 360d);
            queue.Enqueue(JobKind.TrainTroop, ChannelId.Train, "train:militia:capture-c", 240d);

            // ⚠ THE PERK ID WAS NOT REAL. This read "building-research:arcane-tower:warding" and
            // building-tiers.json authors NO perk 'warding' — the arcane-tower tier-3 perk is
            // 'arcane-warding-runes'. BuildingPerkService.IsResearching compares the WHOLE job id
            // (BuildingPerkService.cs:128-131), so the mismatch meant NO perk ever read as
            // in-progress and the "Researching" state word was unreachable in every capture.
            // Ruling 3.9 also parses index 2 of this id to resolve the NOW band's perk icon, which
            // an invented id would silently fail.
            queue.Enqueue(JobKind.BuildingResearch, ChannelId.Research,
                "building-research:arcane-tower:arcane-warding-runes", 540d);
            // ⚠ THE TROOP ID WAS NOT REAL EITHER — the same defect as the perk id above, one line
            // later. troops.json authors no 'militia' (ids run troop-footman ... troop-echo-
            // legionnaire), so the queue row could never resolve a display name and printed
            // "Troop Upgrade:militia - Level 2" (Builds/cap-manage-wave3.log:3739). WO-1564.
            queue.Enqueue(JobKind.TroopUpgrade, ChannelId.Research, "troop-upgrade:troop-footman", 720d, 2);
            queue.Enqueue(JobKind.LearnMagic, ChannelId.Research, "magic:frost-nova", 480d);
        }

        private static bool InstallCaptureState(GameStateService service, GameState state)
        {
            var stateField = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            var instanceField = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (stateField == null || instanceField == null) return false;
            stateField.SetValue(service, state);
            instanceField.SetValue(null, service);
            return true;
        }

        private static void RestoreCaptureState(GameStateService prior)
        {
            var instanceField = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceField != null) instanceField.SetValue(null, prior);
        }

        // =====================================================================
        //  ⛔ THE CAPTURE LEDGER (WO-1444, 2026-09-06) -- A CAPTURE THAT RESOLVES
        //     NOTHING MUST NEVER LEAVE A FRAME THAT LOOKS FRESH.
        // ---------------------------------------------------------------------
        //  THE DEFECT THIS EXISTS FOR, measured on 2026-09-06 and worse than the
        //  broken resolve that exposed it:
        //
        //    The 14:59 RunManageFlowMapCaptureHeadless run wrote 21/21 pngs into
        //    Builds/ui-capture/ and withheld MANAGE_FLOW_MAP_OK (geometry=132
        //    touch=116). But WITHIN each tab the files were BYTE-IDENTICAL --
        //    ManageFlow_Defense_{railtop,railbottom,locked,max} all 1319974 bytes,
        //    Troops all 1198852, Research all 1386082. Sixteen of the twenty-one
        //    frames were ONE image wearing FIVE FILENAMES, because the scroll seam
        //    and the selection seam had both died with the redesign and the harness
        //    reported neither. A reviewer opening "_locked" saw the grid.
        //
        //    Meanwhile docs/manage-flow-map/ still held the 09:17 baseline. Nothing
        //    in this harness has ever written there (OutDir is Builds/ui-capture/),
        //    so a directory of PNGs from BEFORE the redesign sat looking current.
        //
        //  THREE RULES, and they are cheap enough that there is no excuse:
        //
        //   1. SWEEP FIRST. Every frame a run intends to take -- plus every frame
        //      name the run has RETIRED -- is DELETED before the run starts. A
        //      frame that cannot be re-taken is then honestly ABSENT. Absence is a
        //      finding; a stale png is a lie.
        //   2. HASH AFTER. Two DIFFERENT filenames with IDENTICAL bytes inside one
        //      run is a FAILURE, not a coincidence: it means the state the second
        //      filename claims was never reached. This alone would have caught the
        //      14:59 run.
        //   3. DERIVE THE COUNT. The expected total comes from the PLAN, never from
        //      a `const int Expected = 21` sitting beside it. A hand-maintained
        //      count next to the thing it counts is duplicated state and it goes
        //      stale exactly like CLAUDE.md §2's WO-number block and §5's
        //      dependency table.
        //
        //  NOTE the ledger does NOT decide the marker on its own -- it CONTRIBUTES a
        //  failure count that the entry point folds into its own OK/FAIL test, so a
        //  run can never be green with a missing or duplicated frame.
        // =====================================================================

        private static readonly List<string> _ledgerExpected = new List<string>();

        /// <summary>
        /// Frames whose bytes are allowed to match a sibling's because the SCREEN cannot differ.
        /// The only member so far: a "gridbottom" frame whose grid content already fits inside the
        /// viewport -- there is no bottom to scroll to, so gridtop and gridbottom ARE one picture
        /// and the filename is still truthful. Populated by the probe, which is the only place that
        /// can MEASURE it (content px vs viewport px on the settled layout); never a hardcoded
        /// exemption list, because a hardcoded one would grow into a way to silence real duplicates.
        /// </summary>
        private static readonly HashSet<string> _ledgerIdenticalByConstruction =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string _ledgerRun;

        /// <summary>
        /// ⛔ PIN THE LAST-USED TAB, AND PUT IT BACK. ManageScreenPanel.Open calls
        /// ManageScreenVM.OpenDefaultScreen, which reads PlayerPrefs "manage.lasttab"
        /// (ManageScreenVM.cs:2866/2953) -- so WITHOUT this pin the opening screen of every frame
        /// is whatever tab the PREVIOUS RUN happened to leave behind, and a capture whose content
        /// depends on run history is not evidence. EnterTab WRITES that pref too, so the harness
        /// was also silently editing the editor's own preferences. Both ends are closed here.
        /// </summary>
        private sealed class ManageLastTabPin : IDisposable
        {
            private readonly bool _had;
            private readonly int _prior;

            public ManageLastTabPin()
            {
                _had = PlayerPrefs.HasKey(ManageScreenVM.LastTabPrefKey);
                _prior = PlayerPrefs.GetInt(ManageScreenVM.LastTabPrefKey, (int)DeNelle.Core.Manage.ManageTabId.Build);
                PlayerPrefs.SetInt(ManageScreenVM.LastTabPrefKey, (int)DeNelle.Core.Manage.ManageTabId.Build);
            }

            public void Dispose()
            {
                if (_had) PlayerPrefs.SetInt(ManageScreenVM.LastTabPrefKey, _prior);
                else PlayerPrefs.DeleteKey(ManageScreenVM.LastTabPrefKey);
            }
        }

        /// <summary>
        /// Delete every frame this run intends to write, plus every frame name it has retired,
        /// so nothing older than this run can survive in the output directory under a name the
        /// run owns. Logs the sweep so the count is in the log even when the run then dies.
        /// </summary>
        private static void BeginCaptureLedger(string runName, IEnumerable<string> expectedFiles,
                                               IEnumerable<string> retiredFiles)
        {
            _ledgerRun = runName;
            _ledgerExpected.Clear();
            _ledgerIdenticalByConstruction.Clear();
            int deleted = 0;

            if (expectedFiles != null)
            {
                foreach (var f in expectedFiles)
                {
                    if (string.IsNullOrEmpty(f)) continue;
                    _ledgerExpected.Add(f);
                    if (TryDeleteCaptureFile(f)) deleted++;
                }
            }

            if (retiredFiles != null)
            {
                foreach (var f in retiredFiles)
                {
                    // A RETIRED name is swept but NOT expected back. This is what stops a frame
                    // whose screen no longer exists from sitting in the directory forever, still
                    // looking like current evidence of a screen the redesign deleted.
                    if (!string.IsNullOrEmpty(f) && TryDeleteCaptureFile(f)) deleted++;
                }
            }

            Debug.Log("CAPTURE_LEDGER_SWEPT " + runName + " deleted=" + deleted +
                      " expected=" + _ledgerExpected.Count +
                      " -- any frame missing after this run was NOT re-taken, and is absent on purpose.");
        }

        private static bool TryDeleteCaptureFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                // A file we cannot delete is the dangerous case -- it is exactly the stale frame
                // this ledger exists to remove -- so it is an ERROR, never a silent skip.
                Debug.LogError("[UICap-HL] CAPTURE_LEDGER could not sweep '" + path + "': " + e.Message +
                               " -- a frame from an earlier run may survive under a name this run owns.");
                return false;   // NOT deleted: the count must never claim a sweep that did not happen
            }
        }

        /// <summary>
        /// Close the ledger: every expected frame must exist, and no two expected frames may carry
        /// identical bytes. Returns the FAILURE COUNT (0 == clean) so the caller folds it into its
        /// own marker test rather than this method inventing a second marker.
        /// </summary>
        private static int EndCaptureLedger()
        {
            int failures = 0;
            var byHash = new Dictionary<string, string>(StringComparer.Ordinal);

            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                for (int i = 0; i < _ledgerExpected.Count; i++)
                {
                    string path = _ledgerExpected[i];
                    if (!File.Exists(path))
                    {
                        Debug.LogError("CAPTURE_LEDGER_MISSING " + (_ledgerRun ?? "?") + " " + path +
                                       " -- the run did not write this frame. It was swept before the run, " +
                                       "so there is NO stale image standing in for it. Treat as absent.");
                        failures++;
                        continue;
                    }

                    string hash;
                    try
                    {
                        hash = BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path)));
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("CAPTURE_LEDGER_UNREADABLE " + path + ": " + e.Message);
                        failures++;
                        continue;
                    }

                    if (byHash.TryGetValue(hash, out string first))
                    {
                        // MEASURED EXEMPTION, not a hardcoded one: the probe proved this frame's grid
                        // already fits its viewport, so there is no bottom to scroll to and the two
                        // filenames are both truthful. The mockup's screen 4 ("all 9 troops visible,
                        // no scrolling") makes this the DESIRED state on ARMY, not a defect.
                        if (_ledgerIdenticalByConstruction.Contains(path) ||
                            _ledgerIdenticalByConstruction.Contains(first))
                        {
                            Debug.Log("CAPTURE_LEDGER_IDENTICAL_BY_CONSTRUCTION " + Path.GetFileName(path) +
                                      " == " + Path.GetFileName(first) + " -- the grid fits its viewport, " +
                                      "so there is no scrolled state to differ. Not counted as a failure.");
                            continue;
                        }
                        Debug.LogError("CAPTURE_LEDGER_DUPLICATE " + (_ledgerRun ?? "?") + " '" +
                            Path.GetFileName(path) + "' is BYTE-IDENTICAL to '" + Path.GetFileName(first) +
                            "'. Two filenames, one image: the state the second name claims was never " +
                            "reached, so that frame's filename is a lie. This is a FAILURE, not a " +
                            "coincidence -- fix the seam that was supposed to change the screen.");
                        failures++;
                        continue;
                    }
                    byHash[hash] = path;
                }
            }

            Debug.Log("CAPTURE_LEDGER_CLOSED " + (_ledgerRun ?? "?") + " expected=" + _ledgerExpected.Count +
                      " present=" + byHash.Count + " failures=" + failures);
            return failures;
        }

        // =====================================================================
        //  MANAGE FLOW MAP (2026-09-06) -- a DOCUMENTATION capture, not a fit gate.
        //
        //  THE PROBLEM IT EXISTS FOR, in the owner's words: "the disconnect is the
        //  massive amount of data in manage". Every existing Manage capture shoots the
        //  rail at its resting scroll position, which shows ~2 rows of a list that may
        //  be 17 long, so the VOLUME of the screen is invisible in the evidence. This
        //  entry point documents the whole flow INCLUDING what sits below the fold, at
        //  ONE landscape size (2670x1200, the Seeker's real surface) because it is
        //  documenting CONTENT, not proving geometry at three ratios.
        //
        //  ⛔ IT DOES NOT TOUCH RunManageOperationalCaptureHeadless. That entry point's
        //  frame count is pinned at 12 by its own marker and by suites; this is an
        //  ADDITIVE second door with its own marker and its own fixture.
        //
        //  TWO ARTEFACTS, and the TEXT one is worth as much as the pixels:
        //    Builds/ui-capture/ManageFlow_<TAB>_<state>_2670x1200.png
        //    MANAGE_FLOW_INVENTORY <TAB>: grid tiles=<n> (vm=.. rendered=..) visible=<f>
        //        viewport=<px> content=<px> pitch=<px> queueRows(line=<Channel>)=<n>
        //  "visible" is the number the owner is actually asking about: how much of the
        //  screen's content a player sees before scrolling.
        //
        //  ⚠ THE OUTPUT DIRECTORY IS Builds/ui-capture/, NOT docs/manage-flow-map/.
        //  CAPTURE_LOOP_GOAL.md said "Output: docs/manage-flow-map/" and NOTHING in this
        //  harness has ever written there. That folder is the FROZEN 09:17 baseline every
        //  WO-200x cites as "run Builds/flowmap1"; a reader comparing a new build against
        //  it was comparing against pre-redesign pixels. Read fresh frames out of OutDir.
        //
        // ---------------------------------------------------------------------
        //  WO-1444 (2026-09-06) -- REPOINTED AT THE WORKSPACE, AND THE OLD SEAM IS DEAD.
        //
        //  ⛔ WHAT THIS HARNESS USED TO RESOLVE, AND WHY IT RESOLVES NOTHING NOW.
        //  It looked up a rail by the authored zone name -- "DefenseSelectorRail" /
        //  "BuildingSelectorRail" / "TroopSelectorRail" / "ResearchSelectorRail" -- and
        //  took the ScrollRect under it. Those zones are built by RenderBuildingsDestination
        //  and its three siblings, which are reached ONLY through ManageScreenPanel.
        //  RenderList. Since WO-2001 that method's FIRST LINE is
        //      if (WorkspaceActive) { RenderWorkspace(); return; }
        //  (ManageScreenPanel.cs:1974). ManageWorkspacePanel owns the body well, the legacy
        //  list band is SetActive(false) (ShowWorkspace, :784), and NO rail object is ever
        //  constructed. So the lookup answered null on every tab -- the four
        //  "no rail ScrollRect resolved" lines in Builds/manage-capture-r1.log.
        //
        //  WHAT IT MUST RESOLVE INSTEAD: the workspace GRID.
        //      _workspaceHost ("ManageWorkspace")            ManageScreenPanel.cs:765
        //        -> "ManageGridScroll"  (ScrollRect + RectMask2D)  ManageWorkspacePanel.cs:475
        //             -> "Content"      (GridLayoutGroup, FixedColumnCount)          :487
        //                  -> "ManageTile" x N                                       :546
        //  The rows are DERIVED (ceil(tiles / constraintCount)) and the pitch is
        //  cellSize.y + spacing.y -- read off the live GridLayoutGroup, never authored here.
        //
        //  ⛔ AND THE SELECTION SEAM IS DEAD TOO. The old body wrote _selectedBuildingId /
        //  _selectedTroopId / _selectedDefenseId / _selectedResearchKey by reflection. Those
        //  fields are read only by the LEGACY row factories, which no longer run, so the
        //  "locked" and "max" frames were the plain grid under a filename that claimed a
        //  card. Selection is now a SCREEN: the model's ManageNavEntry (Kind=Detail), reached
        //  by invoking the tile's OWN Activate callback -- the exact seam a player's tap
        //  uses. If the fixture produces no tile in the wanted state the frame FAILS; it is
        //  never quietly substituted.
        //
        //  ⛔ THE HUB FRAME HAS NO ANALOGUE AND IS RETIRED, NOT SILENTLY DROPPED. WO-2001
        //  deleted the four-tile chooser: ManageScreenPanel.Open builds the launcher cards
        //  (they remain the record of the 2026-08-31 approved art) but ShowWorkspace hides
        //  the host and it is never shown again. Manage now OPENS on a tab grid, so the hub
        //  frame and the BUILD gridtop frame would be the same picture -- which is precisely
        //  the duplicate-filename lie the ledger above now fails on. Replaced by
        //  ManageFlow_RESEARCH_school, the one screen in the new flow that nothing else
        //  covers (canon 5: school first, THEN its perks).
        //
        //  ⚠ AND IT MAY COME BACK -- that is a DIVERGENCE, not a settled decision. The owner's
        //  mockup (docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png, screen 1) shows a HUB of
        //  three large BUILD / ARMY / RESEARCH cards, and CAPTURE_LOOP_GOAL 3.0c records that
        //  the mockup WINS where it disagrees with a ruling. This frame is retired because the
        //  hub is UNREACHABLE IN THE BUILD AS IT STANDS, not because a hub is wrong. If the hub
        //  is restored, put a Hub member back on ManageFlowFrame and into BuildManageFlowPlan --
        //  the plan is the only place the frame set lives.
        //
        //  ⛔ FOUR TABS COLLAPSED TO THREE. ManageScreenVM.LegacyTabOf maps BOTH Defense and
        //  Buildings to ManageTabId.Build (ruling 4 -- they share the Builder line), so the
        //  old Defense_* and Buildings_* frames were the same screen twice. The plan below
        //  enumerates ManageTabId, which is the axis the screen actually has.
        // =====================================================================

        /// <summary>ONE landscape target. This is a documentation capture; three ratios would
        /// triple the frames without adding a fact about how much content the screen holds.</summary>
        private static readonly CaptureTarget[] ManageFlowMapTargets =
        {
            new CaptureTarget(2670, 1200),   // THE SEEKER'S REAL SURFACE
        };

        /// <summary>Which frame of the Manage flow a single capture body is shooting.</summary>
        // ⭐ WO-1489 ADDS TWO MEMBERS, AND BOTH EXIST BECAUSE A STATE WAS UNPHOTOGRAPHABLE.
        //
        //  ActionDetail -- THE DETAIL SCREEN A PLAYER CAN ACT ON. The plan shot only LockedDetail
        //  and MaxDetail, which are the two states with NO action by definition, so every painter
        //  in ManageWorkspacePanel that draws a before/after stat pair (:1006-1016), a cost row
        //  (:1110-1115) and the time/UPGRADE action (:1140-1142) was outside the capture set.
        //  ⛔ WO-1489 read that absence as a DEFECT in ManageFlow_BUILD_max ("shows NO before/after
        //  stats, no cost row, no time and no UPGRADE button - although every painter exists").
        //  It is not: that frame is the forge at Level 4 of 4 (MANAGE_FLOW_STATE BUILD/max ->
        //  forge state=Max, Builds/cap-manage-wave5c.log 03:20), and a MAX item HAS no next level
        //  to price. The painters were never reached because the state was correct. The real gap
        //  is that the actionable screen had no frame at all, and this member is that frame.
        //
        //  HubHeart -- THE HUB WITH ITS HEART CHIP UP. ⚠ SEPARATE FROM Hub, NOT A REPLACEMENT FOR
        //  IT. ManageScreenPanel.BuildHubHeartDoor draws the chip ONLY while
        //  ManageScreenVM.HeartUpgradeAvailable, i.e. while HeartProgression.State != Max, and the
        //  fixture sits at VillageTier 4 against an authored maxLevel of 3
        //  (Assets/Resources/Data/Canonical/heart-progression.json:4 -> VillageTierService.MaxTier
        //  -> HeartProgression.IsMax), so the Hub frame is permanently the chip-ABSENT hub. That
        //  frame is worth keeping exactly as it is - mockup panel 1 draws no chip either - so the
        //  chip-present state gets its own shot with its own fixture tier rather than being
        //  smuggled into the frame the owner compares against panel 1.
        private enum ManageFlowFrame
        { Hub, HubHeart, GridTop, GridBottom, QueueDrawer, ActionDetail, LockedDetail, MaxDetail, SchoolPerks }

        /// <summary>One planned shot. The PLAN is the only place the frame set is written down,
        /// and <c>Expected</c> is its Length -- never a constant beside it (CLAUDE.md §2/§5:
        /// a hand-kept count next to the thing it counts is duplicated state).</summary>
        private struct ManageFlowShot
        {
            public readonly DeNelle.Core.Manage.ManageTabId Tab;
            public readonly ManageFlowFrame Frame;
            public ManageFlowShot(DeNelle.Core.Manage.ManageTabId tab, ManageFlowFrame frame)
            { Tab = tab; Frame = frame; }
        }

        private static string ManageFlowStateWord(ManageFlowFrame frame)
        {
            switch (frame)
            {
                case ManageFlowFrame.Hub: return "hub";
                case ManageFlowFrame.HubHeart: return "hubheart";
                case ManageFlowFrame.GridTop: return "gridtop";
                case ManageFlowFrame.GridBottom: return "gridbottom";
                case ManageFlowFrame.QueueDrawer: return "queue";
                case ManageFlowFrame.ActionDetail: return "action";
                case ManageFlowFrame.LockedDetail: return "locked";
                case ManageFlowFrame.MaxDetail: return "max";
                default: return "school";
            }
        }

        private static string ManageFlowShotName(ManageFlowShot shot)
        {
            return "ManageFlow_" + ManageScreenVM.TabWordOf(shot.Tab) + "_" + ManageFlowStateWord(shot.Frame);
        }

        /// <summary>
        /// The marker's own description of the frame set, DERIVED from the plan it just shot.
        /// One "TAB(state,state,...)" group per tab, in plan order. Never a typed list: the
        /// sentence this replaced was a second copy of BuildManageFlowPlan's contents and went
        /// stale the moment the plan grew (CLAUDE.md §2/§5 - duplicated state).
        /// </summary>
        private static string DescribeManageFlowPlan(ManageFlowShot[] plan)
        {
            if (plan == null || plan.Length == 0) return "<empty plan>";
            var order = new List<string>(4);
            var states = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Length; i++)
            {
                string tab = ManageScreenVM.TabWordOf(plan[i].Tab);
                if (!states.TryGetValue(tab, out var list))
                { list = new List<string>(8); states[tab] = list; order.Add(tab); }
                list.Add(ManageFlowStateWord(plan[i].Frame));
            }
            var parts = new List<string>(order.Count);
            for (int i = 0; i < order.Count; i++)
                parts.Add(order[i] + "(" + string.Join(",", states[order[i]].ToArray()) + ")");
            return string.Join(" + ", parts.ToArray());
        }

        /// <summary>
        /// THE PLAN. The three real tabs crossed with every workspace state, plus the two hub
        /// frames and the Research school->perks screen. Change the flow set HERE and nowhere else.
        /// ⛔ DO NOT WRITE THE FRAME COUNT INTO A COMMENT, A MARKER OR A DOC. This summary said
        /// "three real tabs x five states" and was wrong the moment a sixth state was added; the
        /// count is <c>plan.Length</c> and the marker prints it.
        /// </summary>
        private static ManageFlowShot[] BuildManageFlowPlan()
        {
            var tabs = new[]
            {
                DeNelle.Core.Manage.ManageTabId.Build,
                DeNelle.Core.Manage.ManageTabId.Army,
                DeNelle.Core.Manage.ManageTabId.Research,
            };
            var frames = new[]
            {
                ManageFlowFrame.GridTop, ManageFlowFrame.GridBottom, ManageFlowFrame.QueueDrawer,
                ManageFlowFrame.ActionDetail, ManageFlowFrame.LockedDetail, ManageFlowFrame.MaxDetail,
            };
            var plan = new List<ManageFlowShot>(tabs.Length * frames.Length + 3);
            // (!) THE HUB FRAME IS BACK (WO-1567 panel row 1, 2026-09-07).
            // (!) THE RETIREMENT NOTE ABOVE STATED ITS OWN REVERSAL CONDITION and this is it: the hub
            // was retired because it was UNREACHABLE, "not because a hub is wrong", and it said
            // "if the hub is restored, put a Hub member back on ManageFlowFrame and into
            // BuildManageFlowPlan - the plan is the only place the frame set lives." WO-1443
            // restored ShowLauncher and mockup panel 1 is the screen the owner opens Manage onto,
            // so it is now BOTH reachable and the least-covered screen in the flow.
            // !! IT IS NOT A DUPLICATE OF THE BUILD GRID any more, which was the ledger objection:
            // the hub is three cards, a title and CLOSE, and the grid is neither.
            // (!) THE TAB IS CARRIED ONLY TO NAME THE FILE. The hub is above all three destinations;
            // Build is used so the frame sorts beside the screen it leads to. The capture body
            // does NOT enter it - entering a tab is precisely what dismisses the hub.
            plan.Add(new ManageFlowShot(DeNelle.Core.Manage.ManageTabId.Build, ManageFlowFrame.Hub));
            // (!) AND THE SAME HUB WITH THE HEART CHIP UP (WO-1489). Same screen, one fixture
            // difference (VillageTier below the authored Heart ceiling), and it is a SECOND frame
            // rather than a change to the one above because the two hubs are different screens to
            // the owner: panel 1 has no chip, and the chip reserves ElarionUiKit.MinTouchPx of the
            // hub host away from the card band (ManageScreenPanel._hubHeartY0). Photographing only
            // one of them leaves the other's geometry unproven, and the chip's seat is the control
            // that produced seven of the eleven non-queue oracle failures on cap-manage-wave4.log.
            plan.Add(new ManageFlowShot(DeNelle.Core.Manage.ManageTabId.Build, ManageFlowFrame.HubHeart));
            for (int t = 0; t < tabs.Length; t++)
                for (int f = 0; f < frames.Length; f++)
                {
                    // ⛔ WO-1516 (owner ruling 2026-09-06 20:07): THE BUILD GRID CAN NO LONGER
                    // PRODUCE A LOCKED TILE TO PHOTOGRAPH, so asking for one is a guaranteed
                    // MANAGE_FLOW_MAP_FAIL. That lane re-pointed InventoryTiles() at
                    // BuildInventoryModel.Tiles(chip) - the accessor that matches the BUILD
                    // palette's own BuildAvailability.Offered - so the BUILD grid is unlocked-only
                    // by construction, and its own oracle
                    // (ManageProgressiveDisclosureRegression [build-grid-is-unlocked-only]) FAILS
                    // if a Locked tile ever reappears there.
                    // ⚠ ARMY AND RESEARCH KEEP THEIR LockedDetail FRAME - locked troops (mockup
                    // panel 4) and locked perks (WO-1518) both still exist and are still worth
                    // photographing. Expected is derived from plan.Length, so nothing else changes
                    // and no frame count is hand-kept.
                    if (tabs[t] == DeNelle.Core.Manage.ManageTabId.Build &&
                        frames[f] == ManageFlowFrame.LockedDetail) continue;
                    plan.Add(new ManageFlowShot(tabs[t], frames[f]));
                }
            plan.Add(new ManageFlowShot(DeNelle.Core.Manage.ManageTabId.Research, ManageFlowFrame.SchoolPerks));
            return plan.ToArray();
        }

        /// <summary>
        /// The 21 frame names this entry point USED to write. They are SWEPT and not re-taken:
        /// four of them name a tab the screen no longer has (Defense/Buildings are one BUILD tab)
        /// and one names a hub that no longer exists. Leaving them on disk is exactly the lie the
        /// ledger exists to stop -- a reader listing the folder would see current-looking evidence
        /// of screens that were deleted.
        /// </summary>
        private static IEnumerable<string> RetiredManageFlowFrames()
        {
            var tag = ManageFlowMapTargets[0].Tag;
            yield return OutDir + "ManageFlow_Hub_" + tag + ".png";
            string[] legacyTabs = { "Defense", "Buildings", "Troops", "Research" };
            string[] legacyStates = { "railtop", "railbottom", "queue", "locked", "max" };
            for (int t = 0; t < legacyTabs.Length; t++)
                for (int s = 0; s < legacyStates.Length; s++)
                    yield return OutDir + "ManageFlow_" + legacyTabs[t] + "_" + legacyStates[s] + "_" + tag + ".png";
        }

        // Hand-off from the capture body to the settled-layout probe. Set before
        // RenderCanvasToPng, cleared in the body's finally -- never read anywhere else.
        private static ScrollRect _flowGridScroll;
        private static bool _flowGridToBottom;
        private static bool _flowMeasureGrid;
        private static string _flowMeasureTab;
        private static int _flowVmGridTiles;
        private static int _flowVmQueueRows;
        private static string _flowQueueChannel;
        private static readonly List<string> _flowInventory = new List<string>();
        private static readonly List<string> _flowStateNotes = new List<string>();

        /// <summary>
        /// Documents the ENTIRE Manage flow: for each of the three workspace tabs the grid at the
        /// top, the grid SCROLLED TO THE BOTTOM (so the full content length is visible), the queue
        /// drawer, and the two DETAIL screens a player cannot act on (locked and max) -- plus the
        /// Research school->perks screen. Each frame carries a per-tab TEXT INVENTORY of how many
        /// tiles the grid holds versus how many fit on screen.
        /// </summary>
        public static void RunManageFlowMapCaptureHeadless()
        {
            Directory.CreateDirectory(OutDir);
            _fidelityOk = _fidelityDegraded = 0;
            _fidelityReasons.Clear();
            _geoFailures.Clear();
            _touchFailures.Clear();
            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
            _geoCanvasesChecked = _touchPanelsChecked = _touchPanelsClean = 0;
            _flowInventory.Clear();
            _flowStateNotes.Clear();

            // ⛔ THE ASPECT-DIVERGENCE PROOF, WHICH THIS ENTRY POINT HAD NEVER RUN. This is the
            // whole of `UI_CAPTURE_FIDELITY_DEGRADED 16/16` on Builds/cap-manage-wave4.log, and the
            // marker quoted its own cause verbatim: "Reason: the aspect-divergence proof did not
            // run" - which is ReportFidelity's FALLBACK string, printed when `_geoMoveFailure` is
            // null because nothing ever attempted the proof. Five other capture bodies call it
            // (lines 571, 648, 670, 2068, 2600); this one did not, so `_geoMoveProof` was still null
            // at ReportFidelity and it degraded all sixteen builds on a proof that was never asked
            // for. ⚠ THAT IS A HARNESS DEFECT, NOT A LAYOUT ONE: no Manage panel reads Screen.*
            // (verified by grep over Assets/_Modules/Core/Manage and .../Village/UI/Manage - zero
            // hits), so every zone in these frames already resolves through the kit surface the
            // scope moves. The frames were target-accurate; the run simply could not say so.
            // It runs BEFORE the sweep so a failing proof is on the log above the frames it judges.
            ProveGeometryMoves();

            var plan = BuildManageFlowPlan();
            int expected = plan.Length;   // DERIVED from the plan. Never a constant beside it.

            // ⛔ SWEEP BEFORE THE RUN. Every frame this run owns -- and every frame name it has
            // retired -- is deleted first, so nothing that survives can be older than this run.
            var expectedFiles = new List<string>(expected);
            for (int i = 0; i < plan.Length; i++)
                expectedFiles.Add(OutDir + ManageFlowShotName(plan[i]) + "_" + ManageFlowMapTargets[0].Tag + ".png");
            BeginCaptureLedger("MANAGE_FLOW_MAP", expectedFiles, RetiredManageFlowFrames());

            int count = 0;
            using (new ManageLastTabPin())
                for (int i = 0; i < plan.Length; i++) count += CaptureManageFlowFrame(plan[i]);

            // The inventory prints BEFORE the marker so it survives a FAIL: the tile counts are the
            // answer to the owner's question even on a run where a frame did not render.
            for (int i = 0; i < _flowStateNotes.Count; i++) Debug.Log("MANAGE_FLOW_STATE " + _flowStateNotes[i]);
            for (int i = 0; i < _flowInventory.Count; i++) Debug.Log(_flowInventory[i]);

            int ledger = EndCaptureLedger();

            ReportFidelity();
            ReportGeometry();
            ReportTouchOracle();
            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
                                   // table and at its one emit site -- never copied here. Wired at
                                   // EVERY site that emits the touch marker: one path missing it
                                   // prints marker-absent there, read here as a FAILURE not an unknown.
            if (count == expected && ledger == 0 && _fidelityDegraded == 0 &&
                _geoFailures.Count == 0 && _touchFailures.Count == 0)
                // ⛔ THE FRAME SET IS DESCRIBED FROM THE PLAN, NOT RETYPED. This line used to read
                // "the hub + 3 destinations x (grid top, grid bottom, queue, locked, max) +
                // research school" - a hand-kept list of the very thing BuildManageFlowPlan is
                // declared to be the only home of, and therefore a stale copy the first time the
                // set grew (WO-1489 grew it twice in one edit). Deriving it means the marker can
                // never describe a run it did not do.
                Debug.Log("MANAGE_FLOW_MAP_OK " + count + " frames; " + DescribeManageFlowPlan(plan) +
                          "; inventory lines=" + _flowInventory.Count);
            else
                Debug.LogError("MANAGE_FLOW_MAP_FAIL frames=" + count + "/" + expected + " ledger=" + ledger +
                    " fidelity=" + _fidelityDegraded + " geometry=" + _geoFailures.Count +
                    " touch=" + _touchFailures.Count + " inventory=" + _flowInventory.Count);
        }

        /// <summary>
        /// Runs on the SETTLED, camera-space layout inside RenderCanvasToPng -- the only point in
        /// the run where a rect read is in kit reference px (see that method's own comment), so it
        /// is both where the scroll must be applied and where the rail can honestly be measured.
        ///
        /// ⚠ THE SCROLL MUST HAPPEN HERE, NOT IN THE CAPTURE BODY. RenderCanvasToPng does two full
        /// ForceRebuildLayoutImmediate passes over the canvas root before this probe; a scroll set
        /// before those passes is set against a content height that has not settled yet.
        ///
        /// The seam reused is the panel's own rail alignment: StopMovement() followed by an
        /// explicit verticalNormalizedPosition, ManageScreenPanel.cs:1985-1989 (the pixel-exact
        /// alignment inside AddBuildingWorkspaceRow). 1f = top, 0f = bottom.
        /// </summary>
        private static void ManageFlowRailProbe(GameObject canvasGo, string label, int w, int h)
        {
            var scroll = _flowGridScroll;
            if (scroll == null || scroll.content == null)
            {
                if (_flowMeasureGrid)
                {
                    // NO SILENT FAILURE (§12): a missing grid is a finding, not an empty line.
                    // It is an ERROR, not a warning: on 2026-09-06 this printed as a WARNING four
                    // times and the run still wrote 21 files, so the only thing that said the
                    // instrument was blind was a line nobody had to read.
                    Debug.LogError("[UICap-HL] MANAGE_FLOW_MAP " + label + ": no ManageGridScroll resolved " +
                                   "under the workspace host -- the inventory line for " +
                                   (_flowMeasureTab ?? "<null>") + " cannot be measured, and this frame's " +
                                   "scroll state is whatever the panel happened to leave.");
                    _flowMeasureGrid = false;
                }
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = _flowGridToBottom ? 0f : 1f;
            Canvas.ForceUpdateCanvases();

            // ⚠ ManageWorkspacePanel does NOT assign ScrollRect.viewport (Content is anchored
            // directly under the scroll object), and Unity then uses the ScrollRect's OWN rect as
            // the view rect. Reading scroll.viewport.rect blindly would have reported 0 px.
            float viewPx = scroll.viewport != null
                ? scroll.viewport.rect.height
                : ((RectTransform)scroll.transform).rect.height;
            float contentHeightPx = scroll.content.rect.height;

            // A "gridbottom" frame whose grid ALREADY FITS has no bottom to scroll to, so it is the
            // same picture as its "gridtop" sibling BY CONSTRUCTION -- and the mockup's screen 4
            // ("all 9 troops visible, no scrolling") makes that the desired state, not a defect.
            // Say so in the log AND exempt it from the ledger's duplicate rule, so the rule keeps
            // its teeth for the case it was built for (a dead scroll seam) without crying wolf on
            // a screen that is working exactly as specified.
            if (_flowGridToBottom && contentHeightPx <= viewPx + 0.5f)
            {
                _ledgerIdenticalByConstruction.Add(OutDir + label + ".png");
                Debug.Log("[UICap-HL] MANAGE_FLOW_MAP " + label + ": the grid does not scroll (content=" +
                          contentHeightPx.ToString("0") + "px viewport=" + viewPx.ToString("0") +
                          "px) -- gridbottom IS gridtop, by construction.");
            }

            if (!_flowMeasureGrid) return;
            _flowMeasureGrid = false;

            // Count the RENDERED children the way the renderer names them. There is no tail spacer
            // in the grid path.
            //
            // ⛔ TWO NAMES, BECAUSE THE RENDERER HAS TWO SHAPES. "ManageTile" is a grid card;
            // "ManageListRow" is a LIST ROW, which ManageWorkspacePanel.BuildListRow draws whenever
            // the model asks for a single column (mockup panel 7, the research tree).
            // ⚠ THIS COUNTER READ ONLY "ManageTile" AND REPORTED A FALSE EMPTY SCREEN:
            //   MANAGE_FLOW_INVENTORY RESEARCH/school: tiles=4 (vm=4 rendered=0) columns=1 rows=0
            //   ** VM/RENDERED MISMATCH **  ...with content=586px
            // The rows were on screen the whole time. The proof is in the auditor's own numbers:
            // four rows at the 139px cell the model's 4x1 shape yields, plus three 10px gaps, is
            // 4*139 + 30 = 586px - exactly the content height it measured. A genuinely dead subtree
            // reports content=0px, which is what the round-5 bug actually looked like.
            // So a mismatch here now means one of TWO things, and they are not the same defect:
            // nothing was drawn, or something was drawn under a name this counter does not know.
            // Whenever the renderer grows a third shape, add its name HERE - a counter that silently
            // ignores what it cannot name will keep reporting healthy screens as broken.
            int rendered = 0;
            for (int i = 0; i < scroll.content.childCount; i++)
            {
                var child = scroll.content.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                if (!string.Equals(child.name, "ManageTile", StringComparison.Ordinal) &&
                    !string.Equals(child.name, "ManageListRow", StringComparison.Ordinal)) continue;
                rendered++;
            }

            // Rows are DERIVED from the live GridLayoutGroup, never authored here: the model
            // REQUESTS the column count (ManageTabVM.GridColumns) and the renderer sets it on the
            // group, so reading it back is the only honest source.
            var grid = scroll.content.GetComponent<GridLayoutGroup>();
            int columns = grid != null ? Mathf.Max(1, grid.constraintCount) : 1;
            float pitch = grid != null ? grid.cellSize.y + grid.spacing.y : 0f;
            int rows = columns > 0 ? Mathf.CeilToInt(rendered / (float)columns) : 0;

            float viewportPx = viewPx;
            float contentPx = contentHeightPx;
            float visibleRows = pitch > 0.01f ? viewportPx / pitch : 0f;

            _flowInventory.Add("MANAGE_FLOW_INVENTORY " + (_flowMeasureTab ?? "<null>") +
                ": grid tiles=" + _flowVmGridTiles +
                " (vm=" + _flowVmGridTiles + " rendered=" + rendered + ")" +
                " columns=" + columns +
                " rows=" + rows +
                " visibleRows=" + visibleRows.ToString("0.0") +
                " viewport=" + viewportPx.ToString("0") + "px" +
                " content=" + contentPx.ToString("0") + "px" +
                " pitch=" + pitch.ToString("0") + "px" +
                " queueRows(line=" + (_flowQueueChannel ?? "?") + ")=" + _flowVmQueueRows +
                (rendered != _flowVmGridTiles ? "  ** VM/RENDERED MISMATCH **" : ""));
        }

        /// <summary>
        /// The RICH fixture. Deliberately the opposite of a minimal one: long rails are the whole
        /// point of this capture. Every id below is read from the canonical data, not invented --
        /// structures-catalog.json (tower_ground_archer:15 maxLevel 3:31, tower_ballista:88 max 3,
        /// tower_siege_tower:160 max 3, tower_catapult:226 max 3, tower_arcane_spire:1004 max 3,
        /// wall_wood:288 max 2, wall_stone:342 max 1, gate_stone:386, mine_crystal:428 max 3,
        /// healing_caravan:488 max 3, lumberyard:1289 max 6, foundry:1368 max 6, silo:1447 max 6,
        /// barracks:1242, forge:804, lumbermill:762, armorer:853, arcane-tower:951, market:671,
        /// mill:718, workshop:625, jeweler:903) and building-tiers.json (the five ladders that
        /// author perks: arcane-tower 4, armorer 3, barracks 3, forge 3, lumbermill 4 = 17 perks;
        /// 'farm' authors none). Returns a THROWAWAY GameState the caller destroys.
        /// </summary>
        private static GameState BuildManageFlowFixture(ManageFlowFrame frame)
        {
            var fixture = ScriptableObject.CreateInstance<GameState>();
            fixture.Onboarded = true;
            fixture.BarracksLevel = 3;      // troops at unlockBarracksTier 1-3 unlocked, 4-6 locked

            // ⭐ THE HEART TIER IS THE *ONLY* PER-FRAME FIXTURE DIFFERENCE, AND IT IS ONE FRAME'S.
            // Tier 4 is the fixture's standing value and every other frame keeps it byte-for-byte,
            // because it is load-bearing well beyond the Heart chip: the BUILD grid's "HEART GATED"
            // tiles (Lumber Mill on ManageFlow_BUILD_gridbottom) and the deliberately-raised
            // lumbermill tier-2 RequiresVillageTier=5 gate that supplies the Locked card are both
            // read against it, so lowering it globally would silently repaint the grids this run
            // exists to document.
            // ⛔ AND THE NUMBER IS DERIVED, NOT TYPED. HeartUpgradeAvailable is
            // `HeartProgression.State != Max`, i.e. VillageTierService.Current < MaxTier, and
            // MaxTier projects heart-progression.json's authored maxLevel (3 today). One below the
            // live ceiling is chip-present whatever the owner re-authors that file to; a typed 2
            // would become chip-ABSENT the day maxLevel drops to 2, and the frame would go on
            // claiming a chip it no longer shows.
            fixture.VillageTier = frame == ManageFlowFrame.HubHeart
                ? Mathf.Max(1, DeNelle.Village.Buildings.Progression.HeartProgression.MaxLevel - 1)
                : 4;

            // ⚠ THE ARCHER AT (3,7) IS LOAD-BEARING. SeedManageCaptureQueue enqueues
            // PlacedUpgradeKey.Compose("tower_ground_archer", 3, 7) on the Builder line; move that
            // placement and the running job names a tower that is not there, and the Defence
            // "Building" state word (this capture's Defence 'locked' frame) becomes unreachable.
            fixture.BaseLayout = new List<PlacedStructureData>
            {
                // --- defensive types: one rail row per TYPE (ruling 3.1), many types ---
                new PlacedStructureData("tower_ground_archer", 3, 7, 0, 1),   // the queued upgrade
                new PlacedStructureData("tower_ground_archer", 5, 9, 0, 2),   // second instance, higher level
                new PlacedStructureData("tower_ballista", 7, 9, 0, 1),
                new PlacedStructureData("tower_ballista", 7, 11, 0, 2),
                new PlacedStructureData("tower_siege_tower", 9, 11, 0, 1),
                new PlacedStructureData("tower_catapult", 9, 9, 0, 3),        // AT its ceiling -> "Max"
                new PlacedStructureData("tower_arcane_spire", 11, 11, 0, 2),
                new PlacedStructureData("wall_wood", 13, 9, 0, 1),
                new PlacedStructureData("wall_wood", 14, 9, 0, 1),
                new PlacedStructureData("wall_wood", 15, 9, 0, 1),            // "3 placed . lowest L1"
                new PlacedStructureData("wall_stone", 17, 9, 0, 1),           // maxLevel 1 -> "Max"
                new PlacedStructureData("gate_stone", 19, 9, 0, 1),
                new PlacedStructureData("mine_crystal", 21, 9, 0, 2),
                new PlacedStructureData("healing_caravan", 23, 9, 0, 1),
                new PlacedStructureData("lumberyard", 11, 9, 0, 3),           // six-rung container, mid-climb
                new PlacedStructureData("foundry", 25, 9, 0, 2),
                new PlacedStructureData("silo", 27, 9, 0, 1),

                // --- ladder buildings: every ladder that authors perks, so RESEARCH is long ---
                new PlacedStructureData("barracks", 2, 2, 0, 4),
                new PlacedStructureData("arcane-tower", 6, 3, 0, 4),
                new PlacedStructureData("forge", 8, 3, 0, 4),
                new PlacedStructureData("lumbermill", 10, 3, 0, 4),
                new PlacedStructureData("armorer", 12, 3, 0, 4),

                // --- extra town structures: they lengthen the Buildings rail when they carry an
                //     authored ladder, and BuildBuildingChoices simply skips them when they do not
                //     (IsUpgradable, ManageScreenVM.cs:1181). Harmless either way.
                new PlacedStructureData("market", 14, 3, 0, 4),
                new PlacedStructureData("mill", 16, 3, 0, 4),
                new PlacedStructureData("workshop", 18, 3, 0, 4),
                new PlacedStructureData("jeweler", 20, 3, 0, 4),
                new PlacedStructureData("collector_farm", 22, 3, 0, 4),
            };

            fixture.Wood = 100000;
            fixture.Iron = 100000;
            var balances = fixture.Resources;
            balances.Food = 100000;
            balances.Crystals = 100000;
            balances.Coins = 100000;
            fixture.Resources = balances;
            fixture.ObsidianQueue = ObsidianQueueState.Empty();

            fixture.BuildingTiers["barracks"] = 3;
            fixture.BuildingTiers["arcane-tower"] = 3;
            fixture.BuildingTiers["forge"] = BuildingTierCatalog.MaxTier("forge");   // -> "Max"
            fixture.BuildingTiers["lumbermill"] = 1;                                 // -> locked perks
            fixture.BuildingTiers["armorer"] = 2;

            // Research "Researched" cards. Key shape is BuildingPerkService.Key's
            // "<buildingId>:<perkId>" (BuildingPerkService.cs:68) -- the same shape
            // _selectedResearchKey takes. All three perk ids are authored in building-tiers.json.
            fixture.OwnedBuildingPerks = new List<string>
            {
                "forge:forge-efficient-smelting",
                "arcane-tower:arcane-basics",
                "barracks:barracks-swift-recruitment",
            };

            // Troops "Max" card. NOT a magic 99: TroopLevelOf returns the stored value UNCLAMPED
            // (BarracksProgression.cs:60-66), so a sentinel would paint "Level 99" on the card.
            // MaxTroopLevel reads troop-upgrades.json through the lazily-loaded
            // TroopUpgradeCatalog (TroopStatResolver.cs:176-187, CanonicalJson) -- which DOES
            // resolve in edit mode, unlike the RuntimeInitializeOnLoad CatalogRegistry.
            if (fixture.TroopLevels != null)
                fixture.TroopLevels["troop-footman"] = BarracksProgression.MaxTroopLevel("troop-footman");
            else
                Debug.LogWarning("[UICap-HL] MANAGE_FLOW_MAP: GameState.TroopLevels is null -- the Troops " +
                                 "'max' frame has no maxed troop to select.");

            return fixture;
        }

        /// <summary>
        /// Two extra jobs per line on top of <see cref="SeedManageCaptureQueue"/>'s three, so every
        /// channel sits at the AUTHORED DEPTH CAP of 5 (BuildTimerConfig.queueDepthPerLine,
        /// CLAUDE.md §8) and the queue drawer documents a full line rather than a sample.
        /// Enqueue returns null on refusal (BuildTimerService.cs:841-843) -- never ignored here.
        /// </summary>
        private static void SeedManageFlowExtraQueue(BuildTimerService queue)
        {
            if (queue == null) throw new ArgumentNullException(nameof(queue));

            EnqueueFlowJob(queue, JobKind.Upgrade, ChannelId.Builder, "armorer:12:3", 900d, 3);
            EnqueueFlowJob(queue, JobKind.Repair, ChannelId.Builder, "wall_wood:13:9", 240d, 0);

            EnqueueFlowJob(queue, JobKind.TrainTroop, ChannelId.Train, "train:spearman:capture-d", 300d, 0);
            EnqueueFlowJob(queue, JobKind.TrainTroop, ChannelId.Train, "train:archer:capture-e", 360d, 0);

            EnqueueFlowJob(queue, JobKind.TroopUpgrade, ChannelId.Research, "troop-upgrade:troop-archer", 660d, 2);
            EnqueueFlowJob(queue, JobKind.LearnMagic, ChannelId.Research, "magic:chain-lightning", 600d, 0);
        }

        private static void EnqueueFlowJob(BuildTimerService queue, JobKind kind, ChannelId channel,
                                           string targetId, double seconds, int tier)
        {
            var job = queue.Enqueue(kind, channel, targetId, seconds, tier);
            if (job == null)
                Debug.LogWarning("[UICap-HL] MANAGE_FLOW_MAP: queue refused '" + targetId + "' on " + channel +
                                 " (already in flight, or the line is at its depth cap) -- the drawer will " +
                                 "document one row fewer on that line.");
        }

        /// <summary>The tab VM the workspace is currently painting, or null.</summary>
        private static DeNelle.Core.Manage.ManageTabVM ActiveManageTabVm(ManageScreenVM vm)
        {
            return vm == null ? null : vm.ComposeWorkspace().ActiveTab;
        }

        /// <summary>
        /// Find a tile in the wanted visual state on the screen the model is currently painting.
        /// Returns null when there is none -- the caller then FAILS the frame rather than shooting
        /// a substitute, because a silently-substituted screen is a frame whose filename lies.
        /// </summary>
        /// <param name="preferLaddered">⭐ WO-1489 - ORDER MATTERS FOR THE ACTIONABLE FRAME AND FOR
        /// NOTHING ELSE. `Available` covers two very different detail screens: an item ALREADY
        /// BUILT with a next rung (before/after stats, a cost row, a time and UPGRADE - the screen
        /// WO-1489 is actually about), and an item NOT BUILT YET (Echo Hollow / Crafting Station /
        /// Store on ManageFlow_BUILD_gridtop, which wear the same green medallion because
        /// ManageVmProjection.VisualStateFor's `default:` branch maps every non-badge to Available).
        /// First-match would shoot whichever the composer happened to order first. The LADDERED one
        /// is identified by the projection's own marker - it is the only case that emits a
        /// "LEVEL n" Subtitle (`item.MaxLevel > 0 && item.Level > 0`) - so this reads the VM rather
        /// than guessing from an id, and it FALLS BACK to first-match so a tab with no laddered
        /// Available item still shoots its actionable screen instead of failing.</param>
        private static DeNelle.Core.Manage.ManageTileVM FindManageFlowTile(
            ManageScreenVM vm, DeNelle.Core.Manage.ManageTileVisualState want,
            bool preferLaddered = false)
        {
            var tab = ActiveManageTabVm(vm);
            if (tab == null || tab.Tiles == null) return null;
            DeNelle.Core.Manage.ManageTileVM first = null;
            for (int i = 0; i < tab.Tiles.Count; i++)
            {
                var t = tab.Tiles[i];
                if (t == null || t.VisualState != want) continue;
                if (!preferLaddered) return t;
                if (first == null) first = t;
                if (!string.IsNullOrEmpty(t.Subtitle) &&
                    t.Subtitle.StartsWith("LEVEL ", StringComparison.Ordinal)) return t;
            }
            return first;
        }

        /// <summary>
        /// The states the current screen ACTUALLY produced, for a failure message. §11B: a failure
        /// that names what it saw settles the question in ONE run; a failure that only says "not
        /// found" costs a second run to answer "then what was there?".
        /// </summary>
        private static string ManageFlowObservedStates(ManageScreenVM vm)
        {
            var tab = ActiveManageTabVm(vm);
            if (tab == null || tab.Tiles == null || tab.Tiles.Count == 0) return "<no tiles>";
            var seen = new List<string>(5);
            for (int i = 0; i < tab.Tiles.Count; i++)
            {
                var t = tab.Tiles[i];
                if (t == null) continue;
                string s = t.VisualState.ToString();
                if (!seen.Contains(s)) seen.Add(s);
            }
            return string.Join(",", seen.ToArray()) + " over " + tab.Tiles.Count + " tiles";
        }

        /// <summary>
        /// Drive the model to the DETAIL screen of an item in the wanted state, using the tile's
        /// OWN <c>Activate</c> callback -- the exact seam a player's tap runs through.
        ///
        /// <para>⛔ THIS REPLACES A REFLECTION WRITE THAT NOTHING READ. The old body set
        /// <c>_selectedBuildingId</c> / <c>_selectedTroopId</c> / <c>_selectedDefenseId</c> /
        /// <c>_selectedResearchKey</c> on the panel. Those fields are consumed only by the legacy
        /// row factories, which ManageScreenPanel.RenderList short-circuits past whenever the
        /// workspace owns the well - so the write landed and changed nothing, and every "locked"
        /// and "max" png was the plain grid. SetPrivateField only WARNS on a field it cannot find,
        /// so even DELETING those fields would not have failed the run.</para>
        ///
        /// <para>RESEARCH is two levels deep by canon 5 (school, then its perks), so this walks each
        /// school's perk grid in turn. It re-enters the tab rather than pressing Back between
        /// schools, because Back on a root grid raises CloseRequested and would tear the panel down
        /// mid-capture.</para>
        ///
        /// <para>⭐ WO-1489 - IT TAKES THE WANTED STATE, NOT A BOOL. The parameter was
        /// <c>bool wantLocked</c>, i.e. "Locked, else Max", which is a two-valued encoding of a
        /// five-valued enum and had no room for the state WO-1489 needs:
        /// <see cref="DeNelle.Core.Manage.ManageTileVisualState.Available"/>, the one a player can
        /// act on. Passing the enum means the word in the failure message and the word in the
        /// filename are both derived from the same value, so they cannot disagree.</para>
        /// </summary>
        private static bool TryNavigateManageFlowDetail(ManageScreenVM vm,
                                                        DeNelle.Core.Manage.ManageTabId tab,
                                                        DeNelle.Core.Manage.ManageTileVisualState want,
                                                        string word, out string note)
        {
            note = null;
            if (vm == null) return false;
            // Only the ACTIONABLE frame cares which Available tile it gets; Locked and Max are
            // single-shaped states, so ordering there stays exactly as it was.
            bool preferLaddered = want == DeNelle.Core.Manage.ManageTileVisualState.Available;

            // BUILD is now TWO levels deep: category, then item. Walk every model-authored category
            // so action/max proof frames keep finding real item states without hardcoding which
            // family happens to contain today's fixture candidate.
            if (tab == DeNelle.Core.Manage.ManageTabId.Build)
            {
                var root = ActiveManageTabVm(vm);
                int categoryCount = root != null && root.Tiles != null ? root.Tiles.Count : 0;
                for (int c = 0; c < categoryCount; c++)
                {
                    vm.EnterTab(DeNelle.Core.Manage.ManageTabId.Build);
                    var categories = ActiveManageTabVm(vm);
                    if (categories == null || categories.Tiles == null || c >= categories.Tiles.Count) break;
                    var category = categories.Tiles[c];
                    if (category == null || category.Activate == null) continue;
                    string categoryId = category.Id;
                    category.Activate.Invoke();

                    var hit = FindManageFlowTile(vm, want, preferLaddered);
                    if (hit == null) continue;
                    hit.Activate?.Invoke();
                    note = "BUILD/" + word + " -> " + (categoryId ?? "?") + " / " +
                           (hit.Id ?? "<null>") + " state=" + hit.VisualState + " screen=" +
                           (vm.Nav != null ? vm.Nav.Kind.ToString() : "<null>");
                    return vm.Nav != null && vm.Nav.Kind == ManageScreenKind.Detail;
                }
                return false;
            }

            // ARMY is one level deep: a grid tile opens that item's Detail. RESEARCH is TWO
            // (school, then perk), so a Locked SCHOOL must not be mistaken for a locked item.
            if (tab != DeNelle.Core.Manage.ManageTabId.Research)
            {
                var hit = FindManageFlowTile(vm, want, preferLaddered);
                if (hit == null) return false;
                hit.Activate?.Invoke();
                note = ManageScreenVM.TabWordOf(tab) + "/" + word + " -> " + (hit.Id ?? "<null>") +
                       " state=" + hit.VisualState + " screen=" +
                       (vm.Nav != null ? vm.Nav.Kind.ToString() : "<null>");
                return vm.Nav != null && vm.Nav.Kind == ManageScreenKind.Detail;
            }

            // Descend into each school in turn. The school list is re-read after every re-entry
            // because the tiles carry live callbacks bound to the composition that produced them.
            var schools = ActiveManageTabVm(vm);
            int schoolCount = schools != null && schools.Tiles != null ? schools.Tiles.Count : 0;
            for (int s = 0; s < schoolCount; s++)
            {
                vm.EnterTab(DeNelle.Core.Manage.ManageTabId.Research);   // back to the school grid
                var grid = ActiveManageTabVm(vm);
                if (grid == null || grid.Tiles == null || s >= grid.Tiles.Count) break;
                var school = grid.Tiles[s];
                if (school == null) continue;
                string schoolId = school.Id;
                school.Activate?.Invoke();                                // -> that school's perks

                var perk = FindManageFlowTile(vm, want, preferLaddered);
                if (perk == null) continue;
                perk.Activate?.Invoke();                                  // -> the perk's detail
                note = "RESEARCH/" + word + " -> " + (schoolId ?? "?") + " / " + (perk.Id ?? "<null>") +
                       " state=" + perk.VisualState + " screen=" +
                       (vm.Nav != null ? vm.Nav.Kind.ToString() : "<null>");
                return vm.Nav != null && vm.Nav.Kind == ManageScreenKind.Detail;
            }
            return false;
        }

        /// <summary>
        /// Drive the model to a Research SCHOOL's perk grid (canon 5). This is the screen that
        /// REPLACES the retired hub frame: it is the only screen in the new flow no other frame
        /// covers, and it is the long one -- 17 perks over five ladders in this fixture.
        /// </summary>
        private static bool TryOpenManageFlowSchool(ManageScreenVM vm, out string note)
        {
            note = null;
            if (vm == null) return false;
            var grid = ActiveManageTabVm(vm);
            int count = grid != null && grid.Tiles != null ? grid.Tiles.Count : 0;

            // ⚠ NOT Tiles[0] BLIND. building-tiers.json authors perks for only FIVE ladders and the
            // fixture also places 'collector_farm', 'market', 'mill', 'workshop' and 'jeweler'; a
            // school that authors no perks paints the "This school authors no perks yet." empty
            // state, and shooting that as THE perks screen would be a frame whose filename lies.
            // Walk until one actually holds perks.
            for (int i = 0; i < count; i++)
            {
                vm.EnterTab(DeNelle.Core.Manage.ManageTabId.Research);   // back to the school grid
                var schools = ActiveManageTabVm(vm);
                if (schools == null || schools.Tiles == null || i >= schools.Tiles.Count) break;
                var school = schools.Tiles[i];
                if (school == null) continue;
                school.Activate?.Invoke();
                if (vm.Nav == null || vm.Nav.Kind != ManageScreenKind.ResearchPerks) continue;
                var perks = ActiveManageTabVm(vm);
                int perkCount = perks != null && perks.Tiles != null ? perks.Tiles.Count : 0;
                if (perkCount == 0) continue;
                note = "RESEARCH/school -> " + (school.Id ?? "<null>") + " perks=" + perkCount;
                return true;
            }
            Debug.LogError("[UICap-HL] MANAGE_FLOW_MAP: none of the " + count + " research schools " +
                           "opened a non-empty perk grid.");
            return false;
        }

        /// <summary>The production line each destination rides (CLAUDE.md §8). BUILD is the Builder
        /// line because Defense and Buildings MERGED into it (ManageScreenVM.LegacyTabOf).</summary>
        private static string ManageFlowChannelName(DeNelle.Core.Manage.ManageTabId tab)
        {
            switch (tab)
            {
                case DeNelle.Core.Manage.ManageTabId.Army: return "Train";
                case DeNelle.Core.Manage.ManageTabId.Research: return "Research";
                default: return "Builder";
            }
        }

        /// <summary>
        /// Find the workspace GRID's ScrollRect, inactive included (the queue drawer deactivates
        /// the workspace host). Resolved by the renderer's own object name "ManageGridScroll"
        /// (ManageWorkspacePanel.cs:475) under the panel's "_workspaceHost", NOT by a legacy zone
        /// name -- see this region's header for why the zone lookup answers null now.
        /// </summary>
        private static ScrollRect FindManageFlowGrid(ManageScreenPanel panel)
        {
            var host = GetPrivateFieldValue(panel, "_workspaceHost") as RectTransform;
            if (host == null)
            {
                Debug.LogError("[UICap-HL] MANAGE_FLOW_MAP: the panel has no _workspaceHost -- the " +
                               "workspace renderer is not the one painting this screen, so the grid " +
                               "cannot be scrolled or measured.");
                return null;
            }
            var scrolls = host.GetComponentsInChildren<ScrollRect>(true);
            for (int i = 0; i < scrolls.Length; i++)
            {
                if (scrolls[i] == null) continue;
                if (!string.Equals(scrolls[i].gameObject.name, "ManageGridScroll", StringComparison.Ordinal)) continue;
                return scrolls[i];
            }
            return null;
        }

        /// <summary>
        /// One frame of the flow map. Same shape and the same full restore as every sibling
        /// capture: a throwaway GameState + BuildTimerService are installed, the real panel is
        /// built, one png is written, and EVERY mutation is undone in the finally -- including the
        /// shared BuildingTierDef gate and the catalog registry, which are process-wide statics
        /// that would otherwise poison every later capture in the same run.
        /// </summary>
        private static int CaptureManageFlowFrame(ManageFlowShot shot)
        {
            var tab = shot.Tab;
            var frame = shot.Frame;
            string state = ManageFlowStateWord(frame);
            string shotName = ManageFlowShotName(shot);

            return ForEachTarget(shotName, ManageFlowMapTargets, target =>
            {
                GameStateService prior = GameStateService.Instance;
                BuildTimerService priorQueue = BuildTimerService.Instance;
                GameObject stateHost = null, queueHost = null, panelHost = null, canvas = null;
                GameState fixture = null;
                BuildingTierDef gatedTier = null;
                int priorGate = 0;
                bool hydratedCatalog = false;
                try
                {
                    PanelManager.CloseAll();

                    // ⛔ WITHOUT THIS EVERY BaseLayout ROW RESOLVES NULL AND EVERY RAIL COMES BACK
                    // EMPTY. CatalogBootstrap registers structures from a
                    // [RuntimeInitializeOnLoadMethod] (CatalogBootstrap.cs:96) which never runs in
                    // an -executeMethod capture, so CatalogRegistry.Get(placed.itemId) answers null
                    // and BuildDefenseBrowse bails before the ceiling test. Idempotent; cleared
                    // below ONLY when this frame is the one that filled an empty registry.
                    hydratedCatalog = DeNelle.Core.Catalog.CatalogRegistry.Count == 0;
                    HydrateCatalogForCapture();

                    fixture = BuildManageFlowFixture(frame);

                    // Capture-only presentation fixture: production canon tops out at Village Tier
                    // 3 requirements, so temporarily raise one REAL next-tier gate to give the
                    // Buildings tab a genuinely Locked card. Restored in the finally.
                    gatedTier = BuildingTierCatalog.TierOf("lumbermill", 2);
                    if (gatedTier == null)
                        throw new InvalidOperationException("Manage flow map requires lumbermill tier 2");
                    priorGate = gatedTier.RequiresVillageTier;
                    gatedTier.RequiresVillageTier = 5;

                    stateHost = new GameObject("~UICapManageFlowState");
                    if (!InstallCaptureState(stateHost.AddComponent<GameStateService>(), fixture))
                        throw new InvalidOperationException("GameStateService capture seam is unavailable");

                    queueHost = new GameObject("~UICapManageFlowQueue");
                    var queueService = queueHost.AddComponent<BuildTimerService>();
                    if (!InstallCaptureQueue(queueService))
                        throw new InvalidOperationException("BuildTimerService capture seam is unavailable");
                    SeedManageCaptureQueue(queueService);
                    // ⛔ THE ACTIONABLE FRAME GETS A LINE WITH ROOM IN IT, AND THAT IS THE WHOLE
                    // REASON WO-1489's FOURTH SCREEN WAS UNPHOTOGRAPHABLE. SeedManageFlowExtraQueue
                    // exists to sit every channel AT the authored depth cap so the queue drawer
                    // documents a full line - and a full line makes every actionable item
                    // ManageTileBadge.QueueBlocked, which projects to
                    // ManageTileVisualState.QueueBlocked (ManageVmProjection.VisualStateFor:65).
                    // MEASURED, Builds/cap-manage-wave5c.log 03:20: queueRows=5/7/5 on
                    // Builder/Train/Research, and ManageFlow_BUILD_gridtop reads QUEUE FULL on
                    // every built tile. So there was no Available tile ANYWHERE to open a detail
                    // screen on, and no fixture change could have been found by reading the plan.
                    // ⚠ AND IT MUST STAY SEEDED FOR EVERY OTHER FRAME. Two frames depend on the
                    // saturation: QueueDrawer documents a line at its cap, and ARMY/max only reads
                    // Max because the Train line is full - ruling 13 lets Trainable win over Max,
                    // which this body's own failure message already spells out. Draining the queue
                    // globally would flip ManageFlow_ARMY_max to Trainable and FAIL the run.
                    if (frame != ManageFlowFrame.ActionDetail) SeedManageFlowExtraQueue(queueService);

                    panelHost = new GameObject("~UICap" + shotName);
                    var panel = panelHost.AddComponent<ManageScreenPanel>();
                    InvokePrivate(panel, "Awake");
                    // Open() lands on the model's LAST-USED tab (OpenDefaultScreen reads
                    // PlayerPrefs "manage.lasttab"); the pref is pinned to BUILD around this whole
                    // run so the opening screen is the same on every frame and in every run.
                    panel.Open();

                    var vm = GetPrivateFieldValue(panel, "_vm") as ManageScreenVM;
                    if (vm == null)
                        throw new InvalidOperationException("ManageScreenPanel exposed no _vm -- nothing to drive");

                    // !! THE HUB FRAME DOES NOT ENTER A TAB. EnterTab assigns a fresh ManageNavEntry,
                    // and RenderWorkspace drops the hub the moment the model's nav entry changes
                    // (ManageScreenPanel.ShowLauncher documents exactly that seam) -- so entering a
                    // tab here would photograph the grid under a filename that claimed the hub,
                    // which is the substitution this capture body refuses to make anywhere else.
                    // ShowLauncher is the panel's OWN door, the one the root BACK press uses.
                    if (frame == ManageFlowFrame.Hub || frame == ManageFlowFrame.HubHeart)
                    {
                        InvokePrivate(panel, "ShowLauncher");
                        if (!(bool)GetPrivateFieldValue(panel, "_hubShowing"))
                            throw new InvalidOperationException(
                                "ShowLauncher did not raise the hub -- the frame cannot be shot honestly");

                        // ⭐ THE CHIP IS ASSERTED, NOT HOPED FOR. WO-1597 made the HEART face
                        // conditional on ManageScreenVM.HeartUpgradeAvailable, so "the hub with a
                        // chip" and "the hub without one" are two screens that differ by a
                        // MinTouchPx band and are otherwise identical -- exactly the pair a
                        // filename can lie about. _hubHeartShown is ManageScreenPanel's own single
                        // answer (its field doc: "THE ONE ANSWER to does the hub draw a HEART
                        // chip", written by BuildLauncher and read by BuildHubHeartDoor), so the
                        // frame is judged against the panel's decision rather than re-deriving the
                        // predicate here and getting a second opinion.
                        bool chip = (bool)GetPrivateFieldValue(panel, "_hubHeartShown");
                        bool wantChip = frame == ManageFlowFrame.HubHeart;
                        if (chip != wantChip)
                            throw new InvalidOperationException(
                                "the hub drew " + (chip ? "a" : "NO") + " HEART chip but this frame is " +
                                ManageFlowStateWord(frame) + ", which needs " +
                                (wantChip ? "one" : "none") + " -- HeartProgression is Level " +
                                DeNelle.Village.Buildings.Progression.HeartProgression.Level + " of " +
                                DeNelle.Village.Buildings.Progression.HeartProgression.MaxLevel +
                                " (State " + DeNelle.Village.Buildings.Progression.HeartProgression.State +
                                "). The fixture's VillageTier is the dial; shooting it anyway would " +
                                "put the other hub under this filename.");
                        _flowStateNotes.Add(ManageScreenVM.TabWordOf(tab) + "/" +
                            ManageFlowStateWord(frame) + " -> heart chip=" + chip +
                            " level=" + DeNelle.Village.Buildings.Progression.HeartProgression.Level +
                            "/" + DeNelle.Village.Buildings.Progression.HeartProgression.MaxLevel +
                            " state=" + DeNelle.Village.Buildings.Progression.HeartProgression.State);
                    }
                    else
                    {
                    // The tab is entered through the MODEL, not through the legacy ShowOperational
                    // adapter: the plan's axis is ManageTabId, which is the axis the screen has.
                    vm.EnterTab(tab);
                    if (vm.Nav == null || vm.Nav.Tab != tab)
                        throw new InvalidOperationException(
                            "the model refused tab " + tab + " (it is not available in this fixture) -- " +
                            "this frame cannot be shot honestly");
                    }

                    if (frame == ManageFlowFrame.ActionDetail ||
                        frame == ManageFlowFrame.LockedDetail || frame == ManageFlowFrame.MaxDetail)
                    {
                        // The wanted tile state IS the frame. Available is the actionable one -- the
                        // detail screen that carries a before/after stat pair, a cost row, a time
                        // and the primary verb -- and it is the state the plan could not reach
                        // while every line sat at its depth cap (see the seeding note above).
                        var want =
                            frame == ManageFlowFrame.ActionDetail
                                ? DeNelle.Core.Manage.ManageTileVisualState.Available
                                : frame == ManageFlowFrame.LockedDetail
                                    ? DeNelle.Core.Manage.ManageTileVisualState.Locked
                                    : DeNelle.Core.Manage.ManageTileVisualState.Max;
                        if (!TryNavigateManageFlowDetail(vm, tab, want, state, out string note))
                            throw new InvalidOperationException(
                                "no " + state + " item reachable on the " + ManageScreenVM.TabWordOf(tab) +
                                " tab -- the fixture did not produce that state, so this frame cannot be " +
                                "shot honestly. States actually present: " + ManageFlowObservedStates(vm) +
                                ". (Known candidates: on ARMY, ManageTileBadge.Max is only reached when " +
                                "the troop is neither Trainable nor QueueBlocked -- ruling 13 lets those " +
                                "win -- so a full Train line or an affordable train can mask it. On BUILD, " +
                                "the active filter decides which items are on screen at all. For the " +
                                "'action' frame the usual cause is the OPPOSITE one: a channel at its " +
                                "depth cap turns every actionable tile QueueBlocked, so this frame is the " +
                                "one that must NOT be seeded to the cap.)");
                        _flowStateNotes.Add(note);
                    }
                    else if (frame == ManageFlowFrame.SchoolPerks)
                    {
                        if (!TryOpenManageFlowSchool(vm, out string schoolNote))
                            throw new InvalidOperationException(
                                "no research school could be opened -- the perks screen cannot be shot honestly");
                        _flowStateNotes.Add(schoolNote);
                    }
                    else if (frame == ManageFlowFrame.QueueDrawer)
                    {
                        InvokePrivate(panel, "ToggleQueueDrawer");
                    }

                    Canvas.ForceUpdateCanvases();
                    canvas = GetPrivateFieldValue(panel, "_ui") as GameObject;
                    if (canvas == null) return 0;

                    // Hand the probe its work. The queue-drawer frame deliberately gets NO grid:
                    // ShowWorkspace/RenderWorkspace deactivate the workspace host while the drawer
                    // is open, so scrolling a hidden grid would prove nothing. A DETAIL screen has
                    // no grid either (FillActiveTab hands it Tiles = empty and a visible Selection).
                    bool frameHasGrid = frame == ManageFlowFrame.GridTop ||
                                        frame == ManageFlowFrame.GridBottom ||
                                        frame == ManageFlowFrame.SchoolPerks;
                    _flowGridScroll = frameHasGrid ? FindManageFlowGrid(panel) : null;
                    _flowGridToBottom = frame == ManageFlowFrame.GridBottom;

                    // Measure ONCE per tab, on the unscrolled grid-top frame -- AND on the school
                    // perks screen, which is the LONGEST grid in the flow (17 perks over five
                    // ladders in this fixture) and is exactly the "how much data is in Manage"
                    // question this capture exists to answer. Measuring only the tab roots would
                    // have left the deepest screen unmeasured.
                    if (frame == ManageFlowFrame.GridTop || frame == ManageFlowFrame.SchoolPerks)
                    {
                        _flowMeasureGrid = true;
                        _flowMeasureTab = frame == ManageFlowFrame.SchoolPerks
                            ? "RESEARCH/school"
                            : ManageScreenVM.TabWordOf(tab);
                        _flowQueueChannel = ManageFlowChannelName(tab);
                        _flowVmQueueRows = vm.QueueRows.Count;
                        var activeTab = ActiveManageTabVm(vm);
                        _flowVmGridTiles = activeTab != null && activeTab.Tiles != null ? activeTab.Tiles.Count : 0;
                    }

                    _settledProbe = ManageFlowRailProbe;
                    return RenderCanvasToPng(canvas, OutDir + shotName + "_" + target.Tag + ".png",
                                             target.W, target.H) ? 1 : 0;
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] " + shotName + " capture threw: " + e);
                    return 0;
                }
                finally
                {
                    _settledProbe = null;
                    _flowGridScroll = null;
                    _flowGridToBottom = false;
                    _flowMeasureGrid = false;
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                    if (panelHost != null) UnityEngine.Object.DestroyImmediate(panelHost);
                    RestoreCaptureQueue(priorQueue);
                    if (queueHost != null) UnityEngine.Object.DestroyImmediate(queueHost);
                    RestoreCaptureState(prior);
                    if (stateHost != null) UnityEngine.Object.DestroyImmediate(stateHost);
                    if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                    if (gatedTier != null) gatedTier.RequiresVillageTier = priorGate;
                    if (hydratedCatalog) DeNelle.Core.Catalog.CatalogRegistry.Clear();
                    PanelManager.CloseAll();
                }
            });
        }

        private static void InstallCaptureVillageInventory(VillageInventory inventory)
        {
            var field = typeof(VillageInventory).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) throw new MissingFieldException(typeof(VillageInventory).FullName, "Instance backing field");
            field.SetValue(null, inventory);
        }

        private static void RestoreCaptureVillageInventory(VillageInventory prior)
        {
            var field = typeof(VillageInventory).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null) field.SetValue(null, prior);
        }

        private static void InstallCaptureEconomy(EconomyService economy)
        {
            var field = typeof(EconomyService).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) throw new MissingFieldException(typeof(EconomyService).FullName, "Instance backing field");
            field.SetValue(null, economy);
        }

        private static void RestoreCaptureEconomy(EconomyService prior)
        {
            var field = typeof(EconomyService).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null) field.SetValue(null, prior);
        }

        private static void SetPrivateFieldValue(object target, string fieldName, object value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }

        private static void ClearConsumableCaptureCooldown(string id)
        {
            var field = typeof(DeNelle.Village.Items.ConsumableUseService).GetField("_nextReadyAt",
                BindingFlags.NonPublic | BindingFlags.Static);
            var map = field != null ? field.GetValue(null) as IDictionary<string, float> : null;
            map?.Remove(id);
        }

        private static bool InstallCaptureQueue(BuildTimerService service)
        {
            var property = typeof(BuildTimerService).GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            var setter = property != null ? property.GetSetMethod(true) : null;
            if (setter == null) return false;
            setter.Invoke(null, new object[] { service });
            return ReferenceEquals(BuildTimerService.Instance, service);
        }

        private static void RestoreCaptureQueue(BuildTimerService prior)
        {
            var property = typeof(BuildTimerService).GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            var setter = property != null ? property.GetSetMethod(true) : null;
            if (setter != null) setter.Invoke(null, new object[] { prior });
        }

        private static int CaptureBuildCollections()
        {
            return ForEachTarget("BuildCollections", target =>
            {
                GameObject host = null;
                GameObject canvas = null;
                GameObject placedStub = null;   // WO-1629 Step 1a — see the note at the Show() call
                try
                {
                    PanelManager.CloseAll();
                    // RuntimeInitializeOnLoad bootstraps do not execute in an editor
                    // -executeMethod session. Hydrate the same authoritative registry the player
                    // receives before judging category visibility; otherwise every collection is
                    // filtered as "definition missing" and a blank screenshot falsely passes.
                    typeof(CatalogBootstrap).GetMethod("Register",
                        BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
                    host = new GameObject("~UICapBuildCollections");
                    var browser = host.AddComponent<BuildCollectionBrowser>();
                    InvokePrivate(browser, "Awake");
                    InvokePrivate(browser, "OnEnable");
                    // WO-1629 Step 1a -- CAPTURE THE SCREEN THE PLAYER ACTUALLY GETS (EIGHT CARDS).
                    // The live door passes BOTH callbacks (BuildPaletteUI.cs:345-347); this call
                    // passed only the first, so BuildManagePlacedCard returned at its callback gate
                    // (BuildCollectionBrowser.cs:428-434) and every BuildCollections_*.png ever
                    // captured showed a SEVEN-card grid the player never sees. The cards are
                    // children of one HorizontalLayoutGroup, so the missing card changed every
                    // sibling's width too -- WO-1628's band was measured on the wrong frame.
                    //
                    // ⚠ THERE ARE **TWO** GATES, AND WO-1629 sec.1d NAMES ONLY THE FIRST. After the
                    // callback check the builder also refuses when FindObjectsByType<PlacedStructure>
                    // finds no LIVE body (BuildCollectionBrowser.cs:450-457 -- a card that closes the
                    // browser onto a map with nothing selectable is a dead end). This capture places
                    // nothing itself, and UICaptureLaunch.cs opens no scene of its own (grepped
                    // 2026-09-10: no OpenScene/NewScene/LoadScene anywhere in this file), so whether
                    // batchmode's open scene holds a live body is NOT PROVEN either way — and the
                    // second gate's SKIPPED line has never appeared in a log, because the callback
                    // gate above it always fired first. The stub below makes the count >= 1
                    // regardless, so the gate passes whatever the scene holds. It is the
                    // minimum honest fixture for the second gate: one empty marker component, no
                    // catalog row, no art, never rendered -- it is only counted. PlacedStructure has
                    // no [ExecuteAlways]/[ExecuteInEditMode] (grepped 2026-09-10, no match), so its
                    // Start() -- and StorageStackView.Attach with it -- does not run in this
                    // edit-mode capture. Destroyed in the finally below.
                    placedStub = new GameObject("~UICapPlacedStub");
                    placedStub.AddComponent<DeNelle.Village.PlacedStructure>();
                    browser.Show(_ => { }, () => { });
                    Canvas.ForceUpdateCanvases();
                    canvas = GetPrivateFieldValue(browser, "_canvas") as GameObject;
                    if (canvas == null) return 0;
                    int saved = RenderCanvasToPng(canvas,
                        OutDir + "BuildCollections_" + target.Tag + ".png", target.W, target.H) ? 1 : 0;
                    return saved;
                }
                catch (Exception e)
                {
                    Debug.LogError("[UICap-HL] BuildCollections capture threw: " + e);
                    return 0;
                }
                finally
                {
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                    if (host != null) UnityEngine.Object.DestroyImmediate(host);
                    // WO-1629 Step 1a — the counted-only stub leaves the scene with the capture.
                    if (placedStub != null) UnityEngine.Object.DestroyImmediate(placedStub);
                    PanelManager.CloseAll();
                }
            });
        }

        // ---------------------------------------------------------------------
        //  Small reflection helpers (private access into the runtime card).
        // ---------------------------------------------------------------------
        private static GameObject GetPrivateGameObject(object target, string fieldName)
        {
            if (target == null) return null;
            var f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(target) as GameObject : null;
        }

        private static void InvokePrivate(object target, string methodName)
        {
            if (target == null) return;
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                if (m == null) continue;
                m.Invoke(target, null);
                return;
            }
            Debug.LogWarning("[UICap-HL] private method '" + methodName + "' not found -- state skipped.");
        }

        private static void InvokePrivate(object target, string methodName, object argument)
        {
            if (target == null) return;
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var methods = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                for (int i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    var parameters = method.GetParameters();
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) || parameters.Length != 1)
                        continue;
                    method.Invoke(target, new[] { argument });
                    return;
                }
            }
            Debug.LogWarning("[UICap-HL] private method '" + methodName + "' with one argument not found -- state skipped.");
        }

        /// <summary>Find a type by full name across the loaded assemblies (the pause menu lives in
        /// DeNelle.Settings, which this editor assembly does not reference at compile time).</summary>
        private static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Read a private instance field's value as a boxed object (nulls are safe).</summary>
        private static object GetPrivateFieldValue(object target, string fieldName)
        {
            if (target == null) return null;
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(target);
            }
            return null;
        }

        /// <summary>Read a public-or-private instance field's value as a boxed object (nulls are safe).</summary>
        private static object GetFieldValue(object target, string fieldName)
        {
            if (target == null) return null;
            var f = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(target) : null;
        }

        /// <summary>Set a private instance field (used to inject the SettingsController fixture).</summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null) return;
            var f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
            else Debug.LogWarning("[UICap-HL] private field '" + fieldName + "' not found.");
        }
    }
}
