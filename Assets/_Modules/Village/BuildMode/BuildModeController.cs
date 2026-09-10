// =============================================================================
// BuildModeController — the Build Mode entry/exit + placement loop (WO-108 P1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The CREATE-verb controller. Enter() freezes the threat (WaveManager), pulls the
// camera to a top-down overview, shows the grid + the code-built palette. Tapping
// a palette card arms a CatalogEntry; a ghost tracks the cursor and tints green/
// red. TWO-STEP placement (owner ruling 2026-07-13): a world tap DROPS the pending
// ghost at the snapped cell (no commit, no charge; taps elsewhere re-drop); rotate
// buttons/Q/E/d-pad adjust it freely; the PLACE button is the ONLY commit — through
// the ONE creation path (StructureFactory.Create), occupying the grid, charging the
// persisted wallet ONLY AFTER a committed placement (WO-131), and appending to the
// live BaseLayout. Exit() persists BaseLayout via GameStateService.Save(), restores
// the camera, and resumes waves.
//
// P1 = place-only (move / sell / rotate-edit / upgrade are P2, deferred). Rotate
// the ghost before placing is supported (Q/E keys / touch ⟲⟳ buttons, ±45° steps —
// WO-673 L5, owner ruling 2026-07-11) since it is free.
//
// INPUT is read through the IBuildInput seam (Build Mode S6), not Input.* directly:
// DesktopBuildInput (mouse/keyboard, unchanged) is the default; on a touch device
// LeanTouchBuildDriver installs itself on Enter() and feeds the same place/move/
// select/rotate/cancel intents from touch gestures + a code-built button bar.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;       // panel.Pick — detect a UI scrim eating the build click
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;   // TGVRU: instrument the place-spawn seam (§12)

namespace DeNelle.Village
{
    /// <summary>
    /// Drives the Build Mode edit session: enter/exit, the armed-entry ghost loop,
    /// place + cost + persist. Singleton; reuses CatalogRegistry + StructureFactory
    /// + PlacementGrid rather than forking a parallel placement system.
    /// </summary>
    public sealed class BuildModeController : MonoBehaviour
    {
        public static BuildModeController Instance { get; private set; }

        /// <summary>
        /// Broadcast whenever build mode is entered (true) or exited (false). Lets
        /// cross-assembly listeners react without referencing DeNelle.Village types
        /// directly — the BuildModeHudBridge subscribes to this to hide the bottom-middle
        /// combat HUD (which otherwise overlaps the build palette) on Enter and restore it
        /// on Exit. Static so a listener can subscribe before any controller instance
        /// exists (EnsureExists creates it lazily).
        /// </summary>
        public static event System.Action<bool> BuildModeChanged;

        /// <summary>Raised at every committed player placement (after spawn + charge), with the
        /// catalog entry id. F8 2026-07-08 (owner "stuck on raise first tower"): the tutorial's
        /// build.tower_placed adapter listened only to the LEGACY TowerPlacementSystem/BuildMenu —
        /// this is the LIVE placement path's event, so the signal finally has a live source.</summary>
        public static event System.Action<string> StructurePlaced;

        /// <summary>True while a build session is active.</summary>
        public bool IsActive { get; private set; }

        /// <summary>True while a CREATE entry is armed (WO-677 probe seam — read-only).</summary>
        public bool HasArmedEntry => _armed != null;

        [Header("Camera overview")]
        [Tooltip("Camera height (Y) while in build mode — angled 3D overview so structures read as upright.")]
        [SerializeField] private float _buildModeHeight = 22f;
        [Tooltip("Pitch (degrees) while in build mode — angled, not top-down, so 3D orientation is visible.")]
        [SerializeField] private float _buildModePitch = 45f;

        [Header("Build camera pan / zoom (desktop)")]
        [Tooltip("Pan speed (m/sec) for WASD + edge-scroll.")]
        [SerializeField] private float _camPanSpeed = 24f;
        [Tooltip("Pan speed multiplier while middle/right-dragging (m per screen pixel).")]
        [SerializeField] private float _camDragSpeed = 0.08f;
        [Tooltip("Screen-edge band (px) that triggers edge-scroll pan.")]
        [SerializeField] private float _camEdgeBand = 18f;
        [Tooltip("Zoom (height) step per scroll notch.")]
        [SerializeField] private float _camZoomStep = 4f;
        [Tooltip("Min / max build-camera height (zoom clamp).")]
        [SerializeField] private float _camHeightMin = 14f;
        [SerializeField] private float _camHeightMax = 60f;

        // Live overview state — the focus point the camera looks at (XZ on the grid plane)
        // and the current zoom height. Seeded in PullCameraBack, mutated by the pan/zoom
        // loop, and clamped to the grid (map) bounds so the player can't pan into the void.
        private Vector3 _camFocus;
        private float   _camHeight;
        private bool    _camDragging;
        private Vector2 _camDragLastPoint;
        // SINGLE-POINTER drag-to-pan (owner 2026-07-16 "nothing moves around" on web build mode).
        // RCA 2026-07-16: the LeanTouch two-finger camera driver never installs on WebGL
        // (BuildModeController.EnsureTouchInput gates on legacy Input.touchSupported == false in a
        // browser), and UpdateBuildCameraPan had NO single-pointer path — only middle/right mouse,
        // keys, and the d-pad. So on web a LEFT-mouse drag AND a single finger did nothing. This
        // path pans on a left-button / single-touch DRAG (past a threshold so a tap still
        // places/selects), gated to the idle view state so it never fights placement. Works with
        // mouse (desktop web) and one finger (mobile web); native APK keeps the LeanTouch driver.
        private bool    _ptrDragging;      // a qualifying single-pointer drag is actively panning
        private bool    _ptrDown;          // pointer press seen last frame (edge detect)
        private Vector2 _ptrDownPoint;     // where this press started (tap-vs-drag threshold)
        private Vector2 _ptrLastPoint;     // last pan sample
        private const float PtrPanDragThreshold = 12f;   // px of travel before a hold becomes a pan (not a tap)
        // SME camera (Grok #8): raw accumulated orbit yaw about _camFocus. Default 0 =
        // the ORIGINAL top-down-angled framing (identical to the pre-orbit ApplyBuildCamera).
        // The APPLIED framing SNAPS to 45-degree detents (SnappedYaw) so twist rotates the
        // view in clean CoC quarter/eighth steps; _camYaw itself stays continuous so the
        // twist/zoom read-assert can see it move.
        private float   _camYaw;

        [Header("Placement")]
        [SerializeField] private float _rayDistance = 800f;
        [SerializeField] private LayerMask _groundMask = ~0;
        [Tooltip("Min clearance (m) a placement must keep from the gate LANE, so the spawn→Heart corridor stays open.")]
        [SerializeField] private float _gateClearance = 3f;

        private PlacementGrid _grid;
        private BuildPaletteUI _palette;
        // WO-352 — the Structure Info Preview shown on a palette-card tap, BEFORE arming.
        // "Place" arms the entry (deferred from the old immediate-arm tap); "Cancel" / a
        // tap outside dismisses without arming.
        private BuildStructureInfoPanel _infoPanel;
        private BuildSelectionUI _selectionUi;
        private GhostPreview _ghost;
        // WO-335 — simple PLAYER yaw-only rotate panel opened on a tower place-confirm.
        // (The old UIToolkit 3-axis TowerPlacementRotateMenu is now the editor/dev OFFSET
        //  tool — no longer called from placement.)
        private RotateModelMenu _rotateMenu;
        // The 3-axis dev/orient editor opened from the palette "Orient" button — while it is
        // live the placement loops are frozen so a tap behind the modal can't drop a piece.
        private TowerPlacementRotateMenu _orientEditor;

        private Camera _camera;
        private CatalogEntry _armed;
        // WO-673 L5 (owner ruling 2026-07-11): ghost rotation is 45° stepped — 8 facings.
        // The armed yaw is held as EIGHTH-steps (0..7, ×45°) and committed through the
        // EXISTING PlacedStructureData fields with NO schema change:
        //   yawSteps  = _armedYawEighths / 2   (quarter turns, 0..3 — the legacy field)
        //   yawOffset = (_armedYawEighths & 1) * 45   (the odd half-step)
        // BaseLayoutLoader replays yawSteps*90 + yawOffset, so a 45° facing round-trips
        // exactly and old records (yawOffset 0) are untouched.
        private int _armedYawEighths;

        /// <summary>The armed yaw in degrees (eighth-steps × 45).</summary>
        private float ArmedYawDegrees => _armedYawEighths * 45f;
        /// <summary>The legacy quarter-turn component persisted into PlacedStructureData.yawSteps.</summary>
        private int ArmedYawQuarterSteps => (_armedYawEighths >> 1) & 3;
        /// <summary>The 45° half-step component persisted into PlacedStructureData.yawOffset.</summary>
        private float ArmedYawOffsetDeg => (_armedYawEighths & 1) * 45f;

        /// <summary>
        /// Poll the ±45° rotate intents (Q/E on desktop, ⟲/⟳ touch buttons) and step the
        /// armed yaw. Shared by the place and move loops (rotation is free in both).
        /// </summary>
        private void PollRotateIntents()
        {
            int dir = (_input.RotateCw ? 1 : 0) - (_input.RotateCcw ? 1 : 0);
            // Legacy single-direction Rotate (bot probes / old drivers) still steps CW —
            // via the IBuildInput default RotateCw => Rotate, so no extra poll needed here.
            // WO-702 owner ask ("rotate 90 left and right" on the armed placement screen):
            // merge the on-screen Rotate buttons' latched EIGHTH-steps (±2 = ±90°) into the
            // SAME yaw state — one merge point, no second yaw field.
            int eighths = dir + _uiRotateEighthsLatch;
            _uiRotateEighthsLatch = 0;
            if (eighths == 0) return;
            _armedYawEighths = (_armedYawEighths + eighths) & 7;
            BuildFirstUseGuide.Rotated();
            _hud?.RefreshFirstUseGuide();
            FlowTrace.Throttle("Build", "ghost-rotate", 0.25f,
                $"ghost rotate -> {_armedYawEighths * 45}°");
        }

        // Pending place data while modal is open for rotation confirmation.
        private bool _pendingPlace;
        private Vector2Int _pendingCell;
        private Vector2Int _pendingFootprint;
        private Vector3 _pendingSnapped;

        // ── Input seam (Build Mode S6) ────────────────────────────────────────
        // The place/move/select loops poll high-level intents from this source
        // instead of reading Input.* directly. Defaults to the mouse/keyboard impl
        // so desktop is unchanged; on a touch device the Lean.Touch driver installs
        // itself on Enter() and is removed on Exit().
        private IBuildInput _input = new DesktopBuildInput();
        private LeanTouchBuildDriver _touchDriver;

        // On-screen PLACE confirm (owner ask 2026-07-12, web/mobile demo: clicks/taps
        // never placed). A labeled control (now the Build HUD intent bar's PLACE button)
        // sets this latch; ConfirmIntentThisFrame consumes it FIRST and bypasses the
        // joystick-zone suppression — pressing a labeled button is explicit intent, never
        // a stray tap. (The standalone BuildPlaceButton canvas is retired — the Build HUD
        // owns the place intents now; BuildPlaceButton.cs is left dead/untouched.)
        private bool _uiPlaceLatch;
        private bool _uiCancelLatch;

        // Grok slices 1-4: the dedicated Build HUD presentation layer — ONE landscape
        // canvas that owns the wallet row, the BUILD MODE label, the Done exit, and the
        // single place-intent bar (Rotate L/R . PLACE . Cancel). It REPLACES the old
        // BuildPlaceButton canvas as the place-intent surface; the controller (BRAIN)
        // drives it via Show/Hide/SetState/RefreshResources and it calls back into the
        // controller's SAME intent latches (RequestUiRotateQuarter/RequestUiPlaceConfirm/
        // RequestUiCancel/Exit) — so placement behaviour is byte-identical.
        private BuildHudController _hud;

        /// <summary>Explicit confirm from the on-screen PLACE button (web/mobile).</summary>
        public void RequestUiPlaceConfirm() => _uiPlaceLatch = true;

        // ── TWO-STEP placement (owner ruling, live felt-test 2026-07-13) ──────
        // Armed placement is now DROP -> adjust -> PLACE:
        //   1. A world tap only DROPS/positions the pending ghost at the snapped
        //      cell — NO commit, NO charge. Tapping elsewhere RE-DROPS it there.
        //   2. Rotate Left/Right buttons, Q/E, and the d-pad adjust the pending
        //      ghost freely (the d-pad NUDGES the pending point instead of
        //      panning the camera while a drop is pending).
        //   3. The PLACE button (_uiPlaceLatch) is the ONLY commit — it routes
        //      through the same validation + Place() at the pending cell.
        // Instant-place-on-ground-tap is DEAD. The flow is ORDER-FREE: rotation
        // feeds the single _armedYawEighths state that poses the ghost every
        // frame, so rotate-then-drop and drop-then-rotate are equally valid and
        // the yaw persists across drops/re-drops. Before any drop the ghost
        // follows the cursor as before (desktop hover feel unchanged), and PLACE
        // with no drop commits at the hover cell (desktop convenience).
        private bool _dropPending;        // a pending drop exists (ghost frozen at _dropWorldPoint)
        private Vector3 _dropWorldPoint;  // world point of the pending drop (taps re-set it, d-pad nudges it)
        private const float PendingNudgeScale = 0.5f;   // d-pad nudge speed vs. camera pan speed

        /// <summary>What kind of confirm intent (if any) fired this frame — the split the
        /// two-step flow needs: a WORLD tap drops/re-drops, the UI PLACE latch commits.</summary>
        private enum ConfirmKind { None, UiPlace, WorldTap }

        // WO-702 (owner F8 2026-07-13: "on this screen i would like to see a rotate 90
        // left and right"): the on-screen Rotate Left/Right buttons latch quarter turns
        // here; PollRotateIntents merges them (×2 eighths = 90°) into _armedYawEighths —
        // the ONE yaw state Q/E and the touch bar already drive, so the ghost re-poses
        // through the existing per-frame MoveTo path.
        private int _uiRotateEighthsLatch;

        /// <summary>Explicit ±90° rotate from the on-screen Rotate buttons
        /// (dir −1 = Rotate Left / CCW, +1 = Rotate Right / CW).</summary>
        public void RequestUiRotateQuarter(int dir)
        {
            if (dir == 0) return;
            _uiRotateEighthsLatch += (dir > 0 ? 1 : -1) * 2;
            FlowTrace.Step("Build", $"PlaceScreen: Rotate {(dir > 0 ? "Right" : "Left")} pressed (90 deg).");
        }

        /// <summary>
        /// Explicit cancel latch (WO-677) — same web-safe pattern as
        /// <see cref="RequestUiPlaceConfirm"/>: a labeled control or a fleet probe backs
        /// out the armed entry / in-progress move through the controller's REAL cancel
        /// path, regardless of which pointer/input link the platform breaks.
        /// </summary>
        public void RequestUiCancel() => _uiCancelLatch = true;

        /// <summary>
        /// WO-677 Lane D probe seam — begin the MOVE of the currently selected structure
        /// through the real BeginMoveSelected path (the BuildSelectionUI Move button's
        /// handler target). Returns false when nothing is selected. Mirrors the
        /// ProbeArmedPlacementAt probe-seam precedent.
        /// </summary>
        public bool ProbeBeginMoveSelected()
        {
            if (!IsActive || _selected == null) return false;
            BeginMoveSelected();
            return _movingSelected;
        }

        /// <summary>
        /// WO-683 Lane D probe seam — the armed ghost's CURRENT grid cell (where the next
        /// PLACE would land). Read-only over ghost + grid; false when no armed ghost/grid.
        /// The fleet drives the HudMoveInput seam and asserts this cell CHANGES (mirrors
        /// the ProbeArmedPlacementAt / ProbeBeginMoveSelected probe-seam precedent).
        /// </summary>
        public bool ProbeArmedGhostCell(out Vector2Int cell)
        {
            cell = default;
            if (!IsActive || _armed == null || _ghost == null || _grid == null) return false;
            // WO-683 fleet RCA (4/4 FAIL, run detail "stuck at (15, 15)" = WorldToCell(origin)):
            // GhostPreview.MoveTo drives its CHILD visual; the host transform never leaves
            // world origin — read the tracked visual's position (GhostPreview.CurrentPosition),
            // never _ghost.transform.position.
            cell = _grid.WorldToCell(_ghost.CurrentPosition);
            return true;
        }

        /// <summary>
        /// SME camera probe seam (Grok #8) — read the live overview orbit yaw + zoom
        /// height WITHOUT mutating anything. The fleet drives AdjustYaw/AdjustZoom (twist/
        /// pinch) and asserts these move. Read-only; false when no live build camera.
        /// </summary>
        public bool ProbeBuildCameraState(out float yaw, out float height)
        {
            yaw = _camYaw;
            height = _camHeight;
            return IsActive && _camera != null;
        }

        // ── WO-683: kit d-pad merge (loose-reflection read of HudMoveInput.Move) ──
        // The build-overlay d-pad (LeanTouchBuildDriver) and the combat/town HUD cross
        // both publish DeNelle.HUD.Kit.HudMoveInput.Move; build mode reads it by the
        // SAME reflection-by-name pattern HeroLocomotion uses (no Village->HUD asmdef
        // edge, §5) and merges it into the arrow-key pan vector — ONE merge point, so
        // d-pad = arrow keys exactly for the armed ghost AND the in-progress move.
        private static System.Reflection.PropertyInfo s_hudMoveProp;
        private static bool s_hudMoveResolved;
        private bool _dpadConsumedTraced;   // first-consumed FlowTrace once per build session
        // docs/audit/input-controls.md §3.1: innerDeadZone 0.18 — on the digital kit pad
        // this is the no-press threshold (the t^1.6 response curve is a no-op on a
        // unit-step direction vector, so it is not applied here).
        private const float DpadDeadZone = 0.18f;

        /// <summary>
        /// Read the kit d-pad's published vector (HudMoveInput.Move) by cached loose
        /// reflection. Zero when the HUD assembly/static is absent; a resolve miss or a
        /// read throw WARNS (§12 — never a silent catch) instead of silently deadening
        /// the pad.
        /// </summary>
        private static Vector2 ReadHudDpadMove()
        {
            if (!s_hudMoveResolved)
            {
                s_hudMoveResolved = true;
                try
                {
                    var t = System.Type.GetType("DeNelle.HUD.Kit.HudMoveInput, DeNelle.HUD");
                    s_hudMoveProp = t != null
                        ? t.GetProperty("Move",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        : null;
                }
                catch (System.Exception ex)
                {
                    s_hudMoveProp = null;
                    FlowTrace.Warn("Build", "HudMoveInput.Move reflection resolve threw: " + ex.Message);
                }
                if (s_hudMoveProp == null)
                    FlowTrace.Warn("Build", "HudMoveInput.Move reflection MISS " +
                        "('DeNelle.HUD.Kit.HudMoveInput, DeNelle.HUD') — the d-pad cannot move the ghost/camera in build mode (WO-683).");
            }
            if (s_hudMoveProp == null) return Vector2.zero;
            try
            {
                object v = s_hudMoveProp.GetValue(null);
                return v is Vector2 vec ? vec : Vector2.zero;
            }
            catch (System.Exception ex)
            {
                FlowTrace.Throttle("Build", "dpad-read-throw", 5f,
                    "HudMoveInput.Move read threw: " + ex.Message);
                return Vector2.zero;
            }
        }

        /// <summary>
        /// Read the cancel intent for THIS frame: the UI/probe latch first (consumed),
        /// then the live input seam. Shared by the place + move loops so both exits
        /// honor the on-screen Cancel identically.
        /// </summary>
        private bool CancelRequestedThisFrame()
        {
            if (_uiCancelLatch)
            {
                _uiCancelLatch = false;
                FlowTrace.Step("Build", "Cancel: UI cancel latch consumed (touch bar / probe seam).");
                return true;
            }
            return _input.Cancel;
        }

        // ── Selection / edit state (P2) ───────────────────────────────────────
        // The currently tap-selected placed structure (move/sell target).
        private PlacedStructure _selected;
        // True while re-placing _selected (the MOVE ghost loop). During a move the
        // structure's OWN cells are freed so it cannot block itself; on a valid tap
        // it commits to the new cells, on cancel it returns to its origin.
        private bool _movingSelected;
        private Vector2Int _moveOriginCell;   // origin to restore if a move is cancelled
        // FIX #2 (2026-07-16) — the world target the MOVE ghost sits at. Seeded from the
        // selected structure's position on BeginMoveSelected, then driven by EITHER the
        // pointer (drag while pressed / desktop hover) OR the arrow keys + kit d-pad
        // (grid-step nudge). Persisting it lets a d-pad/arrow nudge hold between frames
        // instead of snapping back to the pointer every frame.
        private Vector3 _moveWorldPoint;

        // Camera restore state.
        private Vector3 _savedCamPos;
        private Quaternion _savedCamRot;
        private readonly List<MonoBehaviour> _disabledCamDrivers = new List<MonoBehaviour>();

        // DEF-117 — every OTHER screen camera we disabled on enter so the overview
        // does not split-screen with a second view (a runtime-spawned embedded FBX
        // camera, etc.). Re-enabled on exit. While SmartMobileCamera (the depth=100
        // renderer) is disabled for the overview, its per-second EnforceSoleCamera
        // rogue-camera cull stops running, so the build controller must cull them.
        private readonly List<Camera> _suppressedCameras = new List<Camera>();

        // Wave drivers frozen on enter, re-enabled on exit.
        private readonly List<WaveManager> _frozenWaves = new List<WaveManager>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // WO-702: a controller torn down mid-session (scene swap) must never leave
            // the Core truce flag stuck TRUE — a stale flag would hold tutorial intros
            // deferred and dialogues hidden forever.
            if (IsActive) DeNelle.Core.BuildModeState.SetActive(false);
        }

        /// <summary>Ensure a controller exists (HUD "Build" button entry point).</summary>
        public static BuildModeController EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("BuildModeController");
            return go.AddComponent<BuildModeController>();
        }

        // The build verb this session was entered with (owner 2026-07-10 generic build-
        // mode). Drives which catalog types the palette lists (Defense = Tower/Wall/Gate,
        // Collector = Collector). Defaults to Defense so the legacy no-arg Enter/Toggle
        // path is unchanged (back-compat).
        private BuildType _activeBuildType = BuildType.Defense;

        /// <summary>
        /// Owner ruling 2026-07-13: the palette opens on TOWN for the first open / all
        /// through the founding tutorial (the tutorial builds a town, not defenses);
        /// once Onboarded, the veteran default is DEFENSES. Walls tab is flagged off
        /// entirely for now (settlement building, WO-708).
        /// </summary>
        private static BuildType DefaultTabForPlayer()
        {
            var st = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return (st == null || !st.Onboarded) ? BuildType.Town : BuildType.Defense;
        }

        /// <summary>Toggle the build session (Town during the founding, Defenses after — owner 2026-07-13).</summary>
        public void Toggle()
        {
            if (IsActive) Exit();
            else EnterBuildMode(DefaultTabForPlayer());
        }

        /// <summary>Toggle the build session for a specific build verb (owner 2026-07-10).</summary>
        public void Toggle(BuildType type)
        {
            if (IsActive) Exit();
            else EnterBuildMode(type);
        }

        /// <summary>
        /// GENERIC build entry (owner 2026-07-10): enter Build Mode for a specific verb.
        /// Sets the active <see cref="BuildType"/> (which the palette reads via
        /// BuildCategoryRegistry) then runs the shared <see cref="Enter"/> body — placement
        /// / ghost / grid / persist stay generic (a collector places exactly like a tower).
        /// </summary>
        public void EnterBuildMode(BuildType type)
        {
            _activeBuildType = type;
            Enter();
        }

