// =============================================================================
// HeroLocomotion — WASD / dpad walking for Hero (Blaise) in the village.
// -----------------------------------------------------------------------------
// CORRECTED 2026-06-12 — the old header LIED: it claimed "no NavMeshAgent — pure
// transform". The code is the OPPOSITE and trusting that comment mis-diagnosed
// every movement bug this session. HeroLocomotion is a NavMeshAgent driven
// KINEMATICALLY by input: Awake gets-or-adds a NavMeshAgent (updateRotation off,
// high speed so Move never caps), reads input -> eased Velocity -> _agent.Move(step)
// when on the navmesh, else transform.position += step (off-mesh fallback); facing
// via manual LookRotation. So debug "can't move / can't exit" via the NavMesh BAKE,
// not colliders. Input is camera-relative in follow (rotated by
// SmartMobileCamera.CameraYaw), world-absolute in top-down; read from Keyboard.current
// (WASD / arrows) or Gamepad.current via the new Input System (activeInputHandler
// "Both"). WarpTo disables -> warps -> re-enables the agent for scene-seam crossings.
// Movement is in the XZ plane; the agent keeps the hero grounded.
//
// TRANSFORM OWNERSHIP (WO-968 / WO-1016, 2026-08-10) — READ BEFORE TOUCHING MOVEMENT.
// This component is NOT always the mover. In a dungeon the Keeper carries a
// CharacterController (DeNelle.Dungeons.DungeonHero) and THAT is the integrator. The rule
// is a per-frame CAPABILITY check, ForeignMoverOwnsTransform() — a live CharacterController
// on this rig means this component writes NOTHING (no position, no rotation, no auto-walk,
// no lock-face) that frame. It is the exact inverse of DungeonHero's own "my CC is disabled,
// the arena owns the hero" guard, so exactly ONE of the two writes the transform, ever.
// Ownership is SELF-REPORTING: every flip emits [Flow:HeroOwner] TRANSFORM OWNER -> ... and
// the 1 Hz heartbeat prints owner=/animFeed=.
//
// THE ANIMATOR FEED IS MOVER-AGNOSTIC. Speed is published from the MEASURED root speed
// (delta position / dt, sampled in LateUpdate after every mover has run) whenever a foreign
// mover owns the rig, and from this component's Velocity otherwise. Feeding a dead Velocity
// while another component moved the root is what made the Keeper slide through a dungeon in
// a single idle clip (owner F8 seq 2312).
//
// THE MOVEMENT BASIS comes from ResolveMovementBasisYaw: SmartMobileCamera when the component
// is PRESENT (never keyed on its VALUE — CameraYaw legitimately returns 0 in top-down), else
// the flattened yaw of Camera.main (dungeons have no SmartMobileCamera), else a LOUD
// [Flow:HeroLoco] Fail — a missing basis is never a silent identity.
// =============================================================================

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using DeNelle.Core.Combat; // ActorAnimator (IActorAnimator impl) for guarded Speed/Cast etc. drive

namespace DeNelle.Village
{
    /// <summary>
    /// Walks the hero with WASD / arrows / dpad / left stick. Kinematic
    /// transform translation in the XZ plane; smoothly faces the move
    /// direction. Y position is preserved so the hero stays on the ground.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroLocomotion : MonoBehaviour
    {
        [Tooltip("World units per second at full input deflection.")]
        [SerializeField] private float _moveSpeed = 4.0f;

        [Tooltip("How quickly the hero rotates toward the move direction (rad/sec-ish).")]
        [SerializeField] private float _rotationSpeed = 12f;

        [Tooltip("Acceleration (m/s²) when input is applied. Lower = sluggier start. " +
                 "Owner feedback 2026-05-20: instant max-speed felt rigid; ramp instead.")]
        [SerializeField] private float _accelMetresPerSec2 = 22f;

        [Tooltip("Deceleration (m/s²) when input is released. Higher = sharper stop.")]
        [SerializeField] private float _decelMetresPerSec2 = 28f;

        /// <summary>Current XZ velocity, exposed for the follow camera / animator.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>
        /// True when the player is feeding move input THIS frame (F8 "movement interrupts casting").
        /// Reads the same private <see cref="ReadMoveInput"/> the locomotion consumes, with the WO-423
        /// hasMoveInput deadzone (sqrMagnitude &gt; 0.02) so resting-stick noise never counts. Static so
        /// <see cref="HeroAbilities"/>' cast wind-up can poll it each frame without a component ref.
        /// </summary>
        public static bool WantsToMove => ReadMoveInput().sqrMagnitude > 0.02f;

        /// <summary>
        /// True on ANY drivable move input — the SAME 0.0001 threshold the locomotion
        /// drive uses (:788). For the camera's facing-recenter suspend gate (SME audit
        /// 2026-07-12 #3b): WantsToMove's 0.02 deadzone left a band where the hero MOVES
        /// while the recenter still pivots the camera-relative basis mid-step (a steady
        /// heading curl). Cast-interrupt keeps WantsToMove — noise must not cancel casts.
        /// </summary>
        public static bool HasAnyMoveInput => ReadMoveInput().sqrMagnitude > 0.0001f;

        // WO-423: face-the-target on attack. The player hero previously only faced its
        // MOVE direction (LookRotation on Velocity), so standing still froze facing at the
        // last travel dir — attacks/projectiles fired the wrong way. FaceToward lets the
        // attack/cast code request a brief yaw-slew toward the target; the existing
        // rotation update (the SOLE rotation writer — _agent.updateRotation=false) honors
        // it while there is ~no movement input, and movement input cancels it immediately
        // (we never fight LookRotation(Velocity)). Mirrors the companion slerp pattern
        // (StoryCompanion ~L612) but as a target-yaw + timer the locomotion already owns.
        private bool  _facingActive;        // a FaceToward request is in flight
        private float _faceTargetYaw;       // desired root Y-euler (degrees) to slew toward
        private float _faceHoldRemaining;   // seconds the face-request stays valid
        private const float FaceYawDegPerSec = 540f; // yaw slew rate while facing a target

        // TURN-IN-PLACE feed (owner 2026-07-04, KnightMocap full-turning). When the hero pivots to face a
        // NEW move heading while still ~stationary (start-up / sharp reversal), feed the animator a TurnDir
        // so the studio-mocap turn clip plays instead of an idle foot-slide. COSMETIC only: the existing
        // LookRotation(Velocity) slerp (the sole rotation writer) still owns the actual yaw; this just
        // reports the pivot. Guarded downstream — ActorAnimator.PlayTurn no-ops on controllers without the
        // TurnDir param (every stock hero), so only KnightMocap reacts. Deterministic: pure math on the
        // (camera-relative) input heading vs. current facing; no per-frame allocation, no state machine.
        private const float TurnInPlaceSpeedMax = 2.0f; // only "turning in place" while below the walk band (matches the animator gate)
        private const float TurnMinDeg          = 45f;  // must need to pivot at least this much before a turn clip plays
        private const float TurnAroundDeg       = 135f; // beyond this, use the 180° about-face clip
        // Knight run (owner 2026-07-10, Option 2 "run in the open, calmer in combat"): the OPEN world
        // moves at the full run tier (6 m/s -> the run blend child idle@0/walk@2/run@6), so traversal
        // always reads as a RUN; COMBAT is a calmer, planted pace (the braced CombatLocomotion stance +
        // a lower cap) so fighting feels deliberate. Was combat 6.0 / town 4.4 (walk-in-town). Tunable.
        private const float OverworldRunSpeed = 6.0f;   // no-threat traversal — always a run
        private const float CombatMoveSpeed   = 5.0f;   // in a wave/arena — calmer, planted

        // ── [Flow:HeroTurn] step-in/step-out rotation trace (owner "turn-left-before-walk" RCA
        //    2026-07-10). DEFAULT OFF → zero-cost off (the whole trace block is gated on this AND
        //    FlowTrace.Enabled, so a normal build/play NEVER logs or allocates). Flipped true ONLY
        //    by the headless HERO_TURN_PROBE (AutoPilotDriver.AssertHeroTurnOnMoveStart) for the
        //    ~2s probe window, then flipped back off. A reader can then see, frame by frame, WHICH
        //    rotation branch fired (combat-turn-clip / town-slew / lockface / none), the camera vs
        //    hero yaw, the move heading, the target dYaw, and the EXACT yaw applied this frame vs
        //    _rotationSpeed — so the applied slew can be compared directly against the source math.
        public static bool TurnDebug = false;

        // ── Test-only scripted-move injection seam (headless probe). When active, ReadMoveInput
        //    returns _scriptedMove INSTEAD of reading Keyboard/Gamepad/joystick — so the
        //    HERO_TURN_PROBE can drive a deterministic "press forward" through the SAME
        //    camera-relative move path the player uses, with no keyboard/click dependency
        //    (batchmode -nographics has no input devices). OFF the normal play path: nothing sets
        //    _scriptedMoveActive except SetScriptedMove, and ReadMoveInput only diverts while it is
        //    true. Static because ReadMoveInput is static.
        private static bool    _scriptedMoveActive;
        private static Vector2 _scriptedMove;

        /// <summary>TEST SEAM (headless probe): force ReadMoveInput to return <paramref name="move"/>
        /// (camera-relative input, e.g. (0,1) = press forward) until <see cref="ClearScriptedMove"/>.
        /// Reuses the real ReadMoveInput → camera-basis → Velocity path; never on the normal play path.</summary>
        public static void SetScriptedMove(Vector2 move) { _scriptedMove = move; _scriptedMoveActive = true; }

        /// <summary>TEST SEAM: stop the scripted-move override; input reverts to real devices.</summary>
        public static void ClearScriptedMove() { _scriptedMoveActive = false; _scriptedMove = Vector2.zero; }

        /// <summary>
        /// WO-968 (§12) — is the scripted-move override live RIGHT NOW? Read-only.
        /// <para>This is not a test detail: <see cref="DungeonController"/>'s
        /// <c>EnsureSingleDungeonMover</c> neutralizes this component in a dungeon by calling
        /// <see cref="SetScriptedMove"/>(zero), so this flag IS "the dungeon currently owns the
        /// hero". Until now nothing could observe it, so a capture could not distinguish
        /// "neutralized, DungeonHero is moving" from "NOT neutralized, two movers are fighting" —
        /// the exact ambiguity that cost the 2026-08-10 dungeon session. The
        /// <c>[Flow:HeroOwner]</c> heartbeat below prints it every second.</para>
        /// </summary>
        public static bool ScriptedMoveActive => _scriptedMoveActive;

        // WO-512 slice 3: lock-face / strafe. While a soft lock-on is engaged (driven by
        // HeroTargetIndicator), the hero continuously slews its root yaw toward the LOCKED
        // enemy INSTEAD of the move-direction LookRotation(Velocity) writer — even while
        // moving. The camera-relative MOVE vector is deliberately left untouched, so pressing
        // A/D translates the hero sideways while it keeps facing the orc → strafe falls out
        // for free. When _lockFaceActive is false / the flag is off / the target is null/dead,
        // the EXISTING LookRotation(Velocity) path runs byte-identical (zero regression).
        private bool      _lockFaceActive;  // a lock-face is engaged (HeroTargetIndicator drives it)
        private Transform _lockFaceTarget;  // the locked enemy transform to keep facing

        // WO-1105 R3 (owner 2026-08-16: "when a ranger is targeting a enemy they lock facing the
        // enemy"). The lock-face path above was built for WO-512's soft lock-on and is therefore
        // gated on FeatureFlags.LockOn, which is DEFAULT OFF (nausea risk lives in that flag's
        // CAMERA slices, not here). Archer facing is not part of that experiment: an archer who
        // shoots sideways reads as broken regardless of whether the lock-on camera ships. This
        // flag lets a caller engage the SAME slew without turning the camera feature on. It is a
        // second GATE, never a second facing authority -- ApplyLockFaceYaw stays the one writer.
        private bool      _lockFaceForce;

        /// <summary>
        /// WO-423 — request a brief yaw-slew so the hero turns to face <paramref name="worldPoint"/>
        /// before/while attacking. Only applies while movement input is ~0 (so it never fights
        /// the move-direction LookRotation); a fresh movement input cancels it. Lasts up to
        /// <paramref name="hold"/> seconds — long enough to cover the attack's impact delay.
        /// Null-safe: a worldPoint level with the hero (no horizontal delta) is ignored.
        /// </summary>
        public void FaceToward(Vector3 worldPoint, float hold = 0.35f)
        {
            Vector3 to = worldPoint - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0004f) return;   // target is on top of the hero — nothing to face

            _faceTargetYaw    = Quaternion.LookRotation(to.normalized, Vector3.up).eulerAngles.y;
            _faceHoldRemaining = Mathf.Max(0f, hold);
            _facingActive     = true;
        }

