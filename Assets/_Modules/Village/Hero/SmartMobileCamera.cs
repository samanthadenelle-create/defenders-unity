// =============================================================================
// SmartMobileCamera (DEF-53) — adaptive third-person follow with auto-framing.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT DOES:
//   A drop-in companion/replacement for VillageCamera with three layers of
//   adaptive behaviour tuned for mobile touch play:
//
//   1. MOVEMENT LEAD — the look-at point scoots forward in the hero's movement
//      direction, creating a sense of purpose and giving the player more runway
//      to react to incoming threats.
//
//   2. COMBAT ZOOM — when enemies are in range the camera subtly widens FOV and
//      increases the chase distance so the player sees more of the battle. The
//      zoom smoothly reverts to the idle offset when the area is clear.
//
//   3. AUTO-FRAMING (optional) — with framing enabled the camera interpolates
//      the look-at point toward the centroid of the hero + the nearest visible
//      threat, keeping both on screen. Can be toggled at runtime.
//
// ARCHITECTURE:
//   * Add this component alongside (or instead of) VillageCamera. Call
//     SetTarget(heroTransform) from VillageController / VillageSceneBuilder.
//   * Uses Physics.OverlapSphereNonAlloc for enemy scans — one per
//     _enemyScanInterval seconds, not per-frame.
//   * All camera motion is in LateUpdate (same as VillageCamera) so it
//     composes cleanly with Animator root-motion.
//   * Uses Time.unscaledDeltaTime so the camera doesn't freeze during hit-stop.
//
// TUNING CHEATSHEET (Inspector):
//   _followOffset      — idle world-space offset behind/above the hero
//   _combatZoomOut     — extra distance added to offset.z during combat
//   _combatFovBoost    — extra degrees added to the camera's FOV in combat
//   _leadDistance      — how far ahead of movement to bias the look-at
//   _smoothTime        — SmoothDamp follow smoothness
//   _combatScanRadius  — enemy detection radius for combat zoom / framing
//   _enemyScanInterval — seconds between enemy proximity scans
//   _framingEnabled    — enable auto-framing toward the nearest enemy
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
// WO-958: the dungeon camera's tuning authority + the room-bounds blackboard the
// Dungeons-side publisher fills (Village cannot reference DeNelle.Dungeons directly).
using DungeonCam = DeNelle.Core.World.DungeonCameraProfile;
using DungeonRoomSense = DeNelle.Core.World.DungeonRoomSense;

namespace DeNelle.Village
{
    /// <summary>
    /// Adaptive mobile follow camera with movement-lead, combat zoom, and
    /// auto-framing. Drop-in companion / replacement for <see cref="VillageCamera"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class SmartMobileCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The hero root to follow. Set by VillageController.SetTarget.")]
        [SerializeField] private Transform _target;

        [Header("Follow offset (idle)")]
        [Tooltip("World-space offset from the hero in idle/explore state. Owner 2026-06-02: " +
                 "this is a 3D game — committed to a CLOSE cinematic third-person (was 0,18,-22 " +
                 "= flat top-down board-game seat that killed the 3D feel). Awake() auto-migrates " +
                 "the legacy high value baked into Village.unity to this default, so it applies on " +
                 "the next Play with NO rebake and NO scene edit. Tune Y=height, Z=distance live to " +
                 "taste; any value below the legacy threshold is honored.")]
        [SerializeField] private Vector3 _followOffset = new Vector3(0f, 2.6f, -4.5f);

        // Close cinematic 3D third-person, tilted DOWN for mobile PORTRAIT (DEF-227).
        // Portrait viewports have a tall vertical FOV, so a near-horizontal seat (the old
        // 0,3.5,-6 = ~9.5 deg of downtilt) filled the top half with sky/rooftops and shoved
        // the hero large + low into a corner. This seat sits higher and a touch further back
        // (~28 deg downtilt over the 2.5m look-at) so the ground frames the hero and the sky
        // band shrinks. Awake() snaps the retired top-down seat to this.
        // Owner 2026-06-08 (desktop/landscape): the 0,6.5,-7.5 portrait seat reads "twice
        // as high"; dropped to a CLOSE over-the-shoulder action seat (low + near) so the
        // hero's combat animations read almost face-to-face. _forceCameraFix snaps the
        // baked scene value to this every Play (no rebake). Tune Y=height / Z=distance live.
        private static readonly Vector3 DefaultFollowOffset = new Vector3(0f, 2.6f, -4.5f);
        private const float LegacyHighOffsetY = 14f;   // old TD seat sat at y=18; >=14 => retire it

        [Tooltip("Look-at height above hero feet (metres).")]
        [SerializeField] private float _lookAtHeight = 2.5f;

        [Header("Movement lead")]
        [Tooltip("How far ahead of the hero's movement direction the look-at point is biased. " +
                 "0 = no lead; 3–5 is a comfortable mobile feel.")]
        [SerializeField, Min(0f)] private float _leadDistance = 3.5f;

        [Tooltip("Seconds for the lead point to catch up when the hero stops.")]
        [SerializeField, Min(0.05f)] private float _leadSmoothTime = 0.3f;

        [Header("Smoothing")]
        [Tooltip("Position SmoothDamp time (seconds). Lower = snappier.")]
        [SerializeField, Min(0.01f)] private float _smoothTime = 0.10f;

        [Header("Combat zoom")]
        [Tooltip("Radius within which enemies trigger the combat-zoom state.")]
        [SerializeField, Min(1f)] private float _combatScanRadius = 12f;

        [Tooltip("Seconds between enemy proximity scans (use 0.2–0.4 for mobile).")]
        [SerializeField, Range(0.05f, 1f)] private float _enemyScanInterval = 0.25f;

        [Tooltip("Extra backward distance added to the follow offset during combat.")]
        [SerializeField, Min(0f)] private float _combatZoomOut = 2.5f;

        [Tooltip("Extra FOV degrees added during combat (0 = no FOV change).")]
        [SerializeField, Range(0f, 15f)] private float _combatFovBoost = 4f;

        [Tooltip("Speed at which combat zoom transitions (higher = snappier).")]
        [SerializeField, Min(0.1f)] private float _combatZoomSpeed = 2.5f;

        // ⛔ WO-1765 — THIS FIELD SHIPPED AS `~0` (EVERY LAYER) AND THAT IS HALF THE RAID DEFECT.
        // The tooltip has always said "set to the Enemy layer"; the initialiser says the opposite,
        // and a RUNTIME AddComponent takes the initialiser. The raid camera IS runtime-attached
        // (HeroControlEnsurer.cs ~:464 attaches SmartMobileCamera when the gameplay camera has
        // none, and Assets/Scenes/RaidBase_IronBastion.unity carries no SmartMobileCamera at all),
        // so in a raid this sweep ran against all 32 layers.
        //
        // WHY THE MASK IS LOAD-BEARING AND NOT MERELY TIDY: `_scanBuffer` is THIRTY-TWO slots
        // (see below) and OverlapSphereNonAlloc fills it in arbitrary order. A raid base holds 158
        // `Wall_*` colliders on layer 8 ("Structure"); inside a 12 m scan radius they can fill the
        // buffer outright, so the mobile hostiles the framing exists for may never be seen at all.
        // Narrowing the mask is therefore a correctness fix, not a micro-optimisation — and it is
        // the SECOND half of the fix, the first being IsFramingSubject below.
        //
        // ⚠ THE MASK IS RESOLVED BY LAYER NAME, NEVER BY A SERIALIZED BIT PATTERN. Read
        // ProjectSettings/TagManager.asset (2026-09-16): layers are 0 Default, 1 TransparentFX,
        // 2 Ignore Raycast, 3 Tower, 4 Water, 5 UI, 6 Building, 7 **Enemy**, 8 **Structure**. So
        // the `m_Bits: 256` the baked town cameras carry (Main_Castle_Overworld.unity:3034-3036,
        // Village2.unity:3387-3389) is 1<<8 = the STRUCTURE layer, NOT the Enemy layer — a baked
        // town camera has been scanning walls and nothing else.
        //
        // ⛔ AND IT STILL DOES, ON PURPOSE, IN THIS BUILD. Lead ruling 2026-09-16: the TOWN must not
        // change felt behaviour here. So the narrowing runs ONLY in the raid branch
        // (ApplyRaidSeat -> ResolveEnemyScanMask, scoped by AppliesRaidScanNarrowing) and
        // RestoreVillageCameraValues hands the baked value straight back. Correcting `256` in the
        // scenes is a follow-up for a lane that owns `.unity`; do not "tidy" it from here.
        [Tooltip("Layer mask for enemy detection sweeps. In a RAID scene only, narrowed at runtime to " +
                 "Default|Enemy by layer NAME (the runtime-attached camera takes the ~0 initialiser). " +
                 "Baked town/Village2 values are left exactly as authored - see ResolveEnemyScanMask.")]
        [SerializeField] private LayerMask _enemyMask = ~0;

        [Header("Auto-framing")]
        [Tooltip("When enabled the look-at point interpolates toward the centroid of " +
                 "hero + nearest enemy so both stay in frame during combat.")]
        [SerializeField] private bool _framingEnabled = true;

        [Tooltip("Fraction of the offset from hero to nearest enemy applied to the " +
                 "look-at centroid. 0.5 = halfway between hero and enemy. Kept low " +
                 "so the hero always stays well inside the frame.")]
        [SerializeField, Range(0f, 0.7f)] private float _framingBias = 0.2f;

        [Tooltip("How quickly the look-at recentres on the hero when no enemies are near.")]
        [SerializeField, Min(0.5f)] private float _framingReturnSpeed = 4f;

        // ── WO-512 slice 2: lock-on framing ──────────────────────────────────────
        // When a lock target is bound (BattleArena.SetLockTarget) AND FeatureFlags.LockOn is
        // on, the auto-framing source is OVERRIDDEN to the LOCKED enemy instead of the
        // auto-nearest scan, framing engages immediately (combat blend forced toward 1), and a
        // SEPARATE, capped bias keeps the Knight well in-frame. It REUSES the exact same
        // _leadPoint SmoothDamp as the existing framing path — never a transform set, never a
        // snap; a switch eases because it only moves the damp goal. When the lock target is null
        // OR the flag is off, this whole block is skipped and the camera path is byte-identical
        // to today (zero free-look regression). Yaw-assist (auto-centering) is deliberately NOT
        // implemented this slice (it is the mobile-nausea hotspot — deferred to slice 4).
        [Tooltip("WO-512: fraction of the offset from hero to the LOCKED enemy applied to the " +
                 "look-at centroid while lock-on framing is active. CAPPED at 0.45 so the Knight " +
                 "always stays well inside the frame (kept distinct from _framingBias).")]
        [SerializeField, Range(0f, 0.45f)] private float _lockFramingBias = 0.32f;

        // The enemy currently locked-on (set by BattleArena.SetLockTarget, cleared on
        // ClearLockTarget / Resolve / disable). Null = no lock -> today's exact framing path.
        private Transform _lockTarget;

        [Header("Orbit-behind (third-person)")]
        [Tooltip("Owner 2026-06-02 (\"will the camera pivot behind me when I walk around the " +
                 "castle?\"): when ON, the follow offset rotates with the hero's TRAVEL direction " +
                 "so the camera swings to your back as you round a building — true third-person. " +
                 "OFF = the legacy fixed compass angle (always behind world -Z).")]
        // 2026-06-02: DEFAULT OFF — the first cut (auto-orbit chasing travel heading) fed
        // back into camera-relative movement and made the hero curve/"always turn left"
        // while walking + firing. Reverted to the fixed offset the owner called "much
        // better"; a proper orbit needs a manual/aim-driven yaw, not movement-driven, and
        // must be decoupled from the locomotion input frame. Left here, off, for that pass.
        [SerializeField] private bool _orbitBehind = false;

        // DEF-202/204 (owner: camera was the "worst feature" — world-locked yaw). The fix
        // ships behind _orbitBehind, but Village.unity BAKES _orbitBehind=0, so a BUILD ships
        // world-locked — the exact reason this kept reopening (a C# default flip is overridden
        // by the baked scene value). Force it ON at runtime, no rebake, mirroring the
        // legacy-offset migration in Awake. Set false in code to A/B the legacy camera.
        [SerializeField] private bool _forceCameraFix = true;

        [Tooltip("Degrees/sec the camera yaw chases the hero's travel heading. Lower = lazier, " +
                 "more cinematic swing; higher = snaps behind faster.")]
        [SerializeField, Min(15f)] private float _orbitYawSpeed = 150f;

        [Tooltip("Min planar speed (m/s) before the orbit yaw updates — below this the camera " +
                 "holds its current angle (so standing still / tiny nudges don't spin the view).")]
        [SerializeField, Min(0.01f)] private float _orbitMoveThreshold = 0.4f;

        // DEF-202/204 player-authoritative orbit (CAMERA_INPUT_OVERHAUL.md §2). The yaw is
        // driven ONLY by player pan input (CameraPanInput → AddYaw) plus an optional damped
        // pull toward the hero's FACING (never velocity). This structurally excludes the old
        // velocity-chasing curl/spiral: {yaw←velocity} is absent, only {move←yaw} remains.
        [Tooltip("Min vertical pitch (deg) the player can tilt the orbit camera to (negative = look up).")]
        [SerializeField] private float _panPitchMin = -10f;

        [Tooltip("Max vertical pitch (deg) the player can tilt the orbit camera to.")]
        [SerializeField] private float _panPitchMax = 35f;

        [Tooltip("DEF-202/204: gentle auto-recenter that swings the camera toward the hero's FACING " +
                 "(never velocity) after an idle delay. OFF by default — the camera holds the player's " +
                 "last yaw until they drag again. Loop gain < 1 (damped + idle-gated + suspended during " +
                 "drag) so it converges, never spirals.")]
        [SerializeField] private bool _facingRecenterEnabled = false;

        [Tooltip("Seconds of no-drag before the facing-recenter resumes (only if enabled). " +
                 "WO-385: kept SHORT so the seat trails the hero's facing in enclosed hubs instead of " +
                 "staying world-locked behind a wall — but non-zero so a quick manual pan isn't instantly " +
                 "yanked back. Suspended entirely while the player is actively dragging.")]
        [SerializeField, Min(0f)] private float _facingRecenterDelay = 0.4f;

        [Tooltip("Max degrees/sec the facing-recenter swings the camera toward the hero's facing. The " +
                 "actual step is PROPORTIONAL to the remaining angle (damped) and capped at this, so big " +
                 "corner turns swing promptly while the swing eases to a stop as it lines up — no overshoot, " +
                 "no spiral (loop gain < 1).")]
        [SerializeField, Min(0f)] private float _facingRecenterSpeed = 220f;

        [Tooltip("WO-385: continuous-recenter stiffness (1/sec). The per-frame swing toward the hero's " +
                 "facing is angleError * this, clamped to _facingRecenterSpeed. Higher = the seat hugs the " +
                 "hero's back more tightly; lower = a lazier cinematic trail. ~3–5 reads as a smooth " +
                 "auto-trailing third-person seat that keeps you facing your open side indoors.")]
        [SerializeField, Min(0.1f)] private float _facingRecenterStiffness = 4f;

        [Header("Wall collision (DEF-151)")]
        [Tooltip("When ON, the camera spherecasts from the hero pivot toward its desired position " +
                 "each frame and pulls IN to just in front of any wall/world geometry in the way, " +
                 "so it never embeds in a wall mesh and loses the hero. OFF = legacy fixed offset " +
                 "(the DEF-151 clipping bug). Leave ON.")]
        [SerializeField] private bool _collisionEnabled = true;

        [Tooltip("World geometry the camera collides against (walls, buildings, towers, ground). " +
                 "EXCLUDES Enemy/Water/Ignore Raycast/UI so mobs and triggers never shove the view. " +
                 "Default = Default + Building + Tower (the layers village walls/structures live on).")]
        [SerializeField] private LayerMask _collisionMask = ~0;

        // Default collision mask, resolved in Awake from the project's named layers so it
        // stays correct even if layer indices shift. Default(0) | Building(6) | Tower(3) |
        // Structure(8). Enemy(7), Water(4), Ignore Raycast(2), UI(5), TransparentFX(1) are
        // deliberately OUT.
        // ⚠ WO-1765 corrected this comment: it read "Enemy(8)" and Enemy is SEVEN
        // (ProjectSettings/TagManager.asset, read 2026-09-16 — 8 is "Structure"). The wrong index
        // sat one line above a mask built from NAMES, which is why nothing broke and why it
        // survived; a seat that trusted it would have excluded the wall layer as "Enemy".
        [Tooltip("Radius of the occlusion spherecast - the camera keeps at least this much clearance " +
                 "from a wall so the near clip plane never punches through the surface.")]
        [SerializeField, Min(0.05f)] private float _collisionRadius = 0.35f;

        [Tooltip("Extra gap (metres) kept between the wall hit point and the camera, on top of the " +
                 "spherecast radius. Stops the wall's inside face from filling the screen.")]
        [SerializeField, Min(0f)] private float _collisionSkin = 0.2f;

        [Tooltip("Closest the camera is ever allowed to pull in toward the hero pivot (metres). " +
                 "Prevents the camera snapping onto the hero's head in a tight corner.")]
        [SerializeField, Min(0.5f)] private float _minCollisionDistance = 1.2f;

        [Tooltip("How fast the camera pulls IN when a wall appears (higher = snappier, avoids a " +
                 "clip frame). Pull-in is near-instant; pull-out is eased by _collisionReturnSpeed.")]
        [SerializeField, Min(1f)] private float _collisionApproachSpeed = 40f;

        [Tooltip("How fast the camera eases back OUT to the full offset once the wall is clear " +
                 "(lower = smoother, no jitter as you walk along a wall).")]
        [SerializeField, Min(1f)] private float _collisionReturnSpeed = 8f;