        /// <summary>
        /// WO-1571 - ENTER BUILD MODE ALREADY POINTED AT ONE STRUCTURE. The Manage &gt; BUILD card's
        /// BUILD button lands here, so a not-built row opens ITS OWN ghost instead of the category
        /// root.
        ///
        /// <para>⛔ THE BUG THIS REPLACES, so nobody re-points it back: the Manage card dropped the
        /// id and called <see cref="EnterBuildMode"/>(Town), which shows the palette, which opens
        /// BuildCollectionBrowser at its ROOT. card-collections.json authors collections for Towers
        /// / Walls and Gates / Manage Placed ONLY - so every ECONOMY / CRAFT / STORAGE row (the
        /// captured one is <c>arcane-tower</c>, "Cathedral of Magic") had no collection to be found
        /// in and the door was a dead end BY CONSTRUCTION. Device build 358872, logcat 2026-09-07
        /// 00:58:40: <c>opened workspace 'Build Collections' at root</c> then
        /// <c>BuildMode.Enter - palette shown</c>, and nothing else.</para>
        ///
        /// <para>⭐ NOTHING IS BYPASSED. This routes through
        /// <see cref="BuildPaletteUI.PlaceById"/> -&gt; BuildCollectionBrowser.PlaceById -&gt; its
        /// own Place/Done -&gt; OnEntrySelected -&gt; <see cref="Arm"/>. The unlock/offer gate is the
        /// browser's own IsCollectionItemVisible (which carries the WO-1379 soft gates), the
        /// singleton gate is <see cref="SingletonAlreadyBuilt"/> inside Arm, and affordability is
        /// judged where it always was - at the placement commit, so an unaffordable row still opens
        /// its ghost and gets the why-band rather than a refused door.</para>
        ///
        /// <para>⚠ EXPECT ONE ROOT LINE IN THE LOG. <see cref="Enter"/> shows the palette, so
        /// "opened workspace 'Build Collections' at root" still prints once, IMMEDIATELY followed in
        /// the same frame by "Armed placement for '&lt;id&gt;'". That is synchronous - no rendered
        /// flash - and a trace that stops at the root line is the FAILURE, not the fix.</para>
        /// </summary>
        /// <returns>False when the id is unknown, not currently offered, or build mode refused to
        /// open (enemy-owned scene - <see cref="Enter"/> has already toasted the player).</returns>
        public bool EnterBuildModeForStructure(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                FlowTrace.Warn("Build", "EnterBuildModeForStructure called with an empty id - refused.");
                return false;
            }
            var entry = CatalogRegistry.Get(id);
            if (entry == null)
            {
                FlowTrace.Warn("Build", "EnterBuildModeForStructure '" + id +
                    "': not in CatalogRegistry - build mode is NOT opened.");
                return false;
            }
            // The verb only decides what the palette WOULD list; we never render that list. It is
            // still derived (never hardcoded Town) so the session's active BuildType matches the
            // thing being placed - camera/grid/HUD all read it.
            var verb = BuildFilter.Matches(entry, BuildFilter.Defense) ? BuildType.Defense : BuildType.Town;
            EnterBuildMode(verb);
            if (!IsActive)
            {
                FlowTrace.Warn("Build", "EnterBuildModeForStructure '" + id + "': Enter refused (see the " +
                    "BuildMode.Enter line above) - nothing armed.");
                return false;
            }
            FlowTrace.Step("Build", "Manage direct BUILD door -> place '" + id + "' as verb=" + verb +
                " (WO-1571; the category root is deliberately never shown)");
            if (_palette != null && _palette.PlaceById(id)) return true;
            FlowTrace.Warn("Build", "EnterBuildModeForStructure '" + id + "': the palette could not honour the " +
                "direct pick - the ordinary category root stands and the player is not stranded.");
            return false;
        }

        // =====================================================================
        //  Enter / Exit
        // =====================================================================

        /// <summary>
        /// Enter Build Mode: seed BaseLayout from the default village on first entry,
        /// freeze waves, pull the camera back, show the grid + palette.
        /// </summary>
        public void Enter()
        {
            // Build/upgrade/sell/move all funnel through here — one gate covers them.
            // No building in enemy territory (raid bases are hostile, not buildable).
            //
            // F8 seq 632 ROOT CAUSE 1 (2026-08-02): this refusal used to be a bare Debug.Log.
            // The player tapped BUILD — the spotlit, tutorial-highlighted button — and NOTHING
            // HAPPENED, with no toast, no reason, no trace. The owner sat 300s on founding_hollow
            // because of exactly this. CLAUDE.md sec.12 forbids a silent failure: a refusal the
            // player can feel must be a refusal the player can READ, and a refusal the CLI can
            // find in the captured trace. Now: player-facing toast + FlowTrace.Warn, always.
            if (DeNelle.Village.SceneOwnership.IsEnemyOwned)
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                DeNelle.Core.UI.ElarionUiKit.ShowToast(
                    "You can't build in enemy territory.",
                    DeNelle.Core.UI.ElarionUiKit.ToastTone.Danger);
                FlowTrace.Warn("Build", $"BuildMode.Enter REFUSED in scene '{scene}': it is ENEMY-OWNED " +
                    "(scene-configs.json ownership=Enemy). Nothing opens - the player was told via toast. " +
                    "Any flow that waits on a placement signal here can NEVER complete (F8 seq 632 root cause 1).");
                return;
            }

            if (IsActive) return;
            IsActive = true;
            // WO-702 truce seam: publish "builder open" into Core so TutorialFlow can
            // defer step-intro dialogues and DialogueView (HUD, Core-only) can hide an
            // already-open dialogue until Exit. Village writes, everyone else reads.
            DeNelle.Core.BuildModeState.SetActive(true);
            _dpadConsumedTraced = false;   // WO-683 — first-consumed trace fires once PER build session

            EnsureGrid();
            SeedBaseLayoutIfFirstEntry();

            // F8-39 / WO-1361: census on build ENTRY - how many placed structures are LIVE in the
            // scene vs how many the persisted BaseLayout says should exist.
            //
            // WO-1361 (2026-09-04): the old line here read "live << persisted = structures already
            // gone" - a cause the emitter cannot see. Every such census in the device logs was
            // the FOUNDING load, where the StrategicPlacementMigration.BakedRows records have no
            // PlacedStructure because the BAKED TWIN stands in for them until the next hub load
            // (ShouldReplayRecord == false while standdown is inactive; BaseLayoutLoader.Rebuild
            // WITHHOLDS them for the same reason). A PlacedStructure count can never see a baked
            // twin, so persisted - live == bake-owned is the design working, not a loss.
            // The census now subtracts what it is not allowed to count and NAMES the rest:
            //   replayable  = records ShouldReplayRecord says must have a live body
            //   bake-owned  = migration-managed records the bake owns this load (no body expected)
            //   unaccounted = replayable records with NO live body at their (itemId, cell)
            // Only unaccounted > 0 is a loss, and only that is a Fail - a founding load never Warns.
            // Classification is pure (BaseLayoutCensus.Classify) so the regression pins it headless.
            {
                var liveBodies = FindObjectsByType<PlacedStructure>();
                int liveNow = liveBodies.Length;
                int loadedNow = BaseLayoutLoader.Instance != null ? BaseLayoutLoader.Instance.Loaded.Count : -1;
                var stEnter = GameStateService.Instance != null ? GameStateService.Instance.State : null;
                var layoutNow = stEnter != null ? stEnter.BaseLayout : null;

                var census = Guard.Try("BuildMode", "census classify replayable vs bake-owned", () =>
                {
                    var bodies = new List<(string itemId, int cellX, int cellZ)>(liveBodies.Length);
                    for (int i = 0; i < liveBodies.Length; i++)
                    {
                        var ps = liveBodies[i];
                        if (ps == null) continue;
                        bodies.Add((ps.itemId, ps.gridCell.x, ps.gridCell.y));
                    }
                    return BaseLayoutCensus.Classify(layoutNow, bodies, StrategicPlacementMigration.ShouldReplayRecord);
                }, fallback: null);

                if (census == null)
                {
                    // Guard already emitted the exception via FlowTrace.Fail; this names the consequence.
                    FlowTrace.Fail("BuildMode",
                        $"census: live={liveNow} persisted={(layoutNow != null ? layoutNow.Count : 0)} " +
                        "- classification THREW (see the Guard line above); replayable/bake-owned split unknown this entry.");
                }
                else
                {
                    FlowTrace.Step("BuildMode",
                        BaseLayoutCensus.FormatLine(liveNow, census) +
                        $" loader.Loaded={loadedNow} bake-owned=[{string.Join(",", census.BakeOwnedIds)}]" +
                        $" scene='{DeNelle.Village.SceneOwnership.IsEnemyOwned}'-enemyOwned");
                    if (BaseLayoutCensus.IsLoss(census))
                        FlowTrace.Fail("BuildMode",
                            $"census: {census.Unaccounted} REPLAYABLE record(s) have NO live body = " +
                            $"[{string.Join(", ", census.UnaccountedIds)}] - a genuine loss; " +
                            "the step is in the preceding [Flow:BaseLayout] Rebuild/Spawn FAILED lines (WO-1361).");
                }
            }

            FreezeWaves();

            // ── ORDERED ENEMY WARM (owner 2026-08-20: "set the enemies to load in order of
            // seeing them when they are placing buildings ... they came in as pills") ─────────
            //
            // WHY HERE, and not anywhere else: the line above has just FROZEN the wave loop, so
            // this is provably dead time — nothing spawning, nothing fighting, a menu's frame
            // budget. It is also the single funnel for every town-edit verb (build / upgrade /
            // sell / move all reach Enter), so one call covers them all, and it is literally the
            // moment the owner described. Hooking the wave countdown instead would leave the
            // fetch only the countdown to finish in, which is the window that was already losing.
            //
            // The planner computes the roster the player meets NEXT (the FTUE teaching wave first
            // while the tutorial is unfinished, then the upcoming composed wave), maps it to
            // enemy FAMILIES in first-appearance order, and requests them one at a time so the
            // first body's bundle gets the bandwidth first. Non-blocking, idempotent, per-family
            // — never the whole enemy payload. All of its own reasoning lives in
            // UpcomingWaveWarmPlanner.cs; all of its evidence lands under [Flow:EnemyWarmOrder].
            UpcomingWaveWarmPlanner.WarmForTown();

            PullCameraBack();

            _grid.SetGridVisible(true);

            if (_ghost == null)
                _ghost = new GameObject("GhostPreview").AddComponent<GhostPreview>();

            EnsurePalette();
            // Point the palette at the active build verb BEFORE Show so it lists exactly
            // that verb's catalog types (Defense = Tower/Wall/Gate, Collector = Collector).
            // Configure each entry (the palette persists across sessions) so re-entering
            // with a different verb re-sources its types/lockedIds.
            _palette.Configure(_activeBuildType);
            _palette.Show();

            // Grok slices 1-4: bring up the dedicated Build HUD (wallet + label + Done +
            // single place-intent bar) as the sole edit-chrome surface. Browse by default.
            EnsureHud();
            _hud.Show();
            _hud.SetState(BuildHudState.Browse);
            _hud.RefreshResources();

            // Owner 2026-08-29: "Skip Tutorial" must not sit on the build category / place
            // chrome. Suppress chrome only — TutorialFlow keeps its skip-all arm; Exit restores.
            DeNelle.Core.UI.TutorialSkipUi.SetSuppressed(true);

            FlowTrace.Step("Build", "BuildMode.Enter — palette shown, EnsureTouchInput next");
            EnsureTouchInput();   // install the Lean.Touch driver on a touch device (S6)

            // Notify cross-assembly listeners (BuildModeHudBridge hides the combat HUD
            // so the bottom-middle bars don't overlap the build palette).
            BuildModeChanged?.Invoke(true);

            Debug.Log("[BuildMode] Entered build mode.");
        }

        /// <summary>
        /// Exit Build Mode: commit BaseLayout to GameState + Save(), hide UI, restore
        /// the camera, resume waves.
        /// </summary>
        public void Exit()
        {
            if (!IsActive) return;
            IsActive = false;
            // WO-702 truce seam: builder closed — TutorialFlow releases any deferred
            // intro and DialogueView re-shows a hidden conversation next frame.
            DeNelle.Core.BuildModeState.SetActive(false);

            CancelArmed();
            ClearSelection();
            _infoPanel?.Hide();   // WO-352 — close any open structure preview on exit
            _palette?.Hide();
            _selectionUi?.Hide();
            _hud?.Hide();
            _grid?.SetGridVisible(false);
            DeNelle.Core.UI.TutorialSkipUi.SetSuppressed(false);

            // Stop the Lean.Touch driver + hide its button bar; revert to the desktop
            // input source so a re-entry on desktop is unaffected (S6).
            if (_touchDriver != null)
            {
                _touchDriver.Uninstall();
                _input = new DesktopBuildInput();
            }

            // Drop any unconsumed press so a queued confirm (or a queued 90-degree rotate)
            // can never leak into the next session. (The HUD canvas hides via _hud.Hide()
            // above; its intent bar goes with it.)
            _uiPlaceLatch = false;
            _uiRotateEighthsLatch = 0;

            CommitLayout();

            RestoreCamera();
            ResumeWaves();

            // Restore the combat HUD now that the build palette is gone.
            BuildModeChanged?.Invoke(false);

            Debug.Log("[BuildMode] Exited build mode — layout saved.");
        }

        // =====================================================================
        //  Placement loop
        // =====================================================================

        private void Update()
        {
            // WO-1483: town frame path — the build/place loop ticks in town whether or not
            // build mode is armed, so the disarmed cost belongs in the empty-town table.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "BuildModeController.Update", 4f, 1f);

            // §12 P0 GATE TRACE (owner 2026-07-07 "armed but zero PlaceConfirm checks"):
            // EVERY early-return between 'armed' and the PlaceConfirm evaluation now
            // self-names, throttled ~1/sec, whenever it BLOCKS while an entry is armed.
            // An armed player whose clicks do nothing can no longer fail silently —
            // one '[Flow:Build] PlaceLoop BLOCKED at <gate>' line per second names the
            // culprit. Helper: TraceBlockedWhileArmed (below).
            if (!IsActive)
            {
                TraceBlockedWhileArmed("IsActive",
                    "IsActive=false while an entry is still armed (Exit ran without CancelArmed?)");
                return;
            }

            // WO-377: freeze the placement loops while a Yarn dialogue is on screen so a
            // click meant for the dialogue box can't place/select/cancel a structure.
            // HeroLocomotion owns the global input gate (set on dialogue start, cleared on
            // complete). The ghost is hidden so nothing tracks the cursor mid-conversation.
            // WO-702 truce: while DialogueView is holding a live dialogue HIDDEN because
            // the builder is open (owner F8 2026-07-13 "pause the sylas dialogue"), the
            // dialogue's input lock must NOT freeze the builder — the invisible panel
            // can't be mis-clicked, and the player has to be able to finish the asked
            // build action. The lock still freezes placement for any VISIBLE dialogue.
            if (HeroLocomotion.InputSuppressed && !DeNelle.Core.BuildModeState.DialogueHiddenForBuilder)
            {
                TraceBlockedWhileArmed("HeroLocomotion.InputSuppressed",
                    "dialogue/cutscene input lock is holding the whole placement loop");
                _ghost?.Hide(); return;
            }
            // DEF-117 — raycast from the camera that is ACTUALLY on screen (the one
            // we pulled into the overview), never Camera.main: with rogue cameras in
            // play Camera.main can resolve to a non-rendering / wrong camera, so taps
            // would miss the build grid. _camera is set in PullCameraBack().
            if (_camera == null)
            {
                _camera = ActiveScreenCamera();
                if (_camera == null)
                {
                    TraceBlockedWhileArmed("ActiveScreenCamera",
                        "no enabled screen camera found — nothing to raycast from");
                    return;
                }
            }

            // Move the overview each frame (WASD / edge-scroll / drag / zoom on desktop;
            // touch pans via the Lean driver). Runs in every mode so the player can re-frame
            // while arming, moving, or idle.
            UpdateBuildCameraPan();

            // Grok slices 1-2: the dedicated Build HUD is the single place-intent surface
            // (the old BuildPlaceButton canvas is retired). Drive the three-state chrome
            // from the live mode each frame — Placing while armed or moving (the intent
            // bar shows Rotate L/R . PLACE . Cancel), Selected while a placed structure is
            // picked (BuildSelectionUI owns those verbs), else Browse (shop only).
            EnsureHud();
            _hud.SetState((_armed != null || _movingSelected)
                ? BuildHudState.Placing
                : (_selected != null ? BuildHudState.Selected : BuildHudState.Browse));

            // WO-1010 P1: the ghost carries its own controls, so the HUD needs the ghost's
            // PROJECTED SCREEN POINT every frame. The projection happens HERE because the
            // brain owns the camera — the HUD is presentation and must not reach for one.
            PushGhostAnchorToHud();

            // WO-1010 D12 (owner 2026-08-09): the nudge stick shows on TWO conditions, both
            // automatic — an item is selected AND the carousel is minimized — and it leaves the
            // moment the piece is placed. No toggle: "the + doesn't help ... user should not need
            // to do anything." Placing alone is not enough, because the player can reopen the
            // carousel over a live ghost; the stick must not sit under the shop.
            _hud.SetNudgePadAllowed(
                (_armed != null || _movingSelected) && _palette != null && _palette.IsCollapsed);

            // While the 3-axis orient editor is open, the placement loops are frozen so a tap
            // behind the modal can't drop a piece (the modal owns its own confirm/cancel).
            if (_orientEditor != null && _orientEditor.isActiveAndEnabled && _orientEditor.IsOpen)
            {
                // F8-30 suspect — the orient editor registered with PanelManager; if a dormant
                // instance ever holds IsOpen=true this line names it with its full state.
                TraceBlockedWhileArmed("orient-editor freeze",
                    $"_orientEditor='{_orientEditor.gameObject.name}', isActiveAndEnabled={_orientEditor.isActiveAndEnabled}, " +
                    $"IsOpen={_orientEditor.IsOpen}, PanelManager.AnyOpen={DeNelle.Core.UI.PanelManager.AnyOpen}, " +
                    $"openPanel='{DeNelle.Core.UI.PanelManager.OpenPanelName ?? "<none>"}'");
                _ghost?.Hide(); return;
            }

            // Three exclusive modes: re-placing a selected structure (MOVE), arming a
            // new one (CREATE), or idle (tap a structure to SELECT it).
            if (_movingSelected)
            {
                // Not a hard block, but while armed it DIVERTS the click away from the place
                // loop — a stuck _movingSelected reads as "my armed tower won't place".
                TraceBlockedWhileArmed("move-mode diversion",
                    $"_movingSelected=true (selected='{(_selected != null ? _selected.itemId : "<null>")}') — the MOVE loop owns the click, place loop skipped");
                UpdateMoveLoop(); return;
            }
            if (_armed != null) { UpdatePlaceLoop(); return; }
            // WO-677 — a cancel latch raised while idle (nothing armed/moving) has no
            // target; drop it so it can't fire a phantom cancel on the NEXT arm/move.
            if (_uiCancelLatch)
            {
                _uiCancelLatch = false;
                FlowTrace.Step("Build", "UI cancel latch dropped — nothing armed or moving.");
            }
            UpdateSelectLoop();
        }

        /// <summary>
        /// §12 P0 gate trace — one throttled (~1/sec per gate) line whenever a gate in the
        /// Update→UpdatePlaceLoop chain BLOCKS while an entry is armed. Silent while nothing
        /// is armed (idle/select gating is normal), zero-cost when FlowTrace is off.
        /// </summary>
        private void TraceBlockedWhileArmed(string gate, string state)
        {
            if (_armed == null) return;
            FlowTrace.Throttle("Build", "blocked-" + gate, 1f,
                $"PlaceLoop BLOCKED at {gate}: {state} (armed='{_armed.id}')");
        }

        /// <summary>
        /// Cursor→ground raycast shared by the placement loops. Tries the configured
        /// <see cref="_groundMask"/> first; if it misses — e.g. the scene-baked mask
        /// excludes the ground's layer (WO-215: VillageSceneBuilder baked 1&lt;&lt;0 /
        /// Default-only, but the village ground collider is on another layer, so the
        /// raycast missed every frame and the ghost never appeared) — retries against
        /// all layers so placement always tracks real ground. Mirrors
        /// TowerPlacementSystem's ~0 mask, which works.
        /// </summary>
        private bool RaycastGround(out RaycastHit hit)
            => RaycastGroundAt(_input.ScreenPoint, out hit);

        /// <summary>Screen-point overload of the ground raycast (same camera/mask/fallback rules).</summary>
        private bool RaycastGroundAt(Vector2 screenPoint, out RaycastHit hit)
        {
            Ray ray = _camera.ScreenPointToRay(screenPoint);
            if (Physics.Raycast(ray, out hit, _rayDistance, _groundMask)) return true;
            return Physics.Raycast(ray, out hit, _rayDistance, ~0);
        }

        /// <summary>
        /// AutoPilot probe seam (fleet AssertTutorialFirstTower) — evaluate the SAME
        /// reason-aware validity gate the live place loop runs (RaycastGround → SnapToGrid →
        /// IsValidPlacement, identical rules incl. cost) at an arbitrary screen point,
        /// WITHOUT consuming input, moving the ghost, or placing anything. Read-only over
        /// grid + scene. Requires build mode active with an armed entry (probes for
        /// <c>_armed</c>, exactly what the ghost tint evaluates). Returns false + the
        /// reject reason when the point would not accept a placement.
        /// </summary>
        public bool ProbeArmedPlacementAt(Vector2 screenPoint, out BuildRejectReason reason)
        {
            reason = BuildRejectReason.Generic;
            if (!IsActive || _armed == null || _grid == null || _camera == null) return false;
            if (!RaycastGroundAt(screenPoint, out RaycastHit hit))
            { reason = BuildRejectReason.BadSurface; return false; }
            Vector3 snapped = _grid.SnapToGrid(hit.point);
            return IsValidPlacement(hit, snapped, _armed, out _, out _, out reason, out _, out _);
        }

        /// <summary>
        /// #76: resolve the seat height as the surface DIRECTLY BELOW the committed placement —
        /// "ground is the area below placement", not whatever the camera ray grazed. Probe straight
        /// DOWN through the snapped footprint XZ and take the TOPMOST flat, upward-facing surface
        /// beneath it (so placing on a rampart / a tier-3 roof seats on that top, while a wall the
        /// cursor merely passed over — not below the footprint — never contaminates the height).
        /// Tower/Building tops are skipped (can't stack on another structure here). Returns false if
        /// nothing valid is below (caller keeps the existing seatY).
        /// </summary>
        private bool TryResolveGroundYBelow(Vector3 snapped, out float groundY)
        {
            groundY = snapped.y;
            Vector3 origin = new Vector3(snapped.x, snapped.y + 50f, snapped.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            float best = float.NegativeInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.normal.y < 0.85f) continue;   // skip steep faces (wall sides, slopes)
                if (h.collider == null) continue;
                if (h.collider.CompareTag("Tower") || h.collider.CompareTag("Building")) continue;
                // Never seat a move on its OWN body. OverlapsExistingStructure already excludes
                // _selected from the overlap gate; this closes the matching hole in the HEIGHT
                // probe. Without it the probe lands on the structure's own 4m root box
                // (BaseLayoutLoader) and seats it +4m -- compounding on every commit.
                if (_movingSelected && _selected != null &&
                    h.collider.GetComponentInParent<PlacedStructure>() == _selected) continue;
                if (h.point.y > best) { best = h.point.y; found = true; }
            }
            if (found) { groundY = best; return true; }
            return false;
        }

        /// <summary>
        /// Read the place/select confirm intent for THIS frame, gated so a tap/click
        /// that lands inside the on-screen move joystick's engage zone never confirms a
        /// placement or selection (DEF-171). The hero locomotion stick stays live during
        /// Build Mode, so without this guard a thumb-drag that starts on the stick would
        /// also drop a structure under it. Shares VirtualJoystick.IsInZone — the SAME
        /// circle the stick + CameraPanInput use — so the exclusion can't drift from the
        /// live stick layout. IBuildInput.PlaceOrSelect is a single-frame latch, so we
        /// must consume it (read once) even when suppressing, or a queued tap would leak
        /// to the next frame.
        /// </summary>
        // Scratch buffers for the over-UI tap check (allocated once — the confirm path
        // runs per-frame while armed and must not churn the GC).
        private static readonly List<UnityEngine.EventSystems.RaycastResult> s_uiHits =
            new List<UnityEngine.EventSystems.RaycastResult>(8);
        private static UnityEngine.EventSystems.PointerEventData s_uiProbe;

        /// <summary>
        /// F8 2026-07-13 (rotate-tap leak): true when <paramref name="screenPoint"/> sits
        /// over ANY raycast-receiving uGUI element (the Rotate pair, palette dock, verb
        /// bar, HUD buttons). EventSystem raycast at the point — phase-independent, so a
        /// touch that already ended this frame still resolves (IsPointerOverGameObject is
        /// unreliable after the finger lifts). No EventSystem in the scene => false
        /// (desktop editor edge — never blocks placement).
        /// INTERNAL (not private) so LeanTouchBuildDriver's finger handlers call this same
        /// EventSystem probe instead of trusting LeanFinger.IsOverGui, which only counts hits
        /// on LeanTouch.CurrentGuiLayers (default layer 5) and so misses every code-built
        /// canvas in this project (all layer 0).
        /// </summary>
        internal static bool IsPointOverUi(Vector2 screenPoint)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            if (s_uiProbe == null) s_uiProbe = new UnityEngine.EventSystems.PointerEventData(es);
            s_uiProbe.position = screenPoint;
            s_uiHits.Clear();
            es.RaycastAll(s_uiProbe, s_uiHits);
            return s_uiHits.Count > 0;
        }

        /// <summary>
        /// WO-1615 §3.1 — the full scene path of a transform ("Canvas/Panel/Rail/OkChip"), for the
        /// over-UI suppression trace. A bare GameObject NAME is not enough to identify a raycast
        /// owner: this project builds several code-made canvases whose children share names, so the
        /// path is what tells the reader WHICH canvas ate the tap. Depth-capped so a pathological
        /// hierarchy can never turn one log line into a wall of text.
        /// </summary>
        private static string HierarchyPathOf(Transform t)
        {
            if (t == null) return "<none>";
            var sb = new System.Text.StringBuilder(t.name);
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 16)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            if (p != null) sb.Insert(0, ".../");
            return sb.ToString();
        }

        private bool PlaceConfirmedThisFrame() => ConfirmIntentThisFrame() != ConfirmKind.None;

        /// <summary>
        /// Kind-aware confirm poll (two-step placement, 2026-07-13). Same body + same
        /// consume-once/suppression rules as the old PlaceConfirmedThisFrame — it just
        /// REPORTS the channel: the UI PLACE latch (the only commit while armed) vs. a
        /// world tap (drop/re-drop while armed; still the direct commit for the MOVE
        /// and SELECT loops via the bool wrapper above). Call at most ONCE per frame.
        /// </summary>
        private ConfirmKind ConfirmIntentThisFrame()
        {
            // On-screen PLACE button latch — consumed FIRST and NOT zone-suppressed:
            // a labeled button press is explicit intent (web/mobile fix, 2026-07-12).
            if (_uiPlaceLatch)
            {
                _uiPlaceLatch = false;
                // The touch driver's PlaceOrSelect is a STICKY latch (LeanTouchBuildDriver:128
                // holds it until read), not a per-frame edge like the desktop input. Returning
                // here without reading it leaves the tap that pressed PLACE latched, and the
                // NEXT frame's ConfirmIntentThisFrame reads it as a WorldTap — which re-drops
                // the pending ghost under the PLACE label. Read it here so the consume-once
                // rule this method documents above holds on BOTH exits.
                _ = _input.PlaceOrSelect;
                FlowTrace.Step("Build", "PlaceConfirm: UI PLACE button latch consumed (zone suppression bypassed; touch latch drained).");
                return ConfirmKind.UiPlace;
            }
            bool confirmed = _input.PlaceOrSelect;   // consumes the latch (touch driver)
            if (!confirmed) return ConfirmKind.None;
            // TGVRU §12 (EDIT-ONLY instrumentation) — a confirm WAS read this frame; trace the
            // point + joystick-zone state so a web trace shows whether a real click reached
            // placement and why it may be suppressed. NOTE: we log the already-consumed
            // `confirmed` and reuse a single IsInZone() call — never RE-READ _input.PlaceOrSelect
            // (that would double-consume the single-frame latch and change behaviour).
            bool inZone = VirtualJoystick.IsInZone(_input.ScreenPoint);
            FlowTrace.Step("Build", $"PlaceConfirm check: input.PlaceOrSelect={confirmed}, " +
                $"screenPoint={_input?.ScreenPoint}, inJoystickZone={inZone}");
            // F8 2026-07-13 ("if i click rotate, it rotates but places first — registers
            // as a click event and places right there"): a tap on ANY uGUI control (the
            // new Rotate pair, the palette dock, the verb bar) also lands in the touch
            // driver's PlaceOrSelect latch and leaked into world placement. Suppress any
            // confirm whose screen point sits over interactive UI — a labeled button is
            // its own intent; the world only hears taps on the world. (The PLACE button
            // is unaffected: it arrives via the explicit _uiPlaceLatch above.)
            if (IsPointOverUi(_input.ScreenPoint))
            {
                // WO-1615 §3.1 — NAME THE SURFACE THAT OWNS THE CLICK. PERMANENT (CLAUDE.md §12).
                // The suppression itself is correct and untouched: a tap on a labeled chip must
                // never also drop a building. But saying only "is over UI" is exactly why the move
                // defect could not be diagnosed — the log proved a tap was eaten and never said BY
                // WHAT. IsPointOverUi leaves s_uiHits populated (sorted topmost-first) on the true
                // path, so the owner is readable right here with no second raycast. The module name
                // separates a uGUI GraphicRaycaster (which canvas) from a UI Toolkit PanelRaycaster
                // (a UIDocument over the top), and the loop flag proves the tap happened during MOVE.
                string uiOwner = "<none>", uiPath = "<none>", uiModule = "<none>";
                int uiSort = 0;
                if (s_uiHits.Count > 0)
                {
                    var top = s_uiHits[0];
                    var topGo = top.gameObject;
                    if (topGo != null) { uiOwner = topGo.name; uiPath = HierarchyPathOf(topGo.transform); }
                    if (top.module != null) uiModule = top.module.GetType().Name;
                    uiSort = top.sortingOrder;
                }
                FlowTrace.Warn("Build", $"PlaceConfirm SUPPRESSED: tap at {_input.ScreenPoint} is over UI " +
                    $"(button tap, not a world placement). hits[0]='{uiOwner}' path='{uiPath}' " +
                    $"module={uiModule} sortingOrder={uiSort} hitCount={s_uiHits.Count} " +
                    $"movingSelected={_movingSelected} armed={(_armed != null)}. " +
                    "If hits[0] is NOT 'OkChip' while movingSelected=true, THAT surface owns the PLACE " +
                    "rect during a move; if it IS 'OkChip' and no 'OkChip TAPPED' line follows, the " +
                    "chip's own click delivery is the defect.");
                return ConfirmKind.None;
            }
            // Suppress confirms whose screen point sits in the move-stick zone.
            if (inZone)
            {
                FlowTrace.Warn("Build", "PlaceConfirm SUPPRESSED by joystick zone");
                return ConfirmKind.None;
            }
            // Suppress + REPORT confirms eaten by a pickable UI panel over the cursor — the silent
            // "cannot build" class (a full-screen scrim with a bad PanelSettings ate every click 3x
            // in MainCastle_Hall). Naming the culprit (§12) turns a silent fail into a diagnosis.
            if (PointerOverPickableUI(new Vector2(_input.ScreenPoint.x, _input.ScreenPoint.y), out string blocker))
            {
                if (_blockLogged.Add(blocker))
                    FlowTrace.Warn("BuildMode", $"build/select click SUPPRESSED — cursor is over pickable UI '{blocker}'. " +
                        "If you CANNOT place a structure, THIS panel is eating the click — check its PanelSettings / PickingMode.");
                return ConfirmKind.None;
            }
            FlowTrace.Step("Build", "PlaceConfirm CONFIRMED — placement/select proceeds this frame");
            return ConfirmKind.WorldTap;
        }

        // PANEL-BLOCK GUARD (build-defense RCA 2026-06-19): is a pickable UI element sitting over
        // the cursor? panel.Pick across every live UIDocument returns the topmost picking element
        // (click-through roots are PickingMode.Ignore and are skipped). The dev console legitimately
        // sits on top, so it never counts as a gameplay blocker. Returns the highest-sort culprit.
        private static readonly HashSet<string> _blockLogged = new HashSet<string>();
        private bool PointerOverPickableUI(Vector2 screenPos, out string blocker)
        {
            blocker = null;
            var docs = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);
            if (docs == null) return false;
            float bestSort = float.MinValue;
            foreach (var doc in docs)
            {
                if (doc == null) continue;
                string docName = doc.gameObject != null ? doc.gameObject.name : "?";
                if (docName.IndexOf("Dev", System.StringComparison.OrdinalIgnoreCase) >= 0) continue; // dev console isn't a blocker
                var root = doc.rootVisualElement;
                var panel = root != null ? root.panel : null;
                if (panel == null) continue;
                float sort = doc.panelSettings != null ? doc.panelSettings.sortingOrder : 0f;
                try
                {
                    Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, screenPos);
                    var picked = panel.Pick(p);
                    if (picked != null && sort >= bestSort)
                    {
                        bestSort = sort;
                        blocker = docName + " > " + (string.IsNullOrEmpty(picked.name) ? picked.GetType().Name : picked.name);
                    }
                }
                catch (System.Exception ex) { FlowTrace.Warn("BuildMode", "panel-block pick threw on '" + docName + "': " + ex.Message); }
            }
            return blocker != null;
        }

        /// <summary>Idle mode: a tap/click on a PlacedStructure selects it for edit.</summary>
        private void UpdateSelectLoop()
        {
            if (!PlaceConfirmedThisFrame()) return;

            // WO-677 Lane B (§12 step-in/out): the idle tap-select chain was silent past the
            // confirm — a raycast miss or a non-structure hit died without a trace, so "tap on
            // my tower does nothing" (the mobile Move/Sell symptom) was uncapturable. Every
            // link now names itself; no mechanics change.
            if (!RaycastGround(out RaycastHit hit))
            {
                FlowTrace.Warn("Build", $"SelectLoop: confirm read but ground raycast MISSED at " +
                    $"screenPoint={_input?.ScreenPoint} — nothing selectable under the tap.");
                return;
            }

            // Hit collider's GameObject or any parent may carry the marker.
            var ps = hit.collider != null
                ? hit.collider.GetComponentInParent<PlacedStructure>()
                : null;
            if (ps == null)
            {
                FlowTrace.Step("Build", $"SelectLoop: tap hit '{(hit.collider != null ? hit.collider.gameObject.name : "<no collider>")}' " +
                    "but no PlacedStructure in its parents — not a placed piece, select skipped.");
                return;
            }
            FlowTrace.Step("Build", $"SelectLoop: tap SELECTS '{ps.itemId}' — Move/Upgrade/Sell panel path entered.");
            SelectStructure(ps);
        }

        /// <summary>
        /// FIX #1 (2026-07-16) — WEB TAP-SELECT. Resolve a single-pointer TAP (from
        /// UpdateBuildCameraPan's press/release edge) into a selection: raycast from the tap
        /// point (same RaycastGroundAt + GetComponentInParent&lt;PlacedStructure&gt; rule the
        /// idle UpdateSelectLoop uses) and SELECT the structure under it, or CLEAR the current
        /// selection on empty ground. A tap over UI (shop dock / panels) is ignored so a tap
        /// meant for a control never selects the world behind it. Every branch self-names (§12).
        /// </summary>
        private void TryTapSelectAt(Vector2 screenPoint)
        {
            if (IsPointOverUi(screenPoint) || PointerOverPickableUI(screenPoint, out _))
            {
                FlowTrace.Step("Build", "tap-select: miss — tap point is over UI (shop/panel), world select suppressed.");
                return;
            }
            if (!RaycastGroundAt(screenPoint, out RaycastHit hit))
            {
                FlowTrace.Step("Build", $"tap-select: miss — ground raycast missed at {screenPoint}, nothing under the tap.");
                return;
            }
            var ps = hit.collider != null ? hit.collider.GetComponentInParent<PlacedStructure>() : null;
            if (ps != null)
            {
                FlowTrace.Step("Build", $"tap-select: hit — SELECTS '{ps.itemId}' (web pointer path).");
                SelectStructure(ps);
                return;
            }
            FlowTrace.Step("Build", $"tap-select: miss — tap hit '{(hit.collider != null ? hit.collider.gameObject.name : "<no collider>")}' (no PlacedStructure). {(_selected != null ? "Clearing selection." : "Nothing selected.")}");
            if (_selected != null) ClearSelection();
        }

        /// <summary>CREATE mode: the original armed-entry ghost-follow place loop (P1).</summary>
        private void UpdatePlaceLoop()
        {
            if (_ghost == null)
            {
                TraceBlockedWhileArmed("ghost-null",
                    "_ghost is null — no preview object, place loop cannot evaluate");
                return;
            }

            // WO-334 — while the Preview & Rotate panel is open, the placement is in the
            // player's hands (the modal owns confirm/cancel). Freeze the ghost loop so a
            // stray tap behind the modal can't drop a second structure.
            if (_pendingPlace)
            {
                TraceBlockedWhileArmed("pending-place freeze",
                    $"_pendingPlace=true (dormant WO-334 modal path), rotateMenu={(_rotateMenu != null ? (_rotateMenu.gameObject.activeInHierarchy ? "live" : "inactive") : "<null>")} — a stuck _pendingPlace freezes evaluation forever");
                _ghost.Hide(); return;
            }

            // Cancel (right-click / Escape / touch Cancel button / the WO-677 UI latch)
            // backs out the armed entry (keeps build mode open).
            if (CancelRequestedThisFrame())
            {
                FlowTrace.Step("Build", $"PlaceLoop: Cancel intent read — disarming '{_armed?.id}'");
                CancelArmed();
                return;
            }

            // Rotate (Q/E keys / touch ⟲⟳ buttons / on-screen Rotate pair — WO-673 L5 /
            // WO-702) yaws the ghost ±45°/±90° freely in BOTH two-step states: while the
            // ghost still follows the cursor (pre-drop) AND while it is frozen at a
            // pending drop. One yaw state (_armedYawEighths) — order-free by design.
            PollRotateIntents();

            // TWO-STEP: a pending drop exists — the ghost is frozen at the drop point;
            // this frame re-validates/tints there, applies d-pad nudges, and handles
            // re-drop taps + the PLACE commit.
            if (_dropPending)
            {
                UpdateDroppedPlaceLoop();
                return;
            }

            if (!RaycastGround(out RaycastHit hit))
            {
                TraceBlockedWhileArmed("ground-raycast miss",
                    $"ray from '{(_camera != null ? _camera.name : "<null>")}' through screenPoint={_input?.ScreenPoint} hit NOTHING within {_rayDistance}m — PlaceConfirm never evaluated");
                _ghost.Hide();
                return;
            }

            Vector3 snapped = _grid.SnapToGrid(hit.point);

            bool valid = IsValidPlacement(hit, snapped, _armed, out Vector2Int cell, out Vector2Int footprint,
                out BuildRejectReason reason, out float seatY, out bool wallMounted);
            // WO-1252: a DEPTH-full Builder line is a place-time block, shown as the busy
            // next-step sentence (not LineFullMessage — that one must never say "busy").
            string queueBlock = null;
            if (valid && BuilderLineBlocks(out queueBlock))
                valid = false;
            // Preview at the SEAT height (wall-top for a wall-walk mount, else the surface Y) so the
            // ghost shows exactly where the piece lands.
            Vector3 seatSnapped = new Vector3(snapped.x, seatY, snapped.z);
            _ghost.MoveTo(seatSnapped, ArmedYawDegrees);
            _ghost.SetValid(valid);
            // Owner 2026-07-24 "tell me why it's red": surface the already-computed reason on
            // the ghost's silent floating label while blocked (no buzz, no toast spam); clear
            // it when valid. The drop/place taps still pop the buzzing toast (ShowRejectToast).
            _ghost.SetReason(valid ? null : (queueBlock ?? ReasonLabelText(reason)));

            // §12 heartbeat — EVERY gate above passed this frame: the PlaceConfirm latch IS
            // polled below. If the owner clicks and still nothing happens while these LIVE
            // lines flow, the dead link is the input source itself (device state included).
            FlowTrace.Throttle("Build", "placeloop-live", 1f,
                $"PlaceLoop LIVE: armed='{_armed?.id}', ghostValid={valid}{(valid ? "" : $" (reject={reason})")}, input={_input?.GetType().Name}, " +
                $"Mouse.current={(UnityEngine.InputSystem.Mouse.current != null)} — PlaceConfirm poll runs this frame");

            // TWO-STEP (owner ruling 2026-07-13, live felt-test): a world tap DROPS the
            // pending ghost at this cell — NO commit, NO charge (instant-place-on-tap is
            // DEAD). The PLACE latch is the only commit; with no drop yet it commits at
            // the hover cell (desktop convenience — one fewer tap; harmless on touch).
            // Read the confirm intent once (consume-once latch rule).
            ConfirmKind confirm = ConfirmIntentThisFrame();
            if (confirm == ConfirmKind.WorldTap)
            {
                // DROP — freeze the ghost here; rotate/nudge stay free; PLACE commits.
                // Dropping is allowed even on an invalid cell (the ghost tints red and
                // re-validates per frame; the player can nudge it valid) — but the WHY
                // still surfaces immediately (WO-394 spirit).
                _dropPending = true;
                _dropWorldPoint = hit.point;
                BuildFirstUseGuide.GhostMoved();
                _hud?.RefreshFirstUseGuide();
                FlowTrace.Step("Build", $"Two-step DROP: '{_armed?.id}' pending at cell ({cell.x},{cell.y}), " +
                    $"valid={valid}, yaw={ArmedYawDegrees:F0} — rotate/nudge free; PLACE commits.");
                if (!valid) SurfacePlacementBlock(reason, queueBlock);
            }
            else if (confirm == ConfirmKind.UiPlace)
            {
                if (valid)
                {
                // PIVOT (owner decision) — placement is IN-WORLD for ALL types: the
                // ghost shows the model upright (GhostPreview applies the entry's
                // OrientationFix) and the player rotates in-world via Q/E / touch ⟲⟳
                // buttons (±45° steps, WO-673 L5). The old modal Preview & Rotate panel
                // (OpenRotateMenu/OnRotateConfirmed/OnRotateCancelled/_rotateMenu) is
                // dormant — no longer called from placement. Place() derives yawSteps +
                // yawOffset canonically from _armedYawEighths.
                    FlowTrace.Step("Build", $"Two-step COMMIT at HOVER cell ({cell.x},{cell.y}) — PLACE with no prior drop (desktop convenience).");
                    Place(cell, footprint, seatSnapped, wallMounted);
                    // BM-1 (WO-746): a successful commit RETURNS to the carousel. That
                    // teardown now lives at the END of Place() itself (the single return
                    // point for every commit path), so no per-caller disarm is needed here.
                    // Multi-place-in-a-row is a future per-row opt-in (repo.multiPlace, OFF).
                }
                else
                {
                    SurfacePlacementBlock(reason, queueBlock);
                }
            }
        }

        /// <summary>
        /// WO-394 — surface the specific reject reason for the armed entry. For an
        /// unaffordable entry, name the shortfall (e.g. "Not enough Wood") rather than
        /// the generic "Not enough resources".
        /// </summary>
        private void ShowRejectToast(BuildRejectReason reason)
            => BuildFeedbackToast.Show(ReasonLabelText(reason));

        /// <summary>
        /// WO-1252 — DEPTH-full Builder line at place-time. The ghost and the toast quote
        /// <see cref="BuildTimerService.BusyCrewMessage"/> (wait / Manage + any option the
        /// service says the player has). Never recomposes LineFullMessage: that sentence
        /// is DEPTH and must not say "busy".
        /// </summary>
        private bool BuilderLineBlocks(out string why)
        {
            why = null;
            var timer = BuildTimerService.Instance;
            if (timer == null || !timer.IsLineFull(DeNelle.Core.Jobs.ChannelId.Builder))
                return false;
            why = timer.BusyCrewMessage();
            if (string.IsNullOrEmpty(why))
                why = timer.LineFullMessage(DeNelle.Core.Jobs.ChannelId.Builder);
            return true;
        }

        /// <summary>WO-1252 — toast the busy next-step copy when the line blocked, else the footprint reason.</summary>
        private void SurfacePlacementBlock(BuildRejectReason reason, string queueBlock)
        {
            if (!string.IsNullOrEmpty(queueBlock))
                BuildFeedbackToast.Show(queueBlock);
            else
                ShowRejectToast(reason);
        }

        /// <summary>
        /// The player-facing text for a reject reason -- the specialized "Not enough
        /// &lt;Resource&gt; (N)" shortfall for CannotAfford, else the tightened
        /// <see cref="BuildFeedbackToast.MessageFor"/> line. Shared by the buzzing drop/place
        /// toast (<see cref="ShowRejectToast"/>) and the silent floating ghost reason label
        /// (owner 2026-07-24 "tell me why it's red") so both read identically.
        /// </summary>
        private string ReasonLabelText(BuildRejectReason reason)
        {
            if (reason == BuildRejectReason.CannotAfford) return ShortfallMessage(EffectiveCostFor(_armed));

            string text = BuildFeedbackToast.MessageFor(reason);
            // WO-972 — WORDS, NEVER COLOUR ALONE. The owner is red/green colourblind, so the
            // red ghost tint conveys nothing; "Too close to another building" alone did not
            // say WHAT was in the way. The Occupied gates capture the occupant id, so say it.
            if (reason == BuildRejectReason.Occupied && !string.IsNullOrEmpty(_lastRejectDetail))
                text += " - " + _lastRejectDetail + " is already on that square";
            return text;
        }

        /// <summary>
        /// WO-972 — the last Occupied gate's occupant, in player words. Set by the CellGrid /
        /// WorldOverlap rejects in IsValidPlacement and cleared at the top of every evaluation,
        /// so it can never carry a stale name onto a later reject. ASCII only (player-visible).
        /// </summary>
        private string _lastRejectDetail;

        /// <summary>The occupant's catalog display name, falling back to its id, then plain words.</summary>
        private static string OccupantLabel(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "Something";
            var e = CatalogRegistry.Get(itemId);
            return e != null && !string.IsNullOrEmpty(e.displayName) ? e.displayName : itemId;
        }

        /// <summary>
        /// TWO-STEP dropped state (owner ruling 2026-07-13): the pending ghost sits frozen
        /// at <see cref="_dropWorldPoint"/>. Each frame: apply d-pad nudges to the pending
        /// point, re-validate + tint at its snapped cell (costs/occupancy can change while
        /// the player deliberates), handle a world tap as a RE-DROP, and commit ONLY on
        /// the PLACE latch — through the same IsValidPlacement + Place() path as ever.
        /// </summary>
        private void UpdateDroppedPlaceLoop()
        {
            // Read the confirm intent ONCE this frame (consume-once latch rule).
            ConfirmKind confirm = ConfirmIntentThisFrame();

            // A world tap elsewhere RE-DROPS the pending ghost at the new ground point.
            if (confirm == ConfirmKind.WorldTap)
            {
                if (RaycastGround(out RaycastHit tapHit))
                {
                    _dropWorldPoint = tapHit.point;
                    Vector2Int reCell = _grid.WorldToCell(_grid.SnapToGrid(tapHit.point));
                    FlowTrace.Step("Build", $"Two-step RE-DROP: '{_armed?.id}' pending moved to cell ({reCell.x},{reCell.y}), yaw={ArmedYawDegrees:F0}.");
                }
                else
                {
                    FlowTrace.Warn("Build", "Two-step RE-DROP tap missed the ground — pending drop unchanged.");
                }
            }

            // WO-683 — while a drop is pending, the d-pad NUDGES the pending ghost
            // (UpdateBuildCameraPan skips its camera merge in this state).
            NudgePendingDropFromDpad();

            // Re-validate at the pending point via a straight-down ground probe (the
            // cursor ray is irrelevant here — the ghost no longer follows it).
            if (!RaycastGroundBelow(_dropWorldPoint, out RaycastHit hit))
            {
                TraceBlockedWhileArmed("pending-ground miss",
                    $"down-ray at pending point {_dropWorldPoint} hit NOTHING — pending drop invalid");
                _ghost.SetValid(false);
                _ghost.SetReason(ReasonLabelText(BuildRejectReason.BadSurface));   // "tell me why it's red"
                if (confirm == ConfirmKind.UiPlace) ShowRejectToast(BuildRejectReason.BadSurface);
                return;
            }

            Vector3 snapped = _grid.SnapToGrid(hit.point);
            _dropWorldPoint.y = hit.point.y;   // follow terrain height while nudging

            bool valid = IsValidPlacement(hit, snapped, _armed, out Vector2Int cell, out Vector2Int footprint,
                out BuildRejectReason reason, out float seatY, out bool wallMounted);
            string queueBlock = null;
            if (valid && BuilderLineBlocks(out queueBlock))
                valid = false;
            Vector3 seatSnapped = new Vector3(snapped.x, seatY, snapped.z);
            _ghost.MoveTo(seatSnapped, ArmedYawDegrees);
            _ghost.SetValid(valid);
            _ghost.SetReason(valid ? null : (queueBlock ?? ReasonLabelText(reason)));   // silent "why it's red" label

            // §12 heartbeat for the dropped state — mirrors the hover placeloop-live line.
            FlowTrace.Throttle("Build", "placeloop-pending", 1f,
                $"PlaceLoop PENDING: armed='{_armed?.id}', cell=({cell.x},{cell.y}), ghostValid={valid}{(valid ? "" : $" (reject={reason})")}, " +
                $"yaw={ArmedYawDegrees:F0} — PLACE commits, taps re-drop, rotate/nudge free");

            if (confirm == ConfirmKind.UiPlace)
            {
                if (valid)
                {
                    FlowTrace.Step("Build", $"Two-step COMMIT at PENDING cell ({cell.x},{cell.y}) — PLACE latch consumed, routing through Place().");
                    Place(cell, footprint, seatSnapped, wallMounted);
                    // BM-1 (WO-746): the return-to-carousel teardown moved INTO Place() (the
                    // single point covering every commit path), so it is no longer invoked per
                    // caller here — Place() disarms, expands the palette, and flips the HUD to
                    // Browse on a successful commit.
                }
                else
                {
                    SurfacePlacementBlock(reason, queueBlock);
                }
            }
        }

        /// <summary>
        /// Straight-down ground probe at a world point (two-step pending re-validation).
        /// Same mask + retry rules as <see cref="RaycastGroundAt"/> (configured mask
        /// first, then all layers). The ghost's colliders are stripped (GhostPreview),
        /// so the ray can never hit the pending preview itself.
        /// </summary>
        private bool RaycastGroundBelow(Vector3 worldPoint, out RaycastHit hit)
        {
            Vector3 origin = new Vector3(worldPoint.x, worldPoint.y + 50f, worldPoint.z);
            if (Physics.Raycast(origin, Vector3.down, out hit, 200f, _groundMask)) return true;
            return Physics.Raycast(origin, Vector3.down, out hit, 200f, ~0);
        }

        /// <summary>
        /// WO-683 + two-step: while a drop is pending, the kit d-pad vector moves the
        /// PENDING point (camera-relative XZ, clamped to the grid) instead of panning
        /// the camera — "d-pad nudges adjust the pending ghost freely".
        /// </summary>
        /// <summary>
        /// WO-1010 P1: project the live ghost to screen space and hand it to the HUD, with
        /// the validity the ghost was last set to. Cheap and guarded — a missing camera or
        /// ghost simply means no push this frame, and the HUD keeps its last anchor until
        /// SetState drops it.
        /// </summary>
        private void PushGhostAnchorToHud()
        {
            if (_hud == null || _ghost == null) return;
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;

            Vector3 world = _ghost.CurrentPosition;
            Vector3 sp = cam.WorldToScreenPoint(world);
            // Behind the camera projects to a mirrored point that would fling the chips to
            // the wrong side of the screen; skip rather than draw a lie.
            if (sp.z <= 0f) return;

            _hud.TrackGhost(new Vector2(sp.x, sp.y), _ghost.IsValid, _ghost.BlockedReason);
        }

        private void NudgePendingDropFromDpad()
        {
            // WO-1010 P1: the build-owned nudge pad (corner "+" toggle) feeds the SAME
            // pending-drop nudge as the legacy shared-HUD pad. Either source may drive it;
            // the build pad wins when both are live because it is the one the player opened
            // deliberately for this placement.
            Vector2 buildPad = _hud != null ? _hud.NudgeVector : Vector2.zero;
            Vector2 dpad = buildPad.sqrMagnitude > DpadDeadZone * DpadDeadZone
                ? buildPad
                : ReadHudDpadMove();
            if (dpad.sqrMagnitude <= DpadDeadZone * DpadDeadZone) return;
            if (_camera == null) return;
            dpad = Vector2.ClampMagnitude(dpad, 1f);
            Vector3 fwd = _camera.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = _camera.transform.right; right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            _dropWorldPoint += (right * dpad.x + fwd * dpad.y) *
                (_camPanSpeed * PendingNudgeScale * Time.unscaledDeltaTime);
            // Clamp to the grid (map) bounds — mirrors the camera-focus clamp.
            if (_grid != null)
            {
                float halfW = _grid.gridWidth  * _grid.cellSize * 0.5f;
                float halfH = _grid.gridHeight * _grid.cellSize * 0.5f;
                Vector3 mapCentre = _grid.origin + new Vector3(halfW, 0f, halfH);
                _dropWorldPoint.x = Mathf.Clamp(_dropWorldPoint.x, mapCentre.x - halfW, mapCentre.x + halfW);
                _dropWorldPoint.z = Mathf.Clamp(_dropWorldPoint.z, mapCentre.z - halfH, mapCentre.z + halfH);
            }
            FlowTrace.Throttle("Build", "pending-nudge", 0.5f,
                $"pending ghost NUDGED by d-pad {dpad} -> {_dropWorldPoint}");
        }

        /// <summary>
        /// FIX #2 (2026-07-16) — the combined MOVE nudge axis: keyboard WASD/arrows PLUS the
        /// kit d-pad (ReadHudDpadMove). While moving a selected structure these NUDGE the move
        /// ghost (UpdateMoveLoop) instead of panning the camera (UpdateBuildCameraPan zeroes the
        /// SAME sources when _movingSelected). x = strafe, y = forward; the caller rotates it
        /// into camera space. Zero on desktop when no key/pad is pressed.
        /// </summary>
        private static Vector2 ReadMoveNudgeAxis()
        {
            Vector2 v = Vector2.zero;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  v.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  v.x -= 1f;
            }
            Vector2 dpad = ReadHudDpadMove();
            if (dpad.sqrMagnitude > DpadDeadZone * DpadDeadZone) v += dpad;
            return v;
        }

        /// <summary>
        /// FIX #2 — clamp the MOVE target to the grid (map) bounds so a key/d-pad nudge can't
        /// walk the ghost off the world. Mirrors the pending-drop and camera-focus clamps.
        /// </summary>
        private void ClampMoveWorldToGrid()
        {
            if (_grid == null) return;
            float halfW = _grid.gridWidth  * _grid.cellSize * 0.5f;
            float halfH = _grid.gridHeight * _grid.cellSize * 0.5f;
            Vector3 mapCentre = _grid.origin + new Vector3(halfW, 0f, halfH);
            _moveWorldPoint.x = Mathf.Clamp(_moveWorldPoint.x, mapCentre.x - halfW, mapCentre.x + halfW);
            _moveWorldPoint.z = Mathf.Clamp(_moveWorldPoint.z, mapCentre.z - halfH, mapCentre.z + halfH);
        }

        // NOTE (WO-855): the rotate-panel tower check used to live here as a narrower
        // private duplicate (type == Tower only). It now shares the ONE classifier
        // declared further down -- see IsTowerEntry. That version is a strict superset
        // (same type check, plus a repo.behaviorId fallback), so a row that is a tower
        // but omitted its type now correctly gets the rotate panel too.

        /// <summary>
        /// WO-334 — open the Preview &amp; Rotate panel for the armed tower at a validated
        /// cell. Stashes the placement target in the _pending* fields (which freezes the
        /// ghost loop), seeds the panel with the ghost's current free-rotate yaw, and on
        /// confirm maps the chosen Quaternion → yaw offset/steps then commits via Place();
        /// on cancel the entry stays armed (nothing placed). The preview model is the
        /// armed entry's visual prefab loaded from Resources (CatalogEntry carries a PATH,
        /// not a TowerData SO — see the prefab Open() overload).
        /// </summary>
        private void OpenRotateMenu(Vector2Int cell, Vector2Int footprint, Vector3 snapped)
        {
            if (_armed == null) return;

            _pendingPlace     = true;
            _pendingCell      = cell;
            _pendingFootprint = footprint;
            _pendingSnapped   = snapped;

            EnsureRotateMenu();

            // Addressables-first via the StructureAssetLoader seam (2026-08-17). Behaviour is
            // IDENTICAL while the art still sits in Resources — the loader falls back to exactly
            // this Resources.Load — so pointing the call sites here is a no-op today and the
            // precondition for moving the 62.5 MB force-included folder out of the build.
            GameObject prefab = DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(_armed.visualPrefabPath);
            string name = !string.IsNullOrEmpty(_armed.displayName) ? _armed.displayName : _armed.id;
            double costSkr = EffectiveCostFor(_armed).crystals;   // freebie-aware (0 while the first-build is live)

            // Seed the panel from the ghost's current rotate yaw, rounded down to the
            // quarter-step the (dormant) panel understands.
            int initialYawSteps = ArmedYawQuarterSteps;

            // WO-335 FIX — forward the armed entry's upright correction so the preview
            // matches the placed result (StructureFactory applies entry.orientation at
            // build time; the preview must apply the SAME correction or towers show
            // sideways in the panel but stand up when placed).
            Debug.Log($"[Orient] open: id={_armed.id} prefab={(prefab != null ? prefab.name : "<none>")} yaw0={initialYawSteps * 90} orient={(_armed.orientation != null && _armed.orientation.manual ? "manual" : "none")}");
            _rotateMenu.Open(prefab, name, costSkr, initialYawSteps, OnRotateConfirmed, OnRotateCancelled, _armed.orientation);
        }

        /// <summary>
        /// WO-335 confirm callback — the player committed a yaw step (0..3 → 0/90/180/270°).
        /// Map it onto the shared eighth-step yaw state the place path commits from
        /// (_armedYawEighths — WO-673 L5), then commit at the pending cell.
        /// </summary>
        private void OnRotateConfirmed(int yawSteps)
        {
            // WO-673 L5 — map the quarter-step onto the eighth-step state; Place() derives
            // yawSteps/yawOffset canonically from it, which also retires the latent
            // double-rotation this dormant path used to write (yawOffset = yawSteps*90 ON
            // TOP of yawSteps — replayed as 2× the chosen yaw).
            _armedYawEighths = (yawSteps & 3) * 2;
            Debug.Log($"[Orient] confirmed: yawSteps={ArmedYawQuarterSteps} yaw={ArmedYawDegrees:F0} cell={_pendingCell}");

            _pendingPlace = false;
            // Dormant modal path (no longer called from placement) — ground placement only.
            Place(_pendingCell, _pendingFootprint, _pendingSnapped, false);
        }

        /// <summary>WO-334 cancel callback — keep the entry armed; nothing is placed.</summary>
        private void OnRotateCancelled()
        {
            _pendingPlace = false;
            Debug.Log("[Orient] cancelled (entry stays armed).");
            // Entry stays armed so the player can re-position / re-confirm or cancel out.
        }

        /// <summary>
        /// MOVE mode: re-place the selected structure with a ghost seeded from its own
        /// entry + yaw. Its origin cells are already FREE (released on enter) so it
        /// never blocks itself. A valid tap re-occupies the new cells + moves the
        /// object + syncs BaseLayout; right-click/Escape cancels back to the origin.
        /// </summary>
        private void UpdateMoveLoop()
        {
            if (_ghost == null || _selected == null) { CancelMove(); return; }

            if (CancelRequestedThisFrame())   // WO-677 — UI latch honored in MOVE too
            {
                CancelMove();
                return;
            }

            PollRotateIntents();   // WO-673 L5 — ±45° rotate, free during a move too

            // FIX #2 (2026-07-16) — the move ghost follows EITHER the pointer OR the arrow
            // keys / kit d-pad. A key/d-pad press NUDGES _moveWorldPoint (camera-relative
            // grid-step, mirrors NudgePendingDropFromDpad) and the view holds still; when no
            // nudge is pressed the pointer drives it (desktop hover, or an ACTIVE touch press
            // on mobile/web — a released finger no longer stomps the nudge back). The nudge
            // persists in _moveWorldPoint between frames so fine positioning sticks.
            Vector2 nudge = ReadMoveNudgeAxis();
            bool hasNudge = nudge.sqrMagnitude > DpadDeadZone * DpadDeadZone;
            if (hasNudge && _camera != null)
            {
                nudge = Vector2.ClampMagnitude(nudge, 1f);
                Vector3 nfwd = _camera.transform.forward; nfwd.y = 0f;
                nfwd = nfwd.sqrMagnitude > 1e-4f ? nfwd.normalized : Vector3.forward;
                Vector3 nright = _camera.transform.right; nright.y = 0f;
                nright = nright.sqrMagnitude > 1e-4f ? nright.normalized : Vector3.right;
                _moveWorldPoint += (nright * nudge.x + nfwd * nudge.y) *
                    (_camPanSpeed * PendingNudgeScale * Time.unscaledDeltaTime);
                ClampMoveWorldToGrid();
                FlowTrace.Throttle("Build", "move-nudge", 0.5f,
                    $"move ghost NUDGED by keys/d-pad {nudge} -> {_moveWorldPoint} (camera held)");
            }
            else
            {
                // Pointer follow: desktop hover always, but on a touchscreen only while a press
                // is active (else a released finger's last point would stomp the nudge).
                bool touchActive = UnityEngine.InputSystem.Touchscreen.current != null;
                var ptr = UnityEngine.InputSystem.Pointer.current;
                bool pointerActive = !touchActive || (ptr != null && ptr.press.isPressed);
                if (pointerActive && RaycastGround(out RaycastHit ptrHit))
                    _moveWorldPoint = ptrHit.point;
            }

            // Resolve the ground surface at the (possibly nudged) target via a straight-down
            // probe — supplies the surface hit/normal IsValidPlacement needs even on a frame
            // driven purely by keys (no pointer ray). Off-map -> hide the ghost.
            if (!RaycastGroundBelow(_moveWorldPoint, out RaycastHit hit))
            {
                _ghost.Hide();
                return;
            }

            Vector3 snapped = _grid.SnapToGrid(hit.point);

            // Affordability is irrelevant for a move (free) — validate placement only. Capture the
            // seat height + wall-mount so a moved wall-walk defense keeps sitting on the wall TOP.
            bool valid = IsValidPlacement(hit, snapped, CatalogRegistry.Get(_selected.itemId),
                out Vector2Int cell, out Vector2Int footprint, out BuildRejectReason reason,
                out float seatY, out bool wallMounted, ignoreCost: true);
            Vector3 seatSnapped = new Vector3(snapped.x, seatY, snapped.z);
            _ghost.MoveTo(seatSnapped, ArmedYawDegrees);
            _ghost.SetValid(valid);

            // Read the confirm intent ONCE this frame (consume-once latch rule) and act on the
            // KIND, exactly like UpdatePlaceLoop. Only the UI PLACE latch commits.
            ConfirmKind confirm = ConfirmIntentThisFrame();
            if (confirm == ConfirmKind.UiPlace)
            {
                if (valid) CommitMove(cell, footprint, seatSnapped, wallMounted);
                else BuildFeedbackToast.Show(reason);   // WO-394 — say why the move can't land
            }
            // A WorldTap only RE-AIMS (_moveWorldPoint already tracks the finger above). The
            // PLACE latch is the only commit -- same ruling as UpdatePlaceLoop
            // (instant-place-on-tap is dead). Before this, any tap meant to aim COMMITTED the
            // move, which is why Cancel appeared to do nothing: the move had already landed.
        }

        /// <summary>
        /// Combined validity: flat upward surface, footprint cells free + in-bounds,
        /// gate-lane clearance, and affordable. Pure over grid + config apart from
        /// the surface raycast hit (which the caller supplies). Thin wrapper over the
        /// reason-aware overload (discards the reason) for callers that only need the
        /// bool (the ghost-tint frame loop).
        /// </summary>
        private bool IsValidPlacement(RaycastHit hit, Vector3 snapped, CatalogEntry entry,
            out Vector2Int cell, out Vector2Int footprint, bool ignoreCost = false)
            => IsValidPlacement(hit, snapped, entry, out cell, out footprint, out _, out _, out _, ignoreCost);

        /// <summary>Convenience overload that discards the seat-height / wall-mount outputs.</summary>
        private bool IsValidPlacement(RaycastHit hit, Vector3 snapped, CatalogEntry entry,
            out Vector2Int cell, out Vector2Int footprint, out BuildRejectReason reason, bool ignoreCost = false)
            => IsValidPlacement(hit, snapped, entry, out cell, out footprint, out reason, out _, out _, ignoreCost);

        /// <summary>
        /// WO-394 — the reason-aware validity gate. Same rules as before, but on rejection
        /// it reports WHICH gate failed (<paramref name="reason"/>) so a rejected CLICK can
        /// surface a specific message ("No space here", "Not enough resources", …) instead
        /// of failing silently. The rules themselves are unchanged — only the reason is new.
        /// </summary>
        private bool IsValidPlacement(RaycastHit hit, Vector3 snapped, CatalogEntry entry,
            out Vector2Int cell, out Vector2Int footprint, out BuildRejectReason reason,
            out float seatY, out bool wallMounted, bool ignoreCost = false)
        {
            reason = BuildRejectReason.Generic;
            // Seat height defaults to the raycast/grid plane Y; the wall-walk branch below raises
            // it to the wall TOP. wallMounted stays false for ground placements (unchanged path).
            seatY = snapped.y;
            wallMounted = false;
            cell = _grid.WorldToCell(snapped);
            // FIX (footprint from CORRECTED bounds) — measure the UPRIGHT, OrientationFix-
            // applied mesh (the SAME geometry the ghost + the placed structure use) so the
            // validity footprint matches what lands, letting pieces sit tight to a wall.
            // WO-673 L5 — the claim is ROTATION-HONEST: at the armed yaw's diagonal steps
            // the rotated mesh's world AABB grows ×√2, so the claimed cells inflate to
            // cover it (PlacementGrid.FootprintCells yaw overload; ×1 at cardinals —
            // byte-identical to the legacy claim). Under-claiming at 45° was the
            // placement-lies bug the architecture review vetoed (G-F).
            // WO-972 — the claim metric, not the raw mesh measure. Identical for every row
            // EXCEPT a Wall, whose claim comes off the authored placement.footprint so a
            // 3.03 m palisade on a 3.00 m cell stays a ONE-CELL tile instead of squaring up
            // into the 2x2 block that rejected the neighbouring cell (F8 seq 2327).
            // WO-986: CoC non-square claim (x,z) + yaw AABB — not max-axis square.
            footprint = _grid.FootprintCells(StructureFactory.MeasureClaimFootprintXZ(entry), ArmedYawDegrees);
            _lastRejectDetail = null;   // per-evaluation; only an Occupied gate below sets it

            // SURFACE ROLE (data-driven, PlacementRules.mustSitOn) — a WallWalk defense MUST seat
            // on a wall TOP (defensive posture); everything else is a flat-ground placement. The
            // rules live on the catalog row (entry.repo.placement); null = the legacy Ground path.
            var rules = entry != null && entry.repo != null ? entry.repo.placement : null;
            bool needsWallWalk = rules != null && rules.mustSitOn == PlacementSurface.WallWalk;
            // Find a WallSegment under the cursor (the placement ray hits ~all layers, so it CAN
            // hit the wall collider). The structure seats on the wall's walk-top, NOT the hit point.
            WallSegment supportingWall = hit.collider != null
                ? hit.collider.GetComponentInParent<WallSegment>() : null;

            // 1. Surface check. (TowerPlacementSystem.IsValidSurface rule.)
            if (hit.collider == null) { reason = BuildRejectReason.BadSurface; return false; }
            if (needsWallWalk)
            {
                // A wall-walk defense REQUIRES a wall to sit on. No wall under the cursor → reject
                // (this is the data-driven gate that was DATA-ONLY before — nothing read mustSitOn).
                if (supportingWall == null) { reason = BuildRejectReason.BadSurface; return false; }
                // Seat on the wall TOP. WallSegment's collider is base-pivoted (center.y = Height/2,
                // size.y = Height), so the walk-top = wall.transform.position.y + Height. Skip the
                // flat-normal check here: even a hit on the wall SIDE seats correctly on the top.
                seatY = supportingWall.transform.position.y + supportingWall.Height;
                wallMounted = true;
            }
            else
            {
                // GROUND path — require a flat, upward-facing top, reject tower/building tops.
                if (hit.normal.y < 0.85f) { reason = BuildRejectReason.BadSurface; return false; }
                if (hit.collider.CompareTag("Tower") || hit.collider.CompareTag("Building"))
                {
                    FlowTrace.Warn("Build",
                        $"REJECT Occupied cell=({cell.x},{cell.y}) gate=SurfaceTag — the placement ray hit " +
                        $"'{hit.collider.name}' tag='{hit.collider.tag}' (a structure top, not ground).");
                    reason = BuildRejectReason.Occupied; return false;
                }

                // #76: GROUND is the area DIRECTLY BELOW placement, NOT whatever the camera ray grazed.
                // The camera ray only AIMS; near a corner it can strike a wall TOP the cursor merely
                // passed over, divorcing seatY (wall height) from the snapped XZ (floor cell) so the
                // tower floats at wall height. Re-probe straight DOWN through the committed footprint XZ
                // and seat on THAT surface. Dynamic + relative: floor cell -> floor Y, rampart cell ->
                // rampart top, tier-3 roof -> roof Y (so building UP works); a wall the cursor merely
                // grazed is not below the footprint, so it can't contaminate the height.
                if (TryResolveGroundYBelow(snapped, out float groundY))
                    seatY = groundY;
            }

            // 2. Footprint cells in-bounds (always). Occupancy is skipped for a wall-walk mount —
            //    the SUPPORTING wall legitimately occupies those cells, so CanPlace would always
            //    reject. (During a MOVE the structure's own cells were freed on enter.) V1 caveat:
            //    bypassing occupancy lets two wall-towers target the same cell; the world-overlap
            //    test below still rejects tower-on-tower (the prior tower is not excluded).
            if (!_grid.InBounds(cell, footprint)) { reason = BuildRejectReason.OutOfBounds; return false; }
            if (!wallMounted && !_grid.CanPlace(cell, footprint))
            {
                // CanPlace scans the WHOLE footprint, so the blocker is often not the origin
                // cell. Walk the same dx/dz range CanPlace walks and name the FIRST occupied
                // cell + its occupant id, so the trace points at the cell that actually
                // rejected instead of reading the origin (usually free) and printing nothing.
                int fw = Mathf.Max(1, footprint.x);
                int fh = Mathf.Max(1, footprint.y);
                string occupant = null;
                Vector2Int occupantCell = cell;
                for (int dx = 0; dx < fw && occupant == null; dx++)
                {
                    for (int dz = 0; dz < fh; dz++)
                    {
                        var probe = new Vector2Int(cell.x + dx, cell.y + dz);
                        string id = _grid.OccupantAt(probe);
                        if (string.IsNullOrEmpty(id)) continue;
                        occupant = id;
                        occupantCell = probe;
                        break;
                    }
                }
                FlowTrace.Warn("Build",
                    $"REJECT Occupied cell=({cell.x},{cell.y}) fp=({footprint.x}x{footprint.y}) gate=CellGrid " +
                    $"occupantCell=({occupantCell.x},{occupantCell.y}) occupant='{occupant ?? "<none>"}'.");
                // WORDS, never colour alone (the owner is red/green colourblind, so a red
                // ghost tint carries NO information for her): name the thing that owns the
                // square. The trace already knew; the player was told nothing but a hue.
                _lastRejectDetail = OccupantLabel(occupant);
                reason = BuildRejectReason.Occupied; return false;
            }

            // The footprint's world-space AABB on the XZ plane (cellSize × footprint
            // cells, centred on the snapped cell-block). Used by the world-overlap +
            // gate-clearance tests below, which catch SCENE objects the cell grid does
            // not track (gates + the default-village structures are placed by the
            // scene builder, never Occupy()'d — so CanPlace can't see them).
            Bounds footprintAabb = FootprintWorldBounds(cell, footprint, seatY);

            // 3. World overlap — reject if the footprint overlaps an existing placed
            //    structure or a gate collider in the scene (NOT just the cell grid). For a
            //    wall-walk mount, EXCLUDE the supporting wall (the tower sits ON it by design);
            //    any OTHER overlapping structure still rejects.
            GameObject ignore = supportingWall != null
                ? supportingWall.GetComponentInParent<PlacedStructure>()?.gameObject : null;
            // WO-972 — a WALL may ABUT a wall. Wall bodies are authored slightly wider than
            // their cell (wall_wood's collider dumped 3.03 m across a 3.00 m cell), so with the
            // one-cell claim above two neighbouring segments overlap by centimetres BY DESIGN —
            // that is what makes a run continuous rather than a dashed line of 3 m holes. The
            // strict AABB test would read those centimetres as "occupied" and simply move the
            // reject from gate=CellGrid to gate=WorldOverlap, so wall-on-wall is excluded here.
            // NOT a hole in the rule: the CELL GRID above still refuses two walls on the SAME
            // square, and gates keep their own spawn-to-Heart lane test (step 4, untouched).
            bool armedIsWall = entry != null && entry.type == CatalogType.Wall;
            if (OverlapsExistingStructure(footprintAabb, ignore, armedIsWall, out var blocker, out var blockerBounds))
            {
                // The proving line (F8 2026-07-30): separates the three Occupied gates and
                // names the occupant + its bounds — an inflated renderer-bounds blocker
                // (stray particle/effect child) shows up here as bounds far larger than
                // the visible body.
                FlowTrace.Warn("Build",
                    $"REJECT Occupied cell=({cell.x},{cell.y}) fp=({footprint.x}x{footprint.y}) " +
                    $"aabb c={footprintAabb.center} s={footprintAabb.size} gate=WorldOverlap " +
                    $"blocker='{blocker.name}' id='{blocker.itemId}' bounds c={blockerBounds.center} s={blockerBounds.size} " +
                    $"| gridOccupant='{_grid.OccupantAt(cell) ?? "<none>"}'.");
                _lastRejectDetail = OccupantLabel(blocker.itemId);   // words, never colour alone
                reason = BuildRejectReason.Occupied; return false;
            }

            // 4. Gate-lane clearance — never wall off the spawn→Heart corridor. Tests
            //    the whole footprint AABB against each gate's real bounds (expanded by
            //    the clearance), so a structure whose body overlaps the gate is caught
            //    even when its origin cell is just outside the radius.
            if (_gateClearance > 0f && IsTooCloseToGate(footprintAabb)) { reason = BuildRejectReason.BlocksGate; return false; }

            // 5. Affordable from the persisted multi-resource ledger (EconomyService —
            //    the GameState-backed Wood/Food/Iron/Crystals surface). Crystals-only
            //    entries fall back to a Crystals cost. A move is free, so the cost gate
            //    is skipped for it. EffectiveCostFor: a live first-build freebie is a
            //    zero cost, so the ghost/validator agrees with the Place() commit.
            if (!ignoreCost)
            {
                DeNelle.Core.Catalog.ResourceCost cost = EffectiveCostFor(entry);
                if (!CanAfford(cost)) { reason = BuildRejectReason.CannotAfford; return false; }
            }

            return true;
        }

        /// <summary>
        /// The world-space XZ bounding box of a footprint at <paramref name="cell"/>:
        /// the block of <paramref name="footprint"/> cells, each cellSize wide, with a
        /// thin Y extent at the placement height. Pure over the grid. This is the
        /// real area the structure will occupy, so the world-overlap + gate tests can
        /// reason about the whole structure, not just its origin cell centre.
        /// </summary>
        private Bounds FootprintWorldBounds(Vector2Int cell, Vector2Int footprint, float y)
        {
            float cs = _grid != null ? _grid.cellSize : 3f;
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);

            // Min/max cell corners → world. CellToWorld returns the cell CENTRE, so
            // expand by half a cell on each side to cover the full block.
            Vector3 minC = _grid.CellToWorld(new Vector2Int(cell.x, cell.y));
            Vector3 maxC = _grid.CellToWorld(new Vector2Int(cell.x + fw - 1, cell.y + fh - 1));
            Vector3 min = new Vector3(Mathf.Min(minC.x, maxC.x) - cs * 0.5f, y - 0.5f, Mathf.Min(minC.z, maxC.z) - cs * 0.5f);
            Vector3 max = new Vector3(Mathf.Max(minC.x, maxC.x) + cs * 0.5f, y + 0.5f, Mathf.Max(minC.z, maxC.z) + cs * 0.5f);

            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        /// <summary>
        /// True when the placement footprint overlaps a structure already in the scene
        /// (player-placed OR default-village) on the XZ plane. The cell grid only
        /// tracks structures that were Occupy()'d (loader + player placements); the
        /// default-village buildings + gates are scene objects the grid never saw, so
        /// CanPlace alone cannot reject overlapping them. We test the footprint AABB
        /// against every live PlacedStructure's renderer bounds — using RENDERER bounds
        /// (centre + extents in world) sidesteps the scaled-pivot displacement trap
        /// where transform.position sits far from the visible mesh. During a MOVE the
        /// selected structure is excluded so it never blocks its own re-placement.
        /// </summary>
        private bool OverlapsExistingStructure(Bounds footprintAabb, GameObject ignore = null)
            => OverlapsExistingStructure(footprintAabb, ignore, false, out _, out _);

        /// <summary>Back-compat overload (no wall-abut allowance) for the reason-aware callers.</summary>
        private bool OverlapsExistingStructure(Bounds footprintAabb, GameObject ignore,
            out PlacedStructure blocker, out Bounds blockerBounds)
            => OverlapsExistingStructure(footprintAabb, ignore, false, out blocker, out blockerBounds);

        // F8 2026-07-30 (anonymous Occupied storm): out-param variant NAMES the blocker +
        // its bounds so the reject trace/toast can say WHAT occupies the spot — the owner
        // hit reject=Occupied on cell after cell with nothing in the log naming the cause.
        private bool OverlapsExistingStructure(Bounds footprintAabb, GameObject ignore,
            bool armedIsWall, out PlacedStructure blocker, out Bounds blockerBounds)
        {
            blocker = null;
            blockerBounds = default;
            var all = FindObjectsByType<PlacedStructure>();
            foreach (var ps in all)
            {
                if (ps == null) continue;
                if (_movingSelected && ps == _selected) continue;   // don't self-block a move
                if (ignore != null && ps.gameObject == ignore) continue;   // wall-walk: tower sits ON the supporting wall
                // WO-972 — wall abuts wall by design (see the call site). The WallSegment probe
                // is the same component BaseLayoutLoader uses to identify a placed wall, so a
                // replayed wall and a freshly placed one are treated identically.
                if (armedIsWall && ps.GetComponentInChildren<WallSegment>(true) != null) continue;

                Bounds wb;
                if (!TryWorldBounds(ps.gameObject, out wb)) continue;
                if (OverlapsXZ(footprintAabb, wb)) { blocker = ps; blockerBounds = wb; return true; }
            }
            return false;
        }

        /// <summary>
        /// True when the footprint AABB intrudes on a gate's spawn→Heart LANE. TUNED
        /// (build-mode playtest): the old test expanded each gate's WHOLE bounds by the
        /// clearance on every side, which walled off a broad block around the gate and
        /// blocked legitimate placement along the adjacent wall. Now it guards only the
        /// actual gate OPENING — a narrow lane the width of the gate, running INWARD
        /// toward the Heart (0,0,0) and a short way OUTWARD toward the spawn — so a piece
        /// can sit right next to the gate as long as it doesn't block the doorway the
        /// enemies path through. Clearance is a small pad on the lane (default 3 m), not a
        /// radius around the gate. Uses renderer/collider bounds (displacement-trap safe).
        /// </summary>
        private bool IsTooCloseToGate(Bounds footprintAabb)
        {
            var gates = FindObjectsByType<Gate>();
            foreach (var gate in gates)
            {
                if (gate == null) continue;
                Bounds gb;
                if (!TryWorldBounds(gate.gameObject, out gb)) gb = new Bounds(gate.transform.position, Vector3.one);

                // The lane runs along the gate→Heart axis (the spawn→Heart corridor). The
                // gate's SHORTER horizontal span is the opening WIDTH; the lane keeps that
                // width (+ a small pad) and extends along the through-axis so it covers the
                // doorway the enemies walk, NOT the whole wall the gate sits in.
                Vector3 c = gb.center;
                Vector3 toHeart = new Vector3(-c.x, 0f, -c.z);   // Heart is at origin
                bool throughIsX = Mathf.Abs(toHeart.x) >= Mathf.Abs(toHeart.z);

                float openWidth = throughIsX ? gb.size.z : gb.size.x;   // span across the opening
                float halfWidth = openWidth * 0.5f + _gateClearance;
                float laneReach = 6f + _gateClearance;                   // how far the lane guards in/out

                Bounds lane = new Bounds(c, Vector3.one);
                if (throughIsX)
                    lane.SetMinMax(
                        new Vector3(c.x - laneReach, c.y - 0.5f, c.z - halfWidth),
                        new Vector3(c.x + laneReach, c.y + 0.5f, c.z + halfWidth));
                else
                    lane.SetMinMax(
                        new Vector3(c.x - halfWidth, c.y - 0.5f, c.z - laneReach),
                        new Vector3(c.x + halfWidth, c.y + 0.5f, c.z + laneReach));

                if (OverlapsXZ(footprintAabb, lane)) return true;
            }
            return false;
        }

        /// <summary>
        /// World-space renderer bounds of <paramref name="go"/> (centre + extents in
        /// world). Renderer bounds are robust to the scaled/off-pivot mesh trap; falls
        /// back to a collider, then false when neither exists.
        /// </summary>
        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null) return false;
            // PARTICLE EXCLUSION (F8 2026-07-30 Occupied storm): structures host looping
            // aura/construction VFX PARENTED under their root, and those world-simulated
            // ParticleSystemRenderers carry enormous bounds — captured VIS dump: 12.5-31.2m
            // renderer bounds on a 5m tower. Encapsulating them made ONE aura-bearing
            // structure block a ~15m radius of the build grid ("the layout seems to have
            // shifted" — it truly shrank). Footprint = the SOLID body only; the mesh-renderer
            // reasoning (scaled-pivot displacement trap) never applied to particles.
            var rends = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            if (rends != null)
            {
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null || r is ParticleSystemRenderer) continue;
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                }
            }
            if (any) return true;
            var col = go.GetComponentInChildren<Collider>(true);
            if (col != null) { bounds = col.bounds; return true; }
            return false;
        }

        /// <summary>True when two bounds overlap on the XZ plane (Y is ignored — placement is a flat planner).</summary>
        private static bool OverlapsXZ(Bounds a, Bounds b)
        {
            return a.min.x < b.max.x && a.max.x > b.min.x
                && a.min.z < b.max.z && a.max.z > b.min.z;
        }

        /// <summary>
        /// Commit a placement: build via StructureFactory, occupy the grid, CHARGE the
        /// persisted crystal wallet (only here, after the valid commit — WO-131), add
        /// the PlacedStructure marker, and append to the live BaseLayout. The entry
        /// stays armed so the player can place several in a row (CoC behaviour).
        /// </summary>
        private void Place(Vector2Int cell, Vector2Int footprint, Vector3 snapped, bool wallMounted = false)
        {
            FlowTrace.Step("Build", $"Place() — tower spawn (id='{_armed?.id}', cell=({cell.x},{cell.y}))");
            // Re-check affordability AT commit through the same ledger the validity gate
            // used — never spawn if the player can't pay (defensive: balance may have
            // changed since the ghost frame). Charge ONLY after this, per WO-131.
            // First-build freebie (owner 2026-07-13): read the flag ONCE here so the
            // affordability gate, the charge and the consumption below all agree.
            bool freeBuild = FreeBuildAvailable(_armed);
            // WO-855: charge through the SAME resolver the ghost validator + palette price with
            // (SoftcappedCostFor = CostFor + the tower spam softcap). Behaviour-neutral for every
            // non-tower row and for the first four towers; from the 5th tower on, the commit now
            // charges the surcharge the player was already shown, instead of the flat base cost.
            DeNelle.Core.Catalog.ResourceCost cost = freeBuild ? default : SoftcappedCostFor(_armed);
            if (!CanAfford(cost))
            {
                Debug.Log($"[BuildMode] Not enough resources to place '{_armed.id}' — placement aborted.");
                return;
            }
            // WO-911 (M1, ruling Q4) — BUILDER LINE DEPTH GATE, checked AT COMMIT, BEFORE the spawn
            // and BEFORE the charge. This is the right seam: refusing further down (at the
            // StartBuild call ~180 lines below) would leave a structure standing with no timer, and
            // the charge in this method already lands after loader.Spawn (a known ordering debt
            // documented at the ChargeLedger site). Refusing here costs the player nothing.
            {
                var timerForCap = BuildTimerService.Instance;
                if (timerForCap != null && timerForCap.IsLineFull(DeNelle.Core.Jobs.ChannelId.Builder))
                {
                    // WO-1252: quote the service's busy next-step copy. Do NOT recompose
                    // LineFullMessage here — that sentence is DEPTH and must never say "busy".
                    string why = timerForCap.BusyCrewMessage();
                    if (string.IsNullOrEmpty(why))
                        why = timerForCap.LineFullMessage(DeNelle.Core.Jobs.ChannelId.Builder);
                    BuildFeedbackToast.Show(why);
                    FlowTrace.Warn("BuildMode", $"Place refused for '{_armed.id}' — {why}");
                    CancelArmed();
                    return;
                }
            }

            // WO-707 singleton gate, re-checked AT COMMIT (defensive — the arm gate could
            // be stale if a placement landed between arm and click).
            if (SingletonAlreadyBuilt(_armed))
            {
                BuildFeedbackToast.Show(BuildRejectReason.Singleton);
                CancelArmed();
                return;
            }

            var loader = BaseLayoutLoader.EnsureExists();
            // F8-39 (towers vanish on death, ALL return on next placement): prove this path adds
            // exactly ONE structure (loader.Spawn), NOT a full refresh. The live loaded count
            // BEFORE the add is logged here and AFTER below; if the count jumps by MORE than 1 on a
            // single place, a hidden mass-rebuild is riding the placement (the "all reappear" half).
            FlowTrace.Step("BaseLayout",
                $"Place: committing ONE structure add — loader.Loaded count BEFORE spawn = {loader.Loaded.Count} " +
                $"(scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}').");
            // Persist the SEAT height (snapped.y — wall-top for a wall-walk mount, else the surface
            // Y) + the wall-mounted flag so the piece reloads on the wall TOP (not y=0) and the
            // loader re-applies the elevation perk. worldY defaults 0 / wallMounted false for ground.
            // WO-673 L5 — the 45° facing rides the EXISTING schema: quarter-steps in
            // yawSteps + the odd 45° half-step in yawOffset (replayed as yawSteps*90 +
            // yawOffset by BaseLayoutLoader.Spawn — no schema change, old saves untouched).
            var data = new PlacedStructureData(_armed.id, cell.x, cell.y, ArmedYawQuarterSteps, 1,
                ArmedYawOffsetDeg, snapped.y, wallMounted);

            var ps = loader.Spawn(data, _grid);
            if (ps == null)
            {
                // U + R: the placement produced no structure — the player tapped to build and
                // nothing appeared. Fail-loud (not a swallowed Warn) so the dead placement seam
                // self-reports the entry id; we correctly DON'T charge.
                FlowTrace.Fail("BuildMode",
                    $"Place: BaseLayoutLoader.Spawn returned null for '{_armed.id}' at cell ({cell.x},{cell.y}) — " +
                    "structure NOT placed, player NOT charged.");
                return;
            }

            // F8 2026-07-13 "lumbermill came in damaged": a FRESH placement is a NEW
            // building — reset any collector's PlayerPrefs-persisted HP (keyed by
            // buildingId only, so it outlives demolish/rebuild and even New Game).
            // Fresh-placement path ONLY — reload replay (BaseLayoutLoader on load)
            // keeps standing damage for the repair loop.
            Guard.Try("BuildMode", "fresh-placement collector HP reset", () =>
            {
                var col = ps.GetComponentInChildren<DeNelle.Village.Buildings.Progression.ResourceCollector>(true);
                if (col != null) col.ResetToFullHp();
            });

            // V(erify the placed structure renders): a spawned-but-invisible structure reads as a
            // failed build to the player even though we charged + occupied the grid. Warn (skip-not-
            // abort: the placement is committed) if it carries no enabled Renderer, so a capture
            // splits "didn't spawn" (the Fail above) from "spawned invisible".
            if (ps.GetComponentInChildren<Renderer>(true) == null)
                FlowTrace.Warn("BuildMode",
                    $"Place: placed structure '{_armed.id}' at cell ({cell.x},{cell.y}) has NO Renderer — " +
                    "it will be INVISIBLE (placement committed; check the StructureFactory prefab).");

            // F8-39: loaded count AFTER the single Spawn. A delta of exactly +1 vs the BEFORE line
            // confirms placement adds ONE piece; a larger jump means a full rebuild rode this place.
            FlowTrace.Step("BaseLayout",
                $"Place: loader.Loaded count AFTER spawn = {loader.Loaded.Count} (expected +1 over the BEFORE line). " +
                "If the owner sees ALL prior towers reappear on this single place, they were HIDDEN/torn-down " +
                "earlier (see [Flow:Structures] teardown lines) and this add merely re-triggered their visual.");
            // Charge ONLY AFTER the committed valid placement (WO-131): the persisted
            // multi-resource ledger (EconomyService → GameState-backed Crystals/Food +
            // in-session Wood/Iron). TrySpend is atomic; it can't fail here (we re-checked
            // CanAfford above) but the bool is honoured for safety.
            // First-build freebie (owner 2026-07-17): ONE free placement TOTAL for
            // non-founding builds, PLUS a per-id freebie for each FTUE FoundingKit piece
            // (pet-house / lumberyard / tower_ground_archer) so a zero-resource first run
            // can found free. Skip the charge and burn the freebie HERE, at the committed
            // placement only (an armed/cancelled ghost never consumes it) by appending
            // this id to FreeBuildsUsed; FreeBuildAvailable then reads that ledger (a
            // founding id burns only its own per-id freebie, a non-founding id burns the
            // shared one-free-total) so it is false for every later charged placement. The flag
            // NEVER resets: selling/destroying the building does not restore it. Persistence
            // rides Exit() -> CommitLayout -> GameStateService.Save(), the SAME save that
            // carries the BaseLayout append above -- building and burned flag commit or revert
            // together.
            if (freeBuild)
            {
                var st0 = GameStateService.Instance != null ? GameStateService.Instance.State : null;
                if (st0 != null)
                {
                    if (st0.FreeBuildsUsed == null) st0.FreeBuildsUsed = new List<string>();
                    st0.FreeBuildsUsed.Add(_armed.id);
                }
                bool founding = FoundingKit.Contains(_armed.id);
                FlowTrace.Step("Build", founding
                    ? $"free build (FOUNDING-KIT, per-id) consumed on '{_armed.id}' -- this founding id now charged; general one-free-total untouched (never resets)"
                    : $"free build (one-free-TOTAL, non-founding) consumed on '{_armed.id}' -- all later non-founding placements now charged (never resets)");
            }
            else if (!ChargeLedger(cost))
            {
                // CanAfford passed ~90 lines above and TrySpend is atomic against the same store, so a
                // decline HERE is a genuine race -- but it must never be silent (CLAUDE.md sec.12): the
                // structure is already spawned and the player was NOT charged.
                // KNOWN ORDERING DEBT: the real fix is to charge BEFORE loader.Spawn and refund on a
                // spawn failure. That reorder is its own ticket - it is not safe to do inline here.
                FlowTrace.Fail("BuildMode",
                    $"Place: ChargeLedger DECLINED {Describe(cost)} for '{_armed.id}' AFTER the CanAfford gate -- " +
                    "the structure is standing but UNPAID. Charge must move ahead of loader.Spawn.");
            }

            // Append to the live BaseLayout so Exit() persists it.
            // Owner ruling 2026-08-06 (first-build grace): set at the MarkEverBuilt seam below and
            // read at the timer seam further down. Declared out here because `state` may be null.
            bool firstEverBuild = false;

            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state != null)
            {
                if (state.BaseLayout == null) state.BaseLayout = new List<PlacedStructureData>();
                state.BaseLayout.Add(data);
                // WO-834: the ever-built ledger grows at the SAME commit seam (idempotent
                // set-add; monotonic — selling never removes it, preserving WO-819
                // sell->baked-twin-resurface). Rides the same Save() as the append above.
                // MarkEverBuilt returns TRUE only when the id was NEWLY added -- i.e. this is the
                // player's first ever placement of it. Capture it HERE: this line runs at :1892 and
                // the build timer starts ~40 lines below, so by then HasEverBuilt(_armed.id) is
                // ALREADY true and a check down there would never fire. (Owner ruling 2026-08-06,
                // first-build grace.)
                firstEverBuild = state.MarkEverBuilt(_armed.id);
            }

            // Committed placement — announce it (tutorial build.tower_placed rides this; guarded so
            // a throwing subscriber can never abort the placement it is observing).
            Guard.Try("BuildMode", "StructurePlaced event", () => StructurePlaced?.Invoke(_armed.id));
            BuildFirstUseGuide.PlacementConfirmed();
            _hud?.RefreshFirstUseGuide();

            // SHOW-STOPPER (owner device felt-test 2026-07-16): a player-PLACED storefront had NO
            // vendor NPC to Talk/trade with. The CastleVendorNpcInjector anchor poll covers only a
            // fixed AnchorRoles table (misses workshop/lumberyard/foundry/silo/collector_* ids) and
            // settles each role ONCE, so a freshly-placed building could get no NPC. Spawn its vendor
            // NOW, at THIS building, reusing the injector's OWN SpawnVendor path (no parallel NPC
            // system; the injector maps non-storefront ids to no role, so towers/walls stay NPC-free).
            Guard.Try("BuildMode", "spawn vendor NPC for placed building",
                () => CastleVendorNpcInjector.NotifyBuildingPlaced(_armed.id, ps.transform));

            // WO-855 Phase 1: this placement changed the live tower census -- drop the cached
            // count so the NEXT ghost frame prices the next tower at the correct ordinal
            // (without waiting out the TTL).
            InvalidateTowerCount();

            // WO-612 (owner 2026-07-06): construction takes TIME — start a WO-172 timer job
            // AFTER the charge (the WO-131 seam the service documents). A null job (both
            // free slots busy / service absent) degrades to instant completion: placement
            // is NEVER blocked, the timer is pacing, not a wall.
            if (DeNelle.Core.FeatureFlags.BuildTimers)
            {
                string jobKey = UnderConstructionVisual.KeyFor(data);
                var svc = BuildTimerService.Instance;
                // WO-855 Phase 4 -- THE HARD-CODED ZERO IS GONE. This call passed the literal
                // `0` as the tier, so EVERY structure in the game -- a 40-wood collector and a
                // 2000-basket endgame building alike -- built in exactly baseBuildSeconds, and
                // BuildTimerConfig.tierGrowth was unreachable dead tuning. The tier is now
                // DERIVED from the structure's own authored cost basket (see
                // BuildTimerConfig.TierForCost): the one economic weight WO-855 sec.4 defines,
                // it needs no new RepoProps field (there is no repo.buildSeconds / repo.tier),
                // and it tracks the Phase 2/3 JSON cost retune automatically.
                // CostFor (NOT EffectiveCostFor): the timer keys off the structure's INTRINSIC
                // weight, so a freebie does not make a build instant and the tower softcap
                // surcharge does not stretch the timer.
                int tier = svc != null && svc.Config != null
                    ? svc.Config.TierForCost(CostFor(_armed)) : 0;
                // OWNER CARVE-OUT 2026-08-06: "other than the pallets". The pallets are the STORAGE
                // CONTAINERS -- lumberyard / foundry / silo, the three catalog entries that declare
                // a storageCapacity and add to the town bank on top of baseCap. Keyed off the data
                // (via TownBankCapacity, the ONE reader of that seam) rather than a hardcoded id
                // list, so a container added later is excluded automatically. They are deliberate
                // capacity-progression buildings (WO-837) and must pay their real timer.
                bool isPallet = _armed != null && DeNelle.Core.Economy.TownBankCapacity.IsStorageContainer(_armed.repo);
                // WO-945: while the player is NOT Onboarded, EVERY qualifying build gets the
                // grace, not just the first-per-id — the tutorial asks for TWO towers of the
                // SAME id, and tower #2 ran the real 90s curve into the scripted teaching wave.
                // Same Onboarded read the tutorial/steward code uses (state != null &&
                // !state.Onboarded — SylasStewardInjector.FoundingArcIncomplete's shape); a null
                // state grants NO onboarding grace, matching firstEverBuild's own null-state
                // default above. The pallets carve-out survives for BOTH reasons (GraceReasonFor).
                bool notYetOnboarded = state != null && !state.Onboarded;
                var graceReason = GraceReasonFor(firstEverBuild, notYetOnboarded, isPallet);
                // WO-911 (M2): record what was ACTUALLY charged (line 1772 — default for a
                // freebie, else SoftcappedCostFor at the tower count that applied at charge time)
                // so a cancel refunds 100% of it (ruling Q1) instead of re-deriving a number the
                // player never paid. `cost` is the same value ChargeLedger debited above.
                var job = svc != null
                    ? svc.StartBuild(jobKey, tier, graceReason, BuildTimerService.ToJobCost(cost))
                    : null;
                // Sec.12 proving line: a capture must show the RESOLVED tier + duration, so
                // "every building takes 15s" can never silently come back.
                FlowTrace.Step("BuildTimer",
                    $"build '{jobKey}' tier={tier} (basket {BuildTimerConfig.CostBasket(CostFor(_armed)):0}) -> " +
                    $"{(svc != null && svc.Config != null ? svc.Config.DurationSecondsForTier(tier, BuildJobKind.Build) : 0f):0}s " +
                    "(WO-855: tier derived from the authored cost basket, no longer the hard-coded 0).");
                if (job != null) UnderConstructionVisual.Attach(ps, jobKey);
                else FlowTrace.Step("Build",
                    $"no free build slot for '{jobKey}' — completed instantly (never block)");
            }

            Debug.Log($"[BuildMode] Placed '{_armed.id}' at cell ({cell.x},{cell.y}) yaw {ArmedYawDegrees:F0}°, charged {(freeBuild ? "nothing (first-build FREE)" : Describe(cost))}.");

            // BM-1 (WO-746, owner 2026-07-18 "should go back to carousel after placement"):
            // a SUCCESSFUL commit RETURNS to the building-selection carousel — the Placing
            // intent bar must not linger. This is the SINGLE return point for EVERY commit
            // path (hover PLACE, two-step PLACE, the dormant rotate-confirm). It runs only
            // AFTER the charge + BaseLayout append + StructurePlaced signal above, so the
            // tutorial placement grant is never bypassed. CancelArmed(afterPlacement:true)
            // disarms (_armed=null) + _palette.Expand() (carousel back, armed glow cleared,
            // and singleton cards re-render as Built — BM-2); then the HUD returns to Browse
            // so the intent bar hides. Multi-place-in-a-row (lay a wall run) is a future
            // per-row opt-in (repo.multiPlace, default OFF); today every row returns to Browse.
            CancelArmed(afterPlacement: true);
            _hud?.SetState(BuildHudState.Browse);
            FlowTrace.Step("BuildHud", "state -> Browse (placement committed; intent bar hidden, carousel restored)");
        }

        /// <summary>
        /// WO-945 — the PURE build-grace decision (headlessly testable; BuildEconomyRegression
        /// drives it). The pallets carve-out (owner ruling 2026-08-06: storage containers pay
        /// their real timer) beats BOTH grace rules; first-build precedence over onboarding keeps
        /// the existing FIRST-BUILD trace line on genuine first builds, so captured baselines
        /// stay comparable. <paramref name="notYetOnboarded"/> must already encode the null-state
        /// default (state != null &amp;&amp; !state.Onboarded — no grace when state is unknowable).
        /// </summary>
        public static BuildGraceReason GraceReasonFor(bool firstEverBuild, bool notYetOnboarded, bool isPallet)
        {
            if (isPallet) return BuildGraceReason.None;              // carve-out wins in every state
            if (firstEverBuild) return BuildGraceReason.FirstBuild;  // owner ruling 2026-08-06
            if (notYetOnboarded) return BuildGraceReason.Onboarding; // WO-945: tutorial never stalls
            return BuildGraceReason.None;
        }

        // =====================================================================
        //  Arming
        // =====================================================================

        /// <summary>
        /// Probe/dev entry — arm a catalog entry by id through the SAME <see cref="Arm"/> path a
        /// palette-card tap uses (BuildPaletteUI.OnEntrySelected → Arm). NOTHING is bypassed:
        /// the armed entry still runs the full ghost / validity / cost placement loop. Used by
        /// the AutoPilot 'AssertTutorialFirstTower' real-input probe. Returns false (and traces)
        /// when the id is not in the registry.
        /// </summary>
        public bool ArmById(string id)
        {
            var entry = CatalogRegistry.Get(id);
            if (entry == null)
            {
                FlowTrace.Warn("Build", $"ArmById: '{id}' not found in CatalogRegistry — cannot arm.");
                return false;
            }
            if (SingletonAlreadyBuilt(entry)) { BuildFeedbackToast.Show(BuildRejectReason.Singleton); return false; }
            Arm(entry);
            return true;
        }

        /// <summary>
        /// WO-707 singleton ENFORCEMENT (owner 2026-07-13 "allows me to build two echo
        /// hollow — should be a singleton enforce"): a catalog row flagged repo.singleton
        /// may exist at most ONCE, judged by the persisted BaseLayout records (the source
        /// of truth every placement appends to at commit). MOVE is unaffected — the move
        /// path never re-arms/re-Places, it repositions the existing record. Containers
        /// (Lumberyard/Foundry/Silo) are deliberately not singleton.
        /// </summary>
        /// <summary>
        /// BM-2 (WO-746) — QUIET shared query: is <paramref name="entry"/> a singleton row
        /// that already has a standing BaseLayout record? Hoisted to <c>internal static</c> so
        /// the palette (same assembly) can render placed singletons as a non-armable "Built"
        /// card instead of offering a card that can only fail at arm time. No trace here (it is
        /// polled per-card per-render); the arm/commit enforcement path traces via the
        /// <see cref="SingletonAlreadyBuilt"/> wrapper below — WO-707 semantics unchanged.
        /// </summary>
        internal static bool IsSingletonBuilt(CatalogEntry entry)
        {
            // StructureSingleton v2: the ONE authority answers, memoized per-frame for
            // this per-card per-render poll — no second copy of the truth query.
            // WO-843 (owner F8 2026-08-02 "lumber mill destroyed no option to rebuild"):
            // the card/arm gate now asks IsPlayerBuilt, NOT IsBuilt. IsBuilt counts an
            // ACTIVE BAKED TWIN — but after a WO-753 destruction (or a sell) the twin
            // RESURFACES as the WO-819 visual stand-in, and counting it locked the card
            // as "Built" with no way to rebuild. A twin-only state must read BUILDABLE
            // at full cost (freebies stay burned); committing the rebuild stands the
            // twin down via NotifyPlaced -> Enforce (placed wins, only-ever-ONE holds).
            if (entry?.repo == null || !entry.repo.singleton) return false;
            return StructureSingleton.IsPlayerBuilt(entry);
        }

        private static bool SingletonAlreadyBuilt(CatalogEntry entry)
        {
            if (!IsSingletonBuilt(entry)) return false;
            FlowTrace.Step("Build", $"singleton gate: '{entry.id}' already recorded — arm/place refused (WO-707)");
            return true;
        }

        private void Arm(CatalogEntry entry)
        {
            if (SingletonAlreadyBuilt(entry)) { BuildFeedbackToast.Show(BuildRejectReason.Singleton); return; }
            FlowTrace.Step("Build", $"Armed placement for '{entry?.id}'");
            // Entering CREATE mode clears any active selection / move (P2).
            ClearSelection();
            _armed = entry;
            _armedYawEighths = 0;
            _pendingPlace = false;
            _dropPending = false;   // two-step: a fresh arm starts in hover (no pending drop)
            if (_ghost == null) _ghost = new GameObject("GhostPreview").AddComponent<GhostPreview>();
            _ghost.SetEntry(entry);

            // Grok slice 4 (owner "minimize on select"): collapse the shop to the armed-
            // card summary while placing, and switch the HUD to the Placing intent bar.
            string armedLabel = entry != null && !string.IsNullOrEmpty(entry.displayName)
                ? entry.displayName : entry?.id;
            _palette?.Collapse(armedLabel);
            _hud?.SetPlacingLabel(armedLabel);   // fold "Placing: <name>" into the HUD intent bar
            _hud?.SetState(BuildHudState.Placing);
            _hud?.RefreshFirstUseGuide();
            _hud?.RefreshResources();
        }

        // <paramref name="afterPlacement"/> = true when a SUCCESSFUL placement is returning
        // to the carousel (owner 2026-07-16/17), so the teardown is identical to a cancel but
        // the captured FlowTrace line reflects "placed -> returned to carousel" rather than
        // "placement aborted". Defaults false so every existing cancel caller is unchanged.
        private void CancelArmed(bool afterPlacement = false)
        {
            _armed = null;
            _dropPending = false;   // two-step: drop any pending (uncommitted) drop with the arm
            _ghost?.Hide();
            // WO-334 — if the rotate panel was mid-confirm, tear it down (no callback).
            if (_pendingPlace)
            {
                _pendingPlace = false;
                _rotateMenu?.Close();
            }
            // Grok slice 4: expand the shop back to the full carousel when disarming.
            _palette?.Expand();
            // Owner device felt-test 2026-07-16 ("after i select place the cancel should
            // close out and the selection bar opens back up"): capture the transition. _armed
            // is now null, so the next Update re-derives the HUD state as Browse (intent bar
            // hides) while BuildPaletteUI.Expand has just brought the carousel back = "choosing
            // a building". The line differs by entry point (placed vs cancelled) but the
            // resulting state — carousel back, Browse next frame — is the SAME.
            if (afterPlacement)
                FlowTrace.Step("BuildHud", "placed -> returned to carousel");
            else
                FlowTrace.Step("BuildHud",
                    "Cancel: placement aborted (armed disarmed) -> selection bar re-opened " +
                    "(palette Expand); next frame returns to Browse = choosing a building");
        }

        // =====================================================================
        //  Select / Move / Sell (P2 edit verbs)
        // =====================================================================

        /// <summary>
        /// WO-2006 / OWNER_RULINGS_LOCKED §25 — THE MANAGE PLACED DOOR'S LANDING.
        ///
        /// <para>Owner ruling 2026-09-06, from a friend's playtest: <i>"he accidentally put a
        /// palisade down and he didn't mean to and now he has no way to move the Palisade"</i>.
        /// The ruling's own finding is the important half: <b>the capability EXISTS and is
        /// fully wired; only the DOOR was missing.</b></para>
        ///
        /// <para>⛔ THIS METHOD DELIBERATELY BUILDS NOTHING. It does not create a panel, does
        /// not hold a selection, and does not introduce a second answer to "what is selected"
        /// — the failure mode CLAUDE.md warns about four times. It clears any half-started
        /// CREATE/EDIT state and ANNOUNCES the gesture, then gets out of the way: the already
        /// live idle loops (<see cref="UpdateSelectLoop"/> on desktop,
        /// <see cref="TryTapSelectAt"/> on touch/web) call <see cref="SelectStructure"/>,
        /// which raises the real MOVE / UPGRADE / SELL panel (<see cref="BuildSelectionUI"/>).
        /// The whole defect was that nothing on screen ever said so.</para>
        ///
        /// <para>§12: every branch names itself, and the placed-structure census is emitted
        /// because "I tapped and nothing happened" splits into <i>nothing to select</i> vs
        /// <i>the tap missed</i>, and only the census can tell those apart after the fact.</para>
        /// </summary>
        private void BeginManagePlaced()
        {
            if (!IsActive)
            {
                // Cannot happen through the palette (the browser only exists inside a live
                // session) — but a silent no-op here would look exactly like the bug this
                // door fixes, so it is said out loud rather than swallowed.
                FlowTrace.Warn("Build",
                    "ManagePlaced requested while build mode is NOT active — ignored. The palette " +
                    "should only be able to raise this from inside a live session.");
                return;
            }

            CancelArmed();     // a half-armed CREATE entry must not survive into EDIT
            ClearSelection();  // start from no selection; the player's tap chooses

            // WO-1581 - THE DOOR WAS BEING STOMPED BY ITS OWN DISARM.
            //
            // CancelArmed() ends in _palette.Expand(), and since WO-1273 Expand() means
            // "Show() the modal BuildCollectionBrowser", not "restore a dock carousel".
            // So the card's Close() was undone one line later, in the SAME frame, and the
            // toast below was announced UNDER a re-opened full-screen catalog holding a
            // timeScale-0 WorldHold. Device log, build 2026.09.07.359076:
            //   08:28:00.191  Manage Placed card TAPPED - closing the browser ...
            //   08:28:00.193  Navigation: closed workspace 'Build Collections' to world
            //   08:28:00.194  PanelManager: 'Build Collections' opened and verified visible
            //   08:28:00.210  kit toast -> 'Tap a building or wall to move, upgrade or sell it.'
            // The owner tapped it twice (00.191, 00.972), saw the catalog both times, and
            // reported "manage buildings doesnt do anything". The door was never dead - it
            // was overwritten.
            //
            // Collapse is the INVERSE of Expand and is the same seam SelectStructure already
            // uses "AFTER CancelArmed has had its say" (:2504). Ordering is load-bearing:
            // this must run after CancelArmed, never before it.
            _palette?.Collapse(null);

            int placed = FindObjectsByType<PlacedStructure>(FindObjectsSortMode.None).Length;

            if (placed <= 0)
            {
                DeNelle.Core.UI.ElarionUiKit.ShowToast(
                    "Nothing is built yet — place something first.",
                    DeNelle.Core.UI.ElarionUiKit.ToastTone.Info);
                FlowTrace.Warn("Build",
                    "ManagePlaced entered with ZERO live PlacedStructure bodies — the player was told " +
                    "via toast instead of being left tapping an empty map (§12: no silent refusal).");
                return;
            }

            DeNelle.Core.UI.ElarionUiKit.ShowToast(
                "Tap a building or wall to move, upgrade or sell it.",
                DeNelle.Core.UI.ElarionUiKit.ToastTone.Info);

            FlowTrace.Step("Build",
                $"ManagePlaced ENTERED (ruling §25 door): armed cleared, selection cleared, " +
                $"the shop CancelArmed re-expanded was COLLAPSED (WO-1581), and " +
                $"{placed} live PlacedStructure bodies are selectable. The next world tap routes " +
                "through the EXISTING UpdateSelectLoop/TryTapSelectAt -> SelectStructure -> " +
                "BuildSelectionUI (Move/Upgrade/Sell). No new selection state was created.");
        }

        /// <summary>
        /// Select a placed structure: highlight it and show the Move/Sell/Cancel
        /// action panel (refund = 50% of its catalog buildCost, rounded down). Any
        /// previously-armed CREATE entry is dropped so the modes never overlap.
        /// </summary>
        private void SelectStructure(PlacedStructure ps)
        {
            if (ps == null) return;
            CancelArmed();
            ClearSelection();   // drop any prior highlight before re-selecting

            _selected = ps;
            _selected.SetHighlighted(true);

            EnsureSelectionUi();
            ShowSelectionPanel(ps);

            // OWNER 2026-08-04 ("when i select to upgrade should minimize the selection bar"):
            // minimize-on-select was only ever wired to the ARM-TO-PLACE path (:2042). Selecting
            // a PLACED structure routes through CancelArmed(), which deliberately calls
            // _palette.Expand() to restore the carousel on disarm — correct for disarming, but it
            // meant picking a building to upgrade actively RE-OPENED the full shop over the map,
            // on top of the selection panel the player just asked for. Collapse it here, AFTER
            // CancelArmed has had its say, so the two do not fight. ClearSelection() expands it
            // back, keeping select/deselect symmetric with arm/disarm.
            var selEntry = CatalogRegistry.Get(ps.itemId);
            string selLabel = selEntry != null && !string.IsNullOrEmpty(selEntry.displayName)
                ? selEntry.displayName : ps.itemId;
            _palette?.Collapse(selLabel);

            // WO-1581 sec.12 - the step the device log was missing. Selection ALREADY worked
            // (08:28:02.424 "tap-select: hit - SELECTS 'lumberyard'"); what no line said was
            // that the catalog re-opened over the panel one frame later (08:28:02.429), which
            // is why the tester reported MOVE as missing. Now the collapse is stated with the
            // selection, so the next capture shows the verb row standing alone.
            FlowTrace.Step("BuildMove",
                $"SELECTED id='{ps.itemId}' cell=({ps.gridCell.x},{ps.gridCell.y}) level={ps.level} -> " +
                "BuildSelectionUI is up with MOVE / UPGRADE / SELL / CANCEL, and the collection " +
                "browser was collapsed so nothing covers it. MOVE is now ONE TAP away.");
        }

        /// <summary>
        /// Populate + show the edit panel for <paramref name="ps"/> with its current tier
        /// state (S5): label, sell refund, level/maxLevel, and the next-tier upgrade cost +
        /// whether it is currently affordable. Shared by select + post-move + post-upgrade.
        /// </summary>
        private void ShowSelectionPanel(PlacedStructure ps)
        {
            if (ps == null) return;
            var entry = CatalogRegistry.Get(ps.itemId);
            string label = entry != null && !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : ps.itemId;

            int level = Mathf.Max(1, ps.level);
            int maxLevel = MaxLevelFor(entry);

            int upgradeTotal = 0;
            bool canAfford = false;
            if (level < maxLevel)
            {
                DeNelle.Core.Catalog.ResourceCost up = UpgradeCostFor(entry, level);
                upgradeTotal = up.wood + up.food + up.iron + up.crystals;
                canAfford = CanAfford(up);
            }

            _selectionUi?.Show(label, RefundFor(ps), level, maxLevel, upgradeTotal, canAfford);
        }

        /// <summary>Drop the current selection (highlight + panel) and any in-progress move.</summary>
        private void ClearSelection()
        {
            if (_movingSelected) CancelMove();
            bool had = _selected != null;
            if (_selected != null) _selected.SetHighlighted(false);
            _selected = null;
            _selectionUi?.Hide();

            // Symmetry with the collapse in SelectStructure: deselecting restores the carousel,
            // exactly as disarming does. Guarded on `had` so a no-op ClearSelection (it is called
            // defensively before a re-select) cannot expand the shop back open a frame after
            // SelectStructure just collapsed it.
            if (had) _palette?.Expand();
        }

        /// <summary>
        /// SELL the selected structure: free its grid cells, drop its BaseLayout
        /// record (matched by cell + itemId), destroy the GameObject, and REFUND 50%
        /// of its buildCost to the persisted crystal wallet (WO-131 single wallet).
        /// </summary>
        private void SellSelected()
        {
            if (_selected == null) return;
            var ps = _selected;

            DeNelle.Core.Catalog.ResourceCost refund = RefundCostFor(ps);

            // Free the cells it held.
            _grid?.Free(ps.gridCell, ps.footprint);

            // Drop the persisted record (match by cell + itemId).
            RemoveLayoutEntry(ps.itemId, ps.gridCell);

            // Drop it from the loader's live set so it doesn't double-free on Exit.
            BaseLayoutLoader.Instance?.Forget(ps);

            // F8-51 — selling a structure mid-build/upgrade cancels its timer job (the
            // caller-owns-refund contract on CancelJob; the 50% sell refund below stands).
            if (DeNelle.Core.FeatureFlags.BuildTimers)
                BuildTimerService.Instance?.CancelJob(
                    DeNelle.Village.Buildings.Progression.PlacedUpgradeKey.Compose(
                        ps.itemId, ps.gridCell.x, ps.gridCell.y));

            // Refund ~50% of the full multi-resource cost into the persisted ledger
            // (EconomyService.Grant → GameState-backed Crystals/Food + in-session
            // Wood/Iron). Crystals-only entries refund Crystals only.
            RefundLedger(refund);

            Debug.Log($"[BuildMode] Sold '{ps.itemId}' at cell ({ps.gridCell.x},{ps.gridCell.y}) — refunded {Describe(refund)}.");

            // Clear selection BEFORE destroy so the highlight teardown sees a live object.
            _selected.SetHighlighted(false);
            _selected = null;
            _selectionUi?.Hide();
            Destroy(ps.gameObject);
        }

        // =====================================================================
        //  Upgrade (S5 — the CoC sink: level 1→maxLevel)
        // =====================================================================

        /// <summary>
        /// OPEN the upgrade PAGE for the selected structure — the ONE destination every doorway
        /// leads to (owner ruling 2026-08-16). A city/resource building opens under its ladder id;
        /// a placed structure (tower / wall / container / mine / caravan) opens under its job key
        /// so the panel resolves UpgradeFamily.PlacedStructure and shows its real level ladder.
        /// The charge/queue itself lives in PlacedStructureUpgradeService — the panel's CTA calls
        /// it, and so does the kill-switch fallback below; this method starts nothing of its own.
        /// <para/>
        /// HISTORICAL (retired 2026-08-16): this method used to ALSO be a second destination —
        /// if it is below its catalog maxLevel AND
        /// the next-tier cost is affordable, spend that cost through the persisted ledger,
        /// increment <see cref="PlacedStructure.level"/>, step the visual (StructureTierVisual)
        /// and the gameplay stats (DefenseTower range/damage · WallSegment toughness) up a tier,
        /// re-persist the level in BaseLayout, and re-show the panel at the new tier. Bails
        /// (logs) at the ceiling or when unaffordable — never charges on a no-op.
        /// </summary>
        private void UpgradeSelected()
        {
            // §12 INSTRUMENT-FIRST (owner F8 2026-07-17 "Upgrade ... does NOTHING"): this
            // handler used to emit only Debug.Log on its SUCCESS paths, so the F8 harvest saw
            // NO [Flow] line whether or not it fired — the dead step was invisible. Every
            // branch below now traces [Flow:BuildUpgrade] and pops a BuildFeedbackToast, so the
            // next build's capture pinpoints the taken path and the player FEELS the result.
            var ps = _selected;
            if (ps == null)
            {
                FlowTrace.Warn("BuildUpgrade", "Upgrade tapped but no structure is selected.");
                return;
            }

            var entry = CatalogRegistry.Get(ps.itemId);
            int level = Mathf.Max(1, ps.level);
            int maxLevel = MaxLevelFor(entry);

            // COLLECTOR ID RESOLUTION (collector bug fix): a placed collector's itemId is the
            // catalog id ("collector_lumbermill"/"collector_farm"), but its tier/level ladder is
            // keyed on the bare collectorBuildingId ("lumbermill"/"farm", building-tiers.json +
            // ResourceBuildingProgression). Resolve to the upgrade-keyed id (unchanged for every
            // non-collector) so the panel predicate + open target the right ladder -- else a
            // collector fell through to the tower inline path and toasted "Max tier reached".
            string upgradeId = CatalogRegistry.ResolveUpgradeId(ps.itemId);

            // ONE DESTINATION, MANY DOORWAYS (owner ruling 2026-08-16: "upgrades should be
            // accessable from manage tab. You should go to the modeled page to manage all
            // towers ... cleaner to do like the others"). EVERY upgradable structure now opens
            // the SAME BuildingUpgradePanelMvvm page — a city/resource building under its ladder
            // id, a PLACED structure (tower / wall / container / mine / caravan) under its job
            // key, which UpgradeFamilyResolver classifies as PlacedStructure.
            //
            // The old inline tier-bump that used to live here (a charge + timer start with NO
            // page) is RETIRED as a second destination: its LOGIC survives verbatim inside
            // PlacedStructureUpgradeService, which the panel's CTA and the kill-switch fallback
            // below both call. The family is resolved by the SHARED UpgradeFamilyResolver — this
            // site no longer hand-derives it.
            var family = DeNelle.Village.Buildings.Progression.UpgradeFamilyResolver.Resolve(upgradeId);
            bool ladderBuilding = family == DeNelle.Village.Buildings.Progression.UpgradeFamily.City
                               || family == DeNelle.Village.Buildings.Progression.UpgradeFamily.Resource;
            string jobKey = DeNelle.Village.Buildings.Progression.PlacedUpgradeKey.Compose(
                ps.itemId, ps.gridCell.x, ps.gridCell.y);
            bool placedLadder = !ladderBuilding && maxLevel > 1;
            string panelId = ladderBuilding ? upgradeId : jobKey;

            FlowTrace.Step("BuildUpgrade",
                $"click id='{ps.itemId}' upgradeId='{upgradeId}' lvl={level}/{maxLevel} family={family} " +
                $"ladderBuilding={ladderBuilding} placedLadder={placedLadder} " +
                $"panelFlag={DeNelle.Core.FeatureFlags.BuildingUpgradePanel} buildTimers={DeNelle.Core.FeatureFlags.BuildTimers}");

            if (!ladderBuilding && !placedLadder)
            {
                FlowTrace.Step("BuildUpgrade", $"'{ps.itemId}' has no upgrade ladder (maxLevel {maxLevel}) — nothing to open.");
                BuildFeedbackToast.Show("No upgrades for this structure.");
                return;
            }

            if (DeNelle.Core.FeatureFlags.BuildingUpgradePanel)
            {
                // Canonical guarded open (same path HudKitController uses): routes to the
                // registered BuildingUpgradePanelMvvm.Open(id) for THIS structure.
                bool opened = DeNelle.Core.UI.PanelRouter.Open(DeNelle.Core.UI.PanelId.BuildingUpgrade, panelId);
                if (opened)
                {
                    FlowTrace.Step("BuildUpgrade", $"opened BuildingUpgrade panel for '{panelId}'.");
                    return;
                }
                FlowTrace.Fail("BuildUpgrade", $"PanelRouter.Open(BuildingUpgrade,'{panelId}') returned false — panel did NOT open.");
                if (ladderBuilding) return;   // no headless fallback exists for the tier/perk ladders
            }
            else if (ladderBuilding)
            {
                FlowTrace.Warn("BuildUpgrade", $"panel flag OFF (kill-switch) for building '{ps.itemId}' — no upgrade surface.");
                BuildFeedbackToast.Show("Upgrades unavailable.");
                return;
            }

            // ── KILL-SWITCH / PANEL-FAILED FALLBACK for a PLACED structure ──
            // No page available, but the upgrade must still be reachable. This calls the SAME
            // service the panel's CTA does — one behaviour, two callers, never a second copy of
            // the charge -> gate -> StartUpgrade sequence.
            var upgrade = DeNelle.Village.Buildings.Progression.PlacedStructureUpgradeService.TryStart(jobKey);
            if (!string.IsNullOrEmpty(upgrade.Message)) BuildFeedbackToast.Show(upgrade.Message);
            ShowSelectionPanel(ps);
        }

        /// <summary>
        /// Land a level on a placed structure: live marker + visual (per-tier model swap or
        /// legacy scale/accent) + tier stats + the persisted BaseLayout record. F8-51: shared
        /// by the instant upgrade path (flag OFF / no service) and the timer-completion path
        /// (CompletedUpgradeApplier), so both apply identically.
        /// </summary>
        internal static void ApplyUpgradeLevel(PlacedStructure ps, int newLevel)
        {
            if (ps == null) return;
            var entry = CatalogRegistry.Get(ps.itemId);
            ps.level = newLevel;

            // Owner F8 2026-07-06 ("upgrade just makes it bigger — replace with new structure"):
            // when the catalog authors a per-tier model (upgradeVisualPath), SWAP the visual and
            // skip the legacy scale-step (the model IS the progression). No tier model = legacy
            // scale + accent, unchanged. Reskin BEFORE Apply so Apply re-collects new renderers.
            bool swapped = StructureFactory.ReskinForLevel(ps.gameObject, entry, newLevel);
            if (ps.TierVisual != null) { ps.TierVisual.Apply(swapped ? 1 : newLevel); ps.TierVisual.Refresh(); }

            // Step the gameplay stats per tier (range/damage for towers, toughness for walls).
            ApplyTierStats(ps, newLevel);

            // Persist the new level in the live BaseLayout (so Exit()'s Save() round-trips it).
            UpdateLayoutLevel(ps.itemId, ps.gridCell, newLevel);

            // WO-1275: wall_wood L2 is the Wooden Palisade -> Stone Wall transition.
            // Award at this persisted upgrade-commit seam, never from scene art.
            RewardedProgression.TryUnlockStoneGate(ps.itemId, newLevel);

            // ── Upgrade-success TELL (shared by BOTH paths) ── the stats change but were near-
            // silent, and the TIMER-completion path had NO feedback at all ("looks like nothing
            // happened"). This is the ONE hook both the instant apply (BuildUpgrade) and the timer
            // completion (CompletedUpgradeApplier) call, so putting the tell here covers every
            // DefenseTower/ArcaneTower/wall that upgrades, on either path. Feedback ONLY — no
            // change to the upgrade LOGIC (stats/tier/cost already applied above).
            //   (a) ASCII toast — derive the building name from the catalog, fall back to
            //       "Structure" if the entry is null.
            //   (b) WOW payoff (owner 2026-07-24): the owner-tagged "UpgradeStructureComplete_Aura"
            //       FIREWORKS burst (Mirza Beig), fired the MOMENT the upgrade lands through the ONE
            //       VFXManager Hovl pool. Shared by BOTH paths (instant apply + timer completion),
            //       so every DefenseTower/ArcaneTower/wall celebrates identically. Seated ABOVE the
            //       structure so the fireworks read as a loud burst over it, scaled 1.5x for WOW.
            //       Motion/particle-based => colorblind-safe. Null-safe no-op if the key/prefab is
            //       missing (throttled log), never throws. No new pool, no raw Instantiate.
            string tellName = (entry != null && !string.IsNullOrEmpty(entry.displayName))
                ? entry.displayName : "Structure";
            BuildFeedbackToast.Show($"{tellName} upgraded to Tier {newLevel}.");
            Vector3 burstAt = ps.transform.position + Vector3.up * 2.5f;
            //       EXPLICIT 3.5s LIFETIME (2026-08-05): the Fireworks prefab emits
            //       CONTINUOUSLY on loop (rate 5, no bursts). The catalog row is pinned to
            //       IsLoop=false by a standing owner ruling - the "perma-fireworks" bug, where
            //       the celebration never ended - so it takes the one-shot path and is
            //       reclaimed. But without an explicit bound that path falls back to
            //       DetectDuration, and a looping emitter has no natural end to detect. This
            //       call also discards its handle, so nothing else would stop it. Stating the
            //       duration is what makes a finite celebration finite; it is the same ruling
            //       the pin encodes, said at the call site.
            VFXManager.PlayKey("UpgradeStructureComplete_Aura", burstAt,
                               Quaternion.identity, null, null, 1.5f, 3.5f);
            //   (c) CRACKLE SFX (owner: "a crackling sound tied to the fireworks/celebration").
            //       No dedicated Crackle clip exists in Resources/Sfx, so the closest AUTHORED event
            //       is used: SfxId.FireExplosion (doc = "boom + crackle", noisy synth). Layered with
            //       the LevelUp rising chime the prior VFXManager.Play path fired via VfxToSfx, so
            //       the celebration keeps its progression tone AND gains the crackle. Both null-safe
            //       (procedural fallback when no library clip is authored) — never a silent no-op.
            var audio = DeNelle.Audio.AudioService.Instance;
            audio?.PlaySfxAtPosition(DeNelle.Audio.SfxId.LevelUp, burstAt);
            audio?.PlaySfxAtPosition(DeNelle.Audio.SfxId.FireExplosion, burstAt);
        }

        /// <summary>
        /// Apply per-tier gameplay stats to a structure at <paramref name="level"/>. Towers
        /// scale range + damage with the tier (the catalog base × a per-tier multiplier); walls
        /// step their durability toughness. Visual stepping is owned by StructureTierVisual;
        /// this is the BEHAVIOUR half. Null-/component-safe (a decoration upgrades visually only).
        /// Internal so BaseLayoutLoader can re-assert the tier stats when a saved structure
        /// reloads above level 1 (so a tier-3 tower comes back at tier-3 stats, not base).
        /// </summary>
        internal static void ApplyTierStats(PlacedStructure ps, int level)
        {
            if (ps == null) return;
            // DELIBERATELY still 1..3, NOT RepoProps.MaxStructureLevel: this is the TOWER/WALL stat
            // ladder (s_towerTierMul has 3 rungs, WallSegment.SetTier clamps 1..3). WO-966 raised the
            // LEVEL ceiling to 6 for the storage containers, which carry neither DefenseTower nor
            // WallSegment -- so nothing here reads a 4th rung. Widening this clamp without widening
            // s_towerTierMul would index past the table; widening the table is a tower-balance
            // decision, not a storage one.
            int tier = Mathf.Clamp(level, 1, 3);
            var entry = CatalogRegistry.Get(ps.itemId);
            var repo = entry != null ? entry.repo : null;

            // Tower — scale range + damage from the catalog base off the tier multiplier so a
            // tier-3 tower hits harder + further (1.0 / 1.25 / 1.55). Read base off the catalog
            // (not the live field) so repeated upgrades never compound.
            var tower = ps.GetComponent<DefenseTower>();
            if (tower != null && repo != null)
            {
                float mul = s_towerTierMul[tier];
                tower.Range  = repo.range  * mul;
                tower.Damage = repo.damage * mul;
                // FireRate intentionally unchanged — range + damage are the readable tier wins.
            }

            // Arcane spire (tower_arcane_spire) — a SEPARATE ArcaneTower component, NOT DefenseTower,
            // so the DefenseTower branch above never touched it: L2/L3 charged escalating cost but
            // scaled NOTHING (owner F8 "Upgrade does NOTHING"). Mirror DefenseTower's convention:
            // scale Range + Damage (and the AoE blast radius) off the CATALOG base by the SAME
            // per-tier multiplier (1.0 / 1.25 / 1.55), read from repo so repeated upgrades never
            // compound. FireRate/slow/splash intentionally unchanged — range/damage/radius are the
            // readable tier wins. AoeRadius only scales when the catalog authored one (else the
            // component keeps its serialized default rather than collapsing to 0).
            var arcane = ps.GetComponent<ArcaneTower>();
            if (arcane != null && repo != null)
            {
                float mul = s_towerTierMul[tier];
                arcane.Range  = repo.range  * mul;
                arcane.Damage = repo.damage * mul;
                if (repo.aoeRadius > 0f) arcane.AoeRadius = repo.aoeRadius * mul;
                FlowTrace.Step("BuildUpgrade",
                    $"ArcaneTower '{ps.itemId}' tier {tier} stats: x{mul:0.00} -> range={arcane.Range:0.#}, " +
                    $"damage={arcane.Damage:0.#}, aoeRadius={arcane.AoeRadius:0.#}.");
            }

            // Wall — step the durability tier (incoming-damage toughness on the 0-100 track).
            var wall = ps.GetComponent<WallSegment>();
            if (wall != null) wall.SetTier(tier);
        }

        // Per-tier tower stat multiplier (index 0 unused): L1 ×1.0 · L2 ×1.25 · L3 ×1.55.
        private static readonly float[] s_towerTierMul = { 1f, 1f, 1.25f, 1.55f };

        /// <summary>
        /// The tower stat multiplier for a tier (1..3). THE ONE AUTHORITY — the upgrade card reads
        /// this to show the player the real deltas, and the placer above applies it to
        /// <c>tower.Range</c>/<c>tower.Damage</c>. Same number, one definition.
        /// </summary>
        /// <remarks>
        /// ⚠ ADDED 2026-08-17 because the upgrade card said only <i>"Stronger Archer Tower at
        /// Level 3"</i> for 225 wood + 100 iron. The scaling was REAL all along — it just lived
        /// here, in the PLACER, while a reader searching <c>DefenseTower.cs</c> for level-driven
        /// stats found only the projectile key and reasonably concluded the upgrade bought a
        /// reskin. It does not: L2 is ×1.25 and L3 is ×1.55 on BOTH range and damage.
        ///
        /// The card MUST call this rather than re-declaring the ladder. A second copy of these
        /// three numbers is the duplicate-authority bug this project keeps paying for — and here
        /// it would be the worst kind, because the copy that drifts is the one that TELLS THE
        /// PLAYER what they are buying.
        ///
        /// Fire rate is deliberately NOT scaled (see the placer comment): range + damage are the
        /// readable tier wins. Do not add a fire-rate rung here without an owner ruling.
        /// </remarks>
        internal static float TowerStatMultiplier(int tier) =>
            s_towerTierMul[Mathf.Clamp(tier, 1, s_towerTierMul.Length - 1)];

        /// <summary>
        /// The catalog max upgrade level for an entry (S5). Clamped to the ONE named ceiling
        /// <see cref="DeNelle.Core.Catalog.RepoProps.MaxStructureLevel"/> (6 since WO-966 -- it was
        /// a hardcoded 3, tied to StructureTierVisual's 1..3 accent ladder, which made the storage
        /// containers' owner-ruled levels 4-6 unreachable dead data). The VISUAL still tops out at
        /// tier 3 (StructureTierVisual.Apply clamps internally), which is a cosmetic gap, not a
        /// gameplay one. Defaults to 1 (not upgradeable) for a null entry or a row that omits
        /// maxLevel.
        /// </summary>
        // DELEGATES to PlacedStructureUpgradeService.MaxLevelFor — the ONE ceiling rule, so the
        // controller, the upgrade panel's VM and the oracles can never disagree about where a
        // placed structure's ladder ends (two clamps in two files is how the WO-966 levels 4-6
        // became unreachable dead data in the first place).
        internal static int MaxLevelFor(CatalogEntry entry)
            => DeNelle.Village.Buildings.Progression.PlacedStructureUpgradeService.MaxLevelFor(entry);

        /// <summary>Resolve upgrade cost for the given level transition (L→L+1). Null-safe.</summary>
        public static DeNelle.Core.Catalog.ResourceCost UpgradeCostFor(CatalogEntry entry, int fromLevel)
        {
            var repo = entry != null ? entry.repo : null;
            if (repo == null) return default;

            int idx = Mathf.Max(0, fromLevel - 1);   // L1→L2 uses index 0
            if (repo.upgradeCost != null && idx < repo.upgradeCost.Length)
            {
                var authored = repo.upgradeCost[idx];
                if (!authored.IsZero) return authored;   // authored table wins
            }

            // Fallback scaler: the resolved build cost × the level being left.
            DeNelle.Core.Catalog.ResourceCost baseCost = CostFor(entry);
            int scale = Mathf.Max(1, fromLevel);
            return new DeNelle.Core.Catalog.ResourceCost
            {
                wood     = baseCost.wood     * scale,
                food     = baseCost.food     * scale,
                iron     = baseCost.iron     * scale,
                crystals = baseCost.crystals * scale,
            };
        }

        /// <summary>
        /// Begin MOVE: seed the ghost from the selected structure's entry + current
        /// yaw, FREE its current cells (so it can't block its own re-placement), and
        /// hand control to the move loop. The action panel hides during the move.
        /// </summary>
        private void BeginMoveSelected()
        {
            // WO-1581 sec.12 - the MOVE chain carried NO FlowTrace at all: begin/commit/cancel
            // spoke only through Debug.Log/LogWarning, so a device log could not tell
            // "MOVE was never reachable" from "MOVE ran and refused". The tester has asked
            // for MOVE several times; every step below now names itself.
            if (_selected == null)
            {
                FlowTrace.Warn("BuildMove",
                    "MOVE requested with NOTHING selected - ignored. BuildSelectionUI raises " +
                    "OnMoveRequested only while a structure is selected, so reaching this means " +
                    "the selection was dropped between the tap and the callback.");
                return;
            }
            var entry = CatalogRegistry.Get(_selected.itemId);
            if (entry == null)
            {
                FlowTrace.Fail("BuildMove",
                    $"MOVE REFUSED for '{_selected.itemId}': no CatalogRegistry entry, so the ghost " +
                    "cannot be seeded. The structure stays put and its cells stay occupied. This is " +
                    "a catalog gap, not a placement failure.");
                Debug.LogWarning($"[BuildMode] Cannot move '{_selected.itemId}' - not in registry.");
                return;
            }

            _moveOriginCell = _selected.gridCell;
            // FIX #2 (2026-07-16) — seed the move target at the structure's current spot so
            // an arrow/d-pad nudge starts from where it stands (not from the last pointer
            // point). The pointer/keys drive _moveWorldPoint from here in UpdateMoveLoop.
            _moveWorldPoint = _selected.transform.position;
            // WO-673 L5 — seed the eighth-step yaw from the structure's persisted facing
            // (quarter-steps ×2 + the 45° half-step from yawOffset). Old pieces
            // (yawOffset 0) seed exactly as before.
            _armedYawEighths = ((_selected.yawSteps & 3) * 2
                + Mathf.RoundToInt(Mathf.Repeat(_selected.yawOffset, 360f) / 45f)) & 7;

            // Release the structure's own cells for the duration of the move.
            _grid?.Free(_selected.gridCell, _selected.footprint);

            if (_ghost == null) _ghost = new GameObject("GhostPreview").AddComponent<GhostPreview>();
            _ghost.SetEntry(entry);

            _movingSelected = true;
            _selectionUi?.Hide();

            FlowTrace.Step("BuildMove",
                $"MOVE BEGUN id='{_selected.itemId}' origin cell=({_moveOriginCell.x},{_moveOriginCell.y}) " +
                $"footprint=({_selected.footprint.x}x{_selected.footprint.y}) yawEighths={_armedYawEighths} " +
                $"level={_selected.level}. Origin cells FREED so the piece cannot block its own " +
                "re-placement; ghost seeded at the current spot. MOVE IS FREE and preserves level " +
                "(WO-1445 / ruling 25) - no wallet call happens on this path at all. The next legal " +
                "confirm routes to CommitMove.");
            // Fold "Placing: <name>" into the HUD intent bar for the move loop too, so the
            // collapsed shop needs no summary panel (the palette stays hidden during a move).
            _hud?.SetPlacingLabel(entry != null && !string.IsNullOrEmpty(entry.displayName)
                ? entry.displayName : _selected.itemId);
        }

        /// <summary>
        /// Commit a MOVE to a validated cell: occupy the new cells, reposition the
        /// GameObject, and sync the PlacedStructure marker + its BaseLayout record
        /// (cellX/cellZ/yawSteps). Free, so the wallet is untouched.
        /// </summary>
        private void CommitMove(Vector2Int cell, Vector2Int footprint, Vector3 snapped, bool wallMounted = false)
        {
            if (!_movingSelected) return;   // one commit per BeginMoveSelected gesture (WO-F8 move fix)
            if (_selected == null)
            {
                FlowTrace.Fail("BuildMove",
                    "MOVE COMMIT ABORTED: the selection vanished mid-gesture. CancelMove re-occupies " +
                    "the ORIGIN cells, so the grid is left consistent and nothing is stranded.");
                CancelMove();
                return;
            }

            _grid?.Occupy(cell, footprint, _selected.itemId);

            // Move the object (keep the SEAT height from the snap point — wall-top for a wall-walk
            // mount, else the surface Y).
            _selected.transform.SetPositionAndRotation(
                snapped, Quaternion.Euler(0f, ArmedYawDegrees, 0f));

            // Sync the live marker, then the matching persisted record (old cell → new).
            var oldCell = _selected.gridCell;
            _selected.gridCell = cell;
            _selected.footprint = footprint;
            _selected.yawSteps = ArmedYawQuarterSteps;
            _selected.yawOffset = ArmedYawOffsetDeg;   // WO-673 L5 — keep the 45° half-step across a move
            _selected.worldY = snapped.y;
            _selected.wallMounted = wallMounted;

            // Re-apply (or clear) the elevation range perk for the moved piece's new seat — a piece
            // moved ONTO a wall gains the high-ground bonus; one moved OFF a wall loses it.
            float elevMult = wallMounted ? 1.25f : 1f;
            var movedDt = _selected.GetComponent<DefenseTower>();
            if (movedDt != null) movedDt.ElevationRangeMult = elevMult;
            var movedAt = _selected.GetComponent<ArcaneTower>();
            if (movedAt != null) movedAt.ElevationRangeMult = elevMult;

            UpdateLayoutEntry(_selected.itemId, oldCell, cell, ArmedYawQuarterSteps, ArmedYawOffsetDeg,
                snapped.y, wallMounted);

            // F8-51 — the job key is cell-derived: a move mid-build/upgrade re-keys the
            // in-flight job so its completion still finds this structure. No-op when idle.
            if (DeNelle.Core.FeatureFlags.BuildTimers)
            {
                string newKey = $"{_selected.itemId}@{cell.x}_{cell.y}";
                BuildTimerService.Instance?.RepointJob(
                    $"{_selected.itemId}@{oldCell.x}_{oldCell.y}", newKey);
                _selected.GetComponent<UnderConstructionVisual>()?.Rekey(newKey);
            }

            Debug.Log($"[BuildMode] Moved '{_selected.itemId}' to cell ({cell.x},{cell.y}) yaw {ArmedYawDegrees:F0}° (free).");

            FlowTrace.Step("BuildMove",
                $"MOVE COMMITTED id='{_selected.itemId}' ({oldCell.x},{oldCell.y}) -> ({cell.x},{cell.y}) " +
                $"yaw={ArmedYawDegrees:F0}deg wallMounted={wallMounted} worldY={snapped.y:F2} " +
                $"level={_selected.level} (PRESERVED). New cells OCCUPIED, origin cells were freed at " +
                "BeginMoveSelected and stay free; UpdateLayoutEntry re-keyed the persisted BaseLayout " +
                "row from the old cell to the new one, and any in-flight build/upgrade job was " +
                "repointed to the new cell-derived key. COST: zero - MOVE is free by ruling.");

            _movingSelected = false;
            _ghost?.Hide();

            // Re-show the action panel on the moved structure (stays selected).
            ShowSelectionPanel(_selected);
        }

        /// <summary>Abort an in-progress move: re-occupy the origin cells, keep it put.</summary>
        private void CancelMove()
        {
            bool was = _movingSelected;
            _movingSelected = false;
            _ghost?.Hide();
            if (_selected != null)
                _grid?.Occupy(_moveOriginCell, _selected.footprint, _selected.itemId);
            if (was)
                FlowTrace.Step("BuildMove",
                    $"MOVE CANCELLED id='{(_selected != null ? _selected.itemId : "<null>")}' - origin cells " +
                    $"({_moveOriginCell.x},{_moveOriginCell.y}) RE-OCCUPIED, the piece never moved and nothing " +
                    "was charged. A cancel that failed to re-occupy would leave a hole another structure " +
                    "could be dropped into, so the re-occupy is the load-bearing half of this method.");
        }

        /// <summary>
        /// Display refund for the sell panel: the SUM of the 50% multi-resource refund
        /// across all four pools, rounded down. The panel shows a single "◆ N" value,
        /// so we surface the total units refunded; the actual per-resource refund is
        /// applied by <see cref="RefundLedger"/>. For a crystals-only entry this equals
        /// 50% of buildCost, so the old display is unchanged.
        /// </summary>
        private static int RefundFor(PlacedStructure ps)
        {
            var r = RefundCostFor(ps);
            return r.wood + r.food + r.iron + r.crystals;
        }

        /// <summary>
        /// The full ~50% multi-resource refund for a placed structure: the build cost PLUS
        /// every upgrade step it has paid into (S5 — invested-cost-aware), each slot halved
        /// (rounded down). So selling a tier-3 tower returns 50% of build + L1→L2 + L2→L3.
        /// Crystals-only entries refund only Crystals (their buildCost fallback halved).
        /// </summary>
        private static DeNelle.Core.Catalog.ResourceCost RefundCostFor(PlacedStructure ps)
        {
            if (ps == null) return default;
            var entry = CatalogRegistry.Get(ps.itemId);

            // Base build cost…
            var total = CostFor(entry);

            // …plus each upgrade step paid to reach the current level (L1→L2, …, (lvl-1)→lvl).
            int level = Mathf.Max(1, ps.level);
            for (int from = 1; from < level; from++)
            {
                var step = UpgradeCostFor(entry, from);
                total.wood     += step.wood;
                total.food     += step.food;
                total.iron     += step.iron;
                total.crystals += step.crystals;
            }

            // WO-676 STEWARD (Salvager): ONE HeroTalentModifiers read at this existing
            // refund calc — `salvage` boosts the 50% sell refund (e.g. +0.15 => 57.5% of
            // invested cost). StatSum is internally null-safe (0 with no service/tree/
            // nodes), so the refund is byte-identical to baseline at sum 0.
            float salvage = DeNelle.Village.Talents.HeroTalentModifiers.StatSum(
                HeroTalentClassReader.Slug(), "salvage");
            if (salvage > 0f)
                DeNelle.Core.Diagnostics.FlowTrace.Once("Talent", "salvage",
                    $"salvage x{1f + salvage:0.###} applied to sell refund (WO-676 Salvager).");

            return new DeNelle.Core.Catalog.ResourceCost
            {
                wood     = ApplySalvage(total.wood     / 2, salvage),
                food     = ApplySalvage(total.food     / 2, salvage),
                iron     = ApplySalvage(total.iron     / 2, salvage),
                crystals = ApplySalvage(total.crystals / 2, salvage),
            };
        }

        /// <summary>WO-676 Salvager: scale one halved refund slot by (1 + salvage sum); identity at 0.</summary>
        private static int ApplySalvage(int halfRefund, float salvageBonus)
            => salvageBonus > 0f ? Mathf.RoundToInt(halfRefund * (1f + salvageBonus)) : halfRefund;

        // =====================================================================
        //  Cost resolution + ledger boundary (S4 — multi-resource economy)
        // =====================================================================

        /// <summary>
        /// Resolve a catalog entry's build cost to the Core multi-resource shape, with
        /// the crystals-only FALLBACK: if the entry authored a non-zero multi-cost
        /// (repo.cost), use it verbatim; otherwise charge repo.buildCost Crystals so
        /// legacy / cost-less rows never regress. Null-safe (returns a free cost).
        /// </summary>
        public static DeNelle.Core.Catalog.ResourceCost CostFor(CatalogEntry entry)
        {
            var repo = entry != null ? entry.repo : null;
            if (repo == null) return default;
            if (!repo.cost.IsZero) return repo.cost;                       // multi-cost wins
            return new DeNelle.Core.Catalog.ResourceCost { crystals = repo.buildCost };   // crystals fallback
        }

        /// <summary>
        /// FTUE founding kit (owner ruling 2026-07-17): the exact structure ids the
        /// founding tutorial steps force the player to place while they still have ZERO
        /// starting resources (v32 zeroed StartingBudget). These are EXEMPT from the
        /// general one-free-total rule so a first run can never soft-lock at founding:
        ///   pet-house            <- founding_hollow  (build.structure_placed:pet-house)
        ///   collector_lumbermill <- founding_stores  (build.structure_placed:collector_lumbermill)
        ///   lumberyard           <- founding_stores, the ACCEPTED EQUIVALENT (TutorialFlow
        ///                           .BuildIdMatches treats the wood ids as one building), so it
        ///                           stays exempt: a player who places the Lumberyard instead
        ///                           also completes the step and must not be charged for it.
        ///   tower_ground_archer  <- founding_defense (build.tower_placed -- the canonical
        ///                           founding tower: cheapest Tower row, the AutoPilot's
        ///                           tutorial first-tower; a non-archer tower still comes
        ///                           free via the untouched general one-free-total below).
        /// Verified against StreamingAssets/Data/Canonical/tutorial/tutorial-steps.json.
        /// Each seeds free ONCE (per-id), independent of the general freebie.
        ///
        /// F8 seq 632 ROOT CAUSE 2 (2026-08-02): founding_stores was retargeted from
        /// `lumberyard` to `collector_lumbermill`. The catalog proves why: `lumberyard` is
        /// behaviorId GameplayBuilding with storageCapacity 1000 (was 500 before WO-966) /
        /// storageResource "wood" -- a
        /// STOCKPILE that stores timber and harvests nothing -- while `collector_lumbermill`
        /// (displayName "Lumbermill") is behaviorId ResourceCollector, the card that actually
        /// harvests. The step's own copy said "it harvests timber for you", so the awaited id
        /// and the promise disagreed. `collector_lumbermill` MUST be exempt here or the
        /// retargeted step soft-locks a v32 zero-resource founding (it costs wood 40 / food 20 /
        /// iron 30).
        /// </summary>
        private static readonly HashSet<string> FoundingKit =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "pet-house",
                "collector_lumbermill",
                // "lumberyard" REMOVED 2026-08-07 — WO-837 is BINDING canon: stockpiles
                // (lumberyard / foundry / silo) are CAPACITY-CAP PROGRESSION buildings, never
                // founding freebies. A free lumberyard hands the player +500 wood cap for nothing,
                // which is the whole progression step it is supposed to sell. WO-901 §5 named this
                // as a hard blocker on its Phase F and Phase F shipped past it anyway.
                // NOTE collector_lumbermill above is a DIFFERENT building and stays: it is a
                // harvester, not a stockpile, and the tutorial step awaits it (removing it
                // soft-locks a zero-resource founding).
                "tower_ground_archer",
            };

        /// <summary>
        /// Wooden starter towers whose FIRST TWO placements TOTAL are free (owner ruling
        /// 2026-07-24: "only the first 2 towers, both WOODEN, are free; everything else
        /// including Arcane is paid from placement 1"). Only the basic wooden Archer Tower
        /// qualifies -- the Ballista/Wizard tower carries a Crystal cost and the Arcane
        /// Spire / arcane-tower are magic towers, so NONE of them are wooden starters and
        /// all are charged from the first placement. <see cref="tower_ground_archer"/> is
        /// ALSO in <see cref="FoundingKit"/>; the wooden 2-cap is checked first and is the
        /// MORE generous rule, so it supersedes (and still honours) the founding per-id
        /// freebie for the first archer tower.
        /// </summary>
        private static readonly HashSet<string> WoodenTowerFreeKit =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "tower_ground_archer",
            };

        /// <summary>How many wooden-tower placements are free in total (owner 2026-07-24).</summary>
        private const int WoodenTowerFreeCap = 2;

        /// <summary>
        /// Placement freebie policy (owner ruling 2026-07-24, LOCKED -- "lay out a full
        /// starter town without a resource wall"). Three lanes, checked in this order:
        ///  - WOODEN STARTER TOWERS (see <see cref="WoodenTowerFreeKit"/>): the first
        ///    <see cref="WoodenTowerFreeCap"/> (=2) wooden-tower placements TOTAL are free --
        ///    free while the count of wooden ids already in <c>GameState.FreeBuildsUsed</c> is
        ///    under the cap, charged from the third on. Checked FIRST so the more-generous
        ///    2-cap governs the archer tower.
        ///  - NON-WOODEN TOWERS: PAID from placement #1. Tower-ness is read from the CATALOG
        ///    (<c>entry.type == CatalogType.Tower</c>, with the DefenseTower/ArcaneTower
        ///    <c>repo.behaviorId</c> as a belt-and-braces fallback) -- NOT a fragile id-prefix
        ///    guess -- so arcane-tower / tower_arcane_spire / tower_wall_wizard /
        ///    tower_siege_tower / tower_catapult and every other magic/heavy tower is charged
        ///    from the very first placement.
        ///  - EVERYTHING ELSE (any NON-tower building): FIRST placement of EACH distinct
        ///    building id is FREE; a second of the same id is PAID. Free while THIS id is not
        ///    yet in the ledger (per-id first-of-each-type freebie). This subsumes the old
        ///    founding lane -- pet-house / lumberyard are non-tower buildings, so first-of-each
        ///    free still founds a zero-resource first run -- and covers farm / lumbermill /
        ///    forge / market / jeweler / echo-hollow / pet-house / etc.
        /// The ledger never resets (selling/destroying a building does not restore a
        /// freebie). Service-less/state-less = no freebie (fall back to normal cost,
        /// never a free exploit path). Additive default: a never-written list is treated
        /// as empty, so old/new saves alike still have their freebies live.
        /// </summary>
        public static bool FreeBuildAvailable(CatalogEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) return false;
            var st = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (st == null) return false;
            var used = st.FreeBuildsUsed;

            // Lane 1 -- WOODEN STARTER TOWERS: the first WoodenTowerFreeCap (2) placements
            // TOTAL are free. Count how many wooden ids are already committed in the never-
            // reset ledger; free while under the cap, charged from then on. Checked FIRST so
            // it governs the archer tower with the more-generous 2-cap.
            if (WoodenTowerFreeKit.Contains(entry.id))
            {
                if (used == null) return true;   // additive default -- nothing burned yet
                int woodenUsed = 0;
                foreach (var id in used)
                    if (WoodenTowerFreeKit.Contains(id)) woodenUsed++;
                return woodenUsed < WoodenTowerFreeCap;
            }

            // Lane 2 -- NON-WOODEN TOWERS: PAID from placement #1. Classify tower-ness from the
            // CATALOG (entry.type == CatalogType.Tower, plus the DefenseTower/ArcaneTower
            // behaviorId as a fallback for any tower row that omitted the type) -- never an
            // id-prefix guess. The wooden lane above already claimed the free towers, so any
            // tower reaching here (arcane-tower, tower_arcane_spire, tower_wall_wizard,
            // tower_siege_tower, tower_catapult, ...) is charged.
            // WO-855: classification moved to the ONE shared IsTowerEntry helper (same
            // predicate, verbatim) so the freebie lane and the tower softcap can never
            // disagree about what a tower is.
            if (IsTowerEntry(entry)) return false;

            // Lane 3 -- EVERYTHING ELSE (any NON-tower building): the FIRST placement of each
            // distinct building id is FREE, a second of the same id is PAID. Free while THIS id
            // is not yet in the never-reset ledger. Subsumes the old founding lane (pet-house /
            // lumberyard are non-tower buildings) and unwalls the full starter town.
            return used == null || !used.Contains(entry.id);
        }

        // =====================================================================
        //  WO-855 Phase 1 -- TOWER SPAM SOFTCAP (the ONE placement cost multiplier)
        // ---------------------------------------------------------------------
        //  Measured baseline (WO-855 Phase 0): NOTHING limited tower count. No cap, no
        //  singleton flag on any tower row, flat cost -- the 1st and the 50th archer cost
        //  the same, so "wall of towers" was the dominant (and free) strategy.
        //
        //  The fix is ONE multiplier applied at ONE place: the NEW-PLACEMENT cost path.
        //  It deliberately lives in EffectiveCostFor / SoftcappedCostFor and NOT in
        //  CostFor, because CostFor is ALSO the base of UpgradeCostFor (2431) and
        //  RefundCostFor (2579) -- putting the multiply there would inflate every upgrade
        //  step AND every sell refund, which WO-855 sec.5 forbids ("Does not apply to
        //  upgrades of existing towers (only new place)").
        //
        //  Curve (WO-855 sec.5, "linear" mode). `ordinal` is the 1-based index of the tower
        //  BEING placed (= live count + 1), which is how the WO's own worked example reads:
        //  "place when count already 4 -> 5th uses startAtCount=5 -> mult = 1+0.15 = 1.15".
        //      ordinal <  5 : x1.00   (the first four towers are un-surcharged)
        //      ordinal >= 5 : min(3.0, 1 + (ordinal - 5 + 1) * 0.15)
        //  So the 5th costs x1.15, the 8th x1.60, the 18th hits the x3.0 ceiling.
        //
        //  FREEBIES ARE UNTOUCHED (owner-locked, see FreeBuildAvailable): the first two
        //  wooden archer placements and the first placement of each distinct non-tower id
        //  short-circuit to a ZERO cost BEFORE the multiplier is ever consulted. Starting
        //  wood/iron are 0, so those freebies ARE the starting budget -- a softcap that
        //  charged placement #1 or #2 would soft-lock a fresh save.
        // =====================================================================

        /// <summary>WO-855: the 1-based placement ordinal at which the tower surcharge starts (the first 4 are free of it).</summary>
        public const int TowerSoftcapStartAtOrdinal = 5;

        /// <summary>WO-855: linear surcharge added per tower at/after <see cref="TowerSoftcapStartAtOrdinal"/>.</summary>
        public const float TowerSoftcapMultPerExtra = 0.15f;

        /// <summary>WO-855: hard ceiling on the tower surcharge (never more than 3x the authored cost).</summary>
        public const float TowerSoftcapMaxMult = 3.0f;

        /// <summary>
        /// PURE curve: the cost multiplier for the tower at 1-based <paramref name="placementOrdinal"/>.
        /// Monotonic non-decreasing, 1.0 below the start ordinal, clamped at
        /// <see cref="TowerSoftcapMaxMult"/>. No world/state reads -- the oracle drives this directly.
        /// </summary>
        public static float TowerSoftcapMultiplier(int placementOrdinal)
        {
            if (placementOrdinal < TowerSoftcapStartAtOrdinal) return 1f;
            int extras = placementOrdinal - TowerSoftcapStartAtOrdinal + 1;
            return Mathf.Min(TowerSoftcapMaxMult, 1f + extras * TowerSoftcapMultPerExtra);
        }

        /// <summary>
        /// Is this catalog row a TOWER? Read from the CATALOG (<c>entry.type == CatalogType.Tower</c>)
        /// with the DefenseTower/ArcaneTower <c>repo.behaviorId</c> as a belt-and-braces fallback for a
        /// row that omitted the type -- never an id-prefix guess. This is the SINGLE tower classifier;
        /// both the freebie lanes (<see cref="FreeBuildAvailable"/>) and the WO-855 softcap read it, so
        /// "what counts as a tower" can never drift between the two rules.
        /// </summary>
        public static bool IsTowerEntry(CatalogEntry entry)
        {
            if (entry == null) return false;
            if (entry.type == CatalogType.Tower) return true;
            var repo = entry.repo;
            return repo != null
                && (string.Equals(repo.behaviorId, "DefenseTower", System.StringComparison.OrdinalIgnoreCase)
                 || string.Equals(repo.behaviorId, "ArcaneTower",  System.StringComparison.OrdinalIgnoreCase));
        }

        // Live-count cache. EffectiveCostFor runs EVERY FRAME while a ghost is armed
        // (IsValidPlacement gate 5), so an unguarded FindObjectsByType would allocate an
        // array per frame on a mobile target. A short realtime TTL keeps the count honest
        // (a sold tower is reflected within half a second) at one scan per half second;
        // Place() and the oracle call InvalidateTowerCount() for an immediate recount.
        private const float TowerCountTtlSeconds = 0.5f;
        private static int   s_towerCountCache = -1;
        private static float s_towerCountStamp = -1f;

        /// <summary>Force the next <see cref="LiveTowerCount"/> to re-scan the world (call after a place / sell).</summary>
        public static void InvalidateTowerCount()
        {
            s_towerCountCache = -1;
            s_towerCountStamp = -1f;
        }

        /// <summary>
        /// How many PLAYER towers stand in the world right now. Two DISJOINT component sets are
        /// summed because the game has two tower-raising lanes and neither sees the other:
        ///   * <see cref="PlacedStructure"/> whose catalog row is a tower -- the Build-Mode /
        ///     BaseLayout lane (BaseLayoutLoader.Spawn is the only thing that adds the component);
        ///   * <see cref="Tower"/> -- the legacy Build-Menu lane (TowerConstructionQueue /
        ///     TowerPersistenceService are the only things that add it).
        /// They never co-occur on one object, so the sum cannot double-count. Deliberately NOT
        /// counting <see cref="DefenseTower"/> components: raid arenas and enemy strongholds carry
        /// EnemyOwned garrison turrets, and baked scene towers would surcharge the player's very
        /// FIRST placement. No save field (WO-855 forbids one) -- this is a live world read.
        /// </summary>
        public static int LiveTowerCount()
        {
            float now = Time.realtimeSinceStartup;
            if (s_towerCountCache >= 0 && s_towerCountStamp >= 0f && now - s_towerCountStamp < TowerCountTtlSeconds)
                return s_towerCountCache;

            int count = 0;
            var placed = Object.FindObjectsByType<PlacedStructure>(FindObjectsSortMode.None);
            for (int i = 0; i < placed.Length; i++)
            {
                var ps = placed[i];
                if (ps == null || string.IsNullOrEmpty(ps.itemId)) continue;
                if (IsTowerEntry(CatalogRegistry.Get(ps.itemId))) count++;
            }
            count += Object.FindObjectsByType<Tower>(FindObjectsSortMode.None).Length;

            s_towerCountCache = count;
            s_towerCountStamp = now;
            return count;
        }

        /// <summary>
        /// PURE: apply the WO-855 tower softcap to <paramref name="baseCost"/> given
        /// <paramref name="liveTowerCount"/> towers already standing. Non-tower rows and a
        /// multiplier of 1 return the base cost VERBATIM (byte-identical to the pre-WO-855
        /// behaviour). Each slot is CEILed so a surcharge can never round away to nothing.
        /// The oracle drives this overload directly -- no world reads, fully deterministic.
        /// </summary>
        public static DeNelle.Core.Catalog.ResourceCost ApplyTowerSoftcap(
            CatalogEntry entry, DeNelle.Core.Catalog.ResourceCost baseCost, int liveTowerCount)
        {
            if (!IsTowerEntry(entry)) return baseCost;
            int ordinal = Mathf.Max(1, liveTowerCount + 1);
            float mult = TowerSoftcapMultiplier(ordinal);
            if (mult <= 1f) return baseCost;

            var scaled = new DeNelle.Core.Catalog.ResourceCost
            {
                wood     = Mathf.CeilToInt(baseCost.wood     * mult),
                food     = Mathf.CeilToInt(baseCost.food     * mult),
                iron     = Mathf.CeilToInt(baseCost.iron     * mult),
                crystals = Mathf.CeilToInt(baseCost.crystals * mult),
            };
            // Sec.12 proving line: a capture must show the REAL numbers (ordinal + multiplier +
            // before/after cost). Keyed by id+ordinal so each new tower logs exactly once
            // instead of once per ghost frame.
            FlowTrace.Once("Economy", $"softcap:{entry.id}:{ordinal}",
                $"tower softcap x{mult:0.##} on '{entry.id}' -- placement #{ordinal} " +
                $"(live towers {liveTowerCount}, startAt {TowerSoftcapStartAtOrdinal}, " +
                $"perExtra {TowerSoftcapMultPerExtra:0.##}, cap x{TowerSoftcapMaxMult:0.##}): " +
                $"w{baseCost.wood}/f{baseCost.food}/i{baseCost.iron}/c{baseCost.crystals} -> " +
                $"w{scaled.wood}/f{scaled.food}/i{scaled.iron}/c{scaled.crystals}");
            return scaled;
        }

        /// <summary>
        /// The authored build cost WITH the WO-855 tower softcap applied for the CURRENT live
        /// tower count -- but WITHOUT the placement freebie. This is what a surface should read
        /// when it prices a build through a path that does NOT burn the freebie ledger (the
        /// legacy Build Menu), so pricing can never hand out an unlimited free tower.
        /// Surfaces that go through the freebie-consuming Build-Mode commit read
        /// <see cref="EffectiveCostFor"/> instead.
        /// </summary>
        public static DeNelle.Core.Catalog.ResourceCost SoftcappedCostFor(CatalogEntry entry)
            => ApplyTowerSoftcap(entry, CostFor(entry), LiveTowerCount());

        /// <summary>
        /// The cost the player actually pays: ZERO (all components — wood/iron/food/
        /// crystals) while the entry's first-build freebie is live, else CostFor WITH the
        /// WO-855 tower softcap. The ONE cost reader for the ghost validator, the palette,
        /// the info panel and the Place() commit, so every surface agrees with the ledger.
        /// Freebie is checked FIRST and short-circuits, so the owner-locked free placements
        /// (2 wooden archers + first-of-each non-tower id) stay exactly free -- the softcap
        /// can never charge a fresh, zero-resource save for placement #1 or #2.
        /// </summary>
        public static DeNelle.Core.Catalog.ResourceCost EffectiveCostFor(CatalogEntry entry)
            => FreeBuildAvailable(entry) ? default : SoftcappedCostFor(entry);

        /// <summary>Map the Core cost to EconomyService.ResourceCost (1:1 field copy).</summary>
        public static ResourceCost ToEconomy(DeNelle.Core.Catalog.ResourceCost c)
            => new ResourceCost(c.wood, c.food, c.iron, c.crystals);

        /// <summary>
        /// Affordability via the persisted multi-resource ledger (EconomyService). Falls
        /// back to the crystal wallet read if the service isn't up yet, so the build
        /// menu still gates on Crystals in a service-less edge case.
        /// </summary>
        // internal (was private) so the ONE placed-structure upgrade start path
        // (PlacedStructureUpgradeService) gates on the SAME wallet read this controller does,
        // instead of growing a second affordability rule.
        internal static bool CanAfford(DeNelle.Core.Catalog.ResourceCost cost)
        {
            var econ = EconomyService.Instance;
            if (econ != null) return econ.CanAfford(ToEconomy(cost));
            // Service-less fallback: only Crystals are GameState-readable here.
            return CrystalBalance >= cost.crystals;
        }

        /// <summary>
        /// WO-394 — a specific "Not enough &lt;Resource&gt; (N)" message for an unaffordable
        /// cost: finds the FIRST resource pool the player can't cover and names it + the
        /// amount needed, so the rejection reason is concrete (not generic). Falls back to
        /// "Not enough resources" if every pool is somehow covered (shouldn't happen on the
        /// CannotAfford path) or the service is absent.
        /// </summary>
        public static string ShortfallMessage(DeNelle.Core.Catalog.ResourceCost cost)
        {
            // ── WO-1425 PASS 1 — is this a CEILING, not a shortfall? ─────────────────
            // "Not enough Wood (3150)" against a bank sitting FULL at 3000/3000 is
            // indistinguishable from a permanent wall, and nothing else in the game says
            // otherwise. MEASURED this session: structures-catalog 'tower_ground_archer'
            // upgradeCost[1] authors 3150 wood; MaxOf(Wood) with a level-1 lumberyard is
            // 2000 + 1000 = 3000. Saving up can NEVER close that gap -- only storage can,
            // so the sentence must name the container and the level. This is the ONLY
            // link between a refusal and TownBankCapacity, and its absence is the bug.
            // Runs BEFORE the ledger read because the ceiling is true with or without
            // EconomyService, and it is checked across ALL capped resources rather than
            // stopping at the first short one: tower_ballista L2->L3 costs 2100 wood
            // (fits at lumberyard L1) AND 3500 iron (needs foundry L2), so naming only
            // the first short pool would leave the permanent iron wall invisible.
            string capBlock = CapBlockMessage(cost);
            if (!string.IsNullOrEmpty(capBlock)) return capBlock;

            var econ = EconomyService.Instance;
            if (econ != null)
            {
                if (cost.wood     > 0 && !econ.CanAfford(new ResourceCost(cost.wood, 0, 0, 0)))     return $"Not enough Wood ({cost.wood})";
                if (cost.iron     > 0 && !econ.CanAfford(new ResourceCost(0, 0, cost.iron, 0)))     return $"Not enough Iron ({cost.iron})";
                if (cost.food     > 0 && !econ.CanAfford(new ResourceCost(0, cost.food, 0, 0)))     return $"Not enough Stone ({cost.food})";
                if (cost.crystals > 0 && !econ.CanAfford(new ResourceCost(0, 0, 0, cost.crystals))) return $"Not enough Crystals ({cost.crystals})";
            }
            else if (cost.crystals > 0 && CrystalBalance < cost.crystals)
            {
                return $"Not enough Crystals ({cost.crystals})";
            }
            return "Not enough resources";
        }

        /// <summary>
        /// WO-1425 — the cap-block sentence for a cost, or "" when nothing in it exceeds what the
        /// town bank can hold. PUBLIC so a card / pill can show the SAME words the refusal will,
        /// instead of growing a second copy of the story.
        ///
        /// <para>Crystals and Coins are UNCAPPED by design (TownBankCapacity.UncappableResources,
        /// owner ruling 2026-08-04) and are deliberately absent — a cap sentence for crystals would
        /// be a fabricated wall.</para>
        ///
        /// <para>All three capped pools are examined and the DEEPEST blocker leads (highest required
        /// container level; an unreachable one first), because that is the gate the player actually
        /// has to clear. Any other blocked pool is named in a trailing clause so clearing the first
        /// one does not simply reveal the next invisible wall.</para>
        ///
        /// <para>PURE presentation: it reads caps and returns words. No cost, cap, multiplier or
        /// wallet is touched (architecture law — presentation never touches the objects).</para>
        /// </summary>
        public static string CapBlockMessage(DeNelle.Core.Catalog.ResourceCost cost)
        {
            var blocks = new List<DeNelle.Core.Economy.TownBankCapacity.StorageBlock>(3);
            TryAddCapBlock(DeNelle.Core.Economy.BankResource.Wood, cost.wood, blocks);
            TryAddCapBlock(DeNelle.Core.Economy.BankResource.Iron, cost.iron, blocks);
            TryAddCapBlock(DeNelle.Core.Economy.BankResource.Food, cost.food, blocks);   // displayed "Stone"
            if (blocks.Count == 0) return "";

            int worst = 0;
            for (int i = 1; i < blocks.Count; i++)
            {
                if (RankOf(blocks[i]) > RankOf(blocks[worst])) worst = i;
            }

            string msg = DeNelle.Core.Economy.TownBankCapacity.DescribeStorageBlock(blocks[worst]);
            for (int i = 0; i < blocks.Count; i++)
            {
                if (i == worst) continue;
                // Same thousands-separated, culture-invariant shape the lead sentence uses --
                // "3,000" then "3500" in one message reads as two different systems talking.
                msg += " Also over your " + blocks[i].ResourceName + " ceiling of "
                     + blocks[i].CurrentMax.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                     + ": needs "
                     + blocks[i].Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + ".";
            }
            return msg;
        }

        /// <summary>Depth of a cap block: an UNREACHABLE ladder outranks every reachable level, then
        /// the higher required level, then the larger amount. Deterministic — never list order.</summary>
        private static long RankOf(DeNelle.Core.Economy.TownBankCapacity.StorageBlock b)
            => (b.RequiredContainerLevel < 0 ? 1000L : b.RequiredContainerLevel) * 1000000L + b.Amount;

        private static void TryAddCapBlock(DeNelle.Core.Economy.BankResource r, int amount,
                                           List<DeNelle.Core.Economy.TownBankCapacity.StorageBlock> into)
        {
            if (amount <= 0) return;
            if (DeNelle.Core.Economy.TownBankCapacity.TryDescribeStorageBlock(r, amount, out var b))
                into.Add(b);
        }

        /// <summary>
        /// Spend the cost through the ledger (atomic). Returns TRUE only when the cost was actually
        /// deducted (a free cost counts as paid); FALSE means the ledger DECLINED and NOTHING moved.
        /// <para/>
        /// 2026-08-02: this used to return void and THROW TrySpend's bool away, so a declined spend
        /// still produced a building - a live free-structure path. Every caller MUST honour the return.
        /// A spend that cannot be PROVEN is never reported as made: with no economy service a
        /// wood/food/iron cost cannot be charged at all, so it returns false rather than pretending.
        /// </summary>
        public static bool ChargeLedger(DeNelle.Core.Catalog.ResourceCost cost)
        {
            if (cost.IsZero) return true;
            var econ = EconomyService.Instance;
            if (econ != null)
            {
                if (econ.TrySpend(ToEconomy(cost))) return true;
                FlowTrace.Warn("Build", $"ChargeLedger: EconomyService DECLINED {Describe(cost)} -- nothing was deducted.");
                return false;
            }
            // Service-less fallback: charge the persisted crystal wallet directly, but ONLY when it
            // can actually cover the crystal slot.
            if (cost.crystals > 0)
            {
                var gs = GameStateService.Instance;
                if (gs == null || CrystalBalance < cost.crystals)
                {
                    FlowTrace.Warn("Build", $"ChargeLedger: no economy service and the crystal wallet cannot cover {cost.crystals} -- nothing deducted.");
                    return false;
                }
                gs.AddCrystals(-cost.crystals);
                return true;
            }
            FlowTrace.Warn("Build", $"ChargeLedger: no economy service -- {Describe(cost)} could NOT be charged.");
            return false;
        }

        /// <summary>
        /// Refund the cost through the ledger (Grant). No-op for a free cost. PUBLIC since
        /// 2026-08-04: TowerPlacementSystem's cancelled-placement refund routes through this
        /// SAME idiom rather than hand-rolling a crystal grant (which is how a cancelled
        /// wood+iron build paid out crystals). One refund site, one behaviour.
        /// </summary>
        public static void RefundLedger(DeNelle.Core.Catalog.ResourceCost cost)
        {
            if (cost.IsZero) return;
            var econ = EconomyService.Instance;
            if (econ != null) { econ.Grant(ToEconomy(cost)); return; }
            // Service-less fallback: refund the persisted crystal wallet directly.
            if (cost.crystals > 0) GameStateService.Instance?.AddCrystals(+cost.crystals);
        }

        /// <summary>Compact human-readable cost string for logs (skips zero slots).</summary>
        private static string Describe(DeNelle.Core.Catalog.ResourceCost c)
        {
            if (c.IsZero) return "nothing";
            var parts = new List<string>(4);
            if (c.wood     > 0) parts.Add($"{c.wood} wood");
            if (c.food     > 0) parts.Add($"{c.food} stone");
            if (c.iron     > 0) parts.Add($"{c.iron} iron");
            if (c.crystals > 0) parts.Add($"{c.crystals} crystals");
            return string.Join(", ", parts);
        }

        // ── BaseLayout sync (struct list — match by cell + itemId) ───────────────

        /// <summary>Remove the persisted record matching <paramref name="itemId"/> at <paramref name="cell"/>.</summary>
        private static void RemoveLayoutEntry(string itemId, Vector2Int cell)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            var layout = state != null ? state.BaseLayout : null;
            if (layout == null) return;
            for (int i = layout.Count - 1; i >= 0; i--)
            {
                if (layout[i].itemId == itemId && layout[i].cellX == cell.x && layout[i].cellZ == cell.y)
                {
                    layout.RemoveAt(i);
                    // StructureSingleton v2: a sell may have removed the LAST representation
                    // of a singleton id - notify the authority so it can resurface the baked
                    // twin (post-sell) and raise SingletonReleased. No-op for non-singletons.
                    Guard.Try("Singleton", $"NotifyRemoved('{itemId}') after layout remove",
                        () => StructureSingleton.NotifyRemoved(itemId));
                    return;
                }
            }
        }

        /// <summary>
        /// Re-point the persisted record from <paramref name="oldCell"/> to
        /// <paramref name="newCell"/> + yaw. PlacedStructureData is a struct, so we
        /// replace the element by index (not mutate a copy).
        /// </summary>
        private static void UpdateLayoutEntry(string itemId, Vector2Int oldCell, Vector2Int newCell, int yawSteps,
            float yawOffset = 0f, float worldY = 0f, bool wallMounted = false)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            var layout = state != null ? state.BaseLayout : null;
            if (layout == null) return;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].itemId == itemId && layout[i].cellX == oldCell.x && layout[i].cellZ == oldCell.y)
                {
                    var d = layout[i];
                    d.cellX = newCell.x;
                    d.cellZ = newCell.y;
                    d.yawSteps = yawSteps;
                    d.yawOffset = yawOffset;      // WO-673 L5 — persist the 45° half-step across a move
                    d.worldY = worldY;            // keep the seat height across a move (wall-top vs ground)
                    d.wallMounted = wallMounted;  // keep the elevation-perk flag across a move
                    layout[i] = d;
                    return;
                }
            }
        }

        /// <summary>
        /// S5 — re-stamp the persisted record's upgrade level (match by cell + itemId).
        /// PlacedStructureData is a struct, so replace the element by index. Exit()'s Save()
        /// then round-trips the new level so an upgraded structure reloads at its tier.
        /// </summary>
        // Internal (F8-51): CompletedUpgradeApplier persists a timer-finished level here even
        // when the live PlacedStructure is not spawned (offline sweep before BaseLayoutLoader).
        internal static void UpdateLayoutLevel(string itemId, Vector2Int cell, int level)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            var layout = state != null ? state.BaseLayout : null;
            if (layout == null) return;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].itemId == itemId && layout[i].cellX == cell.x && layout[i].cellZ == cell.y)
                {
                    var d = layout[i];
                    d.level = level;
                    layout[i] = d;
                    return;
                }
            }
        }

        // =====================================================================
        //  Seeding — first entry copies the default village into BaseLayout
        // =====================================================================

        /// <summary>
        /// First build-mode entry init. BaseLayout holds ONLY the player's added deltas
        /// (catalog placements) — never the baked default village. The baked village stays
        /// the scene default; BaseLayout is the delta layered on top.
        ///
        /// FIX (double-village / mass-rebuild): this used to copy every in-scene
        /// PlacedStructure (incl. baked GameplayBuildings + a stale prior-session
        /// loaded set) into BaseLayout. That double-counted the baked village and grew
        /// the persisted set, which Rebuild()/LoadFromState() then destroyed-and-
        /// respawned wholesale — making all prior pieces pop on at once. We no longer
        /// seed the baked buildings. The lazy-init of an empty list is preserved so the
        /// Place() add path has a list to append to. Idempotent: a non-empty layout is
        /// left untouched.
        /// </summary>
        private void SeedBaseLayoutIfFirstEntry()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null) return;
            if (state.BaseLayout != null && state.BaseLayout.Count > 0) return;

            // Ensure the delta list exists so Place() can append, but DO NOT seed it from
            // baked scene buildings — the baked village is the scene default, not a delta.
            if (state.BaseLayout == null) state.BaseLayout = new List<PlacedStructureData>();
        }

        // =====================================================================
        //  Persist
        // =====================================================================

        private void CommitLayout()
        {
            // BaseLayout is mutated live as structures are placed; persist it now.
            // §12 CAPTURE (do NOT blind-guard hub scenes here — MainCastle_Hall is the HOME hub
            // where the player's base IS built; a blanket hub-skip would BREAK base persistence,
            // a worse regression than the "remembers prior play's towers" bug). This trace records
            // EXACTLY which scene each persist fires from, so an owner save-then-replay repro
            // pinpoints the wrong-scene persist before we pick the correctly-scoped fix. Pairs with
            // BaseLayoutLoader.LoadFromState's replay trace to close the save↔load loop.
            var st = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            int n = st != null && st.BaseLayout != null ? st.BaseLayout.Count : 0;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            FlowTrace.Step("BuildMode",
                $"CommitLayout: persisting BaseLayout ({n} structure(s)) from scene '{scene}'.");
            GameStateService.Instance?.Save();
        }

        // =====================================================================
        //  Waves freeze / resume
        // =====================================================================

        private void FreezeWaves()
        {
            _frozenWaves.Clear();
            foreach (var wm in FindObjectsByType<WaveManager>())
            {
                if (wm == null || !wm.enabled) continue;
                wm.enabled = false;   // stops the wave loop's Update/coroutine progression
                _frozenWaves.Add(wm);
            }
        }

        private void ResumeWaves()
        {
            foreach (var wm in _frozenWaves)
                if (wm != null) wm.enabled = true;
            _frozenWaves.Clear();
        }

        // =====================================================================
        //  Camera overview
        // =====================================================================

        private void PullCameraBack()
        {
            // DEF-117 — drive the camera that is ACTUALLY rendering to the screen, not
            // the tag-resolved Camera.main (which can be a rogue / embedded FBX camera
            // once the game camera's per-frame sole-camera cull is paused below).
            _camera = ActiveScreenCamera();
            if (_camera == null) return;

            _savedCamPos = _camera.transform.position;
            _savedCamRot = _camera.transform.rotation;

            // Disable any camera-driver behaviours so they don't fight the overview.
            _disabledCamDrivers.Clear();
            foreach (var mb in _camera.GetComponents<MonoBehaviour>())
            {
                if (mb == null || !mb.enabled) continue;
                string n = mb.GetType().Name;
                if (n.IndexOf("Camera", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Cinemachine", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Brain", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mb.enabled = false;
                    _disabledCamDrivers.Add(mb);
                }
            }

            // DEF-117 (split-screen) — disabling the game camera's driver above also
            // pauses its EnforceSoleCamera() rogue-cull. So suppress EVERY other live
            // screen camera ourselves for the duration of build mode; otherwise a
            // second view (e.g. a runtime-spawned embedded FBX camera on a half
            // viewport) renders alongside the overview and the screen splits.
            SuppressRogueCameras();

            // Top-down overview centred on the grid. The overview owns the whole
            // screen (full viewport, top render priority) so nothing double-renders.
            _camera.rect = new Rect(0f, 0f, 1f, 1f);
            _camera.enabled = true;

            Vector3 centre = _grid != null
                ? _grid.CellToWorld(new Vector2Int(_grid.gridWidth / 2, _grid.gridHeight / 2))
                : Vector3.zero;
            // Seed the live pan/zoom state, then apply. The camera now LOOKS AT _camFocus
            // from _camHeight at the build pitch, so pan = move focus, zoom = change height.
            _camFocus  = new Vector3(centre.x, 0f, centre.z);
            _camHeight = Mathf.Clamp(_buildModeHeight, _camHeightMin, _camHeightMax);
            _camYaw    = 0f;   // reset orbit to the original framing on every entry
            _camDragging = false;
            ApplyBuildCamera();
        }

        /// <summary>Applied orbit yaw — the raw <see cref="_camYaw"/> SNAPPED to 45° detents
        /// (owner ruling: twist rotates the view in clean CoC steps). Zero at default.</summary>
        private float SnappedYaw => Mathf.Round(_camYaw / 45f) * 45f;

        /// <summary>
        /// Place the overview camera ORBITING <see cref="_camFocus"/> at the applied yaw
        /// (SnappedYaw) and the build pitch. At yaw 0 this is byte-identical to the original
        /// framing (back-and-up by the height, 45° down); a non-zero yaw swings the same
        /// focus/height offset around the Y axis so the view rotates without changing what
        /// it looks at. Driven by _camFocus / _camHeight / _camYaw so the pan/zoom/twist
        /// setters just mutate those and re-apply.
        /// </summary>
        private void ApplyBuildCamera()
        {
            if (_camera == null) return;
            float yaw = SnappedYaw;
            // Original (yaw 0) offset from the focus: back by height on Z, up by height on Y.
            Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, _camHeight, -_camHeight);
            _camera.transform.position = new Vector3(_camFocus.x, 0f, _camFocus.z) + offset;
            _camera.transform.rotation = Quaternion.Euler(_buildModePitch, yaw, 0f);
        }

        /// <summary>Clamp <see cref="_camFocus"/> to the grid (map) bounds so the view can't
        /// leave the world. Shared by the desktop pan loop and the touch pan setter.</summary>
        private void ClampFocusToMap()
        {
            if (_grid == null) return;
            float halfW = _grid.gridWidth  * _grid.cellSize * 0.5f;
            float halfH = _grid.gridHeight * _grid.cellSize * 0.5f;
            Vector3 mapCentre = _grid.origin + new Vector3(halfW, 0f, halfH);
            _camFocus.x = Mathf.Clamp(_camFocus.x, mapCentre.x - halfW, mapCentre.x + halfW);
            _camFocus.z = Mathf.Clamp(_camFocus.z, mapCentre.z - halfH, mapCentre.z + halfH);
        }

        // =====================================================================
        //  SME camera setters (Grok #8) — the Lean.Touch driver calls THESE
        //  instead of writing camera.transform.position (which would fight
        //  ApplyBuildCamera every frame). One finger = placement, two = camera.
        // =====================================================================

        /// <summary>
        /// Pan the overview by a two-finger SCREEN delta (LeanGesture.GetScreenDelta),
        /// converted to world on the camera's yaw-aware right/forward basis. Content
        /// follows the fingers (drag right pushes the world left), so the focus moves
        /// opposite the drag. Clamped to the map.
        /// </summary>
        public void PanFocusBy(Vector2 screenDelta, float metresPerPixel)
        {
            if (_camera == null) return;
            Vector3 fwd = _camera.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = _camera.transform.right; right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            _camFocus -= (right * screenDelta.x + fwd * screenDelta.y) * metresPerPixel;
            ClampFocusToMap();
            ApplyBuildCamera();
        }

        /// <summary>
        /// Zoom the overview by a pinch SCALE (LeanGesture.GetPinchScale): scale &gt; 1
        /// (fingers apart) LOWERS the camera height (zoom IN), matching the old driver's
        /// height/scale. Clamped to the height min/max. No-op for a non-positive scale.
        /// </summary>
        public void AdjustZoom(float pinchScale)
        {
            if (_camera == null || pinchScale <= 0f) return;
            _camHeight = Mathf.Clamp(_camHeight / pinchScale, _camHeightMin, _camHeightMax);
            ApplyBuildCamera();
        }

        /// <summary>
        /// Rotate the overview by a twist (LeanGesture.GetTwistDegrees). The accumulated
        /// yaw is continuous (so the twist read-assert sees it move); the APPLIED framing
        /// snaps to 45° detents (SnappedYaw in ApplyBuildCamera).
        /// </summary>
        public void AdjustYaw(float twistDegrees)
        {
            if (_camera == null || Mathf.Approximately(twistDegrees, 0f)) return;
            _camYaw += twistDegrees;
            ApplyBuildCamera();
        }

        /// <summary>
        /// Desktop pan + zoom for the build overview (touch already pans via the Lean
        /// driver). WASD / arrow keys + screen-edge scroll move the focus on the XZ plane;
        /// middle- or right-drag grabs the view; the scroll wheel zooms (changes height).
        /// The focus is CLAMPED to the grid (map) bounds so the player can't pan into the
        /// void. New Input System reads (Village runs with legacy Input disabled).
        /// </summary>
        private void UpdateBuildCameraPan()
        {
            if (_camera == null) return;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            float dt = Time.unscaledDeltaTime;

            // Camera-local right/forward projected onto the XZ plane (pitch-independent pan).
            Vector3 fwd = _camera.transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = _camera.transform.right; right.y = 0f; right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;

            // FIX #2 (2026-07-16) — while MOVING a selected structure, the arrow keys +
            // d-pad NUDGE the move ghost (UpdateMoveLoop), NOT the camera. Zero both from the
            // pan vector here (mirrors how dpadNudgesPending diverts the pad for a pending
            // drop) so one press can't pan the view AND nudge the building. The view holds.
            bool nudgingMovedStructure = _movingSelected;

            Vector2 move = Vector2.zero;   // x = strafe, y = forward
            if (kb != null && !nudgingMovedStructure)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  move.x -= 1f;
            }

            // WO-683 — the kit d-pad (HudMoveInput.Move, published by the build-overlay
            // d-pad / the HUD cross) merges into the SAME move vector as the arrow keys:
            // one merge point, so the armed ghost and the in-progress move follow the
            // pad exactly like the desktop key nudge. Reads zero on desktop (nothing
            // publishes), so the keyboard path is byte-identical there. Below the §3.1
            // 0.18 inner dead-zone the pad counts as no-press.
            // TWO-STEP (2026-07-13): while a pending drop is frozen in the CREATE loop,
            // the d-pad NUDGES the pending ghost (NudgePendingDropFromDpad) instead of
            // panning the view — skip the camera merge so one press can't do both.
            bool dpadNudgesPending = _armed != null && _dropPending && !_movingSelected;
            // FIX #2 — also zero the pad from the camera when moving a structure (it nudges
            // the ghost in UpdateMoveLoop instead).
            Vector2 dpad = (dpadNudgesPending || nudgingMovedStructure) ? Vector2.zero : ReadHudDpadMove();
            if (dpad.sqrMagnitude > DpadDeadZone * DpadDeadZone)
            {
                move += dpad;
                if (!_dpadConsumedTraced)
                {
                    _dpadConsumedTraced = true;
                    FlowTrace.Step("Build", $"d-pad move vector CONSUMED (first this build session): {dpad} — " +
                        "merged into the arrow-key pan vector; ghost/move follow it (WO-683).");
                }
            }

            // Edge-scroll — pointer near a screen border nudges the view that way. DESKTOP ONLY:
            // on touch (incl. WebGL on iPad) there is no hover, and the synthesized mouse reports
            // position (0,0) — which sits in BOTH the left and bottom edge bands and would drift the
            // view to the bottom-left every frame (owner repro: itch WebGL on iPad, building towers).
            // Touch pans via the drag driver, so skip edge-scroll when a touchscreen is present, and
            // guard the (0,0) null-pointer regardless.
            bool touchActive = UnityEngine.InputSystem.Touchscreen.current != null;
            if (mouse != null && _camEdgeBand > 0f && !touchActive)
            {
                Vector2 p = mouse.position.ReadValue();
                bool realPointer = p.x > 0.5f || p.y > 0.5f;   // (0,0) == no hover → don't edge-scroll
                if (realPointer)
                {
                    if (p.x <= _camEdgeBand)                    move.x -= 1f;
                    else if (p.x >= Screen.width - _camEdgeBand) move.x += 1f;
                    if (p.y <= _camEdgeBand)                    move.y -= 1f;
                    else if (p.y >= Screen.height - _camEdgeBand) move.y += 1f;
                }
            }

            if (move.sqrMagnitude > 1e-4f)
            {
                move = Vector2.ClampMagnitude(move, 1f);
                _camFocus += (right * move.x + fwd * move.y) * (_camPanSpeed * dt);
            }

            // Middle-drag grabs the view (screen-pixel delta → world pan). Right-drag also
            // pans, but ONLY when idle (nothing armed / not moving) — while armed/moving the
            // right button is reserved for Cancel (IBuildInput.Cancel), so a right-drag there
            // would disarm rather than pan. Touch pans via the Lean driver.
            if (mouse != null)
            {
                bool rightPanAllowed = _armed == null && !_movingSelected;
                bool dragHeld = mouse.middleButton.isPressed || (rightPanAllowed && mouse.rightButton.isPressed);
                Vector2 sp = mouse.position.ReadValue();
                if (dragHeld && !_camDragging) { _camDragging = true; _camDragLastPoint = sp; }
                else if (!dragHeld)            { _camDragging = false; }
                else if (_camDragging)
                {
                    Vector2 d = sp - _camDragLastPoint;
                    _camDragLastPoint = sp;
                    // Drag-right pushes the world left (grab metaphor) → subtract.
                    _camFocus -= (right * d.x + fwd * d.y) * _camDragSpeed;
                }

                // Scroll wheel → zoom (changes height).
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _camHeight = Mathf.Clamp(_camHeight - Mathf.Sign(scroll) * _camZoomStep, _camHeightMin, _camHeightMax);
            }

            // SINGLE-POINTER DRAG-TO-PAN (the web camera fix). Pointer.current unifies mouse-LEFT
            // and the primary TOUCH, so this one path serves desktop web and mobile web. Gated to
            // the IDLE view state (nothing armed, not moving a selection, no pending drop) so it can
            // never fight placement — while armed/moving, the d-pad + two-step flow own the pointer.
            // A tap must still place/select, so a hold only becomes a pan once it travels past
            // PtrPanDragThreshold; a press that starts over the top HUD band is ignored (that's the
            // wallet/tab/PLACE chrome, not the map).
            var ptr = UnityEngine.InputSystem.Pointer.current;
            bool ptrPanAllowed = _armed == null && !_movingSelected && !_dropPending;
            if (ptr != null && ptrPanAllowed)
            {
                bool pressed = ptr.press.isPressed;
                Vector2 pp = ptr.position.ReadValue();
                // Don't start a pan on the HUD chrome (top 12% band = BuildHud wallet/tab/PLACE row).
                bool startedOnHud = pp.y >= Screen.height * 0.88f;

                if (pressed && !_ptrDown)              // press down this frame
                {
                    _ptrDown = true;
                    _ptrDownPoint = pp;
                    _ptrLastPoint = pp;
                    _ptrDragging = false;
                }
                else if (pressed && _ptrDown)          // held
                {
                    if (!_ptrDragging &&
                        !startedOnHud &&
                        (_ptrDownPoint.y < Screen.height * 0.88f) &&
                        (pp - _ptrDownPoint).sqrMagnitude >= PtrPanDragThreshold * PtrPanDragThreshold)
                    {
                        _ptrDragging = true;
                        _ptrLastPoint = pp;            // anchor pan at threshold-cross (no jump)
                        FlowTrace.Step("Build", "single-pointer drag-to-pan ENGAGED (web camera path) — " +
                            "left-mouse/single-finger drag now moves the build view.");
                    }
                    if (_ptrDragging)
                    {
                        Vector2 d = pp - _ptrLastPoint;
                        _ptrLastPoint = pp;
                        // Grab metaphor: drag the world under the finger → subtract (same sign as middle-drag).
                        _camFocus -= (right * d.x + fwd * d.y) * _camDragSpeed;
                    }
                }
                else if (!pressed)                     // released
                {
                    // FIX #1 (2026-07-16) WEB TAP-SELECT: a release that was a TAP (a press was
                    // seen and it never crossed PtrPanDragThreshold into a pan) whose press
                    // started on the MAP (not the top HUD band) SELECTS the structure under the
                    // press point. The raw world-tap confirm (DesktopBuildInput.PlaceOrSelect) is
                    // unreliable on WebGL — placement got the on-screen PLACE button but selection
                    // had no reliable path, so "tap a placed building, nothing happens". This rides
                    // the SAME dependable pointer edge as the drag-to-pan. Only reached in the idle
                    // view state (ptrPanAllowed), so it never fights placement; a real drag (pan)
                    // never selects. Belt-and-braces alongside UpdateSelectLoop.
                    if (_ptrDown && !_ptrDragging && _ptrDownPoint.y < Screen.height * 0.88f)
                    {
                        FlowTrace.Step("Build", "tap-select: TAP released on map (web pointer path) — resolving structure under press point.");
                        TryTapSelectAt(_ptrDownPoint);
                    }
                    _ptrDown = false;
                    _ptrDragging = false;
                }
            }
            else
            {
                _ptrDown = false;
                _ptrDragging = false;
            }

            // Clamp the focus to the grid (map) bounds so the view can't leave the world.
            if (_grid != null)
            {
                float halfW = _grid.gridWidth  * _grid.cellSize * 0.5f;
                float halfH = _grid.gridHeight * _grid.cellSize * 0.5f;
                Vector3 mapCentre = _grid.origin + new Vector3(halfW, 0f, halfH);
                _camFocus.x = Mathf.Clamp(_camFocus.x, mapCentre.x - halfW, mapCentre.x + halfW);
                _camFocus.z = Mathf.Clamp(_camFocus.z, mapCentre.z - halfH, mapCentre.z + halfH);
            }

            ApplyBuildCamera();
        }

        private void RestoreCamera()
        {
            foreach (var mb in _disabledCamDrivers)
                if (mb != null) mb.enabled = true;
            _disabledCamDrivers.Clear();

            // Re-enable the cameras we suppressed for the overview (DEF-117).
            foreach (var c in _suppressedCameras)
                if (c != null) c.enabled = true;
            _suppressedCameras.Clear();

            if (_camera != null)
            {
                _camera.transform.position = _savedCamPos;
                _camera.transform.rotation = _savedCamRot;
            }
        }

        /// <summary>
        /// DEF-117 — the camera the player actually sees: the enabled, screen-bound
        /// (no targetTexture) camera with the highest depth. This is the same rule
        /// SmartMobileCamera/VillageCamera use, so it returns the depth=100 game
        /// camera rather than a rogue embedded one. Falls back to Camera.main.
        /// </summary>
        private static Camera ActiveScreenCamera()
        {
            Camera best = null;
            foreach (var c in Camera.allCameras)
            {
                if (c == null || !c.enabled) continue;
                if (c.targetTexture != null) continue;   // offscreen RT cams are fine
                if (best == null || c.depth > best.depth) best = c;
            }
            return best != null ? best : Camera.main;
        }

        /// <summary>
        /// DEF-117 (split-screen) — disable every OTHER live screen camera for the
        /// build session so only the overview renders, and remember them so Exit()
        /// can re-enable them. The overview camera (_camera) is left untouched.
        /// </summary>
        private void SuppressRogueCameras()
        {
            _suppressedCameras.Clear();
            foreach (var c in Camera.allCameras)
            {
                if (c == null || c == _camera) continue;
                if (c.targetTexture != null) continue;   // leave render-texture cams alone
                if (!c.enabled) continue;
                c.enabled = false;
                _suppressedCameras.Add(c);
            }
        }

        // =====================================================================
        //  Wiring
        // =====================================================================

        private void EnsureGrid()
        {
            _grid = PlacementGrid.Instance;
            if (_grid == null)
                _grid = new GameObject("PlacementGrid").AddComponent<PlacementGrid>();
        }

        private void EnsurePalette()
        {
            if (_palette != null) return;
            var go = new GameObject("BuildPaletteUI");
            _palette = go.AddComponent<BuildPaletteUI>();
            _palette.OnEntrySelected += Arm;
            _palette.OnExitRequested += Exit;
            _palette.OnOrientRequested += OpenOrientEditorForArmed;
            // WO-1010 P2: the minimized "^ Buildings (n)" tab routes to the SAME no-charge
            // cancel every other return-to-carousel already uses (CancelArmed -> Expand), so
            // the tab introduces a new DOOR, never a second set of refund/teardown rules.
            _palette.OnRestoreRequested += () => CancelArmed();
            // WO-2006 / OWNER_RULINGS_LOCKED §25 — the MANAGE PLACED door. Like
            // OnRestoreRequested above, this is a DOOR to an existing verb, never a second
            // set of rules: it hands the session to the same tap-to-select loop that already
            // raises BuildSelectionUI.
            _palette.OnManagePlacedRequested += BeginManagePlaced;

            // WO-352 preview (tap card -> Structure Info Preview -> "Place" -> arm) is
            // DISABLED 2026-06-19 (owner playtest): its UIToolkit panel adopted a bad/null
            // PanelSettings and laid an invisible scrim over the screen, blocking BOTH the
            // placement ghost (no green area on select) AND the palette Done button (couldn't
            // exit build mode). Revert to IMMEDIATE-ARM: with NO OnCardTapped subscriber,
            // BuildPaletteUI.BuildCard arms the entry on tap and raises OnEntrySelected -> Arm,
            // so the green ghost shows on select and Done fires. Re-enable the preview
            // (EnsureInfoPanel + OnCardTapped) once its PanelSettings resolution is fixed
            // (same UIToolkit-panel-render class as WO-465).
        }

        /// <summary>
        /// Lazily create the dedicated Build HUD (one per session). Its intent-bar buttons
        /// call the SAME controller latches the retired BuildPlaceButton did — so placement
        /// behaviour is unchanged; only the chrome ownership moved.
        /// </summary>
        private void EnsureHud()
        {
            if (_hud != null) return;
            _hud = BuildHudController.Create(transform,
                () => RequestUiRotateQuarter(-1),   // Rotate Left  (CCW 90 deg)
                () => RequestUiRotateQuarter(+1),   // Rotate Right (CW 90 deg)
                RequestUiPlaceConfirm,               // PLACE (the only commit latch)
                // Owner felt-test 2026-09-01: the visible Cancel action is an EXIT from
                // Build, not a hidden "disarm but remain in Build Mode" state. Routing it
                // through Exit also tears down the armed ghost, palette, Build HUD, camera
                // override and wave pause atomically. Desktop secondary-cancel remains the
                // lightweight back-to-browser gesture through CancelRequestedThisFrame.
                Exit,
                Exit);                               // X Done (exit build mode)
        }

        /// <summary>Lazily create the WO-352 Structure Info Preview panel (one per session).</summary>
        private void EnsureInfoPanel()
        {
            if (_infoPanel != null) return;
            var go = new GameObject("BuildStructureInfoPanel");
            _infoPanel = go.AddComponent<BuildStructureInfoPanel>();
            _infoPanel.OnPlaceRequested += OnInfoPanelPlace;
            _infoPanel.OnCancelRequested += () => { /* dismiss only — nothing armed */ };
        }

        /// <summary>WO-352 — a palette card was tapped: preview it (no arm yet).</summary>
        private void OnPaletteCardTapped(CatalogEntry entry)
        {
            if (entry == null) return;
            EnsureInfoPanel();
            _infoPanel.Show(entry);
        }

        /// <summary>WO-352 — the player confirmed "Place" in the preview: arm the entry now.</summary>
        private void OnInfoPanelPlace(CatalogEntry entry)
        {
            if (entry == null) return;
            Arm(entry);
            _palette?.SetArmed(entry.id);   // sync the palette gilt highlight + Orient button
        }

        /// <summary>
        /// WO (build-mode orient) — open the 3-axis orient EDITOR on the ARMED entry (no id
        /// typing). Resolves the armed entry's visual prefab from Resources and hands it to
        /// TowerPlacementRotateMenu.OpenDevOrient, which logs an [OrientRecipe] line + applies
        /// the dialed offset on Confirm. The standalone (AdminOverlay) path still drives the
        /// same editor by typed/dropdown id.
        /// </summary>
        private void OpenOrientEditorForArmed(string id)
        {
            var entry = _armed ?? CatalogRegistry.Get(id);
            if (entry == null)
            {
                Debug.LogWarning($"[BuildMode] Orient: no armed entry / '{id}' not in registry.");
                return;
            }

            // Addressables-first via StructureAssetLoader — see the note at the armed-preview site.
            GameObject prefab = DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(entry.visualPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[BuildMode] Orient: '{entry.id}' has no loadable visual prefab ('{entry.visualPrefabPath}').");
                return;
            }

            var menu = FindAnyObjectByType<TowerPlacementRotateMenu>();
            if (menu == null)
            {
                var mgo = new GameObject("DevOrientMenu");
                menu = mgo.AddComponent<TowerPlacementRotateMenu>();
            }
            _orientEditor = menu;
            string name = !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : entry.id;
            menu.OpenDevOrient(entry.id, prefab, name);
            Debug.Log($"[Orient] build-mode orient opened on armed '{entry.id}'.");
        }

        private void EnsureSelectionUi()
        {
            if (_selectionUi != null) return;
            var go = new GameObject("BuildSelectionUI");
            _selectionUi = go.AddComponent<BuildSelectionUI>();
            _selectionUi.OnMoveRequested += BeginMoveSelected;
            _selectionUi.OnSellRequested += SellSelected;
            _selectionUi.OnUpgradeRequested += UpgradeSelected;
            _selectionUi.OnCancelRequested += ClearSelection;
        }

        /// <summary>Lazily create the WO-335 player Rotate Model panel host (one per session).</summary>
        private void EnsureRotateMenu()
        {
            if (_rotateMenu != null) return;
            var go = new GameObject("RotateModelMenu");
            _rotateMenu = go.AddComponent<RotateModelMenu>();
        }

        /// <summary>
        /// Install the Lean.Touch driver as the input source on a touch device (S6).
        /// On desktop (no touch support) the mouse/keyboard DesktopBuildInput stays in
        /// place, so the desktop path is untouched. The driver is wired to the live
        /// overview camera (_camera, set by PullCameraBack() earlier in Enter) so its
        /// two-finger pan/zoom drives the right view.
        /// </summary>
        private void EnsureTouchInput()
        {
            // TGVRU §12 (EDIT-ONLY instrumentation) — a web session must show EXACTLY which
            // IBuildInput the controller chose and the device-detection state behind that pick
            // (the desktop-vs-touch fork that decides whether Build Mode is even placeable on
            // WebGL). No behaviour change; these are pure breadcrumbs.
            FlowTrace.Step("Build", $"EnsureTouchInput: Input.touchSupported={Input.touchSupported}, " +
                $"Application.isMobilePlatform={Application.isMobilePlatform}, _input(before)={_input?.GetType().Name}");
            try
            {
                // InputSystem devices ARE referenceable here — DeNelle.Village references
                // Unity.InputSystem (asmdef) + DesktopBuildInput uses it in-assembly. Wrapped in
                // try/catch only for runtime safety (a device query can never legitimately throw,
                // but a null-device edge on WebGL is captured rather than silently lost).
                FlowTrace.Step("Build", "EnsureTouchInput InputSystem devices: " +
                    $"Touchscreen.current={(UnityEngine.InputSystem.Touchscreen.current != null)}, " +
                    $"Mouse.current={(UnityEngine.InputSystem.Mouse.current != null)}, " +
                    $"Pointer.current={(UnityEngine.InputSystem.Pointer.current != null)}");
            }
            catch (System.Exception ex)
            {
                FlowTrace.Warn("Build", "EnsureTouchInput: InputSystem device probe threw: " + ex.Message);
            }

            if (!Input.touchSupported)
            {
                FlowTrace.Step("Build", $"EnsureTouchInput: desktop path (touchSupported=false) — kept " +
                    $"_input={_input?.GetType().Name} (DesktopBuildInput expected)");
                return;   // desktop → keep DesktopBuildInput
            }

            if (_touchDriver == null)
            {
                var go = new GameObject("LeanTouchBuildDriver");
                _touchDriver = go.AddComponent<LeanTouchBuildDriver>();
            }
            _touchDriver.Install(_camera);
            _input = _touchDriver;
            FlowTrace.Step("Build", $"EnsureTouchInput: touch path — installed LeanTouchBuildDriver, " +
                $"_input={_input?.GetType().Name}");
        }

        /// <summary>
        /// Override the input source (e.g. a test harness, or to force the touch driver
        /// on a desktop build for QA). Null reverts to the default mouse/keyboard impl.
        /// </summary>
        public void SetInput(IBuildInput input)
        {
            _input = input ?? new DesktopBuildInput();
        }

        /// <summary>The persisted crystal wallet (WO-131 — single source of truth).</summary>
        private static int CrystalBalance
        {
            get
            {
                var svc = GameStateService.Instance;
                return svc != null && svc.State != null ? svc.State.Resources.Crystals : 0;
            }
        }
    }

    /// <summary>
    /// WO-1361 - the build-entry census classifier, PURE so it runs headless under
    /// BaseLayoutRoundTripRegression with no scene. Splits the persisted BaseLayout into
    /// REPLAYABLE records (the replay gate says a live body must exist), BAKE-OWNED records
    /// (migration-managed while standdown is inactive - the baked twin stands in, no
    /// PlacedStructure is ever expected) and UNACCOUNTED records (replayable, yet no live
    /// body at that (itemId, cellX, cellZ)). Only the last is a loss. Bodies are consumed
    /// one-to-one so two records at the same id+cell cannot both claim one body.
    /// The gate is injected (BuildModeController passes StrategicPlacementMigration.ShouldReplayRecord,
    /// the same predicate BaseLayoutLoader.Rebuild withholds on) so the suite can exercise it.
    /// </summary>
    public static class BaseLayoutCensus
    {
        public sealed class Result
        {
            public int Persisted;
            public int Replayable;
            public int BakeOwned;
            public int Unaccounted;
            public readonly List<string> BakeOwnedIds = new List<string>();
            /// <summary>Each as "itemId@(cellX,cellZ)" - the capture must name the record.</summary>
            public readonly List<string> UnaccountedIds = new List<string>();
        }

        public static Result Classify(
            IReadOnlyList<PlacedStructureData> layout,
            IReadOnlyList<(string itemId, int cellX, int cellZ)> liveBodies,
            System.Func<string, bool> shouldReplay)
        {
            if (shouldReplay == null) throw new System.ArgumentNullException(nameof(shouldReplay));
            var r = new Result();
            if (layout == null) return r;
            r.Persisted = layout.Count;

            int bodyCount = liveBodies != null ? liveBodies.Count : 0;
            var consumed = new bool[bodyCount];

            for (int i = 0; i < layout.Count; i++)
            {
                var rec = layout[i];
                if (!shouldReplay(rec.itemId))
                {
                    r.BakeOwned++;
                    r.BakeOwnedIds.Add(rec.itemId ?? "<null>");
                    continue;
                }
                r.Replayable++;
                bool found = false;
                for (int b = 0; b < bodyCount; b++)
                {
                    if (consumed[b]) continue;
                    var body = liveBodies[b];
                    if (body.itemId == rec.itemId && body.cellX == rec.cellX && body.cellZ == rec.cellZ)
                    {
                        consumed[b] = true;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    r.Unaccounted++;
                    r.UnaccountedIds.Add($"{rec.itemId ?? "<null>"}@({rec.cellX},{rec.cellZ})");
                }
            }
            return r;
        }

        /// <summary>True only when a replayable record has no live body - the one shape that is a loss.</summary>
        public static bool IsLoss(Result r) => r != null && r.Unaccounted > 0;

        /// <summary>
        /// The mandated line shape (WO-1361):
        /// "census: live=N persisted=M = R replayable + B bake-owned (+ X unaccounted)".
        /// </summary>
        public static string FormatLine(int live, Result r)
        {
            if (r == null) return $"census: live={live} persisted=? (classification unavailable)";
            return $"census: live={live} persisted={r.Persisted} = {r.Replayable} replayable + {r.BakeOwned} bake-owned (+ {r.Unaccounted} unaccounted)";
        }
    }
}