        /// <summary>
        /// WO-512 slice 3 — engage lock-face: while a lock-on holds <paramref name="target"/>, the hero
        /// auto-faces it (yaw slew, reusing <see cref="StepYaw"/>/_rotationSpeed) even while moving, so
        /// A/D strafe around it. Only the FACING is overridden — the camera-relative move vector is
        /// untouched. Null target is treated as a clear. Honored only while <c>FeatureFlags.LockOn</c>;
        /// suspended by the existing InputSuppressed / auto-walk early-returns (dialogue/cutscene win).
        /// </summary>
        /// <param name="force">WO-1105 R3 — honor the slew even with <c>FeatureFlags.LockOn</c> OFF.
        /// Used by the ranged-class auto-facing drive (an archer must face what she is shooting
        /// whether or not the lock-on CAMERA experiment is enabled). Default false keeps every
        /// pre-existing caller byte-identical.</param>
        public void SetLockFace(Transform target, bool force = false)
        {
            if (target == null) { ClearLockFace(); return; }
            bool changed = !_lockFaceActive || !ReferenceEquals(_lockFaceTarget, target) || _lockFaceForce != force;
            _lockFaceTarget = target;
            _lockFaceActive = true;
            _lockFaceForce  = force;
            // Only trace on a CHANGE: the ranged drive re-asserts this every LateUpdate, and an
            // unconditional Step here would be a per-frame firehose that evicts the boot window
            // out of the logcat ring (memory: logcat-ring-buffer-destroys-evidence).
            if (changed)
                DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena",
                    "LOCKON face-lock ON target='" + target.gameObject.name.Replace("(Clone)", "").Trim()
                    + "' force=" + force + ".");
        }

        /// <summary>
        /// WO-512 slice 3 — clear lock-face: the hero returns to the normal LookRotation(Velocity)
        /// facing writer (byte-identical to today). Called on lock release / target death.
        /// </summary>
        public void ClearLockFace()
        {
            if (!_lockFaceActive && _lockFaceTarget == null) return;
            string was = _lockFaceTarget != null
                ? _lockFaceTarget.gameObject.name.Replace("(Clone)", "").Trim() : "none";
            _lockFaceActive = false;
            _lockFaceTarget = null;
            _lockFaceForce  = false;
            DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena",
                "LOCKON face-lock OFF (was '" + was + "') -> free facing.");
        }

        // WO-512 slice 3: slew the root yaw toward the locked enemy (XZ direction only), reusing
        // StepYaw for the shortest-arc step at _rotationSpeed-equivalent feel. Caller already
        // verified _lockFaceTarget != null. If the target is on top of the hero (no horizontal
        // delta) we leave facing as-is (nothing meaningful to face). Yaw only — Y/pitch untouched,
        // matching the move-direction LookRotation writer it replaces.
        private void ApplyLockFaceYaw()
        {
            Vector3 to = _lockFaceTarget.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0004f) return;   // enemy on top of the hero — nothing to face

            float curYaw    = transform.eulerAngles.y;
            float targetYaw  = Quaternion.LookRotation(to.normalized, Vector3.up).eulerAngles.y;
            // _rotationSpeed is a Slerp factor (~rad/sec-ish); scale to a per-frame degree cap so
            // the slew feel matches the normal Slerp path without re-introducing a second tunable.
            float maxDelta  = _rotationSpeed * 60f * Time.deltaTime;
            float nextYaw   = StepYaw(curYaw, targetYaw, maxDelta);
            transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
        }

        /// <summary>
        /// WO-423 — pure, edit-mode-testable shortest-arc yaw slew: step <paramref name="currentYaw"/>
        /// toward <paramref name="targetYaw"/> by at most <paramref name="maxDelta"/> degrees,
        /// wrapping across the ±180° seam so it always turns the short way. Degrees in/out.
        /// </summary>
        public static float StepYaw(float currentYaw, float targetYaw, float maxDelta)
        {
            float delta = Mathf.DeltaAngle(currentYaw, targetYaw); // shortest signed arc, [-180,180]
            if (Mathf.Abs(delta) <= maxDelta) return targetYaw;
            return currentYaw + Mathf.Sign(delta) * maxDelta;
        }

        /// <summary>
        /// TURN-IN-PLACE / directional-turn feed (owner 2026-07-04, KnightMocap full-turning). Compares
        /// the (camera-relative) input heading to the hero's CURRENT facing and, while ~stationary
        /// (Velocity below <see cref="TurnInPlaceSpeedMax"/>) with a large enough pivot, drives
        /// <see cref="ActorAnimator.PlayTurn"/> with the matching <see cref="TurnDirection"/> (±90° /
        /// ±180°). Otherwise clears it to None. Purely a signal — the LookRotation(Velocity) slerp still
        /// performs the actual rotation; the animator just shows the pivot instead of a foot-slide.
        /// Guarded downstream (PlayTurn no-ops when the controller lacks TurnDir), so stock heroes and any
        /// non-mocap controller are unaffected. Pure arithmetic — no allocation, no persistent state.
        /// </summary>
        private void DriveTurnSignal(Vector3 move, bool hasMoveInput)
        {
            TurnDirection turn = TurnDirection.None;
            if (hasMoveInput && Velocity.magnitude < TurnInPlaceSpeedMax)
            {
                Vector3 dir = move; dir.y = 0f;
                if (dir.sqrMagnitude > 0.0004f)
                {
                    float desiredYaw = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles.y;
                    float delta = Mathf.DeltaAngle(transform.eulerAngles.y, desiredYaw); // signed shortest arc
                    float mag   = Mathf.Abs(delta);
                    if (mag >= TurnMinDeg)
                    {
                        bool around = mag >= TurnAroundDeg;
                        turn = delta < 0f
                            ? (around ? TurnDirection.LeftAround  : TurnDirection.Left)
                            : (around ? TurnDirection.RightAround : TurnDirection.Right);
                    }
                }
            }
            _actor?.PlayTurn(turn);
        }

        // WO-377: global player-input suppression. Set true while a Yarn dialogue is on
        // screen so the player can't move / attack / cast / build during story beats —
        // a click on the dialogue box used to fall through to the world and fire the
        // hero's primary attack, breaking the sequence. HeroLocomotion is the canonical
        // hero-input component, so it OWNS this gate: it subscribes to the Yarn
        // DialogueRunner's start/complete events (see HookDialogueGate) and raises/clears
        // the flag. The other per-frame input readers (HeroAbilityInput,
        // PlayerAttackController, BuildModeController) consult it via this single static
        // so the whole player-input surface is suppressed from ONE place. Static (not
        // instance) so those readers don't need a hero reference; defaults false so
        // outside dialogue everything behaves exactly as before.
        public static bool InputSuppressed { get; private set; }

        // WO-557 (Yarn removed): we subscribe to OUR dialogue stack's engine-wide
        // Started/Ended signals (DeNelle.Core.Dialogue.DialogueService) to suppress player
        // input while a conversation is on screen. A static event is always available — no
        // runner to find, no retry coroutine. Guard so we subscribe exactly once per hero.
        private bool _dialogueHooked;

        // WO-383: teleport-aware warp. SceneTransitionTrigger / seam handoffs set the
        // hero far outside the off-mesh ±50 clamp and onto a separately-baked NavMesh
        // (OuterWorld). A raw transform.position = fights the agent + clamp every frame
        // (the "camera + direction break at the gate" bug). WarpTo disables the agent,
        // moves it, re-warps it onto the destination mesh, and raises OnTeleported so the
        // follow camera can snap instead of smooth-chasing the jump. _isTeleporting tells
        // the off-mesh clamp below to leave the warp frame alone.
        //
        // ⛔ _isTeleporting IS A *SPAN* LATCH AND IT ONLY WORKS WHERE A SPAN EXISTS.
        // BeginSeamCross raises it and the seam-cross DONE branch (~:1825) lowers it FRAMES
        // later, so for the slide it genuinely covers the crossing. WarpTo is the opposite
        // shape: it raises and lowers this flag inside ONE SYNCHRONOUS CALL, so by the time
        // Update next runs the latch is already down and the clamp below sees an ordinary
        // off-mesh hero. That is WO-1094 defect 2 — the guard protected ZERO frames, and a
        // deliberate warp to (-400,17,0) was reclaimed to (-50,0) on the very next tick
        // (F8/WO-1091 trace). The fix is a FRAME STAMP, not a second bool: WarpTo records the
        // last frame the guard must still hold, and TeleportGuardHeld reads it. That spans
        // exactly the frames the warp needs — the agent disable -> warp -> re-enable frame
        // AND the next Update, which is the first tick on which the re-enabled agent's
        // isOnNavMesh can be trusted.
        private bool _isTeleporting;

        /// <summary>Last frame index on which a WarpTo's teleport guard still holds.
        /// <see cref="int.MinValue"/> = never warped, so the guard is down.</summary>
        private int _teleportGuardUntilFrame = int.MinValue;

        /// <summary>
        /// WO-1094 — the last frame a warp landed on <paramref name="warpFrame"/> must keep
        /// protecting. One frame past the warp: the warp frame itself is covered because
        /// <see cref="TeleportGuardHeld"/> is inclusive, and the frame after it is the first
        /// Update that can observe the re-enabled agent. Pure + public so the regression can
        /// assert the span without a PlayMode session.
        /// </summary>
        public static int TeleportGuardEndFrame(int warpFrame) => warpFrame + 1;

        /// <summary>
        /// WO-1094 — true while a deliberate teleport still owns the transform, so the
        /// playable-bounds clamp must not touch it. Either a live SPAN (the seam slide) or a
        /// frame stamp that has not expired yet. Pure + public for the regression.
        /// </summary>
        public static bool TeleportGuardHeld(bool spanActive, int currentFrame, int guardUntilFrame)
            => spanActive || currentFrame <= guardUntilFrame;

        // ── Playable-bounds clamp OWNERSHIP (dungeon walk-fail P0, owner 2026-08-05) ─────────
        // The off-mesh ±50 clamp in Update (~:1100) was written for ONE situation: a
        // castle/overworld hero who has walked off the baked mesh, in a scene whose playable
        // region really IS ±50 around the origin. It was gated ONLY on "off the navmesh" —
        // which is also the NORMAL, CORRECT state in two places where this component is not
        // the mover, so it fired there by construction:
        //   • DUNGEONS — unbaked scenes where DungeonHero's CharacterController is the sole
        //     mover. DungeonController.EnsureSingleDungeonMover (:800-833) deliberately
        //     DISABLES this hero's NavMeshAgent to make that so. The clamp therefore ran every
        //     frame of every dungeon and wrote transform.position straight onto a LIVE
        //     CharacterController, racing DungeonHero.Update under default execution order —
        //     a CC re-asserts/depenetrates on a raw position write. That is the owner's
        //     "won the fight and then could not move at all".
        //   • THE STAGED ARENA — BattleArena stages the fight at (5000,0,5000)
        //     (BattleArena.cs:81) with the agent off-mesh. Clamped to the then-±50 that is EXACTLY
        //     (50.00, 0.00, 50.00) — the unattributed ~7km teleport seen in the capture, which
        //     had no WarpTo line because the clamp, not a warp, wrote it.
        // Note the x/z clamp was NOT gated on GroundSnapEnabled (only the Y-snap sub-blocks
        // are), so the dungeon's existing GroundSnapEnabled=false neutralisation never
        // suppressed it. Both cases are answered by asking WHO OWNS THE TRANSFORM — a
        // capability check, not a scene-name check, so it generalises to any future mover.

        // ~0.5mm². Below this an assignment is a no-op that only pokes the physics bodies.
        private const float PositionWriteEpsilonSqr = 2.5e-7f;

        // Cached CharacterController probe. Re-probed while null so it self-heals in BOTH
        // directions: a CC that arrives when this hero is injected into a dungeon gets picked
        // up, and a CC destroyed on dungeon teardown falls back to a fresh probe (a destroyed
        // Unity object compares == null). Same self-heal idiom as ResolveAnimator.
        private CharacterController _ccProbe;

        // Throttle for the clamp-relocation warning. FlowTrace.Throttle logs at Info level
        // (Sink.Info, FlowTrace.cs:173); a relocation this large must read as a WARNING, so we
        // gate FlowTrace.Warn on our own timer rather than downgrade it.
        private float _nextClampWarnAt;

        // ── WO-1094: WHERE THE CLAMP BOUND COMES FROM ────────────────────────────────────────
        // It used to be a castle-era `const float` half-extent of 50 — a literal left standing in
        // a MERGED 1000x1000 world (terrain seated at (-500,-4,-500), size 1000x42). Any
        // legitimate off-mesh position past ±50 anywhere in Main_Castle_Overworld was silently
        // reclaimed to the castle box; the WO-1091 capture caught it moving the hero from
        // (-400,0) to (-50,0) on one tick. A second literal — even a bigger one, even a tunable
        // one — would be the SAME defect with a later expiry date, which is the
        // RepoProps.MaxStructureLevel lesson (CLAUDE.md §8): one authority, read from it.
        //
        // THE AUTHORITY IS THE WORLD ITSELF: DeNelle.Core.World.BiomeRoads.TryMeasureWorldBounds
        // measures the live extent off the active Terrain(s) and REFUSES rather than typing a
        // fallback. Wherever it answers, its answer wins and nothing else is consulted.
        //
        // ⛔ AN UNMEASURABLE SCENE DOES NOT LOSE ITS BOUND (lead ruling 2026-09-09).
        // The first draft of this fix inherited BiomeRoads' refusal literally and SKIPPED the
        // clamp when the extent could not be measured. That was wrong, and the reason is counted
        // rather than argued: THREE SHIPPED SCENES CARRY ZERO Terrain components —
        // Village2 (0), RaidBase_IronBastion (0) and the legacy MainCastle_Hall (0), counted in
        // the scene files 2026-09-09. In those, "no measurement" would have meant NO BOUND AT
        // ALL, and this component's off-mesh path is `transform.position += step` — so the hero
        // drifts unbounded, inside the raid loop that is the north star. Deleting a bound is not
        // a safer failure than a stale one; it is the same class of failure with no ceiling.
        //
        // So the KEY_FACTS invariant applies here exactly as it does on the tunables rail:
        // NO MEASUREMENT => TODAY'S BEHAVIOUR, EXACTLY. Today's behaviour was a ±50 box, and 50
        // is now a ROW (RemoteTunables hero.playableFallbackHalf, default 50) rather than a
        // constant — so an empty table reproduces the shipped clamp byte for byte in every
        // unmeasured scene, and moving it in a raid base costs a database write, not a rebuild.
        // The relocation trace names WHICH bound applied, so a capture never has to infer it.
        //
        // Resolved LAZILY and only from inside the clamp branch, so a dungeon (foreign CC mover)
        // or a staged arena frame never pays the Terrain scan at all, and cached per scene so a
        // per-frame FindObjectsByType is impossible.
        //
        // ⛔ KEYED ON THE **ACTIVE SCENE**, NOT gameObject.scene — AND THAT IS NOT A STYLE CHOICE.
        // The town hero TRAVELS: SceneRouter.cs:666-676 marks the hero root DontDestroyOnLoad before
        // a Single load and HeroControlEnsurer.TryRecoverCarriedHero re-homes it with
        // MoveGameObjectToScene (HeroControlEnsurer.cs:244) — a call that is Guard-wrapped precisely
        // because it can FAIL (:247 warns). So gameObject.scene is the DDOL scene for part of every
        // crossing, and permanently if the re-home fails: a handle that never changes again. Keying
        // on it would let the extent measured in one world be enforced in the NEXT one — the same
        // "bound from a world that no longer exists" defect this ticket exists to kill, wearing a
        // new number. The ACTIVE scene is always the world the hero is standing in.
        //
        // ⛔ PROBED EXACTLY ONCE PER ACTIVE SCENE — NO TIMED RE-PROBE, DELIBERATELY.
        // BiomeRoads.TryMeasureWorldBounds emits FlowTrace.Fail on a miss, and Fail routes to
        // Sink.Error (FlowTrace.cs:169-172), which is what break-log.jsonl records and what wakes the
        // §14 F8 daemon. A retry timer in a terrain-less scene would therefore fire a live ERROR at
        // the owner on a loop, with a message written for the biome-drop caller ("no biome drop can
        // be derived"), forever. Instrumentation that manufactures false captures is worse than none.
        // The retry would also be speculative: these scenes carry their Terrain in the scene file
        // itself (Main_Castle_Overworld.unity has one), so there is no proven stream-in case to catch.
        private int _boundsSceneHandle = int.MinValue;
        private bool _boundsProbed;
        private Bounds _boundsBox;
        private string _boundsSource = "unresolved";

        /// <summary>
        /// WO-1094 — the pure clamp. Clamps XZ into <paramref name="playable"/>'s real min/max
        /// (NOT a symmetric half-extent: the authority hands back a Bounds, and assuming it is
        /// centred on the origin would re-introduce a typed assumption by the back door).
        /// Public + static so the regression can prove both halves — "a legitimate far position
        /// survives" and "a genuinely out-of-world one is still recovered" — with no scene.
        /// </summary>
        public static Vector3 ClampToPlayableBounds(Vector3 p, Bounds playable)
        {
            p.x = Mathf.Clamp(p.x, playable.min.x, playable.max.x);
            p.z = Mathf.Clamp(p.z, playable.min.z, playable.max.z);
            return p;
        }

        /// <summary>
        /// WO-1094 — the FALLBACK bound for a scene whose extent cannot be measured, built from
        /// the rail value. A symmetric half-extent about the origin, because that is the shape
        /// the shipped ±50 had and inventing an asymmetric fallback would be picking a world
        /// nobody measured. CLAMPED 1..100000: a rail value of 0 would pin every unmeasured
        /// scene's hero to the origin, which is a worse outcome than any bound. Pure + public so
        /// the regression can prove "an unmeasurable scene still clamps, and at today's number".
        /// </summary>
        public static Bounds FallbackPlayableBounds(int halfFromRail)
        {
            float half = Mathf.Clamp(halfFromRail, 1, 100000);
            return new Bounds(Vector3.zero, new Vector3(half * 2f, half * 2f, half * 2f));
        }

        /// <summary>
        /// Resolve the playable bounds for the hero's ACTIVE scene. ALWAYS answers: the measured
        /// world extent where a Terrain exists, else the rail fallback (today's shipped ±50 by
        /// default). <paramref name="source"/> names which, verbatim, for the trace.
        /// </summary>
        private Bounds ResolvePlayableBounds(out string source)
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.handle != _boundsSceneHandle)
            {
                // New world: drop everything the previous one taught us, and probe again.
                _boundsSceneHandle = active.handle;
                _boundsProbed = false;
                _boundsSource = "unresolved";
            }

            if (!_boundsProbed)
            {
                _boundsProbed = true;
                if (DeNelle.Core.World.BiomeRoads.TryMeasureWorldBounds(out Bounds measured))
                {
                    _boundsBox = measured;
                    _boundsSource = "measured-terrain (BiomeRoads.TryMeasureWorldBounds)";
                }
                else
                {
                    // NO MEASUREMENT => TODAY'S BEHAVIOUR, EXACTLY. The rail default is 50, the
                    // bound this build shipped, so an empty tunables table reproduces the old
                    // clamp here rather than removing it.
                    int rail = DeNelle.Core.Ops.RemoteTunables.Int(
                        DeNelle.Core.Ops.RemoteTunables.KeyHeroPlayableFallbackHalf);
                    _boundsBox = FallbackPlayableBounds(rail);
                    _boundsSource = $"fallback-tunable ({DeNelle.Core.Ops.RemoteTunables.KeyHeroPlayableFallbackHalf}={rail})";
                }

                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroLoco",
                    $"playable bounds RESOLVED for active scene '{active.name}': " +
                    $"x[{_boundsBox.min.x:F1}..{_boundsBox.max.x:F1}] z[{_boundsBox.min.z:F1}..{_boundsBox.max.z:F1}] " +
                    $"source={_boundsSource} — the scene is ALWAYS bounded (WO-1094): measured where a " +
                    "Terrain exists, else the rail's default, which is exactly the bound this build shipped.");
            }

            source = _boundsSource;
            return _boundsBox;
        }

        /// <summary>
        /// True when a mover OTHER than this component owns the transform this frame — i.e. a
        /// live, ENABLED CharacterController sits on the same rig. This is the exact inverse of
        /// the guard DungeonHero.Update already keeps (DungeonHero.cs:209-219 — it skips its
        /// whole movement step while its CC is disabled, because "the arena owns the hero"), so
        /// the two components become mutually exclusive: exactly ONE of them writes this
        /// transform on any given frame. Deliberately a capability check rather than a
        /// DungeonHero type check — DeNelle.Dungeons references DeNelle.Village one-way, so
        /// this assembly cannot see DungeonHero at all.
        /// </summary>
        private bool ForeignMoverOwnsTransform()
        {
            if (_ccProbe == null) _ccProbe = GetComponent<CharacterController>();
            return _ccProbe != null && _ccProbe.enabled;
        }

        // ── ONE OWNER OF THE HERO TRANSFORM — the decision seam (WO-968 S1 / WO-1016) ─────────
        // These three statics are PURE so the rule itself can be regression-tested with no scene,
        // no play session and no Unity object (see DungeonMoverOwnershipRegression). The live code
        // below CALLS them — the test therefore covers the shipped decision, not a copy of it.
        //
        // THE RULE, stated once: exactly ONE component may integrate this transform on a frame, and
        // whichever one does is the one the animator is fed from. Ownership is decided by CAPABILITY
        // (a live CharacterController on the same rig), never by scene name and never by a static
        // side-channel — the side-channel (DungeonController's SetScriptedMove(zero) stomp) is what
        // silently lapsed in the owner's 2026-08-10 capture: [Flow:HeroLoco] vel=0.00 while the root
        // moved on some frames, and [Flow:HeroDrift] vel=(0,5) with live input on others, in the SAME
        // session, with nothing in the log naming which was live.

        /// <summary>
        /// May THIS component write the hero transform this frame? False whenever a foreign mover
        /// (a live CharacterController — the dungeon Keeper's <c>DungeonHero</c>) owns it. Exact
        /// inverse of DungeonHero.Update's own "my CC is disabled, the arena owns the hero" guard,
        /// so the two are mutually exclusive by construction.
        /// </summary>
        public static bool SelfMayWriteTransform(bool foreignOwnsTransform) => !foreignOwnsTransform;

        /// <summary>
        /// The value the animator's Speed is fed. WO-968 F1: when a foreign mover owns the
        /// transform, publish the MEASURED root speed (delta position / dt) — what actually
        /// happened in the world — instead of this component's own <see cref="Velocity"/>, which is
        /// dead by design while it is not the mover. Mover-agnostic: no scene check, so the same
        /// code is correct in town, overworld, raid and dungeon.
        /// </summary>
        public static float ResolveAnimatorFeed(bool crossingSeam, float seamSpeed,
                                                bool foreignOwnsTransform, float selfSpeed,
                                                float measuredRootSpeed)
        {
            if (crossingSeam) return seamSpeed;
            return foreignOwnsTransform ? measuredRootSpeed : selfSpeed;
        }

        /// <summary>
        /// <para>WO-1298 — the animator feed for the ONE frame-shape the function above cannot see:
        /// <see cref="InputSuppressed"/> is raised (a dialogue / tutorial beat owns the player) and
        /// the root moves ANYWAY, driven by something that is neither this component nor a
        /// CharacterController.</para>
        /// <para>⚠ THE OLD CODE DID NOT MERELY FAIL TO UPDATE THE ANIMATOR — IT ACTIVELY WROTE A
        /// DEAD ZERO into Speed every frame of the suppression and returned. That is the owner's
        /// F8 seq 4362 capture verbatim: <c>velSelf=0.00 velRoot=14.49 animFeed=velSelf
        /// animSpeed=0.00 inputSuppressed=True autoWalk=False ownerCC=none</c> — the hero gliding
        /// west out of the castle gate in an idle pose. Suppressing INPUT is not the same claim as
        /// "the hero is stationary", and the branch conflated the two.</para>
        /// <para>Pure so the rule is regression-testable with no scene and no play session
        /// (DungeonMoverOwnershipRegression Case 2). Below the stall threshold it publishes a hard
        /// 0 — a suppressed, genuinely stationary hero must settle to idle exactly as before, which
        /// is the whole point of the WO-377 hold. Clamped to the run tier so a large single-frame
        /// displacement cannot drive the blend tree past its authored top child.</para>
        /// </summary>
        public static float ResolveSuppressedAnimatorFeed(float measuredRootSpeed, float runSpeedCap)
        {
            if (measuredRootSpeed <= AnimStallRootSpeed) return 0f;
            return Mathf.Min(measuredRootSpeed, runSpeedCap);
        }

        /// <summary>
        /// The named defect of WO-1016, as a predicate: the root travelled but the animator is
        /// holding ~idle (the hero slides through the dungeon playing a single idle clip). NaN
        /// animSpeed = "no Speed parameter on this controller", which is a different fault and is
        /// deliberately NOT a stall.
        /// </summary>
        public static bool IsAnimationStalled(float rootSpeed, float animSpeed)
        {
            return rootSpeed > AnimStallRootSpeed
                   && !float.IsNaN(animSpeed)
                   && animSpeed < AnimStallAnimSpeed;
        }

        /// <summary>Root speed (m/s) above which a walk cycle MUST be playing.</summary>
        public const float AnimStallRootSpeed = 0.5f;
        /// <summary>Animator Speed below which the rig reads as standing still.</summary>
        public const float AnimStallAnimSpeed = 0.1f;

        // ── MOVEMENT BASIS — where "forward" comes from (WO-968 S3) ──────────────────────────
        /// <summary>Which source supplies the camera-relative movement basis this frame.</summary>
        public enum MovementBasis
        {
            /// <summary>Town / overworld: SmartMobileCamera.CameraYaw (the player-pan yaw).</summary>
            SmartMobileCamera,
            /// <summary>Dungeon: the flattened yaw of the camera that ACTUALLY exists in the scene.</summary>
            MainCamera,
            /// <summary>No basis at all — the stick is world-absolute. Always reported as a failure.</summary>
            None
        }

        /// <summary>
        /// Pure basis selection. NOTE THE TRAP this encodes: the fallback keys on the
        /// SmartMobileCamera COMPONENT BEING ABSENT, never on its VALUE being zero — in town
        /// top-down framing <c>CameraYaw</c> legitimately returns 0, and treating that as "no basis"
        /// would silently re-base the town stick.
        /// </summary>
        public static MovementBasis ResolveBasisKind(bool hasSmartCamera, bool hasUsableMainCamera)
        {
            if (hasSmartCamera) return MovementBasis.SmartMobileCamera;
            return hasUsableMainCamera ? MovementBasis.MainCamera : MovementBasis.None;
        }

        /// <summary>
        /// Assigns <paramref name="p"/> only when it actually differs from the current position.
        /// An UNCONDITIONAL per-frame write to transform.position is what makes a live
        /// CharacterController re-assert/depenetrate in the first place — a no-op write must
        /// cost nothing and touch nothing.
        /// </summary>
        private void WritePositionIfChanged(Vector3 p)
        {
            if ((transform.position - p).sqrMagnitude > PositionWriteEpsilonSqr)
                transform.position = p;
        }

        /// <summary>
        /// WO-383 — raised after the hero is warped to a new world position (e.g. a scene
        /// seam). SmartMobileCamera subscribes to snap its seat so it never smooth-chases
        /// the teleport through intermediate positions.
        /// </summary>
        public event System.Action OnTeleported;

        /// <summary>
        /// WO-383 — teleport-aware reposition. Samples the nearest NavMesh point near
        /// <paramref name="worldPos"/> (so the hero lands on valid mesh even if the caller's
        /// target is slightly off), then disables / moves / re-warps the agent so it
        /// re-acquires the (possibly additively-loaded) destination NavMesh. Raises
        /// <see cref="OnTeleported"/> for the follow camera. Null-safe: with no agent it
        /// falls back to a plain transform move.
        /// </summary>
        public void WarpTo(Vector3 worldPos, Quaternion? rot = null)
        {
            // F8-15 death forensic window: EVERY explicit hero warp names its caller (stack-derived —
            // this signature must stay exactly (Vector3, Quaternion?) because BattleArena.WarpHero
            // resolves it by exact-signature reflection GetMethod). Always logs (throttled outside
            // the window); FlowTrace-gated so the StackTrace cost never runs with tracing off.
            if (DeNelle.Core.Diagnostics.FlowTrace.Enabled)
                DeNelle.Core.Diagnostics.DeathTrace.HeroMoved(transform.position, worldPos,
                    DeNelle.Core.Diagnostics.DeathTrace.Caller(2),   // 2: skip Caller() + this WarpTo frame
                    "HeroLocomotion.WarpTo explicit warp", always: true);

            _isTeleporting = true;   // clamp/movement skips this frame

            // WO-1298: a TELEPORT IS NOT TRAVEL. The root-speed measurement is a raw
            // delta-position/dt sample, so a warp across a seam publishes a single enormous
            // velRoot — which both trips the ANIMATION-VELOCITY STALL trace with a false positive
            // and (now that the suppression branch feeds the measured value) would flash one frame
            // of run clip on arrival. Rebase the sample on the landed pose instead of measuring
            // across the jump. One frame, one bool; the honest reading is "0 m/s this frame".
            _rootMeasureRebase = true;

            // §12 ticket #2: prove ground-side vs hero-side at the garrison warp. A MISS (or a large
            // sample distance) means the destination navmesh isn't online yet / isn't there — the agent
            // lands off-mesh and (no physics collider) falls through. Captured to break-log.
            bool sampled = NavMesh.SamplePosition(worldPos, out NavMeshHit hit, 5f, NavMesh.AllAreas);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Seam",
                sampled
                    ? $"WarpTo sample HIT @ {hit.position} dist={Vector3.Distance(worldPos, hit.position):F2} (req {worldPos}) scene='{gameObject.scene.name}'"
                    : $"WarpTo sample MISS for {worldPos} (no navmesh within 5m) scene='{gameObject.scene.name}' — hero will land OFF-MESH.");
            if (sampled)
                worldPos = hit.position;   // land on valid mesh

            if (_agent != null)
            {
                _agent.enabled = false;            // critical before moving the transform
                transform.position = worldPos;
                _agent.Warp(worldPos);             // re-acquires the destination NavMesh
                _agent.enabled = true;
                // F8 2026-07-30 (captured error x2): ResetPath throws when the re-enabled
                // agent did not land ON a navmesh (a warp to unbaked ground — e.g. the
                // orphaned-arena return warp). Guard it; the off-mesh self-heal in Update
                // re-seats the agent on the next tick.
                if (_agent.isOnNavMesh) _agent.ResetPath();
                DeNelle.Core.Diagnostics.FlowTrace.Step("Seam",
                    $"WarpTo post-warp: agent.isOnNavMesh={_agent.isOnNavMesh} @ {transform.position}");
            }
            else
            {
                transform.position = worldPos;
            }

            if (rot.HasValue) transform.rotation = rot.Value;

            // F8 2026-08-10 (death shake at the arena return, seq 2253/2255): WarpTo is the ONE
            // sanctioned teleport authority, so if the hero is mid death-freeze (died in the
            // arena; the return warp fires while the pin holds the corpse) REBASE the pin to the
            // landed pose here instead of letting HeroHealth.LateUpdate fight this warp back to
            // the stale death spot. Same-GameObject, same-assembly lookup; no-op on a living hero.
            var health = GetComponent<HeroHealth>();
            if (health != null)
                health.RebaseDeathPin(transform.position, transform.rotation, "HeroLocomotion.WarpTo");

            Velocity = Vector3.zero;   // don't carry pre-warp momentum across the seam
            OnTeleported?.Invoke();

            // WO-1094 defect 2. Lowering the span latch here is correct — the synchronous warp
            // IS over — but on its own it left the clamp free to reclaim the landing on the very
            // next Update. Stamp the guard forward instead, so the frames the warp actually
            // needs (this one, plus the first Update that can read the re-enabled agent) are
            // covered. Traced: a guard that silently protects nothing is what this ticket was.
            _isTeleporting = false;
            _teleportGuardUntilFrame = TeleportGuardEndFrame(Time.frameCount);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Seam",
                $"WarpTo teleport guard armed through frame {_teleportGuardUntilFrame} " +
                $"(landed on frame {Time.frameCount}) — the playable-bounds clamp is held off " +
                $"until the re-enabled agent has had one Update to re-acquire the mesh.");
        }

        private ActorAnimator _actor; // lazy, for driving Speed into the (Humanoid) controller blendtree

        // WO-277 (tutorial auto-walk): while _autoWalkTarget is set, the tutorial
        // OWNS the hero — player input is ignored and the hero is driven toward the
        // target along the shared NavMesh (same agent/Move path as manual walking),
        // so the village tour "companion leads, hero follows" plays without input.
        // ClearAutoWalk restores normal control. Null-safe and self-contained: off
        // the tutorial these stay null and movement behaves exactly as before.
        private Transform _autoWalkTarget;
        // How close (XZ) the hero must get to the target before auto-walk reports
        // arrival (TutorialAutoWalk polls AutoWalkArrived to advance the waypoint).
        private const float AutoWalkArriveRadius = 1.6f;

        /// <summary>
        /// WO-277 — true while the tutorial is driving the hero (player input
        /// suppressed). TutorialAutoWalk / the director read this to know the hero
        /// is under scripted control.
        /// </summary>
        public bool IsAutoWalking => _autoWalkTarget != null;

        /// <summary>
        /// WO-277 — true once the hero is within <see cref="AutoWalkArriveRadius"/>
        /// (XZ) of the current auto-walk target. False when not auto-walking.
        /// </summary>
        public bool AutoWalkArrived
        {
            get
            {
                if (_autoWalkTarget == null) return false;
                Vector3 d = _autoWalkTarget.position - transform.position;
                d.y = 0f;
                return d.sqrMagnitude <= AutoWalkArriveRadius * AutoWalkArriveRadius;
            }
        }

        /// <summary>
        /// WO-277 — hand the hero to the tutorial: ignore player input and drive the
        /// hero toward <paramref name="target"/> via the existing NavMeshAgent. Call
        /// <see cref="ClearAutoWalk"/> to return control to the player. A null target
        /// is treated as a clear.
        /// </summary>
        public void SetAutoWalk(Transform target)
        {
            _autoWalkTarget = target;
            if (target == null) Velocity = Vector3.zero;
        }

        /// <summary>WO-277 — return movement control to the player (ends auto-walk).</summary>
        public void ClearAutoWalk()
        {
            _autoWalkTarget = null;
            Velocity = Vector3.zero;
        }

        // DEF-147 (Part B): off-mesh ground-snap / re-bind. When the hero leaves the
        // baked NavMesh (walks off a ledge / rampart edge), the agent's height-clamp
        // stops applying and the raw transform-fallback has NO downward force — so the
        // hero keeps its last Y forever ("hover exploit"). This re-binds the hero to
        // the navmesh (NavMesh.SamplePosition → pull Y down, re-warp the agent on)
        // instead of letting it float. Flag-guarded so it's instantly flippable to
        // false in playtest if it ever misbehaves; default true (it's a correctness fix).
        public static bool GroundSnapEnabled = true;

        // Accumulated downward fall velocity (m/s, magnitude) for a gravity-like snap
        // rather than a hard teleport. Reset to 0 whenever the hero is grounded/on-mesh.
        private float _fallSpeed;
        private const float FallAccel    = 30f;   // m/s² downward accel while floating
        private const float FallSpeedMax = 20f;   // m/s terminal fall speed (cap)
        // How far down/around to look for the nearest navmesh point to re-bind onto.
        private const float SnapSampleRadius = 6f;
        // Vertical band within which we re-warp the AGENT back onto the mesh (rather
        // than only nudging the transform) — i.e. the hero is essentially at ground.
        private const float ReBindYBand = 0.35f;

        private bool _loggedFirstInput;
        private Animator _animator;
        // WO-174 param-guard: cache whether the live controller actually declares the
        // float/trigger params we drive. SetFloat/SetTrigger on a controller WITHOUT
        // the param logs an error EVERY frame (the project-wide param-spam pitfall).
        // Recomputed whenever _animator is (re)resolved — HeroBodySwapper swaps the
        // controller at runtime, so we can't trust a one-shot Awake check.
        private Animator _paramCheckedAnimator;
        private bool _hasSpeedParam;
        private bool _hasVictoryParam;
        private NavMeshAgent _agent;   // unified navigation: hero shares the enemies' NavMesh

        // WO-387: follow camera for camera-relative movement. CameraYaw returns 0 in
        // top-down, so movement auto-degrades to world-absolute there. Cached in Start; lazy-refreshed.
        private SmartMobileCamera _smartCamera;