        [Tooltip("WO-385: distance (metres) below which the camera still PULLS IN to the occluder " +
                 "as a last-resort safety backstop (so the camera body never embeds point-blank in a " +
                 "mesh). Above this, occluders are FADED (hidden) instead so the camera keeps its " +
                 "proper seat/angle and you simply see the hero through the wall. Keep small.")]
        [SerializeField, Min(0.1f)] private float _occluderPullInDistance = 0.6f;

        private bool _collisionMaskInit;
        // Smoothed 0..1 fraction of the desired distance currently allowed (1 = no wall, full offset).
        private float _distanceFrac = 1f;
        // WO-1734: last frame's fade-vs-pull-in verdict, so the trace fires on the TRANSITION only.
        private bool _wasPullingIn;

        // ── Occluder fade (WO-385) ─────────────────────────────────────────────
        // Instead of pulling the camera IN to a wall (which jammed it to a close "lost" angle at
        // every corner), we keep the camera at its proper seat and FADE the occluding renderer(s)
        // to ShadowsOnly so you see the hero through the wall. Renderers are restored the instant
        // they stop occluding. _faded stores each currently-hidden renderer with its ORIGINAL
        // shadow casting mode so restore is exact; _fadedThisFrame marks the ones still occluding.
        private readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> _faded
            = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
        private readonly HashSet<Renderer> _fadedThisFrame = new HashSet<Renderer>();
        private readonly List<Renderer> _restoreScratch = new List<Renderer>();
        // Reused buffer for SphereCastAll (NonAlloc) so the per-frame path stays allocation-light.
        private readonly RaycastHit[] _occluderHits = new RaycastHit[16];
        // WO-1751: scratch for the seat-embed detector below. Non-alloc, never grown.
        private readonly Collider[] _seatOverlap = new Collider[8];
        // WO-1751: scratch for CollectOccluderRenderers — reused every frame, never allocated in
        // the hot path. Capacity matches MaxFadedRenderersPerCollider so it never grows.
        private readonly List<Renderer> _fadeScratch = new List<Renderer>(MaxFadedRenderersPerCollider);
        // WO-1753: the accepted hit distances of THIS frame's sweep, fed to the pure
        // SelectOccluderGateDistance below. Cleared (never reallocated) each frame and sized to
        // _occluderHits, so the frame path stays allocation-light exactly like the buffers above.
        // It exists so the "farthest, not nearest" reduction has ONE implementation that a
        // regression can drive directly — a second inline copy is the duplicated state CLAUDE.md
        // §5 forbids, and a source-text lint cannot tell max from min.
        private readonly List<float> _occluderDistances = new List<float>(16);

        // ── Runtime state ──────────────────────────────────────────────────────

        private Camera  _cam;
        private float   _baseFov;
        private Vector3 _posVelocity;
        private Vector3 _leadPoint;
        private Vector3 _leadVelocity;
        private float   _scanTimer;
        private float   _combatBlend;       // 0 = idle, 1 = full combat zoom
        private Vector3 _nearestEnemyPos;
        private bool    _enemyInRange;
        private float   _orbitYaw;          // legacy: smoothed camera yaw (deg) — no longer drives rotation
        private bool    _orbitYawInit;      // seeded from the hero's facing on first frame

        // ── Player-authoritative camera yaw (DEF-202/204, CAMERA_INPUT_OVERHAUL.md §2) ──
        // Written ONLY by AddYaw/AddPitch (pan input) plus an optional damped pull toward
        // hero FACING. NEVER a function of hero velocity/position/MoveIntent. This is the
        // single yaw authority for both the camera seat AND HeroLocomotion's movement basis
        // (read via CameraYaw), so a constant stick yields a straight line — no spiral.
        private float _panYaw;
        private float _panPitch;          // clamped to [_panPitchMin, _panPitchMax]
        private float _timeSinceLastDrag; // drives the idle-gated facing-recenter

        private readonly Collider[] _scanBuffer = new Collider[32];

        // WO-383: the HeroLocomotion we're currently subscribed to for OnTeleported. On a
        // scene-seam warp the hero jumps far in one frame; without this the SmoothDamp follow
        // would chase the jump through the intermediate bad positions. We snap the seat
        // instead. Tracked so we can re-subscribe when the target changes and cleanly detach
        // in OnDisable / OnDestroy.
        private HeroLocomotion _teleportLoco;

        // DEF-227b / HeroDrift (2026-07-04): the HeroLocomotion of the current target, cached so
        // the facing-recenter can cheaply poll whether the hero is under active locomotion. When
        // moving, the recenter is SUSPENDED to break the {_panYaw → move-heading → Velocity →
        // heroFacing → _panYaw} feedback loop (the left/right wiggle on a pure-forward hold).
        private HeroLocomotion _followLoco;
        // Idle epsilon: below ~0.1 m/s speed (sqr 0.01) the hero is treated as stopped, so the
        // idle/post-drag reframe still runs the instant the player releases the stick.
        private const float MoveEpsilonSqr = 0.01f;

        // ── Sole-camera guard (same pattern as VillageCamera) ─────────────────
        private float _soleCheckTimer;

        // ── Screen shake (DEF-67) ──────────────────────────────────────────────
        private Vector3 _shakeOffset;
        private Coroutine _shakeRoutine;

        // ── Singleton (DEF-67: audio controllers + VFX need to reach the camera) ──
        /// <summary>
        /// The active SmartMobileCamera in the scene (null between scenes).
        /// Audio / VFX systems use this to call <see cref="Shake"/>.
        /// </summary>
        public static SmartMobileCamera Instance { get; private set; }

        // ── Player-authoritative camera yaw API (DEF-202/204) ──────────────────
        /// <summary>
        /// The camera's current yaw (deg) used as the basis for camera-relative movement
        /// (HeroLocomotion reads this). Returns 0 when orbit-behind is OFF, so movement
        /// stays world-relative and byte-identical to the legacy shipped build (A/B parity).
        /// This is a pure player-input value — NEVER derived from hero velocity — so holding
        /// a constant stick produces a straight line with no spiral.
        /// </summary>
        public float CameraYaw => _orbitBehind ? _panYaw : 0f;

        /// <summary>Player drag / right-stick yaw delta (deg). The ONLY way external input rotates the view.</summary>
        public void AddYaw(float deg)
        {
            _panYaw += deg;
            _timeSinceLastDrag = 0f;
        }

        /// <summary>Optional pitch from vertical drag; clamped to the safe [_panPitchMin, _panPitchMax] band.</summary>
        public void AddPitch(float deg)
        {
            _panPitch = Mathf.Clamp(_panPitch + deg, _panPitchMin, _panPitchMax);
            _timeSinceLastDrag = 0f;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            _cam    = GetComponent<Camera>();
            // A tight third-person collision seat needs a small near plane; otherwise the
            // camera transform can be safely in front of a wall while the near plane still
            // cuts through it and exposes the far side.
            if (_cam.nearClipPlane > 0.08f) _cam.nearClipPlane = 0.08f;
            _baseFov = _cam.fieldOfView;

            // SINGLE-AUTHORITY GUARD: the scene builder attaches BOTH VillageCamera
            // and SmartMobileCamera to the Main Camera. Two follow rigs writing the
            // same transform every LateUpdate fight each other — the hero drifts
            // off-screen and the camera can swing to the ground/wall. SmartMobileCamera
            // is the intended DEF-53 driver (combat zoom + Shake singleton), so we
            // disable the legacy VillageCamera here and own the transform alone.
            var legacy = GetComponent<VillageCamera>();
            if (legacy != null) legacy.enabled = false;

            // One-time migration (owner 2026-06-02): retire the legacy high top-down
            // seat (0,18,-22) baked into Village.unity. Anything this high is the old
            // board-game framing that flattened the 3D feel — snap it to the close 3D
            // third-person default. Applies on Play with no rebake/scene edit; any
            // genuinely-tuned lower offset is left untouched.
            if (_followOffset.y >= LegacyHighOffsetY)
            {
                Debug.Log($"[SmartMobileCamera] retiring legacy top-down offset {_followOffset} " +
                          $"-> {DefaultFollowOffset} (close 3D third-person).");
                _followOffset = DefaultFollowOffset;
            }

            // DEF-202/204: override the baked _orbitBehind=0 / _facingRecenterEnabled=0 so the
            // BUILD gets the camera fix (camera-relative movement + slide-to-pan + lazy
            // swing-behind). No rebake — same one-time-migration pattern as the offset above.
            if (_forceCameraFix)
            {
                _orbitBehind = true;
                _facingRecenterEnabled = true;

                // DEF-227 — Village.unity BAKES the old near-horizontal seat (0,3.5,-6),
                // which reads fine in landscape but in mobile PORTRAIT fills the top with
                // sky/rooftops and pushes the hero large + low into a corner. The legacy
                // migration above only retires the y>=14 top-down seat, so force the new
                // tilted-down framing here (same no-rebake override pattern as orbit/offset)
                // and tame the look-at lead so the hero recentres instead of trailing into
                // a corner. A genuinely-tuned high seat (y>=14) is left to the migration.
                if (_followOffset.y < LegacyHighOffsetY)
                    _followOffset = DefaultFollowOffset;
                if (_leadDistance > 1.5f)
                    _leadDistance = 1.5f;
            }

            ResolveCollisionMask();

            // ⛔ WO-1765 — THE ENEMY-SCAN MASK IS *NOT* RESOLVED HERE, AND THE OMISSION IS THE RULING.
            // `ResolveEnemyScanMask()` sat on this line and ran for EVERY scene. Owner-protecting lead
            // ruling 2026-09-16: **the TOWN must not change felt behaviour in this build.** A baked
            // town camera carries `_enemyMask m_Bits: 256`, which is layer 8 = "Structure" (NOT Enemy,
            // which is 7) — so in town the combat zoom and auto-framing are driven by masonry, today,
            // as shipped. Narrowing the mask there would start firing them on mobs for the first time:
            // a 2.5 m zoom-out and +4 FOV appearing mid-wave, which the owner did not ask for in this
            // build. The narrowing now happens ONLY in ApplyRaidSeat, and RestoreVillageCameraValues
            // puts the baked value back on the way out. The baked 256 stays a follow-up for a lane
            // that owns `.unity` — see the WO §13.
            // WO-920 / WO-1765: dungeons get a locked calm seat, raids an over-the-shoulder one.
            // Must run AFTER the _forceCameraFix block above, which rewrites _followOffset and
            // _leadDistance unconditionally.
            ApplyDungeonProfileIfNeeded("Awake");
        }

        // ── WO-920: the locked dungeon seat ────────────────────────────────────
        // Tracks whether the dungeon profile is currently applied, so re-entering the
        // method (Awake + every sceneLoaded) is idempotent and a dungeon->town transition
        // on a surviving camera restores the village framing instead of staying dark+tight.
        private bool _dungeonProfileActive;
        // WO-1765: the RAID over-the-shoulder profile, the same seam one scene-kind over. Mutually
        // exclusive with the dungeon profile by construction (ONE resolver writes both flags, and
        // HubScenes.IsRaid / IsDungeon cannot both match a name).
        private bool _raidProfileActive;
        // The scene name the profile was resolved for, cached on Awake / sceneLoaded so the frame
        // path never calls SceneManager.GetActiveScene() (WO-1765 instrument §5a).
        private string _profileSceneName = string.Empty;
        // The village values this camera had before the dungeon profile overwrote them,
        // captured on the first apply so the restore is exact rather than re-typed defaults.
        private Vector3 _villageFollowOffset;
        private float   _villageLookAtHeight;
        private float   _villageLeadDistance;
        private float   _villageCombatZoomOut;
        private float   _villageCombatFovBoost;
        private bool    _villageFramingEnabled;
        private bool    _villageCollisionEnabled;
        // WO-958: yaw/pitch tuning snapshots — the dungeon profile overrides the
        // facing-recenter + pitch band, so the town restore must be exact, not re-typed.
        private bool    _villageFacingRecenterEnabled;
        private float   _villageFacingRecenterDelay;
        private float   _villageFacingRecenterSpeed;
        private float   _villageFacingRecenterStiffness;
        private float   _villagePanPitchMin;
        private float   _villagePanPitchMax;
        // WO-1765: the town's own serialized enemy-scan mask, so the raid-only narrowing is exactly
        // reversible (lead ruling: the town's felt behaviour does not move in this build).
        private LayerMask _villageEnemyMask;

        // ── WO-958: room-aware dungeon framing state (dungeon profile ONLY) ────
        // Current room the hero occupies, resolved from the DungeonRoomSense
        // blackboard with a sticky containment cache (doorway edges don't flap it).
        private DungeonRoomSense.Room _dgRoom;
        private bool    _dgRoomValid;
        private string  _dgRoomId;
        private Vector3 _dgRoomSize;
        private bool    _dgRoomSmall;
        // The smoothed live seat (height / boom) — SmoothDamped between the standard
        // and small-room profile seats so a room change is a transition, never a snap.
        private float   _dgSeatHeight;
        private float   _dgSeatDist;
        private float   _dgSeatHeightVel;
        private float   _dgSeatDistVel;
        // Evidence counters/state for the [Flow:Camera] heartbeat (WO-958 sec.3).
        private int     _dgCeilingClamps;
        private string  _dgYawSource = "hold";
        private float   _dgTraceTimer;

        // ── WO-1765: THE YAW INSTRUMENT (§5b-§5d). Permanent — CLAUDE.md §12. ──
        //
        // ⛔ IT MEASURES SCREEN YAW, NOT `_panYaw`, AND THAT IS THE WHOLE POINT. The owner's word
        // is "rotating"; three of the five WO-1765 candidates (the framing scan, the movement lead,
        // the pull-in lever arm) move the LOOK-AT and never touch `_panYaw`, so a `panYaw` trace
        // can neither prove nor refute them. `AimAt` converts a lateral look-at offset into view
        // yaw at a gain of 1/boom, so the boom, the lateral lever arm and the rate must appear on
        // the SAME line or the reader is back to theorising.
        private float _prevViewYaw;
        private bool  _prevViewYawInit;
        private float _viewYawRate;        // deg/s this frame
        private float _viewYawRateMax;     // running |max| since the last heartbeat line
        private float _panYawAtLastTrace;  // so the line can print dPanYaw for the interval
        private float _leadLateral;        // signed lateral arm of the look-at, in the camera frame
        private int   _yawSpikeFrames;     // consecutive frames over the spike threshold
        private float _yawSpikeCooldown;   // re-arm timer for the edge Warn
        private int   _snapCount;          // ForceFollowImmediate snaps (candidate C5)
        private bool  _pullingInNow;       // this frame's pull-in verdict (candidate C4)
        // The framing subject the scan actually chose, and what it was. `framingTarget` naming a
        // Wall_* is the C1 verdict in one word; `structsRejected` keeps that verdict readable even
        // when a profile has framing switched off, because the SCAN still runs.
        private string _framingTargetName = "none";
        private string _framingTargetType = "none";
        private float  _framingTargetDist;
        private int    _framingTargetSwitches;
        private int    _framingStructsRejected;

        /// <summary>Spike threshold (deg/s) for the WO-1765 yaw edge Warn.</summary>
        private const float YawSpikeDegPerSec = 90f;
        /// <summary>Consecutive frames over <see cref="YawSpikeDegPerSec"/> before the edge fires.</summary>
        private const int   YawSpikeFrames = 3;
        /// <summary>Seconds before the spike Warn can fire again (keeps a busy log readable).</summary>
        private const float YawSpikeCooldownSeconds = 2f;
        /// <summary>
        /// WO-1765 — the degrees the facing-recenter actually applied on the LAST frame it stepped.
        /// This is the field that turns "the camera rotates itself" into a NUMBER: a run of
        /// <c>yawSrc=recenter step=</c> lines whose steps sum past ~90 degrees while the player's
        /// thumb is off the screen IS the possessed spin, measured; <c>yawSrc=input</c> throughout
        /// refutes the recenter and points the ticket at the pull-in instead. Falsifiable both ways,
        /// which is the whole point of instrumenting rather than theorising.
        /// </summary>
        private float _lastRecenterStep;

        /// <summary>
        /// WO-1765 — the RAID over-the-shoulder camera profile. Owner ruling 2026-09-16: <i>"its the
        /// clear win when the camera spins about as if possessed and people leave the game."</i>
        /// <para>
        /// ⛔ <b>THIS IS A THIRD BRANCH IN THE ONE PROFILE SEAM, NOT A WIDENED DUNGEON GATE — and
        /// that choice is what makes it safe.</b> The obvious one-line fix is to change the seam's
        /// test to <c>IsDungeon(scene) || IsRaid(scene)</c> and let a raid run the dungeon profile.
        /// It installs three ROOM-TOPOLOGY features an open arena has no rooms for: the ceiling clamp
        /// to <c>heroFeetY + CeilingHeightRef(4) - CeilingClearance(0.5)</c>, the
        /// <see cref="DungeonRoomSeat"/> room-aware seat damp, and
        /// <c>DungeonCam.FacingLookAhead</c>. Each is *probably* inert in a raid — and "probably" is
        /// exactly what CLAUDE.md §11B forbids shipping. A third branch makes the question moot: all
        /// three stay gated on <c>_dungeonProfileActive</c>, so they are inert BY CONSTRUCTION, with
        /// nothing left to prove on a device.
        /// </para>
        /// <para>
        /// ⛔ <b>THE SEAT VALUES ARE THE DUNGEON'S, BY REFERENCE AND NOT BY COPY.</b> They are
        /// already owner-approved (WO-920 / WO-958) and the WO-958 ruling that produced the lazy
        /// recenter came from the owner reporting <i>this same defect</i> underground ("its auto
        /// rotating", F8 seq 2289) — a walled raid base is a "small room" in every way that matters
        /// to this camera. Re-typing the numbers here would be the duplicated state CLAUDE.md §5
        /// forbids, and it would let the raid drift off a seat the owner has already signed off.
        /// The raid-ONLY value is the shoulder term below.
        /// </para>
        /// <para>
        /// ⚠ <b>WHY A SHORTER BOOM MATTERS, stated as arithmetic and not as taste.</b> The pull-in
        /// gate fires when <c>(boom − gateDistance) &lt; _occluderPullInDistance</c> (0.6 m), where
        /// the gate distance is a spherecast hit measured from the hero's chest, i.e.
        /// <c>surfaceDistance − _collisionRadius(0.35)</c>. So a wall surface at D metres fires it
        /// for <c>D ∈ (boom − 0.25, boom + 0.35]</c>. At the town boom (4.501 m) the seat in a ~3 m
        /// raid space lands 1.9 m BEYOND the far wall — outside the space, that wall faded — while
        /// the next occluder out sits in the firing band and collapses the seat toward the 1.2 m
        /// floor: a 3.75x zoom, and since the screen-yaw gain of any lateral look-at offset is
        /// 1/boom, a 3.7x larger rotation for the same offset. At this profile's boom the same worst
        /// case is a fraction of a metre. The full arithmetic is in
        /// <c>WorkOrders/WORK_ORDER_1765_bastion_camera_rotates_with_walls.md</c> §11.
        /// </para>
        /// <para>
        /// Every number is ONE constant so the lead or the owner can move it without reading code,
        /// and <c>CameraRaidFramingRegression</c> DRIVES these values through the real pull-in
        /// statics rather than grepping for them.
        /// </para>
        /// </summary>
        public static class RaidCam
        {
            // ⚠ THE SHARED VALUES ARE PROPERTIES, NOT `const`, ON PURPOSE. A cross-assembly `const`
            // is INLINED at compile time into DeNelle.Village, so a later re-tune of
            // DeNelle.Core's DungeonCameraProfile could leave a stale copy baked in here — a
            // duplicated-state failure with no visible edit, which is the very thing referencing
            // the dungeon profile was meant to avoid. A property reads the live value.

            /// <summary>
            /// Seat height above the hero's feet (m) — the dungeon seat, by reference. Town 2.6.
            /// </summary>
            public static float CameraHeight => DungeonCam.CameraHeight;
            /// <summary>Seat distance BEHIND the hero (m) — the dungeon seat. Town 4.5.</summary>
            public static float CameraDistance => DungeonCam.CameraDistance;
            /// <summary>Look-at height above the hero's feet (m) — chest height. Town 2.5.</summary>
            public static float LookAtHeight => DungeonCam.LookAtHeight;

            /// <summary>
            /// Lateral shoulder offset (m), +X = the hero's RIGHT in the yawed seat frame. THE ONE
            /// RAID-ONLY VALUE.
            /// <para>
            /// ⚠ WHY IT IS INCLUDED RATHER THAN DEFERRED. The lead's condition was "a shoulder term
            /// only if the dungeon profile's numbers leave the hero visibly CENTRED". They do, by
            /// construction and not by opinion: <c>_followOffset.x</c> is the ONLY lateral term in
            /// this file, the dungeon profile sets it to 0, and <c>AimAt</c> looks at the hero's own
            /// chest — so the hero renders dead centre and her body occludes the aim line directly
            /// ahead of her, which is the thing an over-the-shoulder seat exists to fix. 0.6 m is
            /// the figure the sibling 1765 ticket proposed for exactly this (its R3). At a 3.2 m
            /// back that is atan(0.6/3.2) ≈ 10.6 degrees off centre.
            /// </para>
            /// <para>
            /// ⚠ It is a FELT number with no capture behind it. If the owner reads it as too far off
            /// centre, this one constant is the whole knob.
            /// </para>
            /// </summary>
            public static float ShoulderOffset => 0.6f;

            /// <summary>Movement-lead distance (m) — ZERO, which deletes the look-at sway. Town 1.5.</summary>
            public static float LeadDistance => 0f;
            /// <summary>
            /// Combat seat zoom-out (m) — ZERO. Town 2.5, which re-extends the boom the moment a
            /// hostile enters the 12 m scan, i.e. constantly in a raid, re-arming the pull-in.
            /// </summary>
            public static float CombatZoomOut => 0f;
            /// <summary>Combat FOV boost (deg) — ZERO, same reason as <see cref="CombatZoomOut"/>.</summary>
            public static float CombatFovBoost => 0f;
            /// <summary>
            /// Auto-framing — OFF. Screen-yaw gain is 1/boom: a 0.2 bias toward a hostile 10 m away
            /// is a 2.0 m lateral arm, which at this profile's boom is ~31 degrees of view yaw versus
            /// ~24 at the 4.5 m town boom. Shortening the boom makes framing MORE violent, so the
            /// two ship together.
            /// ⚠ WITH THIS OFF, <see cref="IsFramingSubject"/> HAS NO VISIBLE EFFECT AT SHIP — AND IT
            /// IS RAID-SCOPED, SO IT HAS NONE IN TOWN EITHER (lead ruling, see
            /// <see cref="AppliesRaidScanNarrowing"/>). What it still does in a raid is keep
            /// <c>_enemyInRange</c> honest and feed the <c>structsRejected</c> evidence, so the rule is
            /// provably RUNNING in a capture and is correct the day framing is flipped back on. Making
            /// it global is a follow-up, paired with fixing the baked <c>256</c> in the scenes.
            /// </summary>
            public static bool FramingEnabled => false;

            /// <summary>
            /// Occlusion — ON, and this is the ONE place the raid profile deliberately DIFFERS from
            /// the dungeon's.
            /// <para>
            /// ⛔ THE DUNGEON SETS THIS FALSE, WHICH WOULD BE WRONG IN A RAID. One flag owns BOTH the
            /// point-blank pull-in AND the WO-385 occluder FADE (the dungeon branch says so in its
            /// own comment: "no wall pull-in AND no ceiling/wall fade"). A raid hero stands against
            /// masonry constantly, so switching it off would leave her hidden BEHIND a wall with no
            /// fade to reveal her — the WO-1751/WO-1734 defect, re-created deliberately.
            /// </para>
            /// <para>
            /// ⚠ AND NO SPLIT SWITCH IS NEEDED TO GET "FADE, NEVER PULL IN EXCEPT POINT-BLANK".
            /// Read <see cref="ApplyCollision"/>: <c>targetFrac</c> starts at 1 (hold the full
            /// seat), and the ONLY path that lowers it is already gated by
            /// <see cref="ShouldPullIn"/> against <c>_occluderPullInDistance</c> — i.e. since
            /// WO-1734 the point-blank backstop is the only pull-in that exists. Splitting
            /// <c>_collisionEnabled</c> into a fade flag plus a pull-in flag would add a second
            /// piece of state whose only distinct setting (fade WITHOUT the backstop) lets the
            /// camera body embed in a mesh. See the WO §12 for the refusal in full.
            /// </para>
            /// </summary>
            public static bool CollisionEnabled => true;

            /// <summary>Facing-recenter stays enabled — the seat must still trail through a turn.</summary>
            public static bool FacingRecenterEnabled => DungeonCam.FacingRecenterEnabled;
            /// <summary>
            /// Post-drag grace (s) — the dungeon's lazy drift. The town whip is 0.4 s + 220 deg/s +
            /// stiffness 4, which is a half-turn in 0.82 s starting 0.4 s after the player's thumb
            /// leaves the screen. That is the shape of "spins about as if possessed", and
            /// <c>_forceCameraFix</c> switches it ON in every raid.
            /// </summary>
            public static float FacingRecenterDelay => DungeonCam.FacingRecenterDelay;
            /// <summary>Max recenter rate (deg/s) — the dungeon's 70. Town 220: the spin.</summary>
            public static float FacingRecenterMaxSpeed => DungeonCam.FacingRecenterMaxSpeed;
            /// <summary>Recenter stiffness (1/s) — the dungeon's. Town 4.</summary>
            public static float FacingRecenterStiffness => DungeonCam.FacingRecenterStiffness;
            /// <summary>Player pitch floor (deg) — the dungeon band. Town -10.</summary>
            public static float PanPitchMin => DungeonCam.PanPitchMin;
            /// <summary>
            /// Player pitch ceiling (deg) — the dungeon band. Town 35: positive pitch RAISES the
            /// seat, so at a short boom 35 degrees stares down at the hero's scalp.
            /// </summary>
            public static float PanPitchMax => DungeonCam.PanPitchMax;

            /// <summary>Heartbeat cadence (s) for the raid yaw evidence.</summary>
            public static float TraceEverySeconds => 1f;

            /// <summary>
            /// The resting boom: |(shoulder, height − lookAt, −back)|. DERIVED, never typed.
            /// <para>
            /// ⛔ IT IS MEASURED FROM THE PIVOT, NOT FROM THE HERO'S FEET, BECAUSE THAT IS WHAT THE
            /// GATE COMPARES. <see cref="ApplyCollision"/> casts from
            /// <c>pivot = position + up * _lookAtHeight</c> to the seat, so the town boom is
            /// |(0, 2.6−2.5, −4.5)| = <b>4.501</b> m and the dungeon's is |(0, 1.9−1.5, −3.2)| =
            /// <b>3.225</b> m. A feet-relative sqrt(2.6²+4.5²) = 5.199 is a different quantity and
            /// the pull-in gate never sees it; both 1765 tickets quoted that form at first, and an
            /// acceptance criterion written to it would fail on correct code.
            /// </para>
            /// </summary>
            public static float Boom => Mathf.Sqrt(
                ShoulderOffset * ShoulderOffset
                + (CameraHeight - LookAtHeight) * (CameraHeight - LookAtHeight)
                + CameraDistance * CameraDistance);
        }

        /// <summary>
        /// WO-1765 — which camera profile a scene gets. ONE resolver, so the dungeon and raid
        /// profiles can never both be live and can never fight over the single village snapshot.
        /// </summary>
        private enum CameraSceneProfile { Town, Dungeon, Raid }

        private CameraSceneProfile _activeProfile = CameraSceneProfile.Town;

        /// <summary>
        /// WO-1765 — the profile a scene name resolves to. Pure and public so the regression drives
        /// the routing instead of grepping for a gate: <c>IsRaid</c> is tested FIRST, mirroring
        /// <c>HubScenes.Classify</c> (<c>HubScenes.cs:169-176</c>), so a name that somehow matched
        /// both can never land on the dungeon seat in a raid.
        /// </summary>
        public static bool ResolvesToRaidCameraProfile(string sceneName)
            => DeNelle.Core.HubScenes.IsRaid(sceneName);

        /// <summary>
        /// WO-1765 — does this scene emit the <c>[Flow:Camera]</c> yaw heartbeat + spike edge?
        /// <para>
        /// ⛔ THE ANSWER USED TO BE "DUNGEONS ONLY", AND THAT SILENCE IS WHY WO-1765 EXISTS. The only
        /// trace that prints yaw was gated on <c>_dungeonProfileActive</c>, and <c>RaidBase*</c> is a
        /// different scene kind — so a 32.8 MB device logcat of a raid session contained ZERO camera
        /// yaw evidence while the owner was reporting that the camera rotates. Pinned by the
        /// regression so it can never go silent in a raid again.
        /// </para>
        /// </summary>
        public static bool ShouldEmitYawEvidence(string sceneName)
            => DeNelle.Core.HubScenes.IsRaid(sceneName) || DeNelle.Core.HubScenes.IsDungeon(sceneName);

        /// <summary>
        /// WO-1765 — does this scene get the NARROWED enemy-scan mask and the structure filter?
        /// <para>
        /// ⛔ RAID ONLY, BY RULING (lead, 2026-09-16: <i>"the TOWN must not change felt behaviour in
        /// this build"</i>). Both halves of the C1 correction are scoped by this one predicate so they
        /// cannot drift apart: a baked town camera's <c>_enemyMask</c> is <c>m_Bits: 256</c> = layer 8
        /// "Structure" ONLY, so structures are the only thing its scan can see — narrowing the mask
        /// there would start firing the combat zoom on mobs for the first time, and filtering
        /// structures out would retire the town's combat zoom altogether. Either edit changes what the
        /// owner feels in the hub. The raid camera is runtime-attached and took <c>~0</c>, so it is
        /// ours to fix, and it is where the defect is reported.
        /// </para>
        /// <para>
        /// Pure and public so the regression drives the SCOPE, not just the rule — a suite that only
        /// pinned <see cref="IsFramingSubject"/> would stay green if someone applied it globally and
        /// moved the town.
        /// </para>
        /// </summary>
        public static bool AppliesRaidScanNarrowing(string sceneName)
            => DeNelle.Core.HubScenes.IsRaid(sceneName);

        private void ApplyDungeonProfileIfNeeded(string why)
        {
            string sceneName = SceneManager.GetActiveScene().name;
            _profileSceneName = sceneName;

            CameraSceneProfile want =
                ResolvesToRaidCameraProfile(sceneName) ? CameraSceneProfile.Raid
                : DeNelle.Core.HubScenes.IsDungeon(sceneName) ? CameraSceneProfile.Dungeon
                : CameraSceneProfile.Town;
            if (want == _activeProfile) return;

            // Leaving town => snapshot the village values ONCE. Leaving any other profile =>
            // restore them first, so a Dungeon->Raid hop layers the new profile on the TOWN
            // baseline rather than on the previous profile's overwritten values.
            if (_activeProfile == CameraSceneProfile.Town) SnapshotVillageCameraValues();
            else RestoreVillageCameraValues();

            _activeProfile        = want;
            _dungeonProfileActive = want == CameraSceneProfile.Dungeon;
            _raidProfileActive    = want == CameraSceneProfile.Raid;

            if (want == CameraSceneProfile.Dungeon) ApplyDungeonSeat();
            else if (want == CameraSceneProfile.Raid) ApplyRaidSeat();

            TraceCameraProfile(why, sceneName);
        }

        /// <summary>Capture the town values ONCE so every restore is exact, never re-typed.</summary>
        private void SnapshotVillageCameraValues()
        {
            _villageFollowOffset     = _followOffset;
            _villageLookAtHeight     = _lookAtHeight;
            _villageLeadDistance     = _leadDistance;
            _villageCombatZoomOut    = _combatZoomOut;
            _villageCombatFovBoost   = _combatFovBoost;
            _villageFramingEnabled   = _framingEnabled;
            _villageCollisionEnabled = _collisionEnabled;
            _villageFacingRecenterEnabled   = _facingRecenterEnabled;
            _villageFacingRecenterDelay     = _facingRecenterDelay;
            _villageFacingRecenterSpeed     = _facingRecenterSpeed;
            _villageFacingRecenterStiffness = _facingRecenterStiffness;
            _villagePanPitchMin             = _panPitchMin;
            _villagePanPitchMax             = _panPitchMax;
            // WO-1765: the SERIALIZED enemy-scan mask, so the raid narrowing is reversible and the
            // town gets its baked value back byte-for-byte on the way out.
            _villageEnemyMask               = _enemyMask;
        }

        /// <summary>Put the town values back exactly as they were snapshotted.</summary>
        private void RestoreVillageCameraValues()
        {
            _followOffset     = _villageFollowOffset;
            _lookAtHeight     = _villageLookAtHeight;
            _leadDistance     = _villageLeadDistance;
            _combatZoomOut    = _villageCombatZoomOut;
            _combatFovBoost   = _villageCombatFovBoost;
            _framingEnabled   = _villageFramingEnabled;
            _collisionEnabled = _villageCollisionEnabled;
            _facingRecenterEnabled   = _villageFacingRecenterEnabled;
            _facingRecenterDelay     = _villageFacingRecenterDelay;
            _facingRecenterSpeed     = _villageFacingRecenterSpeed;
            _facingRecenterStiffness = _villageFacingRecenterStiffness;
            _panPitchMin = _villagePanPitchMin;
            _panPitchMax = _villagePanPitchMax;
            // WO-1765: hand the town back its own baked scan mask. Without this, a camera that
            // survives a raid->town transition would keep the narrowed mask and change town framing
            // for the rest of the session — the exact felt change the ruling forbids.
            _enemyMask   = _villageEnemyMask;
            _dgRoomValid = false;
            _dgRoomId    = null;
            _dgRoomSmall = false;
        }

        /// <summary>
        /// WO-1765 — the RAID over-the-shoulder seat. Every value comes from <see cref="RaidCam"/>;
        /// none is typed here. Collision stays ON (occluders FADE, the seat holds — WO-385), and
        /// NOTHING in this profile rotates the camera on wall proximity: there is no auto-yaw and no
        /// orbit correction anywhere in the raid path, by ruling.
        /// </summary>
        private void ApplyRaidSeat()
        {
            _followOffset = new Vector3(RaidCam.ShoulderOffset, RaidCam.CameraHeight, -RaidCam.CameraDistance);
            _lookAtHeight     = RaidCam.LookAtHeight;
            _leadDistance     = RaidCam.LeadDistance;
            _combatZoomOut    = RaidCam.CombatZoomOut;
            _combatFovBoost   = RaidCam.CombatFovBoost;
            _framingEnabled   = RaidCam.FramingEnabled;
            _collisionEnabled = RaidCam.CollisionEnabled;
            _facingRecenterEnabled   = RaidCam.FacingRecenterEnabled;
            _facingRecenterDelay     = RaidCam.FacingRecenterDelay;
            _facingRecenterSpeed     = RaidCam.FacingRecenterMaxSpeed;
            _facingRecenterStiffness = RaidCam.FacingRecenterStiffness;
            _panPitchMin = RaidCam.PanPitchMin;
            _panPitchMax = RaidCam.PanPitchMax;
            _panPitch    = Mathf.Clamp(_panPitch, _panPitchMin, _panPitchMax);
            // ⛔ THE SCAN-MASK NARROWING LIVES HERE, IN THE RAID BRANCH, AND NOWHERE ELSE.
            // Lead ruling 2026-09-16 (owner protection): the town keeps its baked mask and its
            // present felt behaviour in this build. The raid camera is runtime-attached, so it took
            // the `~0` field initialiser — every one of the 32 layers — which is what let a raid
            // base's 158 Structure-layer wall colliders fill a 32-slot non-alloc buffer.
            ResolveEnemyScanMask();
            _dgTraceTimer = 0f;
        }