#if UNITY_EDITOR
        // Collision diagnostic: log each unique blocking collider once.
        private readonly HashSet<Collider> _loggedColliders = new HashSet<Collider>();
#endif
        private static readonly int AnimSpeed   = Animator.StringToHash("Speed");

        // DEF-70: victory pose — triggered when WaveManager fires OnWaveCleared.
        // Suppresses movement so the hero holds the pose until the next wave begins.
        private static readonly int AnimVictory = Animator.StringToHash("Victory");
        private bool _victoryPose;
        private float _victoryPoseTimer;
        // DEF-70 fix: the victory pose is a brief celebration, NEVER a lock. It
        // ends after this many seconds OR the instant the player gives input.
        private const float VictoryPoseSeconds = 2.5f;
        private WaveManager _waveManager;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            // Owner 2026-05-25 "movement feels laggy": the baked serialized values
            // (speed 4 / accel 22) spool up slowly. Force snappier response here —
            // the scene's stale values can't be changed without a risky re-bake.
            _moveSpeed = 6f;
            _accelMetresPerSec2 = 55f;
            _decelMetresPerSec2 = 45f;

            // Owner 2026-05-30: unify hero navigation onto the SAME NavMesh the enemies use,
            // so hero + enemies share one definition of "walkable" and traverse the world
            // (ground, stairs, ramparts, hills, caves) identically — in every scene with a
            // baked NavMesh, and so enemies can climb to attack a hero defending up top.
            // Input still drives movement directly via NavMeshAgent.Move below; the agent
            // just constrains the hero to the walkable surface + follows its height.
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.radius = 0.4f;
            _agent.height = 1.8f;
            _agent.baseOffset = 0f;
            _agent.speed = 30f;              // we drive via Move(); keep high so it never caps us
            _agent.acceleration = 200f;
            _agent.angularSpeed = 0f;
            _agent.updateRotation = false;   // facing handled manually by root LookRotation; HeroBodySwapper's -90f body yaw (WO-326) aligns the visual forward to root +Z
            _agent.updateUpAxis = false;
            _agent.autoBraking = false;
            _agent.stoppingDistance = 0f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

            if (!TryGetComponent(out _actor)) _actor = gameObject.AddComponent<ActorAnimator>();
        }

        private void Start()
        {
            Debug.Log($"[HeroLocomotion] Start — pos={transform.position}, " +
                      $"newInputKb={(Keyboard.current != null ? "OK" : "null")}, " +
                      $"newInputGp={(Gamepad.current != null ? "OK" : "null")}, " +
                      $"animator={(_animator != null ? _animator.name : "null")}, " +
                      $"controller={(_animator != null && _animator.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "null")}");

            // DEF-10: SpawnHeroMarker / HeroIndicatorRing removed — was a debug
            // visibility aid, confirmed unwanted in review screenshots (2026-05-26).

            // DEF-70: subscribe to wave clear so the hero plays a victory pose.
            // WaveManager has no static Instance — FindObjectOfType once in Start
            // (same pattern as DungeonPortal, TowerVoiceController). Safe: Start
            // runs after all Awake() calls so WaveManager is already initialised.
            _waveManager = Object.FindObjectOfType<WaveManager>();
            if (_waveManager != null)
                _waveManager.OnWaveCleared.AddListener(OnWaveCleared);
            else
                Debug.LogWarning("[HeroLocomotion] WaveManager not found — victory pose will not fire.");

            // WO-387: cache the follow camera for the camera-relative movement basis.
            _smartCamera = Object.FindObjectOfType<SmartMobileCamera>();

            // WO-377 / WO-557: suppress player input while a conversation is on screen. We
            // subscribe to OUR dialogue stack's engine-wide Started/Ended signals (Yarn removed),
            // and reconcile to the LIVE state in case a dialogue is already running when we spawn.
            HookDialogueGate();
        }

        private void OnDestroy()
        {
            if (_waveManager != null)
                _waveManager.OnWaveCleared.RemoveListener(OnWaveCleared);

            // WO-557: unhook the dialogue signals and clear the global gate so a hero
            // destroyed mid-dialogue (e.g. scene swap) never leaves input stuck off.
            if (_dialogueHooked)
            {
                DeNelle.Core.Dialogue.DialogueService.Started -= OnDialogueStarted;
                DeNelle.Core.Dialogue.DialogueService.Ended -= OnDialogueEnded;
                _dialogueHooked = false;
            }
            InputSuppressed = false;
        }

        // WO-557: subscribe to OUR dialogue stack's Started/Ended events (parameterless,
        // always available — no runner to find, no retry coroutine). Then reconcile to the
        // live state: if a conversation is ALREADY running when this hero spawns (intro/FTUE),
        // raise suppression now so input never bleeds through the beat (the WO-377 symptom).
        private void HookDialogueGate()
        {
            if (_dialogueHooked) return; // already hooked
            DeNelle.Core.Dialogue.DialogueService.Started += OnDialogueStarted;
            DeNelle.Core.Dialogue.DialogueService.Ended   += OnDialogueEnded;
            _dialogueHooked = true;

            bool running = DeNelle.Core.Dialogue.DialogueService.IsRunning;
            if (running && !InputSuppressed)
            {
                InputSuppressed = true;
                Velocity = Vector3.zero;
                DeNelle.Core.Diagnostics.FlowTrace.Warn("UI",
                    "HeroLocomotion hooked a dialogue ALREADY in progress — suppressing input (catch-up for the missed Started).");
            }
            else if (!running && InputSuppressed)
            {
                // No dialogue live now — don't inherit a stale lock that would dead-freeze this fresh hero.
                InputSuppressed = false;
            }
        }

        // WO-377: dialogue opened — suppress player input and hold the hero in place. The
        // idle POSE is handled by WO-376 (HeroBodySwapper); here we only freeze control:
        // zero the velocity so the hero stops cleanly, and Update() short-circuits its
        // input read while the flag is set.
        private void OnDialogueStarted()
        {
            InputSuppressed = true;
            Velocity = Vector3.zero;
        }

        // WO-377: dialogue closed — restore normal player input.
        private void OnDialogueEnded()
        {
            InputSuppressed = false;
        }

        // DEF-70: called by WaveManager.OnWaveCleared (WaveNumberEvent — int waveId).
        private void OnWaveCleared(int waveId)
        {
            _victoryPose = true;
            _victoryPoseTimer = VictoryPoseSeconds;
            Velocity = Vector3.zero; // zero carried velocity so the hero stops in place

            ResolveAnimator();
            if (_animator != null)
            {
                if (_hasSpeedParam)   _animator.SetFloat(AnimSpeed, 0f);
                if (_hasVictoryParam) _animator.SetTrigger(AnimVictory);
            }

            Debug.Log($"[HeroLocomotion] Victory pose triggered — wave {waveId} cleared.");
        }

        // DEF-70: shared animator resolver — safe to call before HeroBodySwapper fires.
        private void ResolveAnimator()
        {
            if (_animator == null)
            {
                var bodyT = transform.Find("HeroBody");
                if (bodyT != null) _animator = bodyT.GetComponentInChildren<Animator>();
                if (_animator == null) _animator = GetComponentInChildren<Animator>();
            }
            // WO-174: (re)cache the param presence whenever the resolved Animator
            // changes — HeroBodySwapper assigns a fresh runtimeAnimatorController at
            // runtime, so a controller swap means we must re-scan its parameters.
            if (_animator != null && _animator != _paramCheckedAnimator)
                RefreshParamCache();
        }

        // WO-174 param-guard: scan the live controller's declared parameters once
        // per (re)resolve so the per-frame SetFloat/SetTrigger never hits a missing
        // param (which spams an error every frame). A null controller leaves every
        // flag false → the setters silently no-op until a valid controller binds.
        private void RefreshParamCache()
        {
            _paramCheckedAnimator = _animator;
            _hasSpeedParam = false;
            _hasVictoryParam = false;
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var p in _animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float   && p.name == "Speed")   _hasSpeedParam = true;
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Victory") _hasVictoryParam = true;
            }
        }

        /// <summary>
        /// WO-581: explicit animator injection — <see cref="HeroBodySwapper"/> calls this
        /// DIRECTLY after a body swap (no reflection) so this component drives Speed→Walk on the
        /// LIVE swapped rig. Re-scans the controller's params (a swap rebinds the animator).
        /// Replaces the brittle name-based reflection write that wrote 0 when this component
        /// wasn't on the root yet at swap time (the castle-hub "hero will not animate" regression).
        /// </summary>
        public void SetAnimator(Animator anim)
        {
            if (anim == null) return;
            _animator = anim;
            RefreshParamCache();

            // §12 (mocap-locomotion retarget verify, owner 2026-07-04): log the wire-path facts ONCE so a
            // headless AutoPilot run proves the KnightMocap swap took — applyRootMotion (must be false so
            // HeroLocomotion owns movement), the avatar validity (a Humanoid clip poses ONLY through a valid
            // avatar; invalid = the T-pose "sliding statue"), and the bound controller name + clip count
            // (KnightMocap vs Knight, and that its locomotion clips are present).
            var rac = _animator.runtimeAnimatorController;
            DeNelle.Core.Diagnostics.FlowTrace.Once("HeroLoco",
                "wire/" + (_animator != null ? _animator.GetInstanceID().ToString() : "null"),
                $"SetAnimator wired: controller={(rac != null ? rac.name : "<null>")}, " +
                $"applyRootMotion={_animator.applyRootMotion}, " +
                $"avatar={(_animator.avatar != null ? _animator.avatar.name : "<none>")} " +
                $"(valid={( _animator.avatar != null && _animator.avatar.isValid)}), " +
                $"clips={(rac != null && rac.animationClips != null ? rac.animationClips.Length : 0)}.");
        }

        // WO-139 #9: WaveManager may spawn after the hero, leaving _waveManager
        // null forever (victory pose never wires). Retry the resolve+subscribe
        // each frame until it's found, then stop.
        private void TryResolveWaveManager()
        {
            if (_waveManager != null) return;
            _waveManager = Object.FindObjectOfType<WaveManager>();
            if (_waveManager != null)
                _waveManager.OnWaveCleared.AddListener(OnWaveCleared);
        }

        // Countdown seconds at which a wave reads "imminent" (battle-worthy), mirroring the HUD
        // posture authority HudContextEvaluator.ImminentThreshold (owner 2026-07-08). That const is
        // `private` inside a HUD-facing type this Village-scoped file must not couple to, so the value
        // is duplicated with THIS pointer. Keep the two in lockstep; promote to a shared Core const if
        // they ever diverge. (Ticket 2026-07-09: hero must be at friendly idle in town.)
        private const float CombatImminentThreshold = 5f;

        // Tracks the last stance we drove so we log ONE [Flow:HeroLoco] Step on each flip, not every frame.
        private bool _lastCombatStance;
        private bool _hasLastCombatStance;

        // True only while a wave is genuinely live (Active phase, or a Countdown in its final imminent
        // window) or an actual battle is locked. This MIRRORS the HUD posture authority
        // (HudContextEvaluator.IsWaveActive): a long between-wave Countdown reads as Town, so the hero
        // keeps the relaxed idle instead of the braced combat idle. An idle WaveManager sitting in the
        // hub is NOT combat.
        private bool IsWaveInCombat()
        {
            // In-place dungeon/outpost fights (HeroCombatEngagement) AND arena battles register on
            // BattleLock — BattleArena.Awake registers a probe () => BattleInProgress, so
            // BattleLock.IsInBattle() is already true during any real arena battle. We deliberately do
            // NOT short-circuit on BattleArena.AnyBattleInProgress directly: it was redundant with the
            // probe AND a stale/hub battle flag could hold the braced idle while the HUD reads Town
            // (ticket 2026-07-09). BattleLock stays the single genuine-battle signal.
            if (DeNelle.Core.Combat.BattleLock.IsInBattle()) return true;
            if (_waveManager == null) return false;
            var phase = _waveManager.Phase;
            if (phase == WavePhase.Active) return true;
            // A Countdown only counts as combat in its final imminent window — EXACTLY the HUD rule —
            // so a long between-wave gap in the hub leaves the hero in the calm idle (matches Town).
            if (phase == WavePhase.Countdown)
                return _waveManager.CountdownRemaining <= CombatImminentThreshold;
            return false;
        }

        private void Update()
        {
            // WO-1483 frame budget. FIRST line so every early-return path is still timed.
            // Accumulating overload (4-arg) — it does NOT log per frame; PerfReporter rolls
            // it up once a second, and a single pass over 4ms warns at most once a second.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "HeroLocomotion.Update", 4f, 1f);

            TryResolveWaveManager();
            // The legacy FootstepsWalk loop is intentionally not driven. Its leading transient
            // sounded like a UI/load ding every time movement resumed. Footstep cadence belongs
            // to HeroFootstepController once short, licensed one-shot clips are assigned.

            // WO-377: while a Yarn dialogue is on screen, the player has no control —
            // hold the hero in place (zero velocity, no input read) so a click meant for
            // the dialogue box can't walk/turn the hero. The idle POSE is held by WO-376
            // (HeroBodySwapper). We still drive Speed=0 into the animator so the walk
            // blend settles to idle. Returns BEFORE the auto-walk branch so dialogue
            // suppression also pauses a scripted tour mid-step.
            // HERO_TURN_PROBE authority: when the headless turn probe is armed (TurnDebug + a scripted
            // move injected), it must exercise the REAL player movement/rotation path. In the hub a live
            // TutorialFlow / dialogue can hold InputSuppressed or set _autoWalkTarget, whose early-returns
            // below would bypass the rotation writer + the [Flow:HeroTurn] trace entirely (the hero would
            // be steered by AutoWalkStep instead of the slew we're measuring). While the probe is armed we
            // SKIP those competing owners so the scripted forward drives the genuine slew. Gated on
            // TurnDebug (default false) → zero effect on normal play; nothing else sets _scriptedMoveActive.
            bool probeDriving = TurnDebug && _scriptedMoveActive;

            if (InputSuppressed && !probeDriving)
            {
                // WO-1298 — THE SUPPRESSION BRANCH IS MOVER-AGNOSTIC TOO.
                // Velocity stays zeroed: this component is genuinely not the mover while the
                // dialogue holds the player. But the ANIMATOR must be fed what the ROOT did, not
                // what this component did — the two are only the same thing while nothing else is
                // pushing the hero. When something else IS (the tutorial pointing her at the west
                // gate in the owner's seq 4362 capture), the old hard `SetFloat(Speed, 0f)` +
                // `SetLocomotion(0f)` did not merely leave the animator stale, it OVERWROTE a live
                // walk cycle with a dead zero every frame — the glide the owner flagged.
                // Below AnimStallRootSpeed this is byte-identical to the old behaviour (0f), so the
                // WO-377 "hold the hero still during a story beat" contract is untouched.
                float suppressedFeed = ResolveSuppressedAnimatorFeed(_measuredRootSpeed, OverworldRunSpeed);
                Velocity = Vector3.zero;
                ResolveAnimator();
                if (_animator != null && _hasSpeedParam) _animator.SetFloat(AnimSpeed, suppressedFeed);
                _actor?.SetLocomotion(suppressedFeed);

                // §12 — the situation names ITSELF from now on. A hero travelling while her input
                // is taken away is never intended: either a mover is un-owned (the defect) or a
                // scripted beat is moving her and should be saying so. Throttled to 1 Hz; the whole
                // block is gated so it costs nothing with tracing off.
                if (_measuredRootSpeed > AnimStallRootSpeed && DeNelle.Core.Diagnostics.FlowTrace.Enabled)
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroOwner", "suppressed-but-moving", 1f,
                        $"INPUT SUPPRESSED BUT THE ROOT IS MOVING: velRoot={_measuredRootSpeed:F2} m/s " +
                        $"velSelf=0.00 (zeroed here by design) autoWalk={IsAutoWalking} " +
                        $"ownerCC={(ForeignMoverOwnsTransform() ? "LIVE" : "none")} " +
                        $"pos={transform.position:F2} rootYaw={transform.eulerAngles.y:F1} " +
                        $"scene='{gameObject.scene.name}'. The animator is now fed {suppressedFeed:F2} " +
                        "(the MEASURED root speed, clamped to the run tier) so the walk cycle plays " +
                        "through the beat instead of freezing — but an UNCLAIMED mover here is still a " +
                        "defect upstream of this component (WO-1298).");
                return;
            }

            // WO-277 (tutorial auto-walk): while the tutorial owns the hero, IGNORE
            // player input entirely and steer toward the scripted target. We build a
            // WORLD-SPACE move vector here and skip the camera-relative basis below
            // (so the synthetic heading isn't re-rotated by the camera yaw). Arrival
            // is reported via AutoWalkArrived; the tutorial clears the target to hand
            // control back. Returns through the SAME NavMesh Move() path as manual
            // walking so stairs/ramparts/walls behave identically.
            // ── ONE OWNER OF THIS TRANSFORM (WO-968 S1 / WO-1016) ────────────────────────────
            // Resolved BEFORE any writer below (auto-walk included) because every one of them
            // writes transform.position or transform.rotation. When a live CharacterController
            // sits on this rig, THAT component is the integrator this frame and this one must
            // write nothing at all — see SelfMayWriteTransform for the full rationale.
            //
            // Why this is not redundant with the dungeon's existing neutralize: the neutralize is
            // three SHARED STATICS (scripted-move zero + GroundSnapEnabled) that any other system
            // can clear, and the owner's capture proves it was OFF for part of the session
            // ([Flow:HeroDrift] input=(0.00,1.00) vel=(0.000,5.000) inside the dungeon). This gate
            // is a per-frame CAPABILITY check on this rig, so it cannot lapse.
            bool foreignOwnsTransform = ForeignMoverOwnsTransform();

            if (_autoWalkTarget != null && !probeDriving && !foreignOwnsTransform)
            {
                // Crossings must fire during auto-walk too (bot tests + scripted cutscene walks), not just
                // player input — otherwise the hero auto-walks INTO a HeroLinkCrossing and never warps.
                if (TryTraverseSeamLink()) return;
                AutoWalkStep();
                return;
            }

            Vector2 input = ReadMoveInput();

            // The foreign mover owns the transform: stand down COMPLETELY. Zeroing the input +
            // velocity here is enough to disarm every writer below (the translate/face block, the
            // town move-start slew and the lock-face slew are all gated on input or Velocity), and
            // the face-hold request is dropped so it cannot slew the root either. The animator is
            // still driven — from the MEASURED root speed — further down, which is the half of this
            // that fixes WO-1016's "slides in idle".
            if (foreignOwnsTransform)
            {
                input = Vector2.zero;
                Velocity = Vector3.zero;
                _facingActive = false;
            }

            // DEF-70 fix: the victory pose briefly suppresses movement after a wave
            // clear — but it must NEVER lock the hero. It ends the instant the player
            // gives movement input, or after VictoryPoseSeconds. (It previously
            // latched true forever: OnWaveCleared set it and NOTHING cleared it, so
            // the hero froze permanently after the first wave clear — the reported
            // "herolocomotion stopped after the level up.")
            if (_victoryPose)
            {
                _victoryPoseTimer -= Time.deltaTime;
                if (_victoryPoseTimer > 0f && input.sqrMagnitude < 0.01f)
                    return;            // still celebrating, no input — hold the pose
                _victoryPose = false;  // player moved or the timer elapsed — resume
            }

            // WO-387: camera-relative in follow mode, world-absolute in top-down. SmartMobileCamera
            // .CameraYaw returns the player-pan yaw in 3rd-person follow and 0 in top-down/legacy, so a
            // camera-mode change can't break input (WO-368/363 intent preserved). Curl-safe: CameraYaw is
            // pan-driven, never velocity-driven. (Lazy re-fetch so the fix engages if the camera wired
            // up after the hero — otherwise it would silently no-op.)
            float yaw = ResolveMovementBasisYaw(out MovementBasis basisKind);
            Quaternion cameraRotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 move = cameraRotation * new Vector3(input.x, 0f, input.y);
            if (move.sqrMagnitude > 1f) move.Normalize();

            // Option 2 (owner 2026-07-10): the OPEN world runs at the full run tier; COMBAT is calmer/
            // planted. Flipped from the old combat-6/town-4.4 so traversal always runs and fighting reads
            // deliberate (braced stance + lower cap). Overworld hits the run blend child; combat sits just
            // under it.
            bool engaged = IsWaveInCombat();
            float moveSpeedCap = engaged ? CombatMoveSpeed : OverworldRunSpeed;

            bool hasMoveInput = input.sqrMagnitude > 0.0001f;
            // Town/open-world: facing + step ride camera input, not NavMesh lateral velocity.
            // Combat keeps Velocity for both (planted braced gait). Pairs with SmartMobileCamera's
            // facing-recenter suspend — camera pivot must not retarget `move` while steering.
            bool townInputDrive = !engaged && hasMoveInput && move.sqrMagnitude > 0.0001f;

            // Smooth velocity toward target — instant max-speed felt rigid.
            // Higher accel when grabbing speed, higher decel when releasing,
            // so the hero responds promptly to a key press but glides slightly
            // when stopped (no instant-snap to zero).
            // WO-910: talent moveSpeed multiplies on top of injured slow (identity when none).
            float talentMove = 1f;
            var abMove = GetComponent<HeroAbilities>();
            if (abMove != null)
                talentMove = DeNelle.Village.Talents.HeroTalentModifiers.MoveSpeedMultiplier(abMove.HeroClass);
            Vector3 targetVelocity = move * (moveSpeedCap * HeroHealth.MoveSpeedMultiplier * talentMove);
            float maxStep = (targetVelocity.sqrMagnitude > Velocity.sqrMagnitude
                ? _accelMetresPerSec2
                : _decelMetresPerSec2) * Time.deltaTime;
            Velocity = Vector3.MoveTowards(Velocity, targetVelocity, maxStep);
            // Correct stale lateral drift in town (F8 HeroDrift: velX=2.5 on a pure-forward hold)
            // WITHOUT the hard per-frame snap (f7740f4e) — that snap deleted the MoveTowards
            // direction glide, so turns twitched and diagonal wall contact jittered instead of
            // sliding (owner: "broke the movement", 2026-07-11). Rotate the heading toward the
            // stick at a finite-but-fast rate: drift can never accumulate (rate >> recenter gain),
            // turns keep a readable arc, magnitude untouched.
            if (townInputDrive && Velocity.sqrMagnitude > 0.0001f)
            {
                const float headingCorrectDegPerSec = 540f;
                Velocity = Vector3.RotateTowards(
                    Velocity, move.normalized * Velocity.magnitude,
                    headingCorrectDegPerSec * Mathf.Deg2Rad * Time.deltaTime,
                    0f);
            }

            // WO-423: face-the-attack-target yaw slew. A fresh movement input cancels the
            // request immediately (we never fight the move-direction LookRotation below);
            // otherwise, while there is ~no movement input, slew the root yaw toward the
            // requested target for up to the hold duration. This is the SOLE rotation writer
            // (updateRotation=false), so honoring the request here is enough — no animator
            // turn-in-place (v1 = yaw slew only, deferred Lane 3 WO).
            if (_facingActive)
            {
                if (hasMoveInput)
                {
                    _facingActive = false;   // player took back control of facing
                }
                else
                {
                    _faceHoldRemaining -= Time.deltaTime;
                    if (_faceHoldRemaining <= 0f)
                    {
                        _facingActive = false;
                    }
                    else
                    {
                        float curYaw  = transform.eulerAngles.y;
                        float nextYaw = StepYaw(curYaw, _faceTargetYaw, FaceYawDegPerSec * Time.deltaTime);
                        transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
                    }
                }
            }

            // Manual seam-link traversal (WO-468 / navlink RCA 2026-06-20): the hero is
            // INPUT-driven (we call _agent.Move, NOT SetDestination), so it CANNOT auto-cross a
            // NavMeshLink — links are only traversed by a pathfinding agent. We carry it across the
            // castle<->OuterWorld gap IN-WORLD (no warp/fade). While a crossing runs it OWNS
            // movement, so suppress the normal Move this frame.
            bool seamConsumed = TryTraverseSeamLink();

            // WO-512 slice 3: is the lock-face governing facing this frame? Only when a lock is
            // engaged with a live target AND the flag is on. (InputSuppressed/auto-walk already
            // early-returned above, so dialogue/cutscene facing is never fought.) When false, the
            // EXISTING LookRotation(Velocity) writer below runs byte-identical — zero regression.
            // (WO-968 S1) ...and never while a foreign mover owns the transform — ApplyLockFaceYaw
            // writes transform.rotation, which would make a second rotation writer on the rig.
            // WO-1105 R3: _lockFaceForce is the ranged-class facing drive, which is NOT part of the
            // WO-512 lock-on camera experiment and therefore does not wait on its flag.
            bool lockFacing = _lockFaceActive && _lockFaceTarget != null &&
                              (_lockFaceForce || DeNelle.Core.FeatureFlags.LockOn) && !foreignOwnsTransform;

            // [Flow:HeroTurn] step-in: capture the PRE-rotation state so the trace below can report
            // the exact yaw applied this frame vs the target. moveHeading = Atan2 of the (camera-
            // relative) move vector — the heading the hero is being asked to face. Cheap scalars,
            // always computed; the LOGGING is gated on TurnDebug && FlowTrace.Enabled.
            float heroYawBefore = transform.eulerAngles.y;
            float moveHeading   = (move.sqrMagnitude > 1e-6f)
                ? Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg
                : heroYawBefore;
            string turnBranch   = "none";   // which rotation branch actually wrote yaw this frame

            if (!seamConsumed && Velocity.sqrMagnitude > 0.0001f)
            {
                if (!_loggedFirstInput)
                {
                    _loggedFirstInput = true;
                    Debug.Log($"[HeroLocomotion] First input registered: ({input.x:F2}, {input.y:F2}) — moving from {transform.position}");
                }

                // Move on the shared NavMesh: the agent constrains the hero to the walkable
                // surface (so it can't enter walls/buildings — there's no NavMesh there) and
                // follows the surface height, so stairs / ramparts / hills "just work" exactly
                // like the enemies. Fall back to a raw transform move if the hero isn't on a
                // NavMesh yet (scene without a bake / spawned off-mesh) so movement never breaks.
                // NOTE (WO-512): the move STEP is camera-relative — UNCHANGED by lock-face.
                // STEP BASIS RESTORED to Velocity (SME frame-map audit 2026-07-12, owner F8
                // "walking/running reads wrong", onset = d1eea617): the instant
                // move.normalized step changed travel direction in ONE frame while the body
                // slerps over ~0.19s — at 6 m/s that is ~1.1m of travel pointing the wrong
                // way on every stick change (skid/fishtail), and the 540°/s RotateTowards
                // arc (:809) never reached translation. Velocity IS arc-corrected toward
                // the stick at 540°/s, so the F8 HeroDrift lateral-drift proof stays fixed
                // while travel arcs with the body again.
                Vector3 step = Velocity * Time.deltaTime;
                if (_agent != null && _agent.isOnNavMesh)
                    _agent.Move(step);
                else
                    transform.position += step;

                if (lockFacing)
                {
                    // WO-512 slice 3: face the LOCKED enemy instead of the move direction. Reuse
                    // StepYaw + _rotationSpeed (degrees/sec) for the same slew feel; the move vector
                    // above is untouched, so this is the only difference vs. the normal path.
                    ApplyLockFaceYaw();
                    turnBranch = "lockface";
                }
                else
                {
                    // Face the move direction. HeroBodySwapper applies a root-child LocalRotation
                    // (WO-326: -90f, the proven companion value) so the visual mesh's authored +X
                    // forward aligns to the locomotion root's +Z. HeroLocomotion therefore does
                    // pure LookRotation on the velocity for the root transform — no extra Euler
                    // offset. (If the hero ever sidesteps again, the forwardYaw sign in
                    // HeroBodySwapper is the single place to flip.)
                    // Town/open-world: face the camera-relative INPUT heading, not NavMesh velocity —
                    // agent.Move can inject lateral X while the stick is pure-forward, which made
                    // LookRotation(Velocity) yaw back and forth (HeroDrift #1: dYaw oscillation).
                    Vector3 faceBasis = townInputDrive ? move : Velocity;
                    Quaternion target = Quaternion.LookRotation(faceBasis.normalized);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, target, _rotationSpeed * Time.deltaTime);
                    // Rotation writer while MOVING: in the OPEN world (run tier, not engaged) this is
                    // the "town-slew"; in a wave at the run tier the combat turn-clip feed rides it
                    // (labeled below). Both slew the ROOT toward the move heading at _rotationSpeed.
                    turnBranch = (engaged && moveSpeedCap >= OverworldRunSpeed) ? "combat-turn-clip" : "town-slew";
                }
            }
            else if (!seamConsumed && (!engaged || moveSpeedCap < OverworldRunSpeed) && !lockFacing && hasMoveInput && move.sqrMagnitude > 0.0004f)
            {
                // Town move-start: slew toward input heading before velocity spools. Replaces the
                // low-pivot turn-in-place clips (turnleft180 reads as crouch) that only belong in combat.
                Quaternion target = Quaternion.LookRotation(move.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, target, _rotationSpeed * Time.deltaTime);
                turnBranch = "town-slew";
            }
            else if (lockFacing && !seamConsumed)
            {
                // WO-512 slice 3: standing still but locked — keep facing the orc (the normal
                // path only re-faces while moving, which would freeze facing at the last travel
                // dir during a stationary duel). Move vector is zero here, so this is facing-only.
                ApplyLockFaceYaw();
                turnBranch = "lockface";
            }

            // [Flow:HeroTurn] step-OUT — the "turn-left-before-walk" RCA trace (owner 2026-07-10).
            // Logs, EVERY frame while moving/holding input, the ROTATION DECISION so a reader can
            // compare the applied slew directly to the source math: which branch fired, the combat
            // gate inputs (engaged / caps), camera vs hero yaw, the move heading, the target dYaw,
            // and the EXACT yaw applied THIS frame vs _rotationSpeed. NOT throttled (we want every
            // frame of the short probe). Gated on TurnDebug (default false) AND FlowTrace.Enabled, so
            // it is truly zero-cost in normal play — nothing here runs unless the probe armed it.
            if (TurnDebug && DeNelle.Core.Diagnostics.FlowTrace.Enabled
                && (hasMoveInput || Velocity.sqrMagnitude > 0.0001f))
            {
                float heroYawAfter = transform.eulerAngles.y;
                float applied      = Mathf.DeltaAngle(heroYawBefore, heroYawAfter); // yaw actually written this frame
                float dYaw         = Mathf.DeltaAngle(heroYawBefore, moveHeading);  // remaining angle to the target heading
                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroTurn",
                    $"branch={turnBranch} engaged={engaged} moveSpeedCap={moveSpeedCap:F1} " +
                    $"run={OverworldRunSpeed:F1} combat={CombatMoveSpeed:F1} " +
                    $"camYaw={yaw:F1} heroYaw={heroYawBefore:F1} moveHeading={moveHeading:F1} " +
                    $"dYaw={dYaw:F1} applied={applied:F2} rotSpeed={_rotationSpeed:F1} " +
                    $"vel={Velocity.magnitude:F2}");
            }

            // [Flow:HeroDrift] — periodic-WIGGLE RCA (owner refocus 2026-07-04). The camera-relative
            // sideways SLIDE (move basis at line ~656) is EXPECTED and NOT the defect. The defect is a
            // repeating left/right WIGGLE on a pure-forward hold. Two candidate mechanisms; this ONE
            // time-aligned 10 Hz trace captures BOTH so a forward-hold capture DISTINGUISHES them:
            //   #1 CONTROL/FACING oscillation — the LookRotation(Velocity) facing writer (:749-751)
            //      yaws the ROOT back and forth if Velocity carries a periodic X. TELL: 'heroYaw'
            //      (transform.eulerAngles.y) oscillates with a fixed period AND tracks velX.
            //   #2 ANIMATION sway — in-place mocap pelvic/torso weight-shift, or the out-of-phase
            //      walk/run blend (walkforward01 ~8.75s vs runforward_218667 ~4.0s, cycleOffset 0).
            //      TELL: root/heroYaw is STEADY but hipsLocalX (pelvis) sways periodically, and/or the
            //      per-clip nt (normalizedTime) of the two blended clips drift out of phase.
            // Grep marker: [Flow:HeroDrift]. Whole block gated on FlowTrace.Enabled (the clip-info +
            // StringBuilder alloc must not run on the hot path when tracing is off).
            if (input.y > 0.5f && DeNelle.Core.Diagnostics.FlowTrace.Enabled)
            {
                // #2 anim-side: active clip name + blend weight + per-clip normalizedTime (phase).
                var animSb = new System.Text.StringBuilder();
                float hipsLocalX = float.NaN;
                float baseNt = float.NaN;
                if (_animator != null)
                {
                    // Base-state normalizedTime — the shared phase clock of the blend. Blended clips
                    // advance on THIS same nt but at different real-cycle rates (unequal lengths), so
                    // nt%1 + each clip length lets the offline read see the out-of-phase churn (#2).
                    baseNt = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
                    var clips = _animator.GetCurrentAnimatorClipInfo(0);
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i].clip == null) continue;
                        if (animSb.Length > 0) animSb.Append(", ");
                        animSb.Append($"{clips[i].clip.name}(w={clips[i].weight:F2},len={clips[i].clip.length:F2})");
                    }
                    // pelvis/Hips local X — the direct sway signal (humanoid only; null on generic rig).
                    var hips = _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                    if (hips != null) hipsLocalX = hips.localPosition.x;
                }
                if (animSb.Length == 0) animSb.Append("<none>");
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroDrift", "fwd", 0.1f,
                    // #1 control side:
                    $"input=({input.x:F2},{input.y:F2}) move=({move.x:F3},{move.z:F3}) " +
                    $"vel=({Velocity.x:F3},{Velocity.z:F3}) |velX|={Mathf.Abs(Velocity.x):F3} " +
                    // WO-968 F4: this field was named `camYaw`, which COLLIDED with
                    // [Flow:GaitF]'s camYaw (Camera.main.eulerAngles.y). Two different
                    // quantities under one name mis-led the 2026-08-10 investigation: GaitF
                    // read 180 and HeroDrift read 0, and both were correct readings of
                    // different things. This one is the movement BASIS, so it says so, and
                    // names its source.
                    $"basisYaw={yaw:F1} basis={basisKind} heroYaw={transform.eulerAngles.y:F1} " +
                    $"dYaw={Mathf.DeltaAngle(yaw, transform.eulerAngles.y):F1} " +
                    // #2 anim side:
                    $"| baseNt={baseNt:F2} hipsLocalX={hipsLocalX:F3} clips=[{animSb}]");
            }

            // Core animation fix: drive the guarded ActorAnimator (used by DTT + village
            // heroes). This feeds the Speed float into the HeroAnimatorFactory blendtree
            // (Idle 0 / Walk 6 / Run 9) so basic locomotion plays. The old direct
            // _animator.SetFloat is kept for any legacy listeners; ActorAnimator is the
            // canonical (re-resolves on body swap, guards missing params).
            // During a manual seam slide we drive transform.position directly (the agent is released),
            // so Velocity may be ~0 even though the hero is moving — feed the animator a WALK speed so
            // the locomotion cycle plays through the crossing instead of freezing.
            // WO-968 F1 / WO-1016: the feed is MOVER-AGNOSTIC. When a foreign mover owns the
            // transform, Velocity is dead BY DESIGN (this component is standing down, see the
            // ownership gate above), so feeding it is what made the Keeper slide through the
            // dungeon in a single idle clip. Publish the MEASURED root speed instead — what the
            // root ACTUALLY did, regardless of which component wrote it. In town nothing has a
            // CharacterController, so foreignOwnsTransform is false and this is byte-identical to
            // the old line.
            _actor?.SetLocomotion(ResolveAnimatorFeed(
                _crossingSeam, _moveSpeed, foreignOwnsTransform, Velocity.magnitude, _measuredRootSpeed));

            // Turn-in-place / directional-turn feed (owner 2026-07-04, KnightMocap full-turning). Combat
            // only — town uses input-facing slew above (no turnleft180 low-pivot clips). Skipped during
            // a seam slide (the crossing owns movement/facing).
            if (!_crossingSeam)
            {
                // Turn-in-place mocap clips belong ONLY with the RUN gait; below the run child (calmer
                // combat @ CombatMoveSpeed=5 → walk-weight 0.25) they conflict with the walk gait — the
                // "turn-left-before-walk / crouch" Grok fixed in 86847b7f (which relied on combat==run@6).
                // Option 2 dropped combat to 5, reviving it; gate the clip to the run tier, else the smooth
                // slew below handles rotation. Proven: Player.log turnleft90 @vel5 + walk(0.25)+run blend.
                if (engaged && moveSpeedCap >= OverworldRunSpeed) DriveTurnSignal(move, hasMoveInput);
                else _actor?.PlayTurn(TurnDirection.None);
            }

            // Battle Ready (stance) vs casual Idle: combat stance ONLY when actually in
            // combat — a live wave (Countdown/Active phase). Merely having a WaveManager in
            // the scene is NOT combat (the hub/town keeps one idle), so presence alone must
            // NOT raise the ready pose, or the hero stands weapon-ready in town. Movement is
            // intentionally NOT a combat trigger here: walking around town is relaxed, not
            // battle-ready. speed=0/moving + !combat = casual idle/walk; in-wave = ready.
            // [Flow:HeroLoco] one Step per stance flip, naming the decision inputs, so a headless/felt
            // run proves Town->calm (engaged=false with a long countdown + no battle) without breaking
            // brace-in-battle (engaged=true on Active / imminent countdown / BattleLock).
            if (!_hasLastCombatStance || _lastCombatStance != engaged)
            {
                _hasLastCombatStance = true;
                _lastCombatStance = engaged;
                if (DeNelle.Core.Diagnostics.FlowTrace.Enabled)
                    DeNelle.Core.Diagnostics.FlowTrace.Step("HeroLoco",
                        $"stance -> {(engaged ? "COMBAT(braced)" : "CALM(town idle)")} " +
                        $"[battleLock={DeNelle.Core.Combat.BattleLock.IsInBattle()} " +
                        $"wavePhase={(_waveManager != null ? _waveManager.Phase.ToString() : "<none>")} " +
                        $"countdownRemaining={(_waveManager != null ? _waveManager.CountdownRemaining.ToString("0.0") : "n/a")} " +
                        $"imminentThreshold={CombatImminentThreshold:0.0}]");
            }
            _actor?.SetCombatStance(engaged);
            // Weapon carry must mirror the SAME engaged flag (EquipmentController's auto
            // WaveManager mirror treated ANY Countdown as combat — sword stayed drawn for
            // 200s+ while the animator flipped to calm m-standby-idle = "bent pose, sword out").
            var equip = GetComponent<EquipmentController>();
            equip?.SetCombatActive(engaged);

            // Edge/floor clamp + ground-snap ONLY when off the NavMesh (the transform
            // fallback). When the hero is ON the NavMesh, the bake defines the walkable
            // bounds + height, so a manual clamp would fight the agent (and break
            // ramparts/hills by pinning Y to 0).
            // WO-383: a seam warp (WarpTo) deliberately places the hero far out and onto a
            // separately-baked NavMesh — skip the off-mesh clamp on that frame so the warp
            // isn't yanked back. WO-1094: "that frame" now MEANS something — TeleportGuardHeld
            // spans the warp frame AND the next Update, where the old bool spanned zero frames.
            // OWNERSHIP GATE (dungeon walk-fail P0, 2026-08-05 — see ForeignMoverOwnsTransform
            // ~:317 for the full rationale). "Off the navmesh" alone does NOT mean this
            // component may move the hero, and it does NOT mean the ±50 castle bounds apply:
            //   • foreignMover  — a live CharacterController (dungeon Keeper) owns the transform.
            //   • inStagedArena — the fight is staged ~7km out at (5000,0,5000); the home
            //                     scene's playable bounds are meaningless there, and clamping
            //                     them yields exactly (50,0,50).
            // On the overworld both are false, so the condition below reduces to the original
            // `(_agent == null || !_agent.isOnNavMesh) && !_isTeleporting` — unchanged.
            // (WO-968) resolved ONCE at the top of Update now — the clamp and the movement writers
            // must never disagree about who owns the transform on a given frame.
            bool foreignMover  = foreignOwnsTransform;
            bool inStagedArena = DeNelle.Village.Arena.BattleArena.IsArenaPosition(transform.position);
            if ((_agent == null || !_agent.isOnNavMesh)
                && !TeleportGuardHeld(_isTeleporting, Time.frameCount, _teleportGuardUntilFrame)
                && !foreignMover && !inStagedArena)
            {
                var p = transform.position;
                float preX = p.x, preZ = p.z;

                // WO-1094: the bound is MEASURED where a Terrain exists and comes off the
                // RemoteTunables rail where one does not. It is never absent, and never a literal.
                Bounds playable = ResolvePlayableBounds(out string boundsSource);
                p = ClampToPlayableBounds(p, playable);

                // OBSERVABILITY: when the clamp actually RELOCATES the hero, name the before/
                // after, the BOUND IT ENFORCED and WHERE THAT BOUND CAME FROM. A 7km-to-(50,50)
                // jump must never again be a silent, unattributable teleport in a capture, and
                // the old line named the value only — which is exactly why ±50 read as
                // intentional for a month. Exact float compare is intentional: Mathf.Clamp
                // returns the input bit-identical when it is already in range, so this is
                // precisely "the clamp changed something". Throttled to 1/sec.
                if ((preX != p.x || preZ != p.z) && Time.realtimeSinceStartup >= _nextClampWarnAt)
                {
                    _nextClampWarnAt = Time.realtimeSinceStartup + 1f;
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("HeroLoco",
                        $"playable-bounds CLAMP relocated the hero: ({preX:F2},{preZ:F2}) -> ({p.x:F2},{p.z:F2}) " +
                        $"[bound x[{playable.min.x:F1}..{playable.max.x:F1}] z[{playable.min.z:F1}..{playable.max.z:F1}]" +
                        $", source={boundsSource}; off-mesh guard; agent=" +
                        $"{(_agent == null ? "<null>" : (_agent.enabled ? "enabled/off-mesh" : "disabled"))}, " +
                        $"cc={(_ccProbe == null ? "<none>" : (_ccProbe.enabled ? "LIVE" : "disabled"))}, " +
                        $"scene='{gameObject.scene.name}', frame={Time.frameCount}, " +
                        $"teleportGuardUntil={_teleportGuardUntilFrame}]");
                }

                // DEF-147: off-mesh ground-snap / re-bind. The agent goes off-mesh both
                // when the hero walks off a real edge AND when the rampart lift
                // SUSPENDS the agent to hand-carry the hero up/down. We must NEVER snap
                // during the lift carry (it would yank the hero off the slab) — so gate
                // the whole snap on the lift NOT actively carrying. When uncertain, we
                // do nothing (preserve today's behavior) and just floor Y at 0 below.
                if (GroundSnapEnabled && !IsLiftCarrying())
                {
                    // Find the nearest navmesh point to the hero. If one exists within
                    // the sample radius, that's valid ground — pull the hero DOWN toward
                    // its Y at a gravity-like rate (never up; up is the agent's job /
                    // the lift's), so the hero falls to the surface instead of freezing.
                    if (NavMesh.SamplePosition(p, out NavMeshHit hit, SnapSampleRadius, NavMesh.AllAreas))
                    {
                        float groundY = hit.position.y;
                        if (p.y > groundY + 0.01f)
                        {
                            // Accelerate a downward fall, capped, and step Y toward ground.
                            _fallSpeed = Mathf.Min(_fallSpeed + FallAccel * Time.deltaTime, FallSpeedMax);
                            p.y = Mathf.MoveTowards(p.y, groundY, _fallSpeed * Time.deltaTime);
                        }
                        else
                        {
                            // At or below the sampled ground — clamp to it, stop falling.
                            p.y = groundY;
                            _fallSpeed = 0f;
                        }

                        WritePositionIfChanged(p);   // no-op writes poke the physics bodies

                        // Once essentially down at ground, re-bind the AGENT onto the mesh
                        // so it resumes its own height-follow (this also auto-heals the
                        // lift's "suspended-on-deck" float once the lift releases). Only
                        // when the agent is enabled but off-mesh — never re-enable an agent
                        // the lift deliberately disabled mid-ride (excluded above).
                        if (_agent != null && _agent.enabled && !_agent.isOnNavMesh &&
                            Mathf.Abs(p.y - hit.position.y) <= ReBindYBand)
                        {
                            _agent.Warp(hit.position);
                            _fallSpeed = 0f;
                        }
                        return;   // ground-snap handled the Y this frame
                    }
                }

                // Fallback (snap disabled, lift carrying, or no navmesh nearby). DEF-147 /
                // WO-254 hover-exploit ROOT FIX: when the snap finds no navmesh within
                // SnapSampleRadius — the exact case of the "walk off a ramp edge to float
                // above enemies" cheese, where the hero hangs HIGH over a gap with no mesh
                // below — the old code only clamped at Y=0 and otherwise PRESERVED the
                // airborne Y forever (the hover). Apply the same gravity-like fall toward the
                // world floor (Y=0) so the hero always descends instead of suspending midair.
                // The lift-carry case is excluded above (snap-gated), so this never fights the
                // rampart lift. Once at/below the floor, clamp to 0 and reset the fall.
                if (GroundSnapEnabled && !IsLiftCarrying() && p.y > 0f)
                {
                    _fallSpeed = Mathf.Min(_fallSpeed + FallAccel * Time.deltaTime, FallSpeedMax);
                    p.y = Mathf.MoveTowards(p.y, 0f, _fallSpeed * Time.deltaTime);
                }
                if (p.y <= 0f) { p.y = 0f; _fallSpeed = 0f; }
                WritePositionIfChanged(p);   // no-op writes poke the physics bodies
            }
            else
            {
                _fallSpeed = 0f;   // on-mesh: agent owns height, reset any carried fall
            }

            // Self-heal the Animator reference (see ResolveAnimator for rationale).
            // Cheap: only runs while _animator is null, stops once wired.
            ResolveAnimator();
            // RAW Speed write RETIRED (SME frame-map audit 2026-07-12): this legacy direct
            // SetFloat used the SAME "Speed" hash as ActorAnimator.SetLocomotion's 0.12s
            // DAMPED write above (:1016) and ran LATER in the method — the damp was dead
            // code, and MoveTowards' turn chord dips |Velocity| ~30% mid-turn, so the
            // undamped param flicked the walk/run blend on EVERY steering adjustment (the
            // felt gait hiccup). ActorAnimator is now the SOLE Speed writer; it re-resolves
            // on body swap and guards missing params, so legacy listeners still get fed.

            // §12 (mocap-locomotion retarget verify, owner 2026-07-04): ~1/sec, prove WHICH locomotion
            // clip is playing at a given Speed (Walk-vs-Run band) AND that the avatar retargeted (a valid
            // avatar name, not a frozen T-pose). Captured to break-log.jsonl during a headless AutoPilot
            // walk so the KnightMocap swap can be verified without an owner playtest. Near-zero cost when
            // FlowTrace is disabled (Allowed() short-circuits before any string work in Throttle).
            // FOOT-SKATE MEASURE (owner 2026-07-04, gates the KnightMocap builder): alongside the
            // existing state/avatar fields, emit each ACTIVE locomotion clip's name + blend weight +
            // authored clip length, next to the hero's ACTUAL travel speed (Velocity.magnitude m/s
            // from the NavMeshAgent.Move path, ~722-726). The AUTHORED stride m/s side comes from
            // the AnimClipSpeedDump editor pass; the gap between authored-stride and this actual
            // travel = foot-skate, so blend thresholds are set from data not guessed.
            // GetCurrentAnimatorClipInfo allocates, so the WHOLE block is gated on FlowTrace.Enabled
            // -> zero cost when tracing is off (Allowed() alone can't guard the alloc above the call).
            if (_animator != null && DeNelle.Core.Diagnostics.FlowTrace.Enabled)
            {
                var st = _animator.GetCurrentAnimatorStateInfo(0);
                var av = _animator.avatar;
                var clips = _animator.GetCurrentAnimatorClipInfo(0);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < clips.Length; i++)
                {
                    var ci = clips[i];
                    if (ci.clip == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"{ci.clip.name}(w={ci.weight:F2},len={ci.clip.length:F2}s)");
                    if (Velocity.magnitude > 0.5f && ci.weight > 0.5f)
                    {
                        string cn = ci.clip.name.ToLowerInvariant();
                        if (cn.Contains("t-pose") || cn.Contains("tpose") ||
                            (cn.StartsWith("0_") && cn.Contains("pose")))
                        {
                            DeNelle.Core.Diagnostics.FlowTrace.Fail("HeroLoco",
                                $"moving at {Velocity.magnitude:F2} m/s but active clip is T-pose '{ci.clip.name}' " +
                                $"— rebake KnightMocap (BuildKnightMocapController) after Motion Caster pick; " +
                                $"ActorCore FBXs ship 0_T-Pose before the motion take.");
                        }
                        // (2026-07-11) The stale-build guard that flagged 'move_run_m' here is
                        // RETIRED: the registry is owner-authored via Motion Caster now, and
                        // knight.run = move_run_m IS the owner's manual canon pick — hardcoding
                        // expected clip names asserts against whatever the owner chooses next.
                        // The T-pose check above stays: a T-pose take is wrong on ANY pick.
                    }
                }
                if (sb.Length == 0) sb.Append("<none>");
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroLoco", "loco", 1f,
                    $"vel={Velocity.magnitude:F2} m/s | clips=[{sb}] | baseState hash={st.shortNameHash} " +
                    $"nt={st.normalizedTime % 1f:F2} | avatar={(av != null ? av.name : "<none>")} | " +
                    $"controller={(_animator.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "<null>")}");
            }
        }

        // ── MOVEMENT BASIS resolution (WO-968 F3) ────────────────────────────────────
        // WHAT WAS BROKEN, proven at source: this used to read
        //     float yaw = _smartCamera != null ? _smartCamera.CameraYaw : 0f;
        // and there is NO SmartMobileCamera in Dungeon_HealersCottage (zero references to its
        // script GUID in the scene, and nothing runtime-adds one). So in a dungeon the whole
        // camera-relative conversion below silently collapsed to the identity rotation and the
        // player's stick meant WORLD +Z regardless of where the camera pointed — while the OTHER
        // dungeon mover (DungeonHero) projected Camera.main for the SAME stick. Two movers, two
        // frames of reference, one stick. Every [Flow:HeroDrift] line in the owner's dungeon
        // capture reads camYaw=0.0, which is that identity, printed.
        //
        // The fix takes the basis from the camera that ACTUALLY EXISTS in the scene, and a missing
        // basis is a LOUD failure rather than a silent identity.
        private float _nextBasisFailAt;

        private float ResolveMovementBasisYaw(out MovementBasis kind)
        {
            // Lazy re-fetch so the basis engages if the camera wired up after the hero.
            if (_smartCamera == null) _smartCamera = Object.FindObjectOfType<SmartMobileCamera>();

            var cam = Camera.main;
            Vector3 camFwd = Vector3.zero;
            bool usableMainCam = false;
            if (cam != null)
            {
                camFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                // A camera looking straight DOWN has no usable planar forward — that is a
                // degenerate basis, not a basis. Treated as absent (and reported).
                usableMainCam = camFwd.sqrMagnitude > 1e-6f;
            }

            kind = ResolveBasisKind(_smartCamera != null, usableMainCam);
            switch (kind)
            {
                case MovementBasis.SmartMobileCamera:
                    // PRESENCE, not value: CameraYaw legitimately RETURNS 0 in top-down/legacy
                    // framing (WO-387). Falling back on a zero VALUE would re-base the town stick.
                    return _smartCamera.CameraYaw;

                case MovementBasis.MainCamera:
                    return Mathf.Atan2(camFwd.x, camFwd.z) * Mathf.Rad2Deg;

                default:
                    // NEVER a silent identity (§12: no silent failures). With no basis source the
                    // stick is world-absolute — "forward" walks world +Z no matter where the player
                    // is looking — and that must be readable in one line of the capture instead of
                    // inferred from a printed 0.0. Self-throttled: FlowTrace.Fail has no throttle
                    // overload and this would otherwise fire every frame.
                    if (Time.realtimeSinceStartup >= _nextBasisFailAt)
                    {
                        _nextBasisFailAt = Time.realtimeSinceStartup + 5f;
                        DeNelle.Core.Diagnostics.FlowTrace.Fail("HeroLoco",
                            $"NO MOVEMENT BASIS in scene '{gameObject.scene.name}': there is no " +
                            "SmartMobileCamera and no Camera.main with a usable planar forward, so the " +
                            "camera-relative conversion degrades to WORLD-ABSOLUTE — the stick's " +
                            "'forward' is world +Z regardless of the view. Give the scene a camera (or a " +
                            "SmartMobileCamera) rather than letting the basis resolve to identity.");
                    }
                    return 0f;
            }
        }

        // ── OWNERSHIP SELF-REPORT (WO-968 S1 / WO-1016) ──────────────────────────────
        // The neutralize that decides ownership (DungeonController.EnsureSingleDungeonMover) logs
        // ONCE on apply and once on restore, so for the whole run in between "which mover is live"
        // was unobservable — and the owner's capture proves it CHANGED mid-session. Every FLIP now
        // names itself, so the question is answerable from the log alone, forever.
        private bool _lastOwnerForeign;
        private bool _hasLastOwner;

        private void ReportOwnershipFlip(bool foreignOwns)
        {
            if (_hasLastOwner && _lastOwnerForeign == foreignOwns) return;
            bool first = !_hasLastOwner;
            _hasLastOwner = true;
            _lastOwnerForeign = foreignOwns;
            string ownerWord = foreignOwns
                ? "FOREIGN CharacterController (this component writes NOTHING and publishes the MEASURED root speed to the animator)"
                : "HeroLocomotion (sole integrator; animator fed from Velocity)";
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroOwner",
                $"TRANSFORM OWNER {(first ? "=" : "->")} " +
                // NOTE: the ternary's branches are pre-built above the interpolation on purpose --
                // a multi-line concatenation INSIDE a non-verbatim $"..." hole is CS8967 under this
                // project's C# 9 language version (it compiles in a scratch pad on newer versions,
                // which is exactly how it reached the gate).
                $"{ownerWord} " +
                $"[scene='{gameObject.scene.name}' scriptedMove={(ScriptedMoveActive ? "ZEROED" : "off")} " +
                $"agent={(_agent == null ? "<none>" : (_agent.enabled ? "enabled" : "disabled"))}]");
        }

        // ── [Flow:HeroOwner] WHO OWNS THE HERO — 1 Hz heartbeat (WO-968, §12) ────────
        // Forged by the 2026-08-10 dungeon session (owner F8 2312 "Everything is wrong check
        // locomotion" + 2313 "No camera movement"). The captures contained BOTH of these, minutes
        // apart, in the SAME scene:
        //   • [Flow:HeroLoco] vel=0.00 while the hero's world position changed  (this component
        //     neutralized; DungeonHero's CharacterController was the mover), and
        //   • [Flow:HeroDrift] vel=(0.000,5.000) with live input                 (this component
        //     NOT neutralized and translating the root itself).
        // Nothing in the log could tell those two states apart, because the neutralize
        // (DungeonController.EnsureSingleDungeonMover -> SetScriptedMove(zero)) logs ONCE on apply
        // and never again, and the restore logs once on teardown. So a reader could not answer the
        // first question that matters — WHO MOVED THE HERO THIS FRAME — and every downstream
        // reading (animator Speed, gait forensics, camera basis) was un-attributable.
        //
        // This heartbeat answers it from data, every second, in every scene:
        //   ownerCC/ownerAgent  - which mover is live on this rig (the capability check
        //                         ForeignMoverOwnsTransform already uses, printed).
        //   scriptedMove        - the dungeon neutralize gate (see ScriptedMoveActive).
        //   velSelf             - THIS component's Velocity (what feeds ActorAnimator -> Speed).
        //   velRoot             - the MEASURED root speed (delta position / dt) — what actually
        //                         happened in the world, regardless of who wrote it.
        //   animSpeed           - the value the Animator is actually holding.
        // velRoot >> velSelf with animSpeed ~0 is the "moving but playing idle" defect, stated as
        // one line instead of inferred from three separate traces.
        //   basis               - the camera-relative movement basis + WHERE it came from. In a
        //                         dungeon there is no SmartMobileCamera, so CameraYaw is absent and
        //                         the basis silently degrades to world-absolute (proven: every
        //                         [Flow:HeroDrift] line in the dungeon capture reads camYaw=0.0).
        // LateUpdate (not Update) deliberately: Update has several early-returns (dialogue
        // suppression, auto-walk, the ground-snap `return`), and the frames that take them are
        // exactly the frames worth reporting. Whole body gated on FlowTrace.Enabled -> zero cost off.
        private Vector3 _ownerTraceLastPos;
        private bool    _ownerTraceHasLastPos;
        private float   _ownerTraceLastAt;

        // ── MEASURED ROOT SPEED — the mover-agnostic truth (WO-968 F1) ───────────────
        // Delta position / dt, sampled in LateUpdate so it is taken AFTER every Update-order mover
        // has written the transform (DungeonHero's CharacterController.Move included). That sample
        // point is what makes it mover-agnostic BY CONSTRUCTION rather than by a scene check.
        // ⚠ It is computed OUTSIDE the FlowTrace gate on purpose: the animator feed consumes it, and
        // a trace-gated measurement would collapse the feed to zero in a normal (untraced) build —
        // i.e. it would reproduce the very defect this fixes, invisibly, everywhere but a capture.
        private float _measuredRootSpeed;

        /// <summary>
        /// WO-1298: set by <see cref="WarpTo"/> so the next sample is REBASED on the landed pose
        /// rather than measured across the teleport. Without it every seam warp publishes a
        /// one-frame velRoot in the hundreds — a false ANIMATION-VELOCITY STALL and, with the
        /// mover-agnostic suppression feed, a one-frame run clip on arrival.
        /// </summary>
        private bool _rootMeasureRebase;

        /// <summary>
        /// The hero root's measured planar speed (m/s) last frame, regardless of which component
        /// wrote the transform. This — not <see cref="Velocity"/> — is what the animator is fed
        /// whenever a foreign mover owns the rig.
        /// </summary>
        public float MeasuredRootSpeed => _measuredRootSpeed;

        private void LateUpdate()
        {
            Vector3 pos = transform.position;
            float now = Time.time;
            if (_rootMeasureRebase)
            {
                // A warp landed this frame — do not measure across the jump (WO-1298).
                _rootMeasureRebase = false;
                _measuredRootSpeed = 0f;
            }
            else if (_ownerTraceHasLastPos)
            {
                float dt = Mathf.Max(1e-4f, now - _ownerTraceLastAt);
                Vector3 d = pos - _ownerTraceLastPos; d.y = 0f;
                _measuredRootSpeed = d.magnitude / dt;
            }
            _ownerTraceLastPos = pos;
            _ownerTraceLastAt = now;
            _ownerTraceHasLastPos = true;

            bool ccLive = ForeignMoverOwnsTransform();

            // Self-report: every ownership FLIP names itself, so "which mover is live" can never
            // again be unobservable for a whole run. Cheap — flips happen on dungeon enter/exit.
            ReportOwnershipFlip(ccLive);

            if (!DeNelle.Core.Diagnostics.FlowTrace.Enabled) return;

            float velRoot = _measuredRootSpeed;
            float animSpeed = (_animator != null && _hasSpeedParam) ? _animator.GetFloat(AnimSpeed) : float.NaN;
            string agentState = _agent == null ? "<none>"
                : (_agent.enabled ? (_agent.isOnNavMesh ? "on-mesh" : "off-mesh") : "disabled");
            float basisYaw = ResolveMovementBasisYaw(out MovementBasis basisKind);
            string basisSrc = basisKind == MovementBasis.SmartMobileCamera ? "SmartMobileCamera.CameraYaw"
                            : basisKind == MovementBasis.MainCamera ? "Camera.main(flattened)"
                            : "NONE(world-absolute)";
            var mainCam = Camera.main;

            DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroOwner", "owner", 1f,
                $"scene='{gameObject.scene.name}' " +
                // WO-968 S1: the ONE field that answers "who moved the hero this frame".
                $"owner={(ccLive ? "FOREIGN-CC" : "HeroLocomotion")} " +
                $"ownerCC={(ccLive ? "LIVE(foreign mover owns transform)" : "none")} " +
                $"ownerAgent={agentState} scriptedMove={(ScriptedMoveActive ? "ZEROED(dungeon neutralize ON)" : "off")} " +
                $"velSelf={Velocity.magnitude:F2} velRoot={velRoot:F2} " +
                // WO-968 F1: which of the two the animator is actually being fed.
                // WO-1298: suppression is a THIRD feed source. Printing "velSelf" while the
                // suppression branch publishes the measured root would make this heartbeat lie —
                // the exact failure mode that makes a capture unreadable.
                $"animFeed={(ccLive ? "velRoot(measured)" : InputSuppressed ? "velRoot(measured,suppressed)" : "velSelf")} " +
                $"animSpeed={animSpeed:F2} " +
                $"rootYaw={transform.eulerAngles.y:F1} " +
                $"basis={basisSrc} basisYaw={basisYaw:F1} " +
                // P0 2026-08-10 (owner F8 seq 2319, "No locomotioonj in town? Works in builder mode
                // not in here"): the WORLD CLOCK + the two input gates. Their absence is why a
                // three-hour capture could not be read. Every writer below Velocity scales by
                // Time.deltaTime, so at timeScale 0 the hero cannot move, cannot turn and cannot
                // animate WHILE INPUT IS STILL BEING READ — indistinguishable, in the old trace,
                // from a broken locomotion path. Camera orbit and build mode survive because both
                // run on the unscaled clock / their own input path, which is exactly the owner's
                // "works in builder mode" discriminator. Print it, always.
                $"timeScale={Time.timeScale:F2} dt={Time.deltaTime:F4} " +
                $"inputSuppressed={InputSuppressed} autoWalk={IsAutoWalking} " +
                $"mainCamYaw={(mainCam != null ? mainCam.transform.eulerAngles.y : -1f):F1} pos={pos:F2}");

            // The frozen world, called out as a FAILURE rather than left as a field to notice. This
            // is the SOFTLOCK shape: nothing is wrong with locomotion, the clock simply stopped and
            // no owner restarted it. Throttled to once a second while it persists.
            if (Time.timeScale <= 0f)
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroOwner", "frozen-clock", 1f,
                    $"WORLD CLOCK FROZEN: Time.timeScale={Time.timeScale:F2} in scene " +
                    $"'{gameObject.scene.name}'. The hero CANNOT move, turn or animate while this " +
                    "holds, however healthy the locomotion path is — every writer scales by " +
                    "Time.deltaTime. If no pause menu is on screen, a freeze owner (PauseController " +
                    "background auto-pause / BreakCaptureHarness F8 note) failed to restore it.");

            // The named defect, called out as a FAILURE the moment it is true rather than left for a
            // reader to spot: the world moved the hero but the animator is holding ~idle. Throttled
            // so a sustained stall reports once a second, not every frame.
            if (IsAnimationStalled(velRoot, animSpeed))
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroOwner", "anim-stall", 1f,
                    $"ANIMATION-VELOCITY STALL: root travelled {velRoot:F2} m/s but Animator Speed={animSpeed:F2} " +
                    $"(velSelf={Velocity.magnitude:F2}). The animator is being fed a DEAD value — a mover other " +
                    "than this component wrote the transform and nothing re-published its speed.");
        }

        // ── Manual NavMeshLink traversal (WO-468) ────────────────────────────────────
        // The castle<->OuterWorld seam is a NavMeshLink. An INPUT-driven agent (Move, not
        // SetDestination) never auto-crosses links, so we do it ourselves: when the hero
        // reaches one end of the seam while pushing toward the other, slide it across the
        // gap IN-WORLD and re-snap onto the far navmesh (a continuous walk, NOT a warp/fade).
        // Endpoints MUST match CastleHubBuilder.BuildSeamlessOuterWorldSeam's
        // NavLink_CastleToOuterWorld (start on castle navmesh, end on OuterWorld).
        // Castle<->OuterWorld seam endpoints — the original in-world SLIDE crossing (kept working).
        // WO-593 island raise: the CASTLE end rides the tunable base lift (PlayerPrefs "castle.liftY",
        // default 3 — the same key CastleHubBuilder authors the raised footprint from). A y=0 castle
        // end warped the agent 3m below the raised castle nav edge and stranded/mis-seated the hero
        // on slide-in; the OuterWorld end stays at terrain level (y=0). Properties (not static
        // readonly) so the tuned lift is read live, and the arrival Warp additionally snaps to the
        // baked navmesh (see TryTraverseSeamLink) so the landing derives from the mesh, not a constant.
        private static Vector3 SeamCastleEnd     => new Vector3(-4.37f, UnityEngine.PlayerPrefs.GetFloat("castle.liftY", 3f), -63f);
        private static Vector3 SeamOuterWorldEnd => new Vector3(-4.37f, 0f, -76f);
        private bool _crossingSeam;
        private Vector3 _seamTarget;
        private float _seamReengageAt;
        private bool _crossArmed = true;   // paired-crossing: fire on ENTER, re-arm when clear of all crossings

        /// <summary>True while it OWNS movement this frame (a crossing is starting or continuing);
        /// the caller must then skip the normal Move so we don't double-move.</summary>
        private bool TryTraverseSeamLink()
        {
            if (_crossingSeam)
            {
                Vector3 pos = transform.position;
                Vector3 flatTarget = new Vector3(_seamTarget.x, pos.y, _seamTarget.z);
                Vector3 to = flatTarget - pos; to.y = 0f;
                float stepLen = _moveSpeed * Time.deltaTime;
                if (to.magnitude <= stepLen + 0.05f)
                {
                    // Arrived: re-place the agent on the FAR navmesh (Warp re-binds the agent to
                    // whatever surface is under the point), then HAND CONTROL BACK to the agent.
                    // WO-593: snap the warp target to the BAKED mesh first (4m vertical tolerance
                    // covers the castle.liftY raise) so the landing derives from the navmesh, not
                    // the endpoint constant — a mis-tuned lift can't strand the hero off-mesh.
                    Vector3 warpTarget = _seamTarget;
                    if (UnityEngine.AI.NavMesh.SamplePosition(_seamTarget, out UnityEngine.AI.NavMeshHit seamHit, 4f, UnityEngine.AI.NavMesh.AllAreas))
                        warpTarget = seamHit.position;
                    if (_agent != null && _agent.enabled)
                    {
                        _agent.Warp(warpTarget);
                        _agent.updatePosition = true;   // agent drives the transform again
                        _agent.updateRotation = true;
                    }
                    else transform.position = warpTarget;
                    _crossingSeam = false;
                    _isTeleporting = false;
                    _seamReengageAt = Time.time + 1.0f;            // cooldown so we don't bounce back
                    Debug.Log($"[HeroLocomotion] seam-cross DONE -> {_seamTarget} (now on the far navmesh).");
                }
                else
                {
                    transform.position = pos + to.normalized * stepLen;
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(to.normalized), _rotationSpeed * Time.deltaTime);
                }
                return true;
            }

            Vector3 here = transform.position;

            // PAIRED-CROSSING WARP (HeroLinkCrossing, owner 2026-06-21): explicit id-paired portal. ENTER-TRIGGERED
            // (arm/disarm) — NO input-velocity requirement, so it fires whether the hero is driven by player input
            // OR auto-walk, and it's bot-testable. Fires once when the hero ENTERS an enterable crossing's radius;
            // re-arms only when clear of ALL crossings (so warping ONTO the partner never bounces back). Distance-
            // independent, no navmesh adjacency, no "closest" guessing. Checked BEFORE the legacy slide.
            bool nearAnyCrossing = false;
            for (int ci = 0; ci < HeroLinkCrossing.Registry.Count; ci++)
            {
                var c = HeroLinkCrossing.Registry[ci];
                if (c == null || !c.isActiveAndEnabled || !c.bidirectional) continue;
                if (HorizDist(here, c.transform.position) > c.enterRadius) continue;
                nearAnyCrossing = true;
                var partner = c.Partner();
                if (partner == null) continue;
                if (_crossArmed)
                {
                    WarpTo(partner.transform.position, transform.rotation);
                    _crossArmed = false;   // re-arms below once the hero leaves all crossing radii
                    Debug.Log($"[HeroLocomotion] crossing '{c.crossingId}' -> spawned at partner {partner.transform.position}.");
                    // WO-602: roll the passive paired-crossing FIRE up into the RuntimeSeam flow so a
                    // fleet run PROVES the walk-in return functions (the exit-only coverage hole).
                    DeNelle.Core.Diagnostics.FlowTrace.Step("RuntimeSeam",
                        $"crossing '{c.crossingId}' FIRED at {c.transform.position} — warped hero to {partner.transform.position}.");
                    return true;
                }
            }
            if (!nearAnyCrossing) _crossArmed = true;   // clear of every crossing -> ready to fire again

            // ── LEGACY castle<->OuterWorld SLIDE (needs input velocity + direction) ──
            if (Time.time < _seamReengageAt) return false;
            Vector3 v = Velocity; v.y = 0f;
            if (v.sqrMagnitude < 0.0001f) return false;
            // CORRIDOR GUARD (additive safety): the slide may ONLY engage inside the seam corridor
            // (x≈-4.37, z∈[-63,-76]) so no stray navmesh edge triggers a false slide.
            bool inCorridor = Mathf.Abs(here.x - SeamCastleEnd.x) <= 8f && here.z <= -55f && here.z >= -84f;
            if (!inCorridor) return false;

            // Near one endpoint AND pushing toward the other? Begin a crossing.
            if (HorizDist(here, SeamCastleEnd) < 2.5f && Vector3.Dot(v, SeamOuterWorldEnd - SeamCastleEnd) > 0f)
            { BeginSeamCross(SeamOuterWorldEnd, here, v); return true; }
            if (HorizDist(here, SeamOuterWorldEnd) < 2.5f && Vector3.Dot(v, SeamCastleEnd - SeamOuterWorldEnd) > 0f)
            { BeginSeamCross(SeamCastleEnd, here, v); return true; }
            return false;
        }

        private void BeginSeamCross(Vector3 target, Vector3 from, Vector3 vel)
        {
            _crossingSeam = true;
            _seamTarget = target;
            // SPAN latch — correct here (lowered frames later at the seam-cross DONE branch);
            // WO-1094's frame stamp is for WarpTo, whose "span" was one synchronous call.
            _isTeleporting = true;   // skip the off-mesh playable-bounds clamp while we cross the gap
            // RELEASE the agent's grip on the transform during the slide — otherwise updatePosition
            // clamps the hero back onto the castle navmesh every frame and it never crosses the gap
            // (the BEGIN-without-DONE bug). We drive transform.position ourselves, then Warp + re-arm.
            if (_agent != null && _agent.enabled)
            {
                _agent.updatePosition = false;
                _agent.updateRotation = false;
            }
            // Capture from-pos + velocity so a fleet/playtest can CONFIRM whether any unexpected
            // SECOND cross fires (owner's "sliding again"). A forward cross reads from≈(-4.37,-63);
            // any BEGIN logged from a different spot is the stray second seam to chase.
            string dir = (target == SeamOuterWorldEnd) ? "castle→outer" : "outer→castle";
            Debug.Log($"[HeroLocomotion] seam-cross BEGIN [{dir}] from {from} -> {target} vel={vel} " +
                      "(manual NavMeshLink traversal, in-world walk).");
        }

        private static float HorizDist(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f; return Vector3.Distance(a, b);
        }

        // WO-277: one frame of tutorial-driven movement toward _autoWalkTarget. A
        // condensed twin of the manual-input branch above — world-space heading
        // (no camera basis), eased velocity, NavMesh Move(), face-the-direction,
        // Speed animator drive — so the scripted tour reads identically to the
        // player walking. Eases to a stop inside the arrive radius so the hero
        // settles at each waypoint instead of jittering against the target.
        private void AutoWalkStep()
        {
            Vector3 to = _autoWalkTarget.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            // Ease the desired speed down as the hero nears the target so it stops
            // cleanly at the waypoint (mirrors the pet's arrival damping).
            Vector3 dir = dist > 0.0001f ? to / dist : Vector3.zero;
            float speedScale = Mathf.Clamp01(dist / Mathf.Max(0.01f, AutoWalkArriveRadius));
            float talentMoveNav = 1f;
            var abNav = GetComponent<HeroAbilities>();
            if (abNav != null)
                talentMoveNav = DeNelle.Village.Talents.HeroTalentModifiers.MoveSpeedMultiplier(abNav.HeroClass);
            Vector3 targetVelocity = dir * (_moveSpeed * speedScale * HeroHealth.MoveSpeedMultiplier * talentMoveNav);

            float maxStep = (targetVelocity.sqrMagnitude > Velocity.sqrMagnitude
                ? _accelMetresPerSec2
                : _decelMetresPerSec2) * Time.deltaTime;
            Velocity = Vector3.MoveTowards(Velocity, targetVelocity, maxStep);

            if (Velocity.sqrMagnitude > 0.0001f)
            {
                Vector3 step = Velocity * Time.deltaTime;
                if (_agent != null && _agent.isOnNavMesh)
                    _agent.Move(step);
                else
                    transform.position += step;

                Quaternion face = Quaternion.LookRotation(Velocity.normalized);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, face, _rotationSpeed * Time.deltaTime);
            }

            // Drive the walk animation off the actual speed (guarded — controller
            // may lack the param, per WO-174).
            ResolveAnimator();
            if (_animator != null && _hasSpeedParam)
                _animator.SetFloat(AnimSpeed, Velocity.magnitude);
        }

        // DEF-147: the rampart lift suspends the hero's NavMeshAgent and hand-carries
        // the hero by setting transform.position every frame. During that ride the
        // ground-snap MUST stay out of the way (snapping would yank the hero off the
        // slab / fight the lift). LiftPlatform.AnyCarrying() is true only while a lift
        // is actively carrying — both live in DeNelle.Village, so this is a direct,
        // reflection-free static read that does not change any lift behavior.
        private static bool IsLiftCarrying()
        {
            return LiftPlatform.AnyCarrying();
        }

        private static Vector2 ReadMoveInput()
        {
            // TEST SEAM (headless HERO_TURN_PROBE): when a scripted move is injected, return it
            // verbatim so the probe drives a deterministic "press forward" through the SAME
            // camera-relative → Velocity path the player uses. OFF the normal play path
            // (_scriptedMoveActive is only ever set by SetScriptedMove).
            if (_scriptedMoveActive) return _scriptedMove;

            Vector2 v = Vector2.zero;

            // New Input System path — Keyboard.current / Gamepad.current.
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            }

            var gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) v += stick;
                if (gp.dpad.up.isPressed) v.y += 1f;
                if (gp.dpad.down.isPressed) v.y -= 1f;
                if (gp.dpad.right.isPressed) v.x += 1f;
                if (gp.dpad.left.isPressed) v.x -= 1f;
            }

            // On-screen virtual joystick (the web/mobile build's touch movement — the
            // only nav input when there's no keyboard/gamepad). Added BEFORE the legacy
            // fallback so it counts as real input and the deadzoned fallback is skipped.
            v += VirtualJoystick.Move;

            // HUD-001 / Lean D-Pad tie-in (rich dark fantasy mobile HUD): loose reflection read of
            // VirtualDPadLean.Move so the rich HUD's thumb D-Pad drives locomotion without creating
            // a hard assembly reference from DeNelle.Village to DeNelle.HUD. When the HUD is present
            // in a battle/map scene, its normalized Vector2 feeds movement (multi-touch safe).
            v += ReadHudDpadMove();

            // Legacy Input Manager fallback — activeInputHandler=2 (Both)
            // means UnityEngine.Input is always available too. This is the
            // belt-and-braces path for builds where the new system's device
            // singletons aren't populated for some reason.
            if (v == Vector2.zero)
            {
                float h = UnityEngine.Input.GetAxisRaw("Horizontal");
                float ve = UnityEngine.Input.GetAxisRaw("Vertical");
                // DEADZONE (owner 2026-05-25 "camera drifts on its own while idle"):
                // a connected gamepad / VR / joystick resting slightly off-centre
                // feeds tiny constant values here. The new-input path is already
                // deadzoned, but this legacy fallback had none — so resting-stick
                // noise crept the hero forward with no player input and the camera
                // followed, reading as autonomous camera drift. Kill the noise.
                if (Mathf.Abs(h) < 0.25f) h = 0f;
                if (Mathf.Abs(ve) < 0.25f) ve = 0f;
                v.x += h;
                v.y += ve;
            }

            return v;
        }

        // HUD-001: loose (reflection) read of the rich dark-fantasy HUD's VirtualDPadLean.Move.
        // No compile dependency on DeNelle.HUD — Type.GetType + static property lookup.
        // When the HUD prefab/manager is dropped into a battle or map scene, its thumb D-Pad
        // (Lean Touch only) supplies normalized movement that is OR-ed with other inputs.
        // WO-1510: first-hit guard for the ReadHudDpadMove catch below. Static because the read
        // is static and the failure is process-wide (a missing/renamed HudMoveInput type).
        private static bool _hudDpadReadThrewTraced;

        private static Vector2 ReadHudDpadMove()
        {
            try
            {
                // P23 (HUD_OBSIDIAN §1.11): VirtualDPadLean is DELETED; the HUD kit's four
                // round controller buttons write DeNelle.HUD.Kit.HudMoveInput.Move instead.
                var t = System.Type.GetType("DeNelle.HUD.Kit.HudMoveInput, DeNelle.HUD");
                if (t == null) return Vector2.zero;
                var p = t.GetProperty("Move", BindingFlags.Public | BindingFlags.Static);
                if (p == null) return Vector2.zero;
                var val = p.GetValue(null);
                return val is Vector2 v ? v : Vector2.zero;
            }
            catch (System.Exception e)
            {
                // WO-1510: this catch was BARE. When it fired, the HUD thumb-stick contributed
                // nothing and the hero simply stopped moving — with not one line in the log,
                // which CLAUDE.md §12 forbids outright ("a catch that swallows without logging").
                // First hit only, but at WARN severity: this runs inside the movement read every
                // frame, so a per-frame line would bury the capture it is meant to explain —
                // while FlowTrace.Once emits at INFO (FlowTrace.cs:236 Sink.Info), which is the
                // wrong severity for a swallowed input failure. Hence the explicit guard.
                if (!_hudDpadReadThrewTraced)
                {
                    _hudDpadReadThrewTraced = true;
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero",
                        $"ReadHudDpadMove threw ({e.GetType().Name}: {e.Message}) — HUD D-Pad input reads as ZERO " +
                        "for the rest of this session; if the hero will not move from the on-screen pad, this is why.");
                }
                return Vector2.zero;
            }
        }
    }
}