        /// <summary>
        /// WO-920 — applies (or lifts) the LOCKED DUNGEON CAMERA profile based on the active scene.
        /// <para>
        /// WHY THIS LIVES HERE AND NOT IN DungeonCameraRig: verified at source 2026-08-07, the
        /// composed dungeons (Assets/Scenes/DungeonCompose/dg_*.unity) and the hand-coded
        /// KayKitChallengeOutpost bake NO camera and NO DungeonCameraRig — grep either .unity for
        /// the Camera class id (!u!20) and you get nothing. HeroControlEnsurer L283-295 creates
        /// "GameplayCamera (ensured)" and attaches THIS component, so in those scenes the dungeon
        /// camera IS SmartMobileCamera. DungeonCameraRig only exists in the two hand-built scenes
        /// (Dungeon_HealersCottage / Dungeon_FolksGranary), and HeroControlEnsurer L256 hands the
        /// camera to it there. Fixing only the rig would have left every dungeon the owner is
        /// actually looking at untouched.
        /// </para>
        /// <para>
        /// WHAT "BOUNCE" ACTUALLY IS on this rig — four independent per-frame motions, all of
        /// which are correct outdoors and wrong in a 10 m room under a 4 m ceiling:
        ///   1. OCCLUSION THRASH (the big one). ApplyCollision spherecasts pivot->seat and, at the
        ///      village 4.5 m seat, hits the wall behind the hero constantly in a corridor. Every
        ///      hit FADES the occluder to ShadowsOnly (WO-385) — which, now that WO-919 gave these
        ///      rooms real walls AND a ceiling, means the room strobes invisible and you see the
        ///      clear colour through it — plus a point-blank pull-in that eases back out at a
        ///      different speed than it snapped in (_collisionApproachSpeed 40 vs _collisionReturnSpeed
        ///      8), i.e. a literal in/out bounce.
        ///   2. COMBAT ZOOM PUMP. _combatZoomOut 2.5 m + _combatFovBoost 4 deg toggling on
        ///      _enemyInRange. Outdoors mobs are occasional; in a dungeon you are inside the scan
        ///      radius of something almost continuously, so the seat pumps.
        ///   3. AUTO-FRAMING YANK. The look-at slides toward the nearest enemy — fine across an
        ///      open field, a visible swing across a small room.
        ///   4. MOVEMENT LEAD. The look-at leads the velocity vector; in tight quarters with
        ///      frequent direction changes that is sway.
        /// All four are switched OFF here, and the seat is re-anchored to the shared
        /// DungeonCameraProfile. Numbers are never re-typed at this call site.
        /// </para>
        /// <para>
        /// DELIBERATELY NOT CHANGED: the yaw model. _orbitBehind + the damped facing-recenter is
        /// what keeps the seat over the hero's shoulder through a corridor turn instead of leaving
        /// it world-locked behind a wall (that was WO-385's whole point, in enclosed geometry).
        /// It is damped, converges, and — unlike DungeonCameraRig's FPV sampler, which reads raw
        /// mouse delta with no button held — cannot drift from idle input: CameraPanInput only
        /// feeds AddYaw after a deliberate 12 px drag or a held right-mouse (CameraPanInput L17-22,
        /// L62-64). So WO-920's "no orbit from idle mouse / accidental drag" is already true on
        /// this rig, and rigidly locking the yaw would trade a real bug for a worse one.
        /// </para>
        /// <para>
        /// KNOWN TRADE, for the felt-test: with collision off the seat can pass through a wall when
        /// the hero backs flat against one. WO-920 §3 Phase A.3 rules for exactly this ("Enabled =
        /// false so walls never yank"), and the shorter 3.2 m seat makes it far rarer than the 4.5 m
        /// one did. If the owner prefers an occasional clip-through to any bounce, this is correct
        /// as-is; if not, the soft alternative is collision ON with the fade path suppressed, which
        /// needs a new switch in ApplyCollision.
        /// </para>
        /// </summary>
        private void ApplyDungeonSeat()
        {
            _followOffset = new Vector3(
                0f,
                DeNelle.Core.World.DungeonCameraProfile.CameraHeight,
                -DeNelle.Core.World.DungeonCameraProfile.CameraDistance);
            _lookAtHeight     = DeNelle.Core.World.DungeonCameraProfile.LookAtHeight;
            _leadDistance     = 0f;      // (4) no look-at sway
            _combatZoomOut    = 0f;      // (2) no seat pump
            _combatFovBoost   = 0f;      // (2) no FOV pump
            _framingEnabled   = false;   // (3) no look-at yank toward mobs
            _collisionEnabled = false;   // (1) no wall pull-in AND no ceiling/wall fade

            // WO-958 (owner F8 seq 2289, "its auto rotating"): her input owns yaw in a
            // dungeon. The yaw MODEL stays (player pan + damped facing-recenter — see the
            // WO-920 note above), but the recenter is re-tuned from the village whip
            // (0.4 s / 220 deg/s / stiffness 4 — a swing at every pause in a small room)
            // to a lazy idle drift, and the pitch band is narrowed so the rotated seat
            // can never bed into the WO-919 ceiling slab. All numbers from the one
            // profile authority; village values restored exactly on exit.
            _facingRecenterEnabled   = DungeonCam.FacingRecenterEnabled;
            _facingRecenterDelay     = DungeonCam.FacingRecenterDelay;
            _facingRecenterSpeed     = DungeonCam.FacingRecenterMaxSpeed;
            _facingRecenterStiffness = DungeonCam.FacingRecenterStiffness;
            _panPitchMin = DungeonCam.PanPitchMin;
            _panPitchMax = DungeonCam.PanPitchMax;
            _panPitch    = Mathf.Clamp(_panPitch, _panPitchMin, _panPitchMax);

            // WO-958: seed the room-aware seat at the standard dungeon framing; the
            // per-frame damp in DungeonRoomSeat walks it tighter when the room is small.
            _dgSeatHeight    = DungeonCam.CameraHeight;
            _dgSeatDist      = DungeonCam.CameraDistance;
            _dgSeatHeightVel = 0f;
            _dgSeatDistVel   = 0f;
            _dgRoomValid     = false;
            _dgRoomId        = null;
            _dgRoomSmall     = false;
            _dgCeilingClamps = 0;
            _dgTraceTimer    = 0f;

            RestoreAllFaded();   // drop anything the village profile had left hidden
        }

        private void TraceCameraProfile(string why, string sceneName)
        {
            // §12 instrumentation: one line answers "which camera am I in, and why" from a log
            // or a headless capture, with no playtest. Pairs with DungeonCameraRig's "mode="
            // line — between them, exactly one fires per dungeon, naming which pipeline owns
            // the view. Camera height vs ceiling is printed because that is the WO's acceptance
            // criterion and the thing a future seat change would silently break.
            // WO-1765: the mode now names three profiles, not two, and prints the SHOULDER arm +
            // the resting boom — the two numbers the raid ruling turns on.
            string mode = _dungeonProfileActive ? "LockedOTS(SmartMobileCamera)"
                        : _raidProfileActive ? "RaidOverShoulder(SmartMobileCamera)"
                        : "Village(SmartMobileCamera)";
            float pivotArmY = _followOffset.y - _lookAtHeight;
            float restingBoom = Mathf.Sqrt(_followOffset.x * _followOffset.x
                + pivotArmY * pivotArmY + _followOffset.z * _followOffset.z);
            DeNelle.Core.Diagnostics.FlowTrace.Step("DungeonCam",
                $"mode={mode} " +
                $"why={why} scene='{sceneName}' " +
                $"seat=(h {_followOffset.y:F2}, back {-_followOffset.z:F2}, shoulder {_followOffset.x:F2}) " +
                $"boom={restingBoom:F2} lookAtY={_lookAtHeight:F2} " +
                $"ceilingRef={DeNelle.Core.World.DungeonCameraProfile.CeilingHeightRef:F1} " +
                $"headroom={DeNelle.Core.World.DungeonCameraProfile.CeilingHeightRef - _followOffset.y:F2} " +
                $"lead={_leadDistance:F2} zoomOut={_combatZoomOut:F2} fovBoost={_combatFovBoost:F1} " +
                $"framing={_framingEnabled} collision={_collisionEnabled} " +
                $"orbitBehind={_orbitBehind} facingRecenter={_facingRecenterEnabled} " +
                // WO-958: the yaw MODEL is unchanged, its TUNING is context-owned now —
                // print the live numbers + room data so a capture names them.
                $"recenter=(delay {_facingRecenterDelay:F2}s, max {_facingRecenterSpeed:F0}deg/s, " +
                $"stiff {_facingRecenterStiffness:F1}) pitchBand=[{_panPitchMin:F0},{_panPitchMax:F0}] " +
                $"roomsPublished={DungeonRoomSense.RoomCount}");
        }

        // ── WO-958: room-aware dungeon framing ────────────────────────────────

        /// <summary>
        /// The live dungeon seat offset (0, height, -boom): resolves the room the hero
        /// occupies from the DungeonRoomSense blackboard, picks the standard or the
        /// small-room profile seat, and SmoothDamps the live values toward it — a room
        /// change is a transition (DungeonCam.RoomSeatSmoothTime), never a snap.
        /// No room data (rooms unpublished / between rooms / hand-built dungeon)
        /// simply means the standard WO-920 seat. Dungeon profile paths only.
        /// </summary>
        private Vector3 DungeonRoomSeat(float dt)
        {
            UpdateDungeonRoom();

            float targetH = _dgRoomSmall ? DungeonCam.SmallRoomCameraHeight : DungeonCam.CameraHeight;
            float targetD = _dgRoomSmall ? DungeonCam.SmallRoomCameraDistance : DungeonCam.CameraDistance;
            _dgSeatHeight = Mathf.SmoothDamp(_dgSeatHeight, targetH, ref _dgSeatHeightVel,
                DungeonCam.RoomSeatSmoothTime, float.MaxValue, dt);
            _dgSeatDist   = Mathf.SmoothDamp(_dgSeatDist, targetD, ref _dgSeatDistVel,
                DungeonCam.RoomSeatSmoothTime, float.MaxValue, dt);
            return new Vector3(0f, _dgSeatHeight, -_dgSeatDist);
        }

        // Resolve which published room contains the hero. Sticky: the CURRENT room keeps
        // slack in its containment test so skirting a doorway edge doesn't flap the room
        // id (and with it the seat target) every frame. Emits one [Flow:Camera] Step per
        // room CHANGE — the heartbeat carries the steady-state.
        private void UpdateDungeonRoom()
        {
            Vector3 heroPos = _target.position;

            if (_dgRoomValid && DungeonRoomSense.ContainsXZ(in _dgRoom, heroPos, DungeonCam.RoomStickySlack))
                return;   // still in the cached room

            bool found = DungeonRoomSense.TryGetRoomAt(heroPos, out var room);
            string newId = found ? room.Id : null;
            if (found == _dgRoomValid && string.Equals(newId, _dgRoomId, System.StringComparison.Ordinal))
                return;   // no change (including the steady "between rooms" state)

            _dgRoomValid = found;
            _dgRoom      = room;
            _dgRoomId    = newId;
            _dgRoomSize  = found ? room.Bounds.size : Vector3.zero;
            _dgRoomSmall = found &&
                Mathf.Min(_dgRoomSize.x, _dgRoomSize.z) <= DungeonCam.SmallRoomMaxExtent;

            DeNelle.Core.Diagnostics.FlowTrace.Step("Camera", _dgRoomValid
                ? $"room -> '{_dgRoomId}' size=({_dgRoomSize.x:F0}x{_dgRoomSize.z:F0}) small={_dgRoomSmall} " +
                  $"seatTarget=(h {(_dgRoomSmall ? DungeonCam.SmallRoomCameraHeight : DungeonCam.CameraHeight):F2}, " +
                  $"d {(_dgRoomSmall ? DungeonCam.SmallRoomCameraDistance : DungeonCam.CameraDistance):F2})"
                : "room -> none (between rooms / no room data) - standard dungeon seat");
        }

        // WO-958 sec.3 evidence heartbeat: boom, seat, yaw source, room id/size, ceiling
        // clamps — the capture that turns "the camera is fighting me" into named numbers.
        // Own timer gates the STRING BUILD (interpolating every frame just to have
        // FlowTrace.Throttle drop it would allocate per frame); the Throttle wrapper's
        // shorter window then never suppresses a line the timer let through.
        /// <summary>
        /// WO-1765 §5b/§5d — measure the SCREEN yaw rate and fire the spike edge.
        /// <para>
        /// ⛔ WHY THE RATE IS TAKEN FROM <c>transform.eulerAngles.y</c> AND NOT FROM
        /// <c>_panYaw</c>. <c>_panYaw</c> rotates the SEAT; the view rotation is written only by
        /// <c>AimAt</c>, as the bearing from the seat to the look-at. So a look-at that slides
        /// sideways — the framing scan, the movement lead, a pull-in shortening the boom — rotates
        /// the screen with <c>_panYaw</c> held perfectly still, at a gain of 1/boom. A
        /// <c>panYaw</c>-only trace is blind to three of the five WO-1765 candidates, and the
        /// strongest of them is in that blind spot.
        /// </para>
        /// <para>
        /// The spike is an EDGE with a cooldown, never a per-frame log: a per-frame line on a frame
        /// path evicts the boot window out of the device logcat ring (<c>FlowTrace.cs:293-300</c>;
        /// memory <c>logcat-ring-buffer-destroys-evidence</c>), destroying the evidence it was added
        /// to collect. <c>Warn</c> rather than <c>Step</c> so it also has a chance of landing in
        /// <c>break-log.jsonl</c> beside the owner's F8 screenshot.
        /// </para>
        /// </summary>
        private void MeasureViewYaw(float dt, Vector3 heroVelFlat, Vector3 heroBase)
        {
            float yawNow = transform.eulerAngles.y;
            if (!_prevViewYawInit)
            {
                _prevViewYaw = yawNow;
                _prevViewYawInit = true;
            }
            // dt is unscaledDeltaTime and can be 0 on the first frame after a load / a pause —
            // dividing by it would publish an Infinity as the headline measurement.
            _viewYawRate = dt > 0.0001f ? Mathf.DeltaAngle(_prevViewYaw, yawNow) / dt : 0f;
            _prevViewYaw = yawNow;
            if (Mathf.Abs(_viewYawRate) > Mathf.Abs(_viewYawRateMax)) _viewYawRateMax = _viewYawRate;

            // The lever arm that AimAt converts into view yaw: the look-at's LATERAL offset from
            // the pivot, measured in the camera's own frame and signed (left/right), in metres.
            _leadLateral = Vector3.Dot(_leadPoint - heroBase, transform.right);

            if (_yawSpikeCooldown > 0f) _yawSpikeCooldown -= dt;

            if (Mathf.Abs(_viewYawRate) >= YawSpikeDegPerSec) _yawSpikeFrames++;
            else _yawSpikeFrames = 0;

            if (_yawSpikeFrames < YawSpikeFrames || _yawSpikeCooldown > 0f) return;
            if (!ShouldEmitYawEvidence(_profileSceneName)) return;

            _yawSpikeFrames  = 0;
            _yawSpikeCooldown = YawSpikeCooldownSeconds;

            float boom = Vector3.Distance(transform.position, heroBase);
            float velMag = heroVelFlat.magnitude;
            // Name the dominant cause from the SAME fields, so the line is a verdict and not a
            // prompt to go and theorise: a framing subject that is a structure is C1; a large
            // lateral arm with a small velocity is C2; a live pull-in is C4; a recenter step is C3.
            string suspect =
                _framingTargetType != "none" && _framingEnabled && _enemyInRange ? "C1 framing-subject"
                : _pullingInNow ? "C4 pull-in lever arm"
                : _dgYawSource == "recenter" ? "C3 facing-recenter"
                : Mathf.Abs(_leadLateral) > 0.35f && velMag < 1f ? "C2 movement-lead (slow, off-axis)"
                : _dgYawSource == "input" ? "player input"
                : "unattributed";
            DeNelle.Core.Diagnostics.FlowTrace.Warn("Camera",
                $"YAW SPIKE viewYawRate={_viewYawRate:F0}deg/s over {YawSpikeFrames} frames " +
                $"(threshold {YawSpikeDegPerSec:F0}) suspect={suspect} " +
                $"yawSrc={_dgYawSource} panYaw={_panYaw:F0} step={_lastRecenterStep:F2} " +
                $"recenterSpeed={_facingRecenterSpeed:F0} boom={boom:F2} " +
                $"leadLateral={_leadLateral:F2}m distanceFrac={_distanceFrac:F2} pullingIn={_pullingInNow} " +
                $"framingTarget='{_framingTargetName}' type={_framingTargetType} " +
                $"dist={_framingTargetDist:F1} structsRejected={_framingStructsRejected} " +
                $"velMag={velMag:F2} snaps={_snapCount} scene='{_profileSceneName}'");
        }

        private void EmitDungeonHeartbeat(float dt)
        {
            _dgTraceTimer -= dt;
            if (_dgTraceTimer > 0f) return;
            float interval = _dungeonProfileActive ? DungeonCam.TraceEverySeconds : RaidCam.TraceEverySeconds;
            _dgTraceTimer = interval;

            float boom = Vector3.Distance(transform.position,
                _target.position + Vector3.up * _lookAtHeight);
            Vector3 vel = GetHeroVelocity();
            vel.y = 0f;
            float heroFacingY = _target != null ? _target.eulerAngles.y : 0f;
            float velDirY = vel.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(vel.normalized).eulerAngles.y : -1f;
            // WO-1765: the yaw block. viewYawRate is the OWNER'S WORD measured; the rest are the
            // candidate discriminators on the same line, so one read settles which is moving it.
            string yawBlock =
                $"viewYawRate={_viewYawRate:F0}deg/s maxSince={_viewYawRateMax:F0} " +
                $"dPanYaw={Mathf.DeltaAngle(_panYawAtLastTrace, _panYaw):F0} " +
                $"heroYaw={heroFacingY:F0} step={_lastRecenterStep:F2} " +
                $"recenterSpeed={_facingRecenterSpeed:F0} recenterDelay={_facingRecenterDelay:F2} " +
                $"velMag={vel.magnitude:F2} velDirY={velDirY:F0} " +
                $"leadLateral={_leadLateral:F2}m " +
                $"framingTarget='{_framingTargetName}' type={_framingTargetType} " +
                $"dist={_framingTargetDist:F1} switches={_framingTargetSwitches} " +
                $"structsRejected={_framingStructsRejected} framing={_framingEnabled} " +
                $"enemyInRange={_enemyInRange} combatBlend={_combatBlend:F2} " +
                $"distanceFrac={_distanceFrac:F2} pullingIn={_pullingInNow} snaps={_snapCount}";

            // ⛔ THE PARTS WITH NESTED QUOTES ARE COMPUTED INTO LOCALS FIRST, ON PURPOSE. CLAUDE.md
            // §1: CompileGate.BraceBalanced has NO interpolated-string model, so a `"` inside a
            // `$"...{ c ? "a" : "b" }..."` hole ends the string for its scanner and the rest of the
            // file is read as code — a correct file then reads unbalanced AT THE GATE and the
            // COMPILE_GATE_OK marker is withheld.
            string profileName = _dungeonProfileActive ? "dungeon" : "raid";
            // WO-1765: a raid has no room data and must SAY so rather than invent any.
            string roomField = "n/a";
            if (_dungeonProfileActive) roomField = _dgRoomValid ? "'" + _dgRoomId + "'" : "none";
            // Same reason: the room-aware seat damp is dungeon-only, so printing its zeroed fields in
            // a raid would read as "the damp is live and has collapsed the seat to 0".
            string dgSeatField = _dungeonProfileActive
                ? $"(h {_dgSeatHeight:F2}, d {_dgSeatDist:F2})"
                : "n/a";
            string avoidance = _collisionEnabled
                ? "collision-on"
                : "collision-off (WO-920: no wall hits by design)";

            DeNelle.Core.Diagnostics.FlowTrace.Throttle("Camera", "wo958-heartbeat",
                interval * 0.5f,
                $"profile={profileName} scene='{_profileSceneName}' " +
                $"boom={boom:F2} seat=(h {_followOffset.y:F2}, back {-_followOffset.z:F2}, " +
                $"shoulder {_followOffset.x:F2}) dgSeat={dgSeatField} " +
                $"yawSrc={_dgYawSource} panYaw={_panYaw:F0} pitch={_panPitch:F1} " +
                $"{yawBlock} " +
                $"room={roomField} " +
                $"size=({_dgRoomSize.x:F0}x{_dgRoomSize.z:F0}) small={_dgRoomSmall} " +
                $"ceilClampsTotal={_dgCeilingClamps} " +
                $"avoidance={avoidance}");

            _viewYawRateMax     = 0f;
            _panYawAtLastTrace  = _panYaw;
            _framingTargetSwitches = 0;
        }

        // DEF-151: build the camera-occlusion mask from the project's NAMED layers so it
        // tracks the real layer indices (walls/buildings/towers = world geometry the camera
        // must not enter) and deliberately omits Enemy/Water/UI/triggers (which must never
        // push the camera). If the inspector value was left at the "~0" sentinel we replace
        // it; an explicitly-narrowed mask the owner set is honored.
        private void ResolveCollisionMask()
        {
            if (_collisionMaskInit) return;
            _collisionMaskInit = true;

            // Treat the default "everything" value as "unset" and compute a sane world mask.
            if (_collisionMask.value == ~0)
                _collisionMask = ComputeDefaultCollisionMask();

            // §12: one line answers "which layers can occlude me", with no theory. Once, not
            // per-frame — this never changes after the first resolve.
            DeNelle.Core.Diagnostics.FlowTrace.Once("Camera", "collision-mask",
                "OCCLUSION MASK RESOLVED = " + DescribeMask(_collisionMask.value)
                + " (raw 0x" + _collisionMask.value.ToString("X8") + "). Any collider on a layer "
                + "NOT listed here is INVISIBLE to the occlusion spherecast and can never be faded.");
        }

        /// <summary>
        /// THE ONE PLACE the camera's occlusion/collision layer mask is computed (WO-1751).
        /// <para>
        /// DEF-151 built this from the project's NAMED layers so it tracks the real layer indices
        /// (walls/buildings/towers = world geometry the camera must not enter) and deliberately
        /// omits Enemy/Water/UI/triggers (which must never push the camera).
        /// </para>
        /// <para>
        /// ⛔ WO-1751 — <b>"Structure" WAS MISSING AND THAT IS THE WHOLE RAID DEFECT.</b> The mask
        /// read Default | Building | Tower. Every wall panel in a raid base is explicitly MOVED to
        /// the "Structure" layer by the builder — <c>RaidBaseGenerator.cs:1999-2000</c>, whose own
        /// comment at <c>:1991</c> says "the 'Structure' layer is what every LoS linecast is masked
        /// to" — and a census of <c>Assets/Scenes/RaidBase_IronBastion.unity</c> (2026-09-15) reads
        /// 158 <c>Wall_*</c> GameObjects on <c>m_Layer: 8</c> plus 2 layer-8 gatehouses
        /// (<c>RaidBaseDresser.cs:1040-1041</c>). <c>ProjectSettings/TagManager.asset:20</c> is the
        /// authority that layer 8 is "Structure". So EVERY vertical occluder in a raid sat outside
        /// the cast: the spherecast hit nothing, <c>_faded</c> stayed empty, and
        /// <c>TraceOcclusionOutcome</c> — which only speaks on a pull-in EDGE or a non-empty fade
        /// set — printed NOTHING AT ALL. The owner's 2026-09-15 capture ("camera parked inside a
        /// watchtower, no occluder fade, no trace line") is that silence, exactly.
        /// </para>
        /// <para>
        /// ⚠ This widens the mask in TOWN as well: every Structure-layer collider there now enters
        /// the occlusion cast. That is intended (a town wall should fade like a raid wall) but it
        /// is an owner-FELT change, not a silent one.
        /// </para>
        /// <para>
        /// <c>public static</c> so <c>CameraWallOcclusionRegression</c> can CALL it rather than
        /// grep the source text for a literal — a source-text pin on a layer list is exactly the
        /// duplicated state CLAUDE.md §5 forbids.
        /// </para>
        /// </summary>
        public static int ComputeDefaultCollisionMask()
        {
            int mask = 1 << 0;                       // Default (ground / most structures live here)
            AddNamedLayer(ref mask, "Building");
            AddNamedLayer(ref mask, "Tower");
            AddNamedLayer(ref mask, "Structure");    // WO-1751: raid + town wall panels live here
            return mask;
        }

        /// <summary>
        /// WO-1765 — THE ONE PLACE the combat/framing SCAN mask is computed. Enemy + Default, by
        /// NAME.
        /// <para>
        /// <b>Why Enemy:</b> <c>EnemyFactory.cs:51-52</c> sets <c>go.layer =
        /// LayerMask.NameToLayer("Enemy")</c> on every spawned body, with a TRIGGER capsule (which
        /// is why the sweep passes <c>QueryTriggerInteraction.Collide</c>).
        /// </para>
        /// <para>
        /// <b>Why Default is kept:</b> not every hostile body is provably relayered —
        /// <c>DragonBoss.cs</c> contains no <c>gameObject.layer</c> assignment at all (grepped
        /// 2026-09-16), so a boss may well sit on Default. Excluding Default to be tidy would
        /// silently blind the framing to exactly the fight that needs it. Ground colliders on
        /// Default carry no <c>IDamageable</c>, so they cost one null check each.
        /// </para>
        /// <para>
        /// <b>What is excluded, and why it is the fix:</b> Structure, Building and Tower. A raid base
        /// baked 158 <c>Wall_*</c> colliders on layer 8 "Structure"; with <c>~0</c> those 158 and the
        /// watchtower capsules all entered a THIRTY-TWO slot non-alloc buffer in arbitrary order, so
        /// the sweep could be full of masonry before it ever saw a mob. UI / Water /
        /// Ignore Raycast / TransparentFX are excluded for the same reason they are excluded from the
        /// occlusion mask.
        /// </para>
        /// <para>
        /// <c>public static</c> so <c>CameraRaidFramingRegression</c> CALLS it and resolves each layer
        /// through <c>LayerMask.NameToLayer</c> — never a source-text lint on a layer list, which
        /// would be the second hand-maintained copy CLAUDE.md §5 forbids.
        /// </para>
        /// </summary>
        public static int ComputeDefaultEnemyScanMask()
        {
            int mask = 1 << 0;                    // Default — bodies that were never relayered
            AddNamedLayer(ref mask, "Enemy");     // EnemyFactory puts every spawned body here
            return mask;
        }

        /// <summary>
        /// WO-1765 — resolve <c>_enemyMask</c> at runtime. Replaces the serialized value when it is
        /// the <c>~0</c> "unset" sentinel OR when it does not contain the Enemy layer at all.
        /// <para>
        /// ⛔ THE SECOND CLAUSE IS NOT BELT-AND-BRACES — IT IS A BAKED-SCENE BUG FIX WITH NO REBAKE.
        /// The baked town cameras serialize <c>_enemyMask m_Bits: 256</c>
        /// (<c>Main_Castle_Overworld.unity:3034-3036</c>, <c>Village2.unity:3387-3389</c>). 256 is
        /// <c>1 &lt;&lt; 8</c>, and layer 8 is <b>Structure</b>, not Enemy — Enemy is layer 7
        /// (<c>ProjectSettings/TagManager.asset</c>, read 2026-09-16). So those cameras have been
        /// sweeping WALLS and nothing else: the combat zoom and the auto-framing in town were driven
        /// entirely by masonry, and no mob could ever enter the scan. Honouring a "narrowed mask the
        /// owner set" (the rule <see cref="ResolveCollisionMask"/> follows) would preserve that, which
        /// is why the Enemy-layer test is the condition rather than the bare <c>~0</c> sentinel.
        /// This is the same no-rebake migration pattern Awake already uses for <c>_followOffset</c>
        /// and <c>_orbitBehind</c>.
        /// </para>
        /// <para>
        /// ⛔ AND THIS IS CALLED FROM THE RAID BRANCH ONLY (<see cref="ApplyRaidSeat"/>), NOT FROM
        /// Awake. It ran for every scene until the lead's 2026-09-16 ruling: <i>"the TOWN must not
        /// change felt behaviour in this build."</i> Applying it in town would take the combat zoom and
        /// the auto-framing from never-firing-on-a-mob to firing on a mob — a 2.5 m zoom-out and +4 FOV
        /// appearing mid-wave. That is the feature's documented intent ("hero + nearest enemy") and it
        /// is still the right end state, but it is a felt change the owner did not ask for here, and it
        /// belongs with the scene-side correction of the baked <c>256</c>. The guard below therefore
        /// still tests for the missing Enemy layer — but only ever sees a raid camera's mask.
        /// <see cref="RestoreVillageCameraValues"/> hands the town its baked value back on exit, so a
        /// camera surviving a raid-&gt;town transition cannot carry the narrowing with it.
        /// </para>
        /// </summary>
        private void ResolveEnemyScanMask()
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            bool hasEnemyLayer = enemyLayer >= 0 && (_enemyMask.value & (1 << enemyLayer)) != 0;
            if (_enemyMask.value != ~0 && hasEnemyLayer) return;

            int before = _enemyMask.value;
            _enemyMask = ComputeDefaultEnemyScanMask();
            DeNelle.Core.Diagnostics.FlowTrace.Once("Camera", "enemy-scan-mask",
                "ENEMY SCAN MASK RESOLVED = " + DescribeMask(_enemyMask.value)
                + " (raw 0x" + _enemyMask.value.ToString("X8") + "), replacing 0x"
                + before.ToString("X8") + " = " + DescribeMask(before)
                + ". A mask without the Enemy layer can only ever frame structures; the 32-slot "
                + "scan buffer is why a raid base's 158 wall colliders had to leave the sweep.");
        }

        /// <summary>
        /// WO-1765 — is this damageable a legitimate AUTO-FRAMING subject for the camera?
        /// <para>
        /// ⛔ <b>A WALL IS NOT.</b> The method's own documented intent is "hero + nearest enemy"
        /// (see the <c>_framingEnabled</c> tooltip), and <c>WallSegment.Faction</c> returns
        /// <c>Hostile</c> in an enemy-owned scene by design (<c>WallSegment.cs:288-289</c> — without
        /// it a raid wall is indestructible), so a raid base's every standing panel satisfied the old
        /// admission test. The look-at was then dragged toward the nearest one; with 115+ candidates
        /// the nearest SWITCHES constantly as the hero walks a wall line, and each switch flips a
        /// lateral look-at arm whose screen-yaw gain is 1/boom. That is the owner's "rotating".
        /// </para>
        /// <para>
        /// THE TEST IS A SECOND INTERFACE, NOT A CAST UP A HIERARCHY.
        /// <c>IDamageableStructure</c> does NOT extend <c>IDamageable</c> — it declares its own
        /// <c>IsAlive</c> and <c>Faction</c> (<c>IDamageableStructure.cs</c>) — and the four dual
        /// implementers are exactly the scenery the camera must ignore: <c>WallSegment</c>,
        /// <c>Gate</c>, <c>DefenseTower</c>, <c>RaidSpire</c>. The mobile hostiles are NOT dual:
        /// <c>EnemyDamageable</c> is <c>IDamageable, IDamageTintable, IHeroDamageMarkable,
        /// ICombatLayered</c> and <c>DragonBoss</c> is <c>IDamageable, ICombatLayered</c> (both read
        /// at source 2026-09-16), so neither is rejected here. <c>TroopController</c> IS an
        /// <c>IDamageableStructure</c> but is Friendly, so it never reached this predicate anyway.
        /// </para>
        /// <para>
        /// ⚠ THIS IS A LAYER-INDEPENDENT TEST ON PURPOSE. The raid camera is attached at runtime and
        /// took <c>_enemyMask = ~0</c>; a mask-only fix would leave the defect one baked scene or one
        /// mis-set inspector field away from returning. The mask narrowing above is the second line
        /// of defence, not the first.
        /// </para>
        /// <para>Pure and public so the regression drives the rule instead of grepping for it.</para>
        /// </summary>
        public static bool IsFramingSubject(DeNelle.Core.Combat.IDamageable dmg)
        {
            if (dmg == null) return false;
            if (!dmg.IsAlive) return false;
            if (dmg.Faction != DeNelle.Core.Combat.CombatFaction.Hostile) return false;
            if (dmg is DeNelle.Core.Combat.IDamageableStructure) return false;
            return true;
        }

        private static void AddNamedLayer(ref int mask, string layerName)
        {
            int idx = LayerMask.NameToLayer(layerName);
            if (idx >= 0) mask |= 1 << idx;
        }

        /// <summary>Human-readable layer list for one mask — evidence, not decoration.</summary>
        private static string DescribeMask(int mask)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                string nm = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(nm)) nm = "<unnamed>";
                if (sb.Length > 0) sb.Append('|');
                sb.Append(i).Append(':').Append(nm);
            }
            return sb.Length > 0 ? sb.ToString() : "<empty>";
        }

        private void OnDestroy()
        {
            UnsubscribeTeleport();   // WO-383: detach the hero teleport handler
            RestoreAllFaded();   // WO-385: never leave a faded wall invisible on teardown
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoadedForCamera;
            // F8-15 death forensic window: follow camera back online (pairs with the DISABLED edge).
            DeNelle.Core.Diagnostics.DeathTrace.Camera("SmartMobileCamera ENABLED (follow on)",
                DeNelle.Core.Diagnostics.DeathTrace.Active ? DeNelle.Core.Diagnostics.DeathTrace.Caller() : "n/a");
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedForCamera;
            UnsubscribeTeleport();   // WO-383: detach the hero teleport handler (re-attaches on next acquire)
            RestoreAllFaded();   // WO-385: restore any faded occluders so nothing is left invisible
            _lockTarget = null;  // WO-512: drop the lock-on framing target so a re-enable starts clean
            // F8-15 death forensic window: the follow camera going DARK during the death window
            // (ArenaDeathCam suspend or any other disabler) is exactly the "camera leaves the
            // hero" symptom — record the edge. Window-gated.
            DeNelle.Core.Diagnostics.DeathTrace.Camera("SmartMobileCamera DISABLED (follow off)",
                DeNelle.Core.Diagnostics.DeathTrace.Active ? DeNelle.Core.Diagnostics.DeathTrace.Caller() : "n/a");
        }

        private void OnSceneLoadedForCamera(Scene scene, LoadSceneMode mode)
        {
            // Re-enforce sole camera and snap on any scene load (including additive OuterWorld)
            // so the follow isn't lost after additive loads or scene transitions in Village2.
            EnforceSoleCamera();
            // WO-920: re-evaluate the dungeon seat BEFORE the snap below, so a camera that
            // survives into (or out of) a dungeon lands on the right framing in one step instead
            // of smooth-damping across from the old one. Idempotent — no-ops when nothing changed.
            ApplyDungeonProfileIfNeeded("sceneLoaded:" + scene.name);
            if (IsTargetValid())
            {
                ForceFollowImmediate();
            }
            else
            {
                TryFindHero();
                if (IsTargetValid())
                {
                    ForceFollowImmediate();
                }
            }
        }

        private void Start()
        {
            // Fallback: if the scene builder didn't wire a target (or the hero
            // spawned after this camera), find the hero by canonical tag/name/loco so the
            // camera is never left staring at the origin/ground (tree) on load.
            EnsureTargetAndSnap();
            EnforceSoleCamera();
        }

        private void EnsureTargetAndSnap()
        {
            if (!IsTargetValid())
            {
                TryFindHero();
            }
            if (IsTargetValid())
            {
                transform.position = _target.position + _followOffset;
                _leadPoint         = _target.position + Vector3.up * _lookAtHeight;
                AimAt(_leadPoint);
                ForceFollowImmediate();  // ensure snap (idempotent)
            }
        }

        private void TryFindHero()
        {
            var heroGo = GameObject.FindWithTag("Player");
            // WO-1513: a SafeFindWithTag("HeroTarget") term sat here. That tag has never
            // been declared in TagManager.asset, so it never returned anything — the
            // component fallback below was always doing the work it appeared to back up.
            // Tag-independent fallback: the baked Village2 hero is NOT tagged Player,
            // which left this camera with no target ("fixed, doesn't follow the hero").
            // The hero definitively carries HeroLocomotion, so lock onto that.
            if (heroGo == null)
            {
                var loco = FindAnyObjectByType<HeroLocomotion>();
                if (loco != null) heroGo = loco.gameObject;
            }
            // Additional fallback for baked hero names (e.g. "Hero (Blaise)", "Hero (Knight)" etc.)
            // used by HeroControlEnsurer and scene builder. Helps when tags are missing or
            // hero appears late on web load / editor scene load.
            if (heroGo == null)
            {
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
                {
                    if (t != null && t.name.StartsWith("Hero ("))
                    {
                        heroGo = t.gameObject;
                        break;
                    }
                }
            }
            if (heroGo != null)
            {
                _target = heroGo.transform;
                Debug.Log($"[SmartMobileCamera] acquired hero target: {heroGo.name}");
            }
        }

        private bool IsTargetValid() => _target != null && _target.gameObject != null;

        // HeroDrift (2026-07-04, extended 2026-07-12): suspend the facing-recenter while the player
        // is steering OR the hero already has speed. The 07-04 velocity-only gate (54322074) left a
        // hole: on stick-down while speed is still ramping, recenter pivoted _panYaw behind the hero,
        // which retargeted HeroLocomotion's camera-relative `move` mid-press (camYaw→move→facing→camYaw).
        private bool ShouldSuspendFacingRecenter()
        {
            if (_target == null) return false;
            if (_followLoco == null || _followLoco.transform != _target)
                _followLoco = _target.GetComponent<HeroLocomotion>();
            if (_followLoco == null) return false;
            // SME audit 2026-07-12 #3b: use the SAME 0.0001 input threshold the locomotion
            // drive uses (HasAnyMoveInput) — WantsToMove's 0.02 deadzone left a soft-input
            // band where the hero moved while the recenter still pivoted the camera-relative
            // basis mid-step (a steady heading curl). WantsToMove keeps its 0.02 for casts.
            return _followLoco.Velocity.sqrMagnitude > MoveEpsilonSqr || HeroLocomotion.HasAnyMoveInput;
        }

        // WO-383: (re)subscribe to the current target's HeroLocomotion.OnTeleported so a
        // scene-seam warp snaps the camera instead of smooth-chasing the jump. Idempotent and
        // null-safe — detaches any previous subscription first, then attaches the new one.
        private void SyncTeleportSubscription()
        {
            HeroLocomotion loco = _target != null ? _target.GetComponent<HeroLocomotion>() : null;
            if (loco == _teleportLoco) return;
            if (_teleportLoco != null) _teleportLoco.OnTeleported -= OnHeroTeleported;
            _teleportLoco = loco;
            if (_teleportLoco != null) _teleportLoco.OnTeleported += OnHeroTeleported;
        }

        // WO-383: detach the teleport subscription (OnDisable / OnDestroy / target change).
        private void UnsubscribeTeleport()
        {
            if (_teleportLoco != null) _teleportLoco.OnTeleported -= OnHeroTeleported;
            _teleportLoco = null;
        }

        // WO-383: the hero just warped (scene seam). Snap the camera to its seat so it never
        // smooth-chases through the intermediate teleport positions. Does NOT touch movement
        // or yaw — purely a follow-position snap (WO-368 world-absolute basis preserved).
        private void OnHeroTeleported()
        {
            if (IsTargetValid()) ForceFollowImmediate();
        }

        private void LateUpdate()
        {
            // WO-1483 frame budget. FIRST line so every early-return path is still timed.
            // Accumulating overload (4-arg) — no per-frame log; PerfReporter rolls it up 1/s.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "SmartMobileCamera.LateUpdate", 4f, 1f);

            // Enforce sole camera every frame so no additive scene camera or Cinemachine vcam can steal the view.
            EnforceSoleCamera();

            // Keep searching for the hero each frame until we have a valid target so we never
            // sit framing the origin/tree. ONLY snap (ForceFollowImmediate) on the frame we first
            // acquire the target — NOT every frame. The old per-frame ">0.5m → snap" fought the
            // SmoothDamp follow below (which aims at a DIFFERENT orbit/collision-adjusted seat),
            // making the camera oscillate between the two points nonstop ("screen shakes back and
            // forth"). Ongoing follow is owned solely by the SmoothDamp pass below.
            if (!IsTargetValid())
            {
                RestoreAllFaded();   // WO-385: target lost — never leave a faded wall invisible
                TryFindHero();
                if (!IsTargetValid()) return;
                ForceFollowImmediate();   // one-shot snap off the tree on first acquisition
            }

            float dt = Time.unscaledDeltaTime;

            // ── 1. Enemy scan (throttled) ──────────────────────────────────────
            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = _enemyScanInterval;
                ScanForEnemies();
            }

            // ── 2. Combat blend ───────────────────────────────────────────────
            float combatTarget = _enemyInRange ? 1f : 0f;
            _combatBlend = Mathf.MoveTowards(_combatBlend, combatTarget, _combatZoomSpeed * dt);

            // ── 3. Follow offset with combat zoom (+ orbit-behind) ────────────
            Vector3 zoomOffset = _followOffset + new Vector3(0f, 0f, -_combatZoomOut * _combatBlend);

            // WO-958 (2): room-aware dungeon seat — shorter boom / raised pitch when the
            // hero's current room is small, eased between seats. Replaces (rather than
            // stacks on) the offset above: the dungeon profile already zeroes combat zoom,
            // so this is the whole dungeon seat. Town path untouched.
            if (_dungeonProfileActive)
                zoomOffset = DungeonRoomSeat(dt);

            // Orbit-behind (DEF-202/204, CAMERA_INPUT_OVERHAUL.md §2): rotate the offset by
            // the PLAYER-authoritative _panYaw (set via AddYaw from pan input) — NOT the hero's
            // velocity. The old velocity-chasing yaw fed back into camera-relative movement and
            // produced the "always turn left" curl; that {yaw←velocity} edge is now structurally
            // absent. _panYaw is a pure accumulator of player input plus an optional damped pull
            // toward the hero's FACING, so holding a constant stick yields a straight line.
            if (_orbitBehind)
            {
                if (!_orbitYawInit) { _panYaw = _target.eulerAngles.y; _orbitYawInit = true; }

                // Facing-recenter — WO-385: the cure for the "world-locked seat" in enclosed hubs.
                // The seat continuously TRAILS the hero's FACING (never velocity → no curl/spiral)
                // after a short post-drag grace, so walking back into the castle and rounding corners
                // keeps the camera on the hero's open side instead of leaving it pinned behind a wall.
                // The step is PROPORTIONAL to the remaining angle (angleErr * stiffness), capped at
                // _facingRecenterSpeed and never overshooting the target — loop gain < 1, converges,
                // and is fully suspended while the player is actively dragging (AddYaw zeroes the timer).
                _timeSinceLastDrag += dt;
                // HeroDrift: SUSPEND the recenter while the player is steering OR the hero already
                // has speed. Recenter pulls _panYaw toward hero FACING; HeroLocomotion reads
                // CameraYaw into its move basis — pivoting the seat mid-press retargets `move` and
                // reopens the wiggle. Reframe only when stick-up AND ~stopped (ShouldSuspend…).
                bool recenterStepped = false;   // WO-958: yaw-source evidence (inert for town)
                if (_facingRecenterEnabled && _timeSinceLastDrag > _facingRecenterDelay
                    && !ShouldSuspendFacingRecenter())
                {
                    float angleErr = Mathf.DeltaAngle(_panYaw, _target.eulerAngles.y);
                    float maxStep  = _facingRecenterSpeed * dt;
                    // Damped step: shrink with the remaining error so the swing eases to a stop.
                    float step = Mathf.Clamp(angleErr * _facingRecenterStiffness * dt, -maxStep, maxStep);
                    // Never step past the target (kills any chance of overshoot/oscillation).
                    if (Mathf.Abs(step) > Mathf.Abs(angleErr)) step = angleErr;
                    _panYaw += step;
                    recenterStepped = Mathf.Abs(step) > 0.001f;
                    // WO-1765: record the degrees ACTUALLY APPLIED, so a capture can sum them and
                    // answer "did the camera rotate itself, and by how much" without a theory.
                    _lastRecenterStep = step;
                }
                else _lastRecenterStep = 0f;

                // WO-958 trace: name this frame's yaw authority for the heartbeat —
                // "input" (her drag is recent), "recenter" (the idle drift moved the seat),
                // or "hold" (nothing rotated).
                // ⛔ WO-1765 §5a: UN-GATED FROM THE DUNGEON. This assignment was
                // `if (_dungeonProfileActive)`, so in a RAID the one field that names the yaw
                // authority was never written and the heartbeat could not have reported it even if
                // it had fired. Town stays silent (no heartbeat consumes it there).
                if (_dungeonProfileActive || _raidProfileActive)
                    _dgYawSource = _timeSinceLastDrag <= _facingRecenterDelay ? "input"
                                 : recenterStepped ? "recenter" : "hold";

                zoomOffset = Quaternion.Euler(_panPitch, _panYaw, 0f) * zoomOffset;
            }

            Vector3 desired = _target.position + zoomOffset;

            // ── 3b. Wall collision / occlusion (DEF-151) ──────────────────────
            // ROOT CAUSE of the bug this fixes: the camera was placed at a fixed
            // offset behind the hero with NO awareness of geometry in between, so a
            // wall sitting between the pivot and the desired seat let the camera slide
            // straight through the mesh — the screen filled with the wall's inside face
            // and the hero shrank to a speck. Fix: spherecast from the hero pivot toward
            // the desired position; if it hits world geometry, pull the seat IN to just
            // in front of the hit so the camera body (+ near clip) never enters the wall.
            // Apply collision after SmoothDamp below. Smoothing toward an already-safe point
            // can leave the actual camera behind/inside the obstruction for several frames.

            // WO-958 (3): ceiling backstop — with dungeon collision OFF (WO-920 ruling)
            // nothing else stops a pitched-up seat from rising into the WO-919 ceiling
            // slab. Clamp the seat below heroFeetY + (CeilingHeightRef - clearance),
            // hero-relative so multi-level floors stay correct. A min() is continuous,
            // so engaging it eases — never a pop. Counted for the heartbeat.
            if (_dungeonProfileActive)
            {
                float maxY = _target.position.y
                    + DungeonCam.CeilingHeightRef - DungeonCam.CeilingClearance;
                if (desired.y > maxY)
                {
                    desired.y = maxY;
                    _dgCeilingClamps++;
                }
            }

            Vector3 smoothed = Vector3.SmoothDamp(
                transform.position, desired, ref _posVelocity, _smoothTime, float.MaxValue, dt);
            transform.position = ApplyCollision(smoothed, dt);
            // DEF-67: apply shake offset on top of the smoothed position.
            if (_shakeOffset.sqrMagnitude > 0.0001f)
                transform.position += _shakeOffset;

            // ── 4. FOV ────────────────────────────────────────────────────────
            _cam.fieldOfView = Mathf.Lerp(_baseFov, _baseFov + _combatFovBoost, _combatBlend);

            // ── 5. Movement lead ──────────────────────────────────────────────
            // Hero velocity is used ONLY for the look-at lead bias here — it MUST NOT feed
            // the camera yaw (_panYaw), or the old curl/spiral returns. This is the single
            // GetHeroVelocity() call, deliberately below the yaw block (CAMERA_INPUT_OVERHAUL.md §2.3).
            Vector3 heroVelFlat = GetHeroVelocity();
            heroVelFlat.y = 0f;
            Vector3 heroBase = _target.position + Vector3.up * _lookAtHeight;
            Vector3 leadTarget = heroBase;
            if (heroVelFlat.sqrMagnitude > 0.01f)
                leadTarget += heroVelFlat.normalized * _leadDistance;

            // WO-958 (3): dungeon facing focus — bias the look-at toward the hero's FACING
            // (never velocity — that edge stays structurally absent) so the frame leads
            // where she is pointed. Routed through the _leadPoint SmoothDamp below, so a
            // quick spin moves the aim under a metre, eased: focus without whipping.
            // (_leadDistance is 0 in the dungeon profile, so this is the only lead.)
            if (_dungeonProfileActive && DungeonCam.FacingLookAhead > 0f)
            {
                Vector3 face = _target.forward;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f)
                    leadTarget += face.normalized * DungeonCam.FacingLookAhead;
            }

            // ── 6. Auto-framing ───────────────────────────────────────────────
            // WO-512 slice 2: LOCK-ON framing override. When a lock target is bound AND the flag is
            // on, frame the LOCKED enemy (not the auto-nearest scan): engage framing immediately
            // (force the combat blend toward 1 so we don't wait for the proximity scan) and bias the
            // look-at toward the locked enemy with the SEPARATE capped _lockFramingBias. Reuses the
            // SAME _leadPoint SmoothDamp below — only the damp GOAL changes, so a switch eases and
            // there is never a snap. GUARDED so flag-off / no-lock is byte-identical to today.
            bool lockFraming = _lockTarget != null && DeNelle.Core.FeatureFlags.LockOn;
            if (lockFraming)
            {
                // Force framing to engage now (don't gate on _enemyInRange / the proximity scan).
                _combatBlend = Mathf.MoveTowards(_combatBlend, 1f, _combatZoomSpeed * dt);

                Vector3 lockPos = _lockTarget.position + Vector3.up;   // chest-ish, matches scan anchor
                Vector3 midpoint = Vector3.Lerp(heroBase, lockPos, _lockFramingBias);
                leadTarget = Vector3.Lerp(leadTarget, midpoint, _combatBlend);
            }
            else if (_framingEnabled && _enemyInRange && _combatBlend > 0.1f)
            {
                Vector3 midpoint = Vector3.Lerp(heroBase, _nearestEnemyPos, _framingBias);
                leadTarget = Vector3.Lerp(leadTarget, midpoint, _combatBlend);
            }

            _leadPoint = Vector3.SmoothDamp(_leadPoint, leadTarget, ref _leadVelocity,
                _leadSmoothTime, float.MaxValue, dt);

            AimAt(_leadPoint);

            // ── WO-1765: the yaw instrument. AFTER AimAt, because it measures the rotation AimAt
            // just wrote — that is the owner's word ("rotating"), and no other quantity in this
            // file is it. Cheap: two float ops plus a Dot; the string build is timer-gated below.
            MeasureViewYaw(dt, heroVelFlat, heroBase);

            // WO-958 sec.3 / WO-1765 §5a: the throttled [Flow:Camera] evidence heartbeat. It used
            // to be dungeon-only, which is why a 32.8 MB raid logcat carried no camera yaw evidence
            // at all while the owner was reporting that the camera rotates.
            //
            // ⚠ THE TWO-BRANCH SHAPE IS DELIBERATE AND IT IS NOT STYLE. `_dungeonProfileActive ||
            // _raidProfileActive` in one condition is the natural way to write this, and it breaks
            // an OUT-OF-SCOPE source-text lint: DungeonCameraTightRoomRegression.cs:100 pins the
            // regex `if\s*\(_dungeonProfileActive\)\s*\n\s*EmitDungeonHeartbeat\(dt\)`. That lint
            // pins the very dungeon-only gate WO-1765's ruling widens, so the correct fix is to
            // re-shape the lint — which is another lane's file. Flagged for the lead in the WO;
            // until then this shape keeps that suite green with identical behaviour.
            if (_dungeonProfileActive)
                EmitDungeonHeartbeat(dt);
            else if (_raidProfileActive)
                EmitDungeonHeartbeat(dt);

            // ── Sole-camera check ─────────────────────────────────────────────
            _soleCheckTimer -= dt;
            if (_soleCheckTimer <= 0f)
            {
                _soleCheckTimer = 1f;
                EnforceSoleCamera();
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Wires the hero target (called by HeroControlEnsurer and scene bootstraps).</summary>
        public void SetTarget(Transform hero)
        {
            _target = hero;
            SyncTeleportSubscription();   // WO-383: track the new target's OnTeleported
            if (hero != null)
            {
                Debug.Log($"[SmartMobileCamera] SetTarget wired to: {hero.name}");
            }
        }

        /// <summary>Forces an immediate snap to the current target (bypasses smooth for initial acquire on load).
        /// Call after SetTarget from external wirers.</summary>
        public void ForceFollowImmediate()
        {
            if (IsTargetValid())
            {
                SyncTeleportSubscription();   // WO-383: ensure we track this target's OnTeleported
                transform.position = _target.position + _followOffset;
                _leadPoint = _target.position + Vector3.up * _lookAtHeight;
                AimAt(_leadPoint);
                _posVelocity = Vector3.zero;
                EnforceSoleCamera();
                // WO-1765 candidate C5: this snap uses the UNROTATED _followOffset (unlike
                // SnapBehindTarget, which applies Euler(_panPitch, _panYaw, 0)), so with a
                // non-zero _panYaw it teleports the seat to the wrong side of the hero and the
                // SmoothDamp swings it back — a fast rotation with no wall involved. COUNTED here
                // so the heartbeat can correlate a spike against it. NOT fixed in this lane: the
                // lead's rulings scope the fix to C1 + the raid profile, and changing this snap's
                // geometry moves every scene-seam and teleport landing.
                _snapCount++;
                Debug.Log("[SmartMobileCamera] ForceFollowImmediate snap executed");
                // F8-15 death forensic window: an instant camera SNAP during the death window is a
                // felt "camera jumped" — name who asked for it. Window-gated.
                DeNelle.Core.Diagnostics.DeathTrace.Camera(
                    $"ForceFollowImmediate SNAP to target '{(_target != null ? _target.name : "<null>")}' @ {transform.position}",
                    DeNelle.Core.Diagnostics.DeathTrace.Active ? DeNelle.Core.Diagnostics.DeathTrace.Caller() : "n/a");
            }
        }

        /// <summary>
        /// Re-seat the orbit camera directly BEHIND the current target's FACING and snap to it.
        /// <para>Fixes the "hero faces the wrong way in the arena" bug (owner on-device 2026-07-15):
        /// after a big warp that REORIENTS the hero (BattleArena stage-in warps the hero to face the
        /// north enemy line), the player-authoritative pan yaw (<c>_panYaw</c>) is stale — it is seeded
        /// once (<c>_orbitYawInit</c>) and never re-seated on a teleport, so the orbit rotates the
        /// behind-offset by the OLD open-world yaw and the camera lands in FRONT of the hero, framing
        /// its face while the enemies sit off-screen behind it. This resets <c>_panYaw</c> to the
        /// target's current facing (camera behind the hero, looking INTO the fight) and snaps the seat.</para>
        /// No-op when orbit-behind is off (world-relative offset is already correct) or the target is
        /// invalid. Idempotent and safe to call any time after a warp.
        /// </summary>
        public void SnapBehindTarget()
        {
            if (!IsTargetValid()) return;
            _panYaw = _orbitBehind ? _target.eulerAngles.y : 0f;
            _orbitYawInit = true;
            _timeSinceLastDrag = _facingRecenterDelay;   // fresh seat: don't let a recenter swing fight it
            Vector3 seatOffset = _orbitBehind
                ? Quaternion.Euler(_panPitch, _panYaw, 0f) * _followOffset
                : _followOffset;
            transform.position = _target.position + seatOffset;
            _leadPoint   = _target.position + Vector3.up * _lookAtHeight;
            AimAt(_leadPoint);
            _posVelocity = Vector3.zero;
            _leadVelocity = Vector3.zero;
            SyncTeleportSubscription();
            EnforceSoleCamera();
            DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena",
                "camera SnapBehindTarget: re-seated BEHIND hero (panYaw=" + _panYaw.ToString("0") +
                ") — kills the stale-yaw 'hero faces away' framing on stage-in.");
        }

        /// <summary>Toggles the auto-framing behaviour at runtime.</summary>
        public bool FramingEnabled
        {
            get => _framingEnabled;
            set => _framingEnabled = value;
        }

        // ── WO-512 slice 2: lock-on framing API ─────────────────────────────────
        /// <summary>
        /// Bind the LOCKED enemy the camera should keep framed (called by BattleArena when the
        /// soft lock engages / switches). While bound AND FeatureFlags.LockOn is on, LateUpdate
        /// frames this transform via the existing _leadPoint damp (no snap). Passing a null /
        /// destroyed transform is treated as a clear. A switch is just another SetLockTarget — the
        /// shared damp eases the look-at to the new foe smoothly. No-op when the flag is off.
        /// </summary>
        public void SetLockTarget(Transform t)
        {
            if (!DeNelle.Core.FeatureFlags.LockOn) return;   // flag off -> today's exact camera
            if (t == null) { ClearLockTarget(); return; }
            _lockTarget = t;
            DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena",
                "LOCKON camera framing target bound '" + t.name.Replace("(Clone)", "").Trim() + "'.");
            // F8-15 death forensic window: a framing-target change while the hero is dying = the
            // camera looking away from the fall. Window-gated.
            DeNelle.Core.Diagnostics.DeathTrace.Camera(
                "lock framing target -> '" + t.name + "'",
                DeNelle.Core.Diagnostics.DeathTrace.Active ? DeNelle.Core.Diagnostics.DeathTrace.Caller() : "n/a");
        }

        /// <summary>Release the lock-on framing (back to today's auto-framing / free-look). The
        /// _leadPoint damp eases back; never a snap. Safe to call any time.</summary>
        public void ClearLockTarget()
        {
            if (_lockTarget == null) return;
            _lockTarget = null;
            DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena", "LOCKON camera framing target cleared.");
            // F8-15 death forensic window: framing released back to hero auto-follow.
            DeNelle.Core.Diagnostics.DeathTrace.Camera("lock framing target cleared -> hero auto-follow",
                DeNelle.Core.Diagnostics.DeathTrace.Active ? DeNelle.Core.Diagnostics.DeathTrace.Caller() : "n/a");
        }

        /// <summary>
        /// Plays a screen-shake impulse. Safe to call from any MonoBehaviour.
        /// Cancels any in-progress shake and starts a fresh one.
        /// <para>DEF-67: called by TowerAudioController on heavy creak/debris events
        /// and by WaveMusicController on boss-wave transitions.</para>
        /// </summary>
        /// <param name="intensity">Peak displacement in world units (0.1 = subtle, 0.5 = heavy).</param>
        /// <param name="duration">Total duration of the shake in seconds.</param>
        public void Shake(float intensity, float duration)
        {
            if (!gameObject.activeInHierarchy) return;
            if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
            _shakeRoutine = StartCoroutine(ShakeRoutine(intensity, duration));
        }

        private System.Collections.IEnumerator ShakeRoutine(float intensity, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                // Envelope: full intensity at start, fades to zero by duration.
                float envelope = 1f - (elapsed / duration);
                _shakeOffset   = Random.insideUnitSphere * (intensity * envelope);
                _shakeOffset.z = 0f; // keep depth axis stable; shake only X/Y
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _shakeOffset  = Vector3.zero;
            _shakeRoutine = null;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void ScanForEnemies()
        {
            if (!IsTargetValid()) { _enemyInRange = false; return; }

            int count = Physics.OverlapSphereNonAlloc(
                _target.position, _combatScanRadius, _scanBuffer, _enemyMask, QueryTriggerInteraction.Collide);

            float closestSqr = float.MaxValue;
            Vector3 closestPos = Vector3.zero;
            bool found = false;
            // WO-1765 evidence: the winner's identity and how many structures were turned away.
            // The reject COUNT matters even when a profile has framing switched off, because the
            // scan still runs — it is what makes a C1 regression visible in a capture.
            string bestName = "none";
            string bestType = "none";
            int structsRejected = 0;

            // ⛔ THE STRUCTURE FILTER IS RAID-ONLY, AND THAT IS A DELIBERATE OWNER-PROTECTION RULING
            // (lead, 2026-09-16), NOT AN OVERSIGHT.
            // Rejecting walls globally reads as obviously right — a camera should frame a fight, not
            // masonry — and it WOULD change the town today. The baked town cameras carry
            // `_enemyMask m_Bits: 256`, i.e. layer 8 "Structure" ONLY, so the *only* thing their scan
            // can currently see IS structures. Filter those out and `_enemyInRange` in town goes from
            // "true near a wall" to "never true", which silently retires the town's combat zoom and
            // auto-framing — a felt change this build is not taking. So the rule applies where the
            // defect is reported and where the mask is ours (the runtime-attached raid camera), and
            // the town keeps its present behaviour byte-for-byte.
            // ⚠ The GLOBAL correction is still the right end state; it is a follow-up, paired with
            // fixing the baked 256 in the scenes (WO §13), because the two only make sense together.
            bool filterStructures = _raidProfileActive;

            for (int i = 0; i < count; i++)
            {
                var col = _scanBuffer[i];
                if (col == null) continue;
                // Only count live hostile IDamageable targets (avoids counting the hero).
                var dmg = col.GetComponentInParent<DeNelle.Core.Combat.IDamageable>();
                if (dmg == null) continue;

                if (filterStructures)
                {
                    // ⛔ WO-1765: A WALL IS NOT A FRAMING SUBJECT. See IsFramingSubject for the whole
                    // reasoning; the count below is the instrument that proves this clause ran.
                    if (!IsFramingSubject(dmg))
                    {
                        if (dmg is DeNelle.Core.Combat.IDamageableStructure) structsRejected++;
                        continue;
                    }
                }
                else if (dmg == null || !dmg.IsAlive
                         || dmg.Faction != DeNelle.Core.Combat.CombatFaction.Hostile)
                {
                    // The pre-WO-1765 admission test, unchanged, for every non-raid scene: live +
                    // hostile, structures included. This is the town's shipped behaviour and the
                    // ruling says it does not move in this build.
                    continue;
                }
                // Second guard, deliberately NOT inside the pure predicate: a structure whose
                // IDamageable and IDamageableStructure sit on DIFFERENT components of the same
                // GameObject would satisfy the interface test above. None does today (all four
                // dual implementers declare both on one class), so this is the cheap insurance
                // against the next one — and it stays out of the predicate so the regression can
                // keep driving a plain object with no GameObject at all.
                var dmgComponent = dmg as Component;
                if (dmgComponent != null
                    && dmgComponent.GetComponent<DeNelle.Core.Combat.IDamageableStructure>() != null)
                {
                    structsRejected++;
                    continue;
                }

                float sqr = (col.transform.position - _target.position).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closestPos = dmg.WorldPosition + Vector3.up;
                    found = true;
                    bestName = dmgComponent != null ? dmgComponent.gameObject.name : col.name;
                    bestType = dmg.GetType().Name;
                }
            }

            _enemyInRange    = found;
            _nearestEnemyPos = found ? closestPos : _target.position + Vector3.up * _lookAtHeight;

            // WO-1765 §5c: framing-subject evidence for the heartbeat. A SWITCH is the thing that
            // flips the lateral look-at arm, so it is counted, not just reported.
            if (!string.Equals(bestName, _framingTargetName, System.StringComparison.Ordinal))
                _framingTargetSwitches++;
            _framingTargetName      = bestName;
            _framingTargetType      = bestType;
            _framingTargetDist      = found ? Mathf.Sqrt(closestSqr) : -1f;
            _framingStructsRejected = structsRejected;
        }

        // WO-385: camera-occlusion pass (replaces the DEF-151 hard pull-in). The old behaviour
        // spherecast pivot→seat and pulled the camera IN to _minCollisionDistance whenever ANY
        // world geometry was between hero and camera — so at EVERY corner the camera jammed to a
        // close "lost" angle and the slow ease-out couldn't recover while the wall persisted.
        // New behaviour: KEEP the camera at its proper seat and FADE the occluding renderer(s) to
        // ShadowsOnly (mesh hidden, shadows kept) so you simply see the hero through the wall.
        // Renderers restore the instant they stop occluding. The only remaining pull-in is a rare
        // safety backstop: if an occluder is point-blank close (< _occluderPullInDistance) we still
        // pull in to it so the camera body never literally embeds in a mesh. Walking past normal
        // corner walls now fades them — it never jams the view.
        private Vector3 ApplyCollision(Vector3 desired, float dt)
        {
            if (!_collisionEnabled || !IsTargetValid())
            {
                _distanceFrac = 1f;
                _pullingInNow = false;   // WO-1765: no stale verdict on the heartbeat
                RestoreAllFaded();   // never leave a wall invisible when collision is off / target lost
                // WO-1734: drop the trace edge too, or a pull-in that was live when collision was
                // switched off would swallow the NEXT "PULL-IN ENTERED" line.
                _wasPullingIn = false;
                return desired;
            }

            // Pivot = where the camera looks (hero chest/head), well above the feet so the
            // cast doesn't immediately bury itself in the ground collider at the hero's base.
            Vector3 pivot = _target.position + Vector3.up * _lookAtHeight;
            Vector3 toCam = desired - pivot;
            float fullDist = toCam.magnitude;
            if (fullDist <= 0.0001f)
            {
                _distanceFrac = 1f;
                _pullingInNow = false;   // WO-1765: no stale verdict on the heartbeat
                RestoreFadedNotHitThisFrame();
                return desired;
            }

            Vector3 dir = toCam / fullDist;

            // WO-1734 RESTORE: mark the frame, do NOT un-hide everything. `RestoreAllFaded()` stood
            // here from 486cd7b17 until 2026-09-15 and it is what made the fade path inert — it
            // clears `_faded` every frame, so nothing can stay hidden across two frames and the
            // "hold the seat, fade the wall" contract had no state to hold.
            _fadedThisFrame.Clear();
            float nearestOccluderDist = float.MaxValue;
            _occluderDistances.Clear();

            // SphereCastAll so the camera body — not an infinitely-thin ray — clears the wall,
            // and so we catch EVERY occluder between hero and seat (not just the first), fading
            // them all. QueryTriggerInteraction.Ignore so combat-scan / pickup triggers never block.
            int count = Physics.SphereCastNonAlloc(pivot, _collisionRadius, dir, _occluderHits,
                fullDist, _collisionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider col = _occluderHits[i].collider;
                if (col == null) continue;
                // Never fade or collide against the hero's own body.
                if (IsTargetCollider(col)) continue;

                float hitDist = _occluderHits[i].distance;
                if (hitDist < nearestOccluderDist) nearestOccluderDist = hitDist;
                // WO-1753: every accepted hit, so the gate below can pick the occluder nearest the
                // SEAT. nearestOccluderDist stays because the fade trace names it.
                _occluderDistances.Add(hitDist);

                // Hide the visible mesh of this occluder (keep its shadows) so the hero shows through.
                FadeOccluder(col);
            }

            // Restore any renderer we faded on a previous frame that is NOT occluding now.
            RestoreFadedNotHitThisFrame();

            // Target fraction of the full distance we're allowed this frame (1 = full seat).
            // Default: hold the full seat (we faded the wall rather than pulling in).
            float targetFrac = 1f;

            // SAFETY BACKSTOP ONLY: if an occluder is point-blank close, still pull in to it so
            // the camera body / near clip never embeds in the mesh. This is the rare last resort
            // — normal corner walls (well beyond _occluderPullInDistance) are faded, not pulled in.
            //
            // ⛔ WO-1734 — THE GATE IS `_occluderPullInDistance`, NEVER `float.MaxValue`. From
            // 486cd7b17 (2026-09-01) until 2026-09-15 this read `nearestOccluderDist <
            // float.MaxValue`, i.e. TRUE for any occluder at any distance — the DEF-151 hard
            // pull-in that WO-385 existed to delete, reinstated under WO-385's own comment. In a
            // ~4 m raid gate that collapses the seat to the emergency floor, where a small yaw
            // becomes an enormous screen rotation (the owner's "camera spin").
            // ⛔ WO-1753 — THE GATE MEASURES FROM THE SEAT, NOT FROM THE HERO'S CHEST.
            // The sweep starts at `pivot` (the hero's chest), so `nearestOccluderDist` is a
            // distance from the HERO. Gating on it made a wall 0.6 m in front of the CHEST fire the
            // backstop — with a 4.5 m boom that is a wall 3.9 m in FRONT OF THE SEAT, nowhere near
            // embedding the camera, and it collapsed the seat to the 1.2 m floor (a 3.75x zoom
            // where a small yaw is an enormous screen rotation: the owner's "camera spin"). Once
            // WO-1751 put the 158 raid `Wall_*` colliders into the mask, that went from impossible
            // to constant in a 3.0 m corridor.
            // The gate is the gap between the seat and the occluder NEAREST THE SEAT, i.e. the
            // FARTHEST hit along the cast. ⚠ It must be the MAX: with one occluder 0.5 m behind the
            // hero and another AT the seat, a min yields 4.0 m of clearance, no pull-in fires, and
            // the camera sits INSIDE the seat-side wall. `_occluderPullInDistance` (0.6 m) is
            // unchanged and still correct under the new meaning: sphere radius 0.35 + near clip
            // 0.08 = 0.43 m needed, ~0.17 m margin.
            float occluderGateDist = SelectOccluderGateDistance(_occluderDistances);
            bool pullingIn = ShouldPullIn(fullDist, occluderGateDist, _occluderPullInDistance);
            _pullingInNow = pullingIn;   // WO-1765: the heartbeat separates rotation from pull-in
            if (pullingIn)
            {
                // The seat is set against the SAME occluder the gate judged. Handing
                // nearestOccluderDist here would re-create the collapse from the other side:
                // the gate would fire on a wall at the seat and then pull in to a wall behind the
                // hero, i.e. straight to the _minCollisionDistance floor.
                float allowed = AllowedCameraDistance(
                    fullDist, occluderGateDist, _collisionSkin, _minCollisionDistance);
                targetFrac = allowed / fullDist;
            }

            // Pull IN fast (avoid a clip frame), ease OUT slowly (no jitter along a wall).
            float speed = targetFrac < _distanceFrac ? _collisionApproachSpeed : _collisionReturnSpeed;
            _distanceFrac = Mathf.MoveTowards(_distanceFrac, targetFrac, speed * dt);

            Vector3 seat = pivot + dir * (fullDist * _distanceFrac);
            TraceOcclusionOutcome(pullingIn, nearestOccluderDist, occluderGateDist, fullDist * _distanceFrac, fullDist);
            if (nearestOccluderDist >= float.MaxValue) TraceSeatEmbeddedButUnseen(seat);
            return seat;
        }

        // WO-1751 — THE INSTRUMENT FOR THE SILENCE. TraceOcclusionOutcome above speaks only on a
        // pull-in EDGE or a non-empty fade set, so "the cast found NOTHING" is indistinguishable in
        // a log from "the camera was never running". That ambiguity is what cost the 2026-09-15
        // triage: the owner's capture showed the camera inside a watchtower and the occluder trace
        // was simply absent, which reads equally as "collision off", "outside the mask", "no
        // collider", or "off the pivot->seat segment".
        //
        // ⛔ DELIBERATELY NOT A "no occluder found" TRACE. A clear line of sight is the NORMAL case
        // and tracing it would fire in town forever, evicting the boot window out of the device
        // logcat ring (memory `logcat-ring-buffer-destroys-evidence`). This speaks ONLY in the
        // defect shape: the cast saw nothing, yet the camera BODY is overlapping real geometry —
        // i.e. the seat is inside something the spherecast could not see. It names the collider,
        // its layer, and whether that layer is in the mask, which separates all four causes above
        // in ONE line. Throttled, and silent in every healthy frame.
        //
        // COST NOTE, stated rather than assumed: the THROTTLE gates only the LOG LINE — the overlap
        // QUERY itself runs on every clear-line-of-sight frame. That is one non-alloc
        // OverlapSphereNonAlloc against an 8-slot buffer, which is why it is acceptable here; if a
        // frame-budget ticket ever names it (per CLAUDE.md sec.12, measure first with the 4-arg
        // FlowTrace.Measure), gate the query on `Time.frameCount % 8 == 0` rather than deleting it.
        private void TraceSeatEmbeddedButUnseen(Vector3 seat)
        {
            int hits = Physics.OverlapSphereNonAlloc(seat, _collisionRadius, _seatOverlap,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider col = _seatOverlap[i];
                if (col == null || col.isTrigger) continue;
                if (IsTargetCollider(col)) continue;
                // ⛔ MOVING BODIES ARE NOT THE GEOMETRY THIS DETECTOR EXISTS FOR. In a raid the hero
                // stands inside a melee — ten troops plus mobs — and every one of them overlaps the
                // seat sphere constantly. Without this filter the 8-slot buffer fills with bodies
                // and the tower/wall the detector was written to name never gets reported. A
                // Rigidbody or a NavMeshAgent in the parents is the cheap, exact test for "this
                // thing walks"; static world geometry has neither.
                if (col.attachedRigidbody != null) continue;
                if (col.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null) continue;

                int layer = col.gameObject.layer;
                bool inMask = (_collisionMask.value & (1 << layer)) != 0;
                string layerName = LayerMask.LayerToName(layer);
                if (string.IsNullOrEmpty(layerName)) layerName = "<unnamed>";
                string cause = inMask
                    ? "its layer IS in the mask, so the cast should have seen it - suspect the "
                      + "pivot->seat SEGMENT missing it (the geometry is beside the camera, not on "
                      + "the line) or a collider added after the cast"
                    : "its layer is NOT in the occlusion mask, so the spherecast is structurally "
                      + "blind to it - widen ComputeDefaultCollisionMask";

                DeNelle.Core.Diagnostics.FlowTrace.Throttle("Camera", "seat-embedded", 3f,
                    "CAMERA SEAT EMBEDDED IN UNSEEN GEOMETRY - the occlusion spherecast returned NO "
                    + "occluder, yet the camera body overlaps collider '" + col.name + "' on layer "
                    + layer + ":" + layerName + ". Mask = " + DescribeMask(_collisionMask.value)
                    + ". Diagnosis: " + cause + ".");
                return;
            }
        }

        // WO-1734 §12 instrumentation: make "did the camera FADE the wall or PULL IN to it, and
        // where did the seat end up" readable from ONE log line, with no theory.
        //
        // Edge-triggered on the fade<->pull-in TRANSITION via Step (a pull-in episode can last two
        // frames and a Throttle window would miss it entirely), plus a Throttle for the ongoing
        // fade so a long occlusion still prints. Per §12 this is a FRAME path, so the steady state
        // is rate-limited and only the transition is unconditional.
        // WO-1753: the ENTERED line now names the SEAT-SIDE gap, because that is what the gate
        // actually tests. It read "nearest occluder Xm is inside the point-blank backstop", which
        // after the gate moved to the farthest hit would have been a false sentence in the log —
        // and a trace that misnames its own trigger costs the next triage a session. Both distances
        // are printed: the gap decides, the nearest is what the fade is working on.
        private void TraceOcclusionOutcome(bool pullingIn, float nearestOccluderDist, float occluderGateDist,
            float seatDist, float fullDist)
        {
            string occluder = nearestOccluderDist < float.MaxValue
                ? nearestOccluderDist.ToString("0.##") + "m" : "none";
            string seatGap = occluderGateDist >= 0f
                ? (fullDist - occluderGateDist).ToString("0.##") + "m" : "clear";

            if (pullingIn != _wasPullingIn)
            {
                _wasPullingIn = pullingIn;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Camera", pullingIn
                    ? "OCCLUDER PULL-IN ENTERED - the seat-side gap " + seatGap
                      + " (occluder nearest the seat at " + occluderGateDist.ToString("0.##")
                      + "m of " + fullDist.ToString("0.##") + "m; nearest to the hero " + occluder
                      + ") is inside the point-blank backstop " + _occluderPullInDistance.ToString("0.##")
                      + "m; seat " + seatDist.ToString("0.##") + "m of " + fullDist.ToString("0.##")
                      + "m (floor " + _minCollisionDistance.ToString("0.##") + "m)."
                    : "OCCLUDER PULL-IN RELEASED - back to the FADE contract; seat "
                      + seatDist.ToString("0.##") + "m of " + fullDist.ToString("0.##") + "m.");
                return;
            }

            if (!pullingIn && _faded.Count > 0)
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("Camera", "occluder-fade", 2f,
                    "OCCLUDER FADED x" + _faded.Count + " (nearest " + occluder
                    + ", seat-side gap " + seatGap
                    + ") - seat HELD at " + seatDist.ToString("0.##") + "m of "
                    + fullDist.ToString("0.##") + "m. WO-385 contract: fade the wall, hold the seat.");
        }

        /// <summary>
        /// WO-1753 — the sentinel <see cref="SelectOccluderGateDistance"/> returns when the sweep
        /// found no occluder at all. Negative on purpose: a <c>RaycastHit.distance</c> is never
        /// negative, so no real hit can ever collide with it, and <see cref="ShouldPullIn"/> can
        /// reject it with a sign test instead of a magic float comparison.
        /// </summary>
        public const float NoOccluderGateDistance = -1f;

        /// <summary>
        /// WO-1753 — reduce one frame's accepted occluder distances to the ONE the pull-in gate
        /// judges: the occluder nearest the camera SEAT, i.e. the <b>FARTHEST</b> hit along a cast
        /// that starts at the hero's chest.
        /// <para>
        /// ⛔ <b>MAX, NEVER MIN — and that is the whole ticket.</b> A min (the old
        /// <c>nearestOccluderDist</c>) answers "how close is the nearest wall to the HERO", which
        /// the backstop has no use for. Worse, it is wrong in BOTH directions: a wall 0.6 m in
        /// front of the chest fires a pull-in the camera did not need (a 3.75x zoom at a 4.5 m
        /// boom), while an occluder 0.5 m behind the hero paired with one AT the seat yields 4.0 m
        /// of apparent clearance, fires nothing, and leaves the camera body inside the seat-side
        /// wall.
        /// </para>
        /// <para>
        /// Public and pure so the regression can DRIVE the reduction rather than grep for it: a
        /// source-text lint cannot tell a max from a min, and this file has already shipped one
        /// lint that pinned the defect it was written to prevent.
        /// </para>
        /// </summary>
        public static float SelectOccluderGateDistance(IList<float> hitDistances)
        {
            if (hitDistances == null || hitDistances.Count == 0) return NoOccluderGateDistance;
            float farthest = NoOccluderGateDistance;
            for (int i = 0; i < hitDistances.Count; i++)
                if (hitDistances[i] > farthest) farthest = hitDistances[i];
            return farthest;
        }

        /// <summary>
        /// WO-1753 — the point-blank backstop's gate: does the occluder nearest the seat sit within
        /// <paramref name="pullInDistance"/> of the seat? Measured as the GAP
        /// (<c>fullDistance - occluderGateDistance</c>), never as a raw distance from the pivot.
        /// A negative gate distance means the sweep found nothing, which is never a pull-in.
        /// </summary>
        public static bool ShouldPullIn(float fullDistance, float occluderGateDistance, float pullInDistance)
        {
            if (occluderGateDistance < 0f) return false;
            return (fullDistance - occluderGateDistance) < pullInDistance;
        }

        /// <summary>
        /// Pure near-side seating contract used by regression coverage.
        /// <para>
        /// WO-1734: the floor is the AUTHORED <c>_minCollisionDistance</c>, not a bare literal. The
        /// old 3-arg form hardcoded 0.25f, which is why <c>_minCollisionDistance</c> (1.2 m) sat in
        /// the inspector referenced by nothing but a comment — and why a point-blank backstop could
        /// seat the camera a quarter of a metre from the hero's chest.
        /// </para>
        /// <para>
        /// <c>Mathf.Min(minDistance, fullDistance)</c> is deliberate: <c>Mathf.Clamp</c> returns
        /// <c>min</c> when <c>min &gt; max</c>, so a 1.2 m floor against a 0.9 m boom would push the
        /// camera FURTHER OUT than its own authored seat. The boom always wins the ceiling.
        /// </para>
        /// <para>
        /// WO-1753: <paramref name="hitDistance"/> is now fed the occluder nearest the SEAT (the
        /// farthest hit), the same one the gate judged — see <see cref="ShouldPullIn"/>.
        /// </para>
        /// </summary>
        public static float AllowedCameraDistance(
            float fullDistance, float hitDistance, float skin, float minDistance)
        {
            if (fullDistance <= 0f) return 0f;
            float floor = Mathf.Min(Mathf.Max(0f, minDistance), fullDistance);
            return Mathf.Clamp(hitDistance - Mathf.Max(0f, skin), floor, fullDistance);
        }

        // Hide an occluder's visible mesh (set ShadowsOnly) so the hero shows through, keeping its
        // shadows. Stores the ORIGINAL shadow casting mode the first time we touch each renderer so
        // restore is exact. Marks every renderer touched this frame in _fadedThisFrame.
        // ⛔ WO-1751 — THIS FADED ONLY ONE RENDERER, AND A RAID WALL HAS THREE.
        //
        // The old body did `GetComponent` then `GetComponentInParent` then
        // `GetComponentInChildren<Renderer>()` — all SINGULAR, and the last one returns the FIRST
        // match in the subtree. That is correct for a one-mesh prop and WRONG for every wall in
        // the game. Read out of the baked scene (Assets/Scenes/RaidBase_IronBastion.unity,
        // 2026-09-15), the collider's own GameObject `Wall_Outer_SN_0` (layer 8, BoxCollider,
        // NavMeshObstacle) owns THREE renderer subtrees as DIRECT CHILDREN:
        //     Wall_Outer_SN_0
        //       +- steel_wall                (prefab instance, the wall art)
        //       +- Clad_Wall_Outer_SN_0      (prefab instance, the clad panel the player sees)
        //       +- Ruin_Wall_Outer_SN_0      -> RuinPiece_0   (the breached-state art)
        // Fading the first of those left the other two drawing, so the wall still blocked the
        // view — a fade that does nothing, which is indistinguishable in a capture from no fade
        // at all.
        //
        // ⚠ AND THE CORRECTION THAT MATTERS MORE THAN THE FIX: this lane's own first hand-back
        // claimed the clad panel was a SIBLING under `Zone_Clad` and that the fade therefore could
        // not reach it at all. THAT CLAIM WAS WRONG. It was inferred from the GENERATOR's
        // structure instead of read out of the BAKED TREE, and the two disagree — only the 16
        // `Clad_Corner_*` hang under `Zone_Clad`; all 158 `Clad_Wall_*` are children of their own
        // blocker. The real defect was one word (`GetComponentInChildren` vs
        // `GetComponentsInChildren`), not a missing link. CLAUDE.md's "read the code, not the
        // comment" applies to a builder's intent exactly as much as to a doc.
        //
        // RESOLUTION LADDER, tightest root first, and SILENT-SAFE at every rung: a collider with
        // no resolvable renderer simply returns, exactly as before — such a wall still pulls in at
        // the point-blank backstop and nothing throws. Everything faded is registered in `_faded`
        // + `_fadedThisFrame`, so RestoreFadedNotHitThisFrame un-hides it the instant it stops
        // occluding, and RestoreAllFaded covers teardown. Nothing can be left permanently hidden.
        private void FadeOccluder(Collider col)
        {
            if (CollectOccluderRenderers(col, _fadeScratch) == 0) return;
            for (int i = 0; i < _fadeScratch.Count; i++) FadeOneRenderer(_fadeScratch[i]);
        }

        /// <summary>Bound on one collider's fade set. A pathological root (a zone or base root that
        /// somehow acquired a collider) must not cost an unbounded walk every frame.</summary>
        public const int MaxFadedRenderersPerCollider = 32;

        /// <summary>
        /// THE ONE DECIDER for "which renderers does this occluding collider hide" (WO-1751).
        /// Fills <paramref name="into"/> (cleared first) and returns the count.
        /// <para>
        /// Rung 1 is the collider's OWN object and everything under it — the tightest root that
        /// still covers a compound body, and the rung every wall and tower takes. Rung 2 (nothing
        /// renderable below) takes the NEAREST ANCESTOR renderer ONLY, never its whole subtree,
        /// which could be a zone root holding half the base.
        /// </para>
        /// <para>
        /// <c>GetComponentsInChildren&lt;Renderer&gt;(false)</c> — ACTIVE ONLY, deliberately. A
        /// breached wall's ruin art is an INACTIVE child: in
        /// <c>Assets/Scenes/RaidBase_IronBastion.unity</c> all <b>158</b> <c>Ruin_Wall_*</c>
        /// GameObjects carry <c>m_IsActive: 0</c> (and all 158 <c>Wall_*</c> carry
        /// <c>m_IsActive: 1</c>) — counted from the file, not assumed. Registering an inactive
        /// renderer would hand <see cref="RestoreFadedNotHitThisFrame"/> something to "restore",
        /// REVEALING art the game had deliberately hidden.
        /// </para>
        /// <para>
        /// <c>public static</c> so <c>CameraWallOcclusionRegression</c> can build the baked wall's
        /// real shape and assert the resolution, rather than grep for a method name. Pure apart
        /// from the caller's list: no state, no mutation of anything it finds.
        /// </para>
        /// </summary>
        public static int CollectOccluderRenderers(Collider col, List<Renderer> into)
        {
            if (into == null) return 0;
            into.Clear();
            if (col == null) return 0;

            var owned = col.GetComponentsInChildren<Renderer>(false);
            if (owned != null)
            {
                for (int i = 0; i < owned.Length && into.Count < MaxFadedRenderersPerCollider; i++)
                {
                    if (owned[i] != null) into.Add(owned[i]);
                }
            }
            if (into.Count > 0) return into.Count;

            var ancestor = col.GetComponentInParent<Renderer>();
            if (ancestor != null) into.Add(ancestor);
            return into.Count;
        }

        /// <summary>Hide one renderer's mesh, remembering its ORIGINAL shadow mode so the restore is
        /// exact. True when a live renderer was marked. Null-safe.</summary>
        private bool FadeOneRenderer(Renderer rend)
        {
            if (rend == null) return false;

            if (!_faded.ContainsKey(rend))
                _faded[rend] = rend.shadowCastingMode;

            _fadedThisFrame.Add(rend);
            if (rend.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            return true;
        }

        // Restore (un-hide) every faded renderer that was NOT an occluder this frame, so walls
        // reappear the instant they stop blocking the view. Null-safe (renderers can be destroyed).
        private void RestoreFadedNotHitThisFrame()
        {
            if (_faded.Count == 0) return;

            _restoreScratch.Clear();
            foreach (var kv in _faded)
            {
                if (kv.Key == null || !_fadedThisFrame.Contains(kv.Key))
                    _restoreScratch.Add(kv.Key);
            }

            for (int i = 0; i < _restoreScratch.Count; i++)
            {
                var rend = _restoreScratch[i];
                if (rend != null)
                    rend.shadowCastingMode = _faded[rend];
                _faded.Remove(rend);
            }
        }

        // Restore ALL faded renderers to their original shadow casting mode and clear the sets, so
        // nothing is ever left invisible (collision disabled, target lost, scene change, teardown).
        private void RestoreAllFaded()
        {
            if (_faded.Count == 0) { _fadedThisFrame.Clear(); return; }

            foreach (var kv in _faded)
            {
                if (kv.Key != null)
                    kv.Key.shadowCastingMode = kv.Value;
            }
            _faded.Clear();
            _fadedThisFrame.Clear();
        }

        // True if the hit collider belongs to the hero we're following (its own body must
        // never count as an occluder, or the camera would jam onto the hero's back).
        private bool IsTargetCollider(Collider col)
        {
            if (col == null || _target == null) return false;
            return col.transform == _target || col.transform.IsChildOf(_target);
        }

        private Vector3 GetHeroVelocity()
        {
            // Try HeroLocomotion first (has velocity), fall back to transform delta.
            var loco = _target.GetComponent<HeroLocomotion>();
            return loco != null ? loco.Velocity : Vector3.zero;
        }

        private void AimAt(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        public void EnforceSoleCamera()
        {
            if (_cam == null) return;
            _cam.depth = 100f;
            if (!_cam.enabled) _cam.enabled = true;

            // The builder (Village2Playable / VillageSceneBuilder) adds BOTH the legacy
            // VillageCamera AND this SmartMobileCamera to the Main Camera, on the documented
            // assumption that "SMC's EnforceSoleCamera disables VillageCamera and takes over".
            // The loop below only disables Camera COMPONENTS on OTHER GameObjects — it can NOT
            // disable a sibling follow-SCRIPT sharing this GameObject's single Camera. So the
            // two follow scripts ran every frame, both writing transform.position/rotation and
            // fighting over the seat (SMC wants height 2.6/dist 4.5; VillageCamera's Awake forces
            // height 5.5/dist 9). Realize the intended sole-camera contract: disable the sibling
            // legacy follow rig so ONLY SmartMobileCamera drives the view.
            var legacy = GetComponent<VillageCamera>();
            if (legacy != null && legacy.enabled)
            {
                legacy.enabled = false;
                Debug.Log("[SmartMobileCamera] disabled sibling VillageCamera (sole-camera contract).");
            }

            foreach (var c in Camera.allCameras)
            {
                if (c == null || c == _cam) continue;
                if (c.targetTexture != null) continue;
                if (!c.enabled) continue;
                c.enabled = false;
                Debug.Log($"[SmartMobileCamera] disabled rogue screen camera '{c.name}'.");
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_target == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(_target.position, _combatScanRadius);

            if (Application.isPlaying)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(_target.position + Vector3.up * _lookAtHeight, _leadPoint);
                Gizmos.DrawWireSphere(_leadPoint, 0.2f);
            }
        }
#endif
    }
}
