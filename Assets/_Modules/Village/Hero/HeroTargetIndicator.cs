// =============================================================================
// HeroTargetIndicator — a camera-facing reticle billboard over the hero's
// current target, with manual target cycling, so open-world combat is readable
// and controllable (see what you'll hit, and switch which enemy that is).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Pairs with the open-world targeting fix (Enemy auto-carries the EnemyDamageable
// IDamageable adapter). Each scan it gathers the alive Hostile IDamageables in
// range (the same detection the hero's melee/ability sweeps use) and parks a ring
// billboard over the current target's head.
//
// TARGET LOCK / CYCLE: by default the reticle tracks the NEAREST hostile. CYCLE to
// the next hostile in range with the right shoulder (gamepad) or RIGHT-CLICK (desktop,
// WO-497); that manual lock turns the reticle red and is fed to
// HeroAbilities.AimPointOverride so ranged spells aim at it. WO-497 also adds a DIRECT
// pick: TAP (mobile) or LEFT-click an enemy to lock THAT enemy (raycast on the Enemy
// layer), bypassing the AUTO forward-arc/LoS gates like a manual cycle; a tap on empty
// space clears the lock back to auto-nearest. WO-512 MANUAL LOCK ("pull one orc out"):
// MIDDLE mouse / center-click (desktop) or a TOUCH tap (mobile) on an enemy EngageLocks
// THAT foe for the FULL lock-on (camera frames it + Knight faces/strafes); center-clicking
// the already-locked foe TOGGLES off; clicking/tapping empty releases. RIGHT mouse stays
// BLOCK and LEFT mouse stays ATTACK — untouched. The lock auto-clears (back to nearest)
// when the target dies or leaves range. Self-installed on the hero by HeroControlEnsurer
// — no scene edit, no art asset (ring drawn at runtime).
//
// WO-1105 (owner rulings 2026-08-16) — AUTO-TARGET WITH A STICKY TAP OVERRIDE:
//   R1  a tap on a valid enemy REBINDS the lock and it holds until that foe dies, leaves
//       range/LoS, or another tap moves it (LateUpdate's clear allow-list is the contract);
//       the owner-picked Marker 2 Pointer Loop rides the current target via
//       CastingTelegraphVfx.TryBeginTargetMarker — reused verbatim, never a second marker system.
//   R2  AUTO acquisition engages only INSIDE the primary ability's authored range
//       (AutoEngageRange, read off AbilityDef.Range) so "it locked on" IS the range feedback.
//       Melee classes have no ranged primary, so they keep the 45 m acquire ring unchanged.
//
// The transparent-material setup mirrors PetDeployer.BuildSpriteBillboard, which
// is proven to render in WebGL builds (URP/Unlit transparent, double-sided).
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using DeNelle.Core.Combat;

namespace DeNelle.Village
{
    /// <summary>
    /// Shows a camera-facing reticle over the hero's current hostile target and
    /// lets the player cycle targets. Attach to the hero root (HeroControlEnsurer
    /// does this automatically).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroTargetIndicator : MonoBehaviour
    {
        // 2026-06-02: the registry (TargetManager) makes the lock range FREE to widen
        // (no physics-buffer overflow), so push it well past the open-world spawn ring
        // so roaming mobs in OuterWorld lock from a comfortable distance.
        // DEF-269 (owner playtest): 70m let the hero snipe-from-distance and made the
        // flee-and-spam exploit reach far. Dialed back to 45m (~36% shorter) so combat
        // is up-close and committed. Manual Tab-cycle still reaches anything in this range.
        [Tooltip("Radius (m) within which hostiles can be targeted.")]
        [SerializeField, Min(1f)] private float _acquireRange = 45f;

        // DEF-269: forward-arc facing gate for AUTO target acquisition. The hero may only
        // auto-acquire/auto-fire on a hostile roughly IN FRONT of them (Vector3.Dot of the
        // hero's forward vs the to-target direction > this cos threshold). 0.35 ≈ within a
        // ~70° half-angle (~140° total arc) of facing. Kills "flee and spam a target behind
        // you" — run away and the engagement ends. A MANUAL Tab/shoulder lock bypasses this
        // (deliberate player intent to hold a specific foe). Null-safe: degrades to "always
        // in front" if the hero transform has no meaningful forward.
        [Tooltip("Forward-arc dot threshold for AUTO targeting. 0.35 ≈ within ~70° of the " +
                 "hero's facing. Higher = tighter front cone; manual Tab-lock ignores this.")]
        [SerializeField, Range(-1f, 1f)] private float _facingDot = 0.35f;

        [Tooltip("Seconds between target re-scans (the reticle follows every frame).")]
        [SerializeField, Min(0.02f)] private float _scanInterval = 0.12f;

        // WO-449: line-of-sight gate. The hero could lock + attack THROUGH a wall because
        // acquisition was range + forward-arc only (no LoS). This mask names the blockers
        // that should occlude targeting — wall/structure/Default layers — NOT the Enemy or
        // Player layers (or the target's own collider would block itself). When unset
        // (value == 0) the LoS gate degrades OFF so a misconfigured mask never blanks ALL
        // targets (see HasLoS / the call-site degrade guards).
        [Tooltip("WO-449: layers that BLOCK line-of-sight to a target (walls/structures/" +
                 "Default). Do NOT include Enemy or Player. Leave empty to disable the LoS gate.")]
        [SerializeField] private LayerMask _losMask;

        [Tooltip("Height (m) above the target's position to float the reticle.")]
        [SerializeField] private float _headHeight = 2.2f;

        // DEF (owner playtest 2026-06-05): 1.6m read as a "giant circle" over mobs.
        // Dialed to 0.8m for a small, focused reticle that pinpoints the target
        // instead of haloing it. Runtime-attached (HeroControlEnsurer), so the code
        // default is what ships — no prefab override to chase.
        [Tooltip("Reticle world size (m).")]
        [SerializeField, Min(0.1f)] private float _size = 0.5f;  // 2026-06-06: smaller, focused target

        [Tooltip("Reticle tint when auto-tracking the nearest hostile.")]
        [SerializeField] private Color _autoTint = new Color(1f, 0.88f, 0.30f, 0.95f);

        [Tooltip("Reticle tint when the player has manually locked a target.")]
        [SerializeField] private Color _lockTint = new Color(1f, 0.32f, 0.28f, 0.98f);

        /// <summary>The hostile the hero is currently targeting (locked or nearest), or null.</summary>
        public IDamageable CurrentTarget { get; private set; }

        // ── WO-512 lock-owner API (THE single lock owner) ────────────────────────
        // The HUD (BattleHud9Zone) and BattleArena route ALL lock intent through these thin
        // wrappers over the existing _locked/CycleTarget/ClearLock internals, so there is one
        // owner of the manual lock + the per-frame AimPointOverride/LockedTarget writes (collapses
        // the old two-owner bug where the HUD wrote aim fields the indicator overwrote next frame).
        // These change NO behavior on their own — they just expose the existing lock as a clean API.

        /// <summary>True when a soft lock-on is engaged (the player/engage held a specific foe), false in
        /// free-look / auto-nearest. Mirror of "_locked != null engaged via the lock API".</summary>
        public bool LockEngaged { get; private set; }

        /// <summary>The locked enemy when <see cref="LockEngaged"/>, else null. Reads CurrentTarget so a
        /// dropped lock (target died / left range) reports null on the next LateUpdate.</summary>
        public IDamageable LockedEnemyTarget => LockEngaged ? CurrentTarget : null;

        /// <summary>
        /// Engage the soft lock-on onto a specific target (or the nearest candidate when null).
        /// Idempotent: engaging while already locked just re-points. Sets the same _locked the manual
        /// Tab/tap lock uses, so the reticle reds + abilities aim at it through the existing per-frame
        /// writes. No camera/facing change (slices 2-3).
        /// </summary>
        public void EngageLock(IDamageable target = null)
        {
            if (target == null)
            {
                // Pick the nearest hostile from a fresh scan (CurrentTarget may be stale this frame).
                RebuildCandidates();
                target = _candidates.Count > 0 ? _candidates[0] : (_locked ?? CurrentTarget);
            }
            if (target == null || !target.IsAlive) return;   // nothing to lock — stay auto-nearest
            _locked = target;
            LockEngaged = true;
            CurrentTarget = target;   // reflect immediately so reticle/HUD don't lag a frame
            var mb = target as MonoBehaviour;
            string nm = mb != null ? mb.gameObject.name.Replace("(Clone)", "").Trim() : "target";
            DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena", "LOCKON engage target='" + nm + "'.");
            DriveLockFace(target);   // WO-512 slice 3: auto-face + strafe around the locked enemy

            // WO-532: lock-on CONFIRM feedback (additive, gated). A UI confirm ping (the shared
            // CoreServices.Audio UI blip - the only one-shot exposed by IAudioService from this
            // assembly) + a brief reticle scale-punch so the lock READS as committed. Flag-off
            // skips all of it, so EngageLock stays byte-identical to today when LockOn is OFF.
            if (DeNelle.Core.FeatureFlags.LockOn)
            {
                DeNelle.Core.CoreServices.Audio?.PlayUiClick();
                PunchReticle(1.6f, 0.18f);   // pop UP then settle to base
                DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena", "LOCKON confirm feedback (ping+pop) fired.");
            }
        }

        /// <summary>
        /// Release the soft lock-on back to auto-nearest (free-look). Reuses ClearLock internals (drops
        /// _locked + the aim override + the pinned bar) and leaves the reticle in auto/gold; the next
        /// LateUpdate re-acquires the nearest hostile from scratch.
        /// </summary>
        public void ReleaseLock()
        {
            LockEngaged = false;
            ClearLock();   // drops _locked, aim override, prev-target bar; reticle reverts to auto/gold
            DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena", "LOCKON release -> free-look.");

            // WO-532: subtler UNLOCK feedback (gated). The reticle tint already reverts to auto/gold
            // in LateUpdate; add only a small, quick scale dip (no hard confirm ping) so the release
            // reads as softer than the lock. Flag-off skips it (byte-identical to today).
            if (DeNelle.Core.FeatureFlags.LockOn)
            {
                PunchReticle(0.7f, 0.14f);   // dip DOWN then return to base - subtle
                DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena", "LOCKON unlock feedback (subtle dip) fired.");
            }
        }

        /// <summary>
        /// Switch the locked target in the existing nearest-first cycle order (dir reserved for a future
        /// reverse step; current cycle is forward). Engages the lock if it wasn't already.
        /// </summary>
        public void CycleLock(int dir)
        {
            CycleTarget();          // reuse the existing nearest-first cycle ordering
            LockEngaged = _locked != null;
            var t = _locked;
            var mb = t as MonoBehaviour;
            string nm = mb != null ? mb.gameObject.name.Replace("(Clone)", "").Trim() : "none";
            DeNelle.Core.Diagnostics.FlowTrace.Step("BattleArena", "LOCKON switch -> '" + nm + "'.");
            DriveLockFace(t);   // WO-512 slice 3: re-point the lock-face at the newly cycled enemy
        }

        /// <summary>
        /// Clear any MANUAL target lock (revert to auto-nearest) and drop the current target +
        /// its aim override. Called by BattleArena.Resolve on a loss so the hero doesn't return
        /// to the open world still locked onto a dead/stale foe (which would keep aiming abilities
        /// at nothing). Safe to call any time; the next LateUpdate re-acquires from scratch.
        /// </summary>
        public void ClearLock()
        {
            _locked = null;
            LockEngaged = false;   // WO-512: any clear path (Resolve loss, tap-empty) also drops the lock-on flag
            CurrentTarget = null;
            if (_abilities == null) _abilities = GetComponent<HeroAbilities>();
            if (_abilities != null) { _abilities.AimPointOverride = null; _abilities.LockedTarget = null; }
            // Release the previous target's pinned HP bar so it isn't left revealed.
            SetBarTargeted(_prevTarget, false);
            _prevTarget = null;
            // WO-1734: a full clear drops the stickiness state too, or the next LateUpdate could
            // hold a target this call was explicitly asked to release.
            _autoPick = null;
            _autoPickAt = 0f;
            _tracedAutoPick = null;
            SetVisible(false);
            // WO-1105 R1: the marker is part of the target read — a full clear drops it too.
            if (_marker != null) CastingTelegraphVfx.EndTargetMarker(_marker, "lock cleared");
            _marker = null;
            _markerTarget = null;

            // WO-512 slice 3: a full clear also drops the hero's lock-face (back to free facing).
            DriveLockFace(null);
        }

        // ── WO-512 slice 3: lock-face / strafe drive ─────────────────────────────
        // Push the locked enemy's Transform to the sibling HeroLocomotion so the Knight auto-faces
        // it (and A/D strafe around it). Resolved lazily via GetComponent (same pattern as _abilities).
        // Behind FeatureFlags.LockOn: with the flag OFF this is a no-op, so the hero's facing path is
        // byte-identical to today. Passing null clears the lock-face.
        private void DriveLockFace(IDamageable target)
        {
            if (!DeNelle.Core.FeatureFlags.LockOn) return;
            if (_locomotion == null) _locomotion = GetComponent<HeroLocomotion>();
            if (_locomotion == null) return;

            Transform t = (target != null && target.IsAlive) ? (target as MonoBehaviour)?.transform : null;
            if (t != null) _locomotion.SetLockFace(t);
            else           _locomotion.ClearLockFace();
        }

        // ── WO-1105 R3 — RANGED CLASSES LOCK FACING TO THEIR TARGET ──────────────────────────
        //
        // Owner verbatim (2026-08-16): "can we add that when a ranger is targeting a enemy they
        // lock facing the enemy". An archer who shoots sideways reads as broken.
        //
        // THIS IS A WIRING JOB, NOT A NEW FACING SYSTEM. The slew already exists end to end
        // (HeroLocomotion.SetLockFace/ClearLockFace/ApplyLockFaceYaw, WO-512 slice 3) and is
        // already the sole rotation writer for the frames it owns. Two things kept it from ever
        // reaching the ranger, and this method fixes exactly those two:
        //   (1) it was only ever driven from the MANUAL lock paths (EngageLock / CycleLock / tap /
        //       ClearLock via DriveLockFace). The AUTO-acquired target -- which is what the ranger
        //       has almost all the time -- never drove it. Driving off CurrentTarget covers BOTH,
        //       because CurrentTarget is literally `_locked ?? NearestCandidate()` (see LateUpdate).
        //   (2) it was gated on FeatureFlags.LockOn, DEFAULT OFF. That flag guards the lock-on
        //       CAMERA experiment (mobile nausea); body facing is not that experiment, so this
        //       passes force:true to honor the slew with the flag off. Flag ON is unaffected --
        //       both paths set the same target on the same one authority.
        //
        // SCOPED BY THE DERIVED PREDICATE, NEVER A CLASS-NAME CHECK (WO-1105 section 3c). The test
        // is HeroAbilities.TryGetRangedPrimary against the MEASURED melee reach -- the same single
        // seam that already gates auto-acquire (AutoEngageRange) and the Focus refund rule. Melee
        // classes fail it and are byte-identical to today: this method never touches their facing.
        //
        // POSITION IS NEVER TOUCHED. Only the rotation branch changes inside HeroLocomotion; the
        // camera-relative MOVE vector is untouched, so strafing and backing away while facing the
        // foe -- the whole point of an archer -- fall out for free.
        private bool _rangedFaceEngaged;   // we (not the manual lock path) currently own the lock-face

        /// <summary>
        /// True when this hero's class has a RANGED primary, via the one derived seam
        /// <see cref="HeroAbilities.TryGetRangedPrimary"/> and the MEASURED melee reach from
        /// <see cref="PlayerAttackController.AttackRange"/> (never a metre literal, never a class
        /// name). Mirrors <see cref="AutoEngageRange"/>'s resolution exactly so acquisition and
        /// facing can never disagree about whether this hero shoots.
        /// </summary>
        private bool IsRangedClass()
        {
            if (_abilities == null) _abilities = GetComponent<HeroAbilities>();
            if (_abilities == null) return false;
            if (_attack == null) _attack = GetComponent<PlayerAttackController>();
            float meleeReach = _attack != null ? _attack.AttackRange : 0f;
            return _abilities.TryGetRangedPrimary(meleeReach, out _);
        }

        /// <summary>
        /// WO-1105 R3 — hold the hero's facing on <see cref="CurrentTarget"/> for a ranged class.
        /// Runs every LateUpdate AFTER CurrentTarget is resolved, so it follows the target through
        /// auto-acquire, a sticky tap override and a cycle without caring which produced it.
        /// Release is structural rather than a second rule: CurrentTarget goes null when the foe
        /// dies, leaves the acquire ring, breaks line-of-sight or is cleared, and this clears with
        /// it. The hero-death path is unaffected -- HeroHealth disables HeroLocomotion outright
        /// (so no yaw is applied) and also calls ClearLockFace; we additionally skip a disabled or
        /// absent locomotion here so a downed hero is never re-faced.
        /// </summary>
        private void DriveRangedFacing()
        {
            DeNelle.Core.Diagnostics.Guard.Try("Reticle", "ranged-facing", () =>
            {
                if (_locomotion == null) _locomotion = GetComponent<HeroLocomotion>();
                if (_locomotion == null || !_locomotion.enabled)
                {
                    _rangedFaceEngaged = false;
                    return;
                }

                if (!IsRangedClass())
                {
                    // Melee class: never our business. Only release a lock-face WE engaged -- a
                    // manual WO-512 lock on a melee hero is the other path's to own.
                    if (_rangedFaceEngaged)
                    {
                        _rangedFaceEngaged = false;
                        _locomotion.ClearLockFace();
                    }
                    return;
                }

                Transform t = null;
                if (CurrentTarget != null && CurrentTarget.IsAlive)
                    t = (CurrentTarget as MonoBehaviour)?.transform;

                if (t != null)
                {
                    // force:true -- see the block comment: body facing is not the lock-on camera flag.
                    _locomotion.SetLockFace(t, true);
                    _rangedFaceEngaged = true;
                }
                else if (_rangedFaceEngaged)
                {
                    _rangedFaceEngaged = false;
                    _locomotion.ClearLockFace();
                }
            });
        }

        private Transform _reticle;
        private Material _reticleMat;
        private Camera _cam;
        private HeroAbilities _abilities;
        private HeroLocomotion _locomotion;   // WO-512 slice 3: sibling for lock-face/strafe drive
        private float _nextScan;

        // ── WO-1105 R1/R2 — sticky tap override + range-gated auto-acquire + the target marker ──
        private PlayerAttackController _attack;   // sibling: the MEASURED melee reach for the R2 gate
        private GameObject _marker;               // live Marker 2 Pointer Loop on the current target
        private IDamageable _markerTarget;        // which target the live marker is riding
        private float _markerRefreshAt;           // re-spawn time (the marker carries an auto-destroy net)

        /// <summary>
        /// Seconds between marker re-spawns. <see cref="CastingTelegraphVfx.TryBeginTargetMarker"/> is
        /// reused VERBATIM (WO-1105: "do not spawn a second marker system"), and it arms a
        /// windup + 1 s auto-destroy safety net on every instance. A held target outlives any cast,
        /// so the marker is simply re-begun on that cadence — the safety net stays intact instead of
        /// being defeated by passing a fake multi-hour "wind-up".
        /// </summary>
        private const float MarkerRefreshSeconds = 6f;

        private IDamageable _locked;   // manual lock (null = auto-nearest)
        private IDamageable _prevTarget;  // DEF-206: last frame's CurrentTarget, to flip HP bars on change

        // ── WO-1734 — anti-oscillation stickiness on the AUTO pick ──────────────────────────────
        // The owner felt a straight SS_5 <-> SS_6 flip 30 ms apart between two adjacent wall
        // panels. These bound how eagerly the auto pick may move; they do NOT gate the unit-over-
        // wall priority, which crosses classes and is exempt (see HoldsCurrentAutoTarget).
        [Tooltip("WO-1734: a rival must be closer than the currently held auto target by MORE than " +
                 "this many metres to displace it. Deliberately small - it exists to stop two " +
                 "adjacent wall panels trading the reticle, not to make acquisition sluggish.")]
        [SerializeField, Min(0f)] private float _autoSwitchStickinessMeters = 1f;

        [Tooltip("WO-1734: an auto pick younger than this many seconds is never displaced by " +
                 "another target of the SAME kind. Kills the sub-100ms flip outright.")]
        [SerializeField, Min(0f)] private float _autoSwitchMinDwellSeconds = 0.35f;

        private IDamageable _autoPick;        // the auto target currently held by the hysteresis
        private float       _autoPickAt;      // Time.time the hysteresis last CHANGED _autoPick
        private IDamageable _tracedAutoPick;  // last pick NAMED in the trace, so it logs on change only
        private readonly List<IDamageable> _candidates = new List<IDamageable>();
        private readonly List<Enemy> _enemyBuf = new List<Enemy>(64);   // TargetManager scratch

        // ── WO-1047 §3 — WHO IS IN THE HOSTILE SET, BY NAME ──────────────────────────────────
        // The owner's report ("target attaches to this item", an untextured ORANGE cube with a
        // shield glyph and ground ring) is unactionable while the object is unidentified, and the
        // header's own rule — "the alive Hostile IDamageables" — says something is REGISTERING a
        // dungeon prop as hostile. Rather than theorise which prop, every admission into
        // _candidates is now NAMED once: hierarchy path, the concrete IDamageable implementor, the
        // GameObject that actually OWNS that implementor (GetComponentInParent can admit a CHILD
        // collider on behalf of an ancestor — that alone would explain a prop wearing a reticle),
        // the layer that let it through the mask (note Awake ORs "Structure" onto _enemyMask for
        // WO-853), the component list, the renderer/shader/colour (the orange-cube visual owner),
        // and the child names (the shield glyph + ground ring — same object, or a separate marker
        // sitting on top of it?).
        //
        // Volume control: one line per DISTINCT object per hero lifetime. Anything WITHOUT an
        // Enemy component is a FlowTrace.Warn (the suspects); real enemies log a compact Step so
        // the same capture also PROVES combat targeting still works (acceptance criterion 6).
        // ⛔ Do not strip this when the ticket closes (CLAUDE.md §12) — flag it off if it is ever
        // noisy; the registration seam it watches is exactly where the next prop will slip in.
        private readonly HashSet<int> _admissionDumped = new HashSet<int>();

        // 2026-06-02 targeting fix: the scan used mask ~0 into a 64-slot buffer, but the
        // village has ~2,900 colliders — within 32 m the buffer FILLED with walls/ground
        // and the enemy collider was crowded OUT (OverlapSphere result order is arbitrary),
        // so the reticle never acquired even with a foe right there. Attacks still landed in
        // waves because HeroAbilities sweeps the ENEMY layer only (no overflow). Mask to the
        // Enemy layer here too + a roomier buffer so only enemy colliders come back.
        //
        // WO-853 re-opens that mask by exactly ONE layer — Structure — so the reticle can
        // acquire a wall / gate / enemy turret, and raises the buffer 128 → 256 to pay for it:
        // a 45 m scan inside a walled base returns many wall panels, and OverlapSphereNonAlloc
        // truncates in arbitrary order, so the crowd-out failure above would return in kind.
        // The mask is deliberately NOT opened to Default — that is the layer the ground and
        // the ~2,900 hub props sit on, i.e. the exact 2026-06-02 failure this comment records.
        private static readonly Collider[] _hits = new Collider[256];
        private static Texture2D _ringTex;
        private int _enemyMask;

        // ── WO-1458 — the hero's OWN faction, named once. ────────────────────────────────────
        // The reticle used to ask "is this candidate's Faction != Hostile?" at THREE separate
        // sites (RebuildCandidates x2 + PickEnemyAtScreenPoint). That is the hand-copied
        // predicate CombatFactionRules.cs opens by forbidding in capitals, and it is the same
        // duplicated-state failure CLAUDE.md §2/§5/§8/§16 have each already paid for. All three
        // now call CombatFactionRules.MayAttack(HeroFaction, d), which asks the question the
        // right way round: not "is it flagged hostile?" but "may I, a Friendly actor, attack
        // this?" — so a structure of the PLAYER'S OWN faction can never enter the hostile set,
        // no matter which collider layer admitted it.
        //
        // Friendly is a CONSTANT here and that is verified, not assumed: HeroHealth.cs:1630
        // declares `CombatFaction IDamageableStructure.Faction => CombatFaction.Friendly;` and
        // CombatFaction (Core/Combat/IDamageable.cs:28) has exactly two members, so this is
        // behaviour-identical to the three lines it replaces while collapsing them to one rule.
        private const CombatFaction HeroFaction = CombatFaction.Friendly;

        private void Awake()
        {
            BuildReticle();
            _abilities = GetComponent<HeroAbilities>();
            _enemyMask = LayerMask.GetMask("Enemy");
            if (_enemyMask == 0) _enemyMask = ~0;   // layer undefined → fall back to all

            // WO-853: the reticle must also acquire STRUCTURES. Walls and gates stay on the
            // "Structure" layer (it is the tower line-of-sight blocker mask — relayering them
            // onto Enemy would make towers shoot through walls), so the only way the scan can
            // return one is to include that layer. Applied AFTER the fallback above so the
            // ~0 degrade is byte-identical; GetMask returns 0 for an undeclared layer, making
            // the OR a no-op. Safe because RebuildCandidates and PickEnemyAtScreenPoint reject
            // any Faction other than Hostile — the player's own perimeter reports Friendly.
            _enemyMask |= LayerMask.GetMask("Structure");

            // WO-449: ACTIVATE the LoS gate out-of-the-box. The castle wall geometry is built onto
            // the dedicated "Structure" layer (CastleWallsFromRecipe + CastleHubBuilder.BuildInnerWallRing),
            // so a linecast masked to it is blocked by walls but NOT by the hero/ground (Default) or
            // enemies (Enemy) — those stay off the mask so the hero/target never self-block. We only
            // seed the default when unset in the inspector; if "Structure" doesn't exist GetMask
            // returns 0 and HasLoS's degrade rule (value == 0 → LoS clear) keeps targeting safe.
            if (_losMask.value == 0) _losMask = LayerMask.GetMask("Structure");
        }

        private void OnDestroy()
        {
            if (_reticle != null) Destroy(_reticle.gameObject);
            // WO-1105 R1: never leave the target marker parented to a foe after the hero is gone.
            if (_marker != null) CastingTelegraphVfx.EndTargetMarker(_marker, "indicator destroyed");
            _marker = null;
            _markerTarget = null;
            // Don't leave a stale aim override on the abilities component.
            if (_abilities != null) { _abilities.AimPointOverride = null; _abilities.LockedTarget = null; }
            // DEF-206: release the last target's HP-bar flag so it isn't pinned on.
            SetBarTargeted(_prevTarget, false);
            _prevTarget = null;
        }

        private void Update()
        {
            // WO-1483: town frame path — auto-acquire scans the scene for targets every
            // frame even when there is nothing to acquire, which is an empty-town suspect.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "HeroTargetIndicator.Update", 4f, 1f);

            // WO-512 manual-lock (DESKTOP): MIDDLE mouse button (center-click) is the dedicated
            // "pull one enemy out" lock — pick the orc under the cursor and EngageLock(it) for the
            // FULL lock-on (camera frames it, Knight faces/strafes it). RIGHT mouse is BLOCK and
            // LEFT mouse is ATTACK, so center-click is the conflict-free desktop lock. Center-click
            // ON the already-locked foe TOGGLES the lock off (release to auto/free); center-click on
            // EMPTY space releases. Behind FeatureFlags.LockOn so flag-off = today (EngageLock /
            // DriveLockFace are themselves no-ops with the flag off, but we also skip the empty-space
            // release so the WO-497 paths stay byte-identical when the lock-on feature is off).
            if (DeNelle.Core.FeatureFlags.LockOn && MiddleClickThisFrame(out Vector2 midPos))
            {
                var pick = PickEnemyAtScreenPoint(midPos);
                if (pick != null)
                {
                    if (ReferenceEquals(pick, _locked) && LockEngaged)
                    {
                        ReleaseLock();   // toggle: center-click the locked foe again → release
                    }
                    else
                    {
                        string nm = (pick as MonoBehaviour) != null
                            ? (pick as MonoBehaviour).gameObject.name.Replace("(Clone)", "").Trim() : "target";
                        DeNelle.Core.Diagnostics.FlowTrace.Step(
                            "BattleArena", "LOCKON manual pick -> '" + nm + "' (middle-click).");
                        EngageLock(pick);   // pull THAT orc out: full lock-on (camera + face + strafe)
                    }
                }
                else
                {
                    // center-click on empty space → release the manual lock back to auto/free
                    DeNelle.Core.Diagnostics.FlowTrace.Step(
                        "BattleArena", "LOCKON manual release -> empty (middle-click).");
                    ReleaseLock();
                }
                return;
            }

            // WO-497: TAP (mobile) / LEFT-tap-on-enemy direct lock takes priority — a tap ON an
            // enemy collider locks THAT enemy directly (bypasses the AUTO forward-arc/LoS gates,
            // like a manual cycle); a tap on EMPTY clears back to auto-nearest.
            if (TapOrClickThisFrame(out Vector2 screenPos, out bool isTouch))
            {
                // WO-512 manual-lock (MOBILE): a TOUCH tap on an enemy gets the SAME full lock-on
                // as desktop center-click (camera frames it + Knight faces/strafes) by routing
                // through EngageLock — touch has no separate attack-vs-lock button so this is safe.
                // A DESKTOP LEFT-click stays WO-497 direct-lock-only (TryLockAtScreenPoint), because
                // LEFT-click is ALSO the primary attack (HeroAbilityInput slot Q) and must not engage
                // the camera lock-on. The touch full-lock is gated by FeatureFlags.LockOn.
                if (isTouch && DeNelle.Core.FeatureFlags.LockOn)
                {
                    var pick = PickEnemyAtScreenPoint(screenPos);
                    if (pick != null)
                    {
                        // Mobile toggle parity with desktop middle-click: tapping the ALREADY-LOCKED
                        // foe again releases the lock (toggle off), rather than re-engaging it.
                        if (ReferenceEquals(pick, _locked) && LockEngaged)
                        {
                            DeNelle.Core.Diagnostics.FlowTrace.Step(
                                "BattleArena", "LOCKON manual release -> tapped locked foe (tap toggle).");
                            ReleaseLock();
                            return;
                        }
                        string nm = (pick as MonoBehaviour) != null
                            ? (pick as MonoBehaviour).gameObject.name.Replace("(Clone)", "").Trim() : "target";
                        DeNelle.Core.Diagnostics.FlowTrace.Step(
                            "BattleArena", "LOCKON manual pick -> '" + nm + "' (tap).");
                        EngageLock(pick);   // full lock-on for touch
                        return;
                    }
                    // tap on empty space → release the manual lock (revert to auto-nearest)
                    DeNelle.Core.Diagnostics.FlowTrace.Step(
                        "BattleArena", "LOCKON manual release -> empty (tap).");
                    ReleaseLock();
                    return;
                }

                if (TryLockAtScreenPoint(screenPos)) { DriveLockFace(_locked); return; }   // hit an enemy → direct lock done
                // tap on empty space → clear manual lock (revert to auto-nearest)
                _locked = null;
                LockEngaged = false;   // WO-512: keep the lock-on flag honest on a tap-to-clear
                DriveLockFace(null);   // WO-512 slice 3: tap-to-clear also drops lock-face
                return;
            }

            // WO-497: RIGHT-CLICK (desktop) cycles/switches the locked target, additive to the
            // existing Tab/right-shoulder cycle (CyclePressed). WO-512: a manual cycle engages lock.
            if (CyclePressed()) { CycleTarget(); LockEngaged = _locked != null; DriveLockFace(_locked); }
        }

        private void LateUpdate()
        {
            // WO-1483: town frame path — the candidate REBUILD scan lives here, not in Update,
            // so measuring Update alone would have left the scan unattributed.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "HeroTargetIndicator.LateUpdate", 4f, 1f);

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + _scanInterval;
                RebuildCandidates();
            }

            // Drop a manual lock that died or wandered out of range.
            //
            // WO-1105 R1 — THIS IS THE WHOLE STICKINESS CONTRACT, AND IT IS AN ALLOW-LIST OF THREE.
            // A tap-set _locked is cleared in EXACTLY three places and no others: here (the target
            // DIED or left the acquire ring / lost line-of-sight, so it is no longer a candidate),
            // on another tap (Update -> TryLockAtScreenPoint / EngageLock re-points it, or a tap on
            // empty space releases it), and ClearLock/ReleaseLock. Nothing re-derives it per frame:
            // the line below is `_locked ?? NearestCandidate()`, so while _locked lives the auto
            // pick is never consulted. That is what makes "it silently snapped back to the tank
            // while I was killing the healer" structurally impossible rather than merely unlikely.
            if (_locked != null && (!_locked.IsAlive || !_candidates.Contains(_locked)))
            {
                var lostMb = _locked as MonoBehaviour;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                    "TARGET LOST '" + (lostMb != null ? lostMb.gameObject.name.Replace("(Clone)", "").Trim() : "target")
                    + "' - " + (!_locked.IsAlive ? "died" : "left range / line-of-sight")
                    + " -> lock released back to auto-acquire.");
                _locked = null;
                // WO-512 slice 3: the locked foe is gone — release lock-face so the Knight stops
                // facing a dead/absent target and the LookRotation(Velocity) writer resumes.
                if (LockEngaged) { LockEngaged = false; DriveLockFace(null); }
            }

            CurrentTarget = _locked ?? NearestCandidate();

            // AUTO-LOCK DRIVES AIM: feed the CURRENT target (auto-nearest OR manual lock)
            // to the ability aim so what the reticle shows is exactly what your spells hit.
            // (Previously only a manual Tab-lock overrode aim; auto-nearest fell back to
            // HeroAbilities' own pick, which could disagree with the reticle.)
            if (_abilities == null) _abilities = GetComponent<HeroAbilities>();
            if (_abilities != null)
            {
                _abilities.AimPointOverride = CurrentTarget != null ? (Vector3?)CurrentTarget.WorldPosition : null;
                // Hand the ability the EXACT locked foe so single-target hits damage what
                // the ring shows (not whatever an OverlapSphere happens to find).
                _abilities.LockedTarget = CurrentTarget;
            }

            // DEF-206: keep the CURRENT target's floating HP bar revealed (the
            // "engage to reveal" rule). When the target changes, release the old
            // one (it lingers a few seconds then fades) and reveal the new one.
            if (!ReferenceEquals(CurrentTarget, _prevTarget))
            {
                SetBarTargeted(_prevTarget, false);
                SetBarTargeted(CurrentTarget, true);
                _prevTarget = CurrentTarget;
                // WO-1105 R1: name every acquisition change, with WHICH mechanism owns it, so a
                // capture answers "did the lock move, and who moved it" without a theory.
                var newMb = CurrentTarget as MonoBehaviour;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                    "TARGET " + (CurrentTarget == null ? "CLEARED" :
                        (_locked != null ? "OVERRIDDEN (player tap/cycle lock)" : "ACQUIRED (auto)"))
                    + " -> '" + (newMb != null ? newMb.gameObject.name.Replace("(Clone)", "").Trim() : "none")
                    + "' autoEngageRange=" + AutoEngageRange().ToString("0.##") + "m.");
            }

            // WO-1105 R1 marker: the owner-picked Marker 2 Pointer Loop rides the CURRENT target
            // (auto or tap-locked) so "what am I about to shoot" is never colour-only.
            UpdateTargetMarker();

            // WO-1105 R3: an archer faces what she is shooting (owner 2026-08-16).
            DriveRangedFacing();

            if (CurrentTarget == null || !CurrentTarget.IsAlive)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = ResolveCamera();
            if (_reticleMat != null)
            {
                Color want = _locked != null ? _lockTint : _autoTint;
                if (_reticleMat.HasProperty("_BaseColor")) _reticleMat.SetColor("_BaseColor", want);
                _reticleMat.color = want;
            }

            // The reticle quad is a STANDALONE scene object (not parented to the hero), so a
            // scene change that CARRIES the hero (HeroControlEnsurer DDOL across a Single-load
            // seam, e.g. OuterWorld->Village2) destroys the quad while THIS component survives on
            // the carried hero — leaving _reticle a destroyed reference. Setting .position on it
            // then NRE-spams every frame once a target is in range. Rebuild on demand.
            if (_reticle == null) BuildReticle();
            if (_reticle == null) { SetVisible(false); return; }

            Vector3 p = CurrentTarget.WorldPosition + Vector3.up * _headHeight;
            _reticle.position = p;
            if (_cam != null)
            {
                // FULL billboard (face the camera on all axes). A yaw-only quad reads
                // edge-on — i.e. INVISIBLE — from the close 3D third-person cam looking
                // down. The quad's visible face is -Z, so point +Z away from the camera.
                Vector3 away = p - _cam.transform.position;
                if (away.sqrMagnitude > 0.0001f)
                    _reticle.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }

            // WO-512 slice 1 INSTRUMENT (throttled, ~1/sec): prove WHERE the reticle sits.
            // Log the world pos, the resolved camera, whether it's on-screen, and a cheap
            // screen-space size estimate (project two points _size apart). The next run
            // tells us off-screen vs tiny vs behind-camera WITHOUT changing geometry.
            string camName = _cam != null ? _cam.name : "NULL";
            string onScreen = "n/a";
            string ssize = "n/a";
            if (_cam != null)
            {
                Vector3 sp = _cam.WorldToScreenPoint(p);
                bool inFront = sp.z > 0f;
                bool inView = inFront && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
                onScreen = inView ? "yes" : (inFront ? "off-edge" : "behind");
                Vector3 spEdge = _cam.WorldToScreenPoint(p + _cam.transform.right * (_size * 0.5f));
                float px = Mathf.Abs(spEdge.x - sp.x) * 2f;
                ssize = px.ToString("0") + "px";
            }
            DeNelle.Core.Diagnostics.FlowTrace.Throttle("Reticle", "reticle-show", 1f,
                "show pos=(" + p.x.ToString("0.0") + "," + p.y.ToString("0.0") + "," + p.z.ToString("0.0")
                + ") cam='" + camName + "' onScreen=" + onScreen + " screenSize=" + ssize
                + " color=" + (_locked != null ? "LOCK-red" : "auto-gold") + ".");
        }

        // Robust camera lookup: Camera.main only finds a "MainCamera"-tagged camera, which
        // isn't guaranteed — fall back to the SmartMobileCamera rig, then any active camera,
        // so the reticle billboard is never left un-oriented (edge-on quad = invisible).
        private static Camera ResolveCamera()
        {
            var c = Camera.main;
            if (c != null) return c;
            var smc = SmartMobileCamera.Instance;
            if (smc != null) { var cc = smc.GetComponent<Camera>(); if (cc != null) return cc; }
            return Object.FindAnyObjectByType<Camera>();
        }

        // ── Targeting ─────────────────────────────────────────────────────────

        private bool CyclePressed()
        {
            // Mobile-first: the keyboard Tab target-cycle is REMOVED. Target cycling is
            // reached by the gamepad right shoulder (below); on touch the reticle auto-
            // tracks the nearest hostile.
            var gp = Gamepad.current;
            if (gp != null && gp.rightShoulder.wasPressedThisFrame) return true;

            // WO-497: RIGHT-CLICK (desktop) ALSO cycles/switches the locked target — additive
            // to the gamepad shoulder. (A right-click does not carry a useful "what was under
            // the cursor" intent here; it just advances the cycle, matching Tab/shoulder.)
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) return true;
            return false;
        }

        // WO-497: was there a TAP (touch) or LEFT mouse click this frame? Returns the screen
        // point so the caller can raycast it into the world for a direct enemy lock. Touch wins
        // over mouse when both report (a touch device that also exposes a synthetic mouse).
        // WO-512 manual-lock: also reports whether the input was a TOUCH (vs a desktop LEFT-click)
        // so the caller can route ONLY touch through full EngageLock(...) — a desktop LEFT-click
        // is ALSO the primary attack (HeroAbilityInput slot Q), so it must keep its WO-497
        // direct-lock-only behaviour and never engage the camera lock-on.
        private static bool TapOrClickThisFrame(out Vector2 screenPos, out bool isTouch)
        {
            screenPos = default;
            isTouch = false;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame)
            {
                screenPos = ts.primaryTouch.position.ReadValue();
                isTouch = true;
                return true;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPos = mouse.position.ReadValue();
                return true;
            }
            return false;
        }

        // WO-512 manual-lock: was the MIDDLE mouse button (button 2 / center-click) pressed this
        // frame? This is the dedicated DESKTOP manual lock-on input — owner asked for "right OR
        // center click", and RIGHT mouse is the Knight's BLOCK input (PlayerAttackController.
        // UpdateBlock), so center-click is the conflict-free choice. New Input System to match the
        // rest of this file (Mouse.current / Gamepad.current / Touchscreen.current). Returns the
        // screen point so the caller raycasts it into the Enemy layer to pick THAT orc.
        private static bool MiddleClickThisFrame(out Vector2 screenPos)
        {
            screenPos = default;
            var mouse = Mouse.current;
            if (mouse != null && mouse.middleButton.wasPressedThisFrame)
            {
                screenPos = mouse.position.ReadValue();
                return true;
            }
            return false;
        }

        // WO-497: raycast a screen point into the world and, if it strikes an alive Hostile
        // IDamageable, set it as the MANUAL lock directly (bypassing the AUTO forward-ARC gate,
        // like a Tab/shoulder cycle — but LoS IS enforced now, owner 2026-06-27: no through-wall
        // lock). Returns true when an enemy was locked. The raycast uses the Enemy layer mask so
        // terrain between the camera and the foe doesn't eat the pick; range-bounded (no off-screen snipe).
        private bool TryLockAtScreenPoint(Vector2 screenPos)
        {
            var d = PickEnemyAtScreenPoint(screenPos);
            if (d == null) return false;

            // WO-1105 R1: THIS is the tap override the owner asked for, and it works on TOUCH as
            // well as mouse — TapOrClickThisFrame reads Touchscreen.current.primaryTouch first, so a
            // finger on the Seeker screen reaches here even with ff.lockon OFF (that flag gates the
            // lock-on CAMERA, which this WO must not flip). Once set, _locked survives until the
            // foe dies, leaves range/LoS, or another tap moves it — see LateUpdate's three-place
            // clear allow-list. It is NOT re-derived per frame, so it cannot snap back to the auto
            // pick mid-fight (the owner's tank-vs-healer failure).
            var mb = d as MonoBehaviour;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                "TARGET OVERRIDE (tap/click) -> '"
                + (mb != null ? mb.gameObject.name.Replace("(Clone)", "").Trim() : "target")
                + "' - lock is STICKY until it dies, leaves range, or another tap moves it.");
            _locked = d;   // direct manual lock — bypasses AUTO arc; LoS enforced in PickEnemyAtScreenPoint
            LockEngaged = true;   // WO-512: a direct tap-lock engages the lock-on flag
            return true;
        }

        // WO-512 manual-lock: raycast a screen point into the Enemy layer and return the alive
        // Hostile IDamageable struck (or null). Shared by the WO-497 tap/LEFT-click direct lock and
        // the new MIDDLE-click manual lock so they pick identically; the caller decides what to do
        // with the hit (direct _locked set vs full EngageLock vs toggle-release).
        private IDamageable PickEnemyAtScreenPoint(Vector2 screenPos)
        {
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = ResolveCamera();
            if (_cam == null) return null;

            Ray ray = _cam.ScreenPointToRay(screenPos);
            // Pick on the Enemy layer only — a wide pick range so a tap anywhere on the foe's
            // body locks it; bound it generously past the acquire range for camera distance.
            float maxDist = _acquireRange * 2f + 50f;
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDist, _enemyMask, QueryTriggerInteraction.Collide))
                return null;

            var d = hit.collider != null ? hit.collider.GetComponentInParent<IDamageable>() : null;
            // WO-1458: MayAttack folds null + IsAlive + friend-or-foe into the ONE predicate.
            if (!CombatFactionRules.MayAttack(HeroFaction, d)) return null;
            // Owner 2026-06-27: a MANUAL lock must also respect line-of-sight — no locking a foe
            // THROUGH a wall (the flagged bug). Previously WO-497/512 deliberately bypassed HasLoS;
            // now the same Structure-layer linecast that gates the AUTO scan gates the manual pick.
            if (!HasLoS(d)) return null;
            // WO-1047: the tap/click pick is a THIRD route into the hostile set (it does not go
            // through RebuildCandidates), so it gets the same naming. If the owner taps the cube,
            // this is the line that identifies it.
            TraceAdmission(d, hit.collider != null ? hit.collider.gameObject : null,
                           "screen-point raycast pick (tap/click/middle-click)");
            return d;
        }

        private void CycleTarget()
        {
            // WO-449: RebuildCandidates() now applies the additive LoS gate, so the cycle
            // list only contains hostiles with a clear line-of-sight — a manual Tab/shoulder
            // cycle can no longer lock a target through a wall either.
            RebuildCandidates();

            // WO-532: CAMERA-VISIBILITY gate on CYCLE. The shoulder/right-click cycle may only
            // land on a target that is currently ON-SCREEN (and in front of the camera) - you
            // can't cycle-lock a foe across the arena or behind you. The explicit raycast pick
            // (middle-click / tap) is inherently on-screen, so this gates ONLY the cycle path.
            // Range + LoS (RebuildCandidates) still apply. Flag-off = no filtering (byte-identical).
            if (DeNelle.Core.FeatureFlags.LockOn) FilterCandidatesToOnScreen();

            if (_candidates.Count == 0) { _locked = null; return; }
            int idx = _locked != null ? _candidates.IndexOf(_locked) : -1;
            idx = (idx + 1) % _candidates.Count;
            _locked = _candidates[idx];
        }

        // WO-532: drop every candidate that is NOT on-screen (off the viewport rect or behind the
        // camera) so a CYCLE can only land on a visible enemy. Uses the robustly-resolved camera
        // (Camera.main can be untagged here - ResolveCamera mirrors the rest of this file) and the
        // standard viewport test: 0<=x<=1, 0<=y<=1, z>0. Degrades safe: no camera => no filtering.
        private void FilterCandidatesToOnScreen()
        {
            var cam = (_cam != null && _cam.isActiveAndEnabled) ? _cam : ResolveCamera();
            _cam = cam;
            if (cam == null) return;   // can't determine visibility => don't filter

            for (int i = _candidates.Count - 1; i >= 0; i--)
            {
                var d = _candidates[i];
                // Unity-aware: an interface ref misses a destroyed MonoBehaviour with plain `== null`
                // (scene-unloaded crate); prune it so the WorldPosition deref below can't NRE.
                if (d == null || (d as UnityEngine.Object) == null) { _candidates.RemoveAt(i); continue; }
                Vector3 vp = cam.WorldToViewportPoint(d.WorldPosition);
                bool onScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                if (!onScreen) _candidates.RemoveAt(i);
            }
        }

        // WO-532: brief reticle "pop" punch for lock/unlock confirm. Animates ONLY localScale (the
        // billboard's per-frame writer touches position/rotation, never scale, so this never fights
        // LateUpdate), settling back to the base _size. Gated by FeatureFlags.LockOn so flag-off is a
        // no-op. Cheap: one coroutine, unscaled time (still reads when combat slows time).
        private Coroutine _popCo;

        private void PunchReticle(float startScaleMul, float duration)
        {
            if (!DeNelle.Core.FeatureFlags.LockOn) return;
            if (_reticle == null) return;
            if (_popCo != null) StopCoroutine(_popCo);
            _popCo = StartCoroutine(PopRoutine(startScaleMul, duration));
        }

        private IEnumerator PopRoutine(float startScaleMul, float duration)
        {
            float baseS = _size;
            float startS = baseS * startScaleMul;
            float t = 0f;
            while (t < duration)
            {
                if (_reticle == null) { _popCo = null; yield break; }
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(duration > 0f ? t / duration : 1f);
                float s = Mathf.Lerp(startS, baseS, u);   // punch from start scale back to base
                _reticle.localScale = new Vector3(s, s, s);
                yield return null;
            }
            if (_reticle != null) _reticle.localScale = new Vector3(baseS, baseS, baseS);
            _popCo = null;
        }

        private void RebuildCandidates()
        {
            _candidates.Clear();

            // UNION of TWO sources so a nearby enemy is found whether or not it is in the
            // registry (2026-06-02: a mob that spawned BEFORE an editor hot-reload never
            // ran the new OnEnable→Register, so it's missing from the registry; the old
            // code early-returned on the registry and skipped the sweep that would catch
            // it). Both are cheap with the Enemy-layer mask, so always run both + dedup.

            // Source 1 — the cross-scene registry (overflow-proof, catches registered mobs).
            var tm = TargetManager.Instance;
            if (tm != null)
            {
                tm.CollectInRange(transform.position, _acquireRange, _enemyBuf);
                for (int i = 0; i < _enemyBuf.Count; i++)
                {
                    var e = _enemyBuf[i];
                    if (e == null) continue;
                    // Robust lookup (expert 2026-06-02): root first, then parent chain.
                    var d = e.GetComponent<IDamageable>();
                    if (d == null) d = e.GetComponentInParent<IDamageable>();
                    // WO-1458: one predicate, not a fourth hand-copy of the faction comparison.
                    if (!CombatFactionRules.MayAttack(HeroFaction, d)) continue;
                    if (!HasLoS(d)) continue;   // WO-449: additive LoS gate — no targeting through walls
                    if (!_candidates.Contains(d)) _candidates.Add(d);
                    TraceAdmission(d, e != null ? e.gameObject : null, "TargetManager registry");
                }
            }

            // Source 2 — Enemy-layer physics sweep (catches anything not registered, plus
            // non-Enemy hostiles). Few enemy colliders in range, so the 128 buffer is safe.
            int n = Physics.OverlapSphereNonAlloc(
                transform.position, _acquireRange, _hits, _enemyMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i];
                if (c == null) continue;
                var d = c.GetComponentInParent<IDamageable>();
                // WO-1458: the sweep's mask is deliberately WIDE (Enemy|Structure), so this is
                // the line that keeps the player's own perimeter out of the hostile set. Layer
                // decides what physics RETURNS; faction decides what combat ACCEPTS.
                if (!CombatFactionRules.MayAttack(HeroFaction, d)) continue;
                if (!HasLoS(d)) continue;   // WO-449: additive LoS gate — no targeting through walls
                if (!_candidates.Contains(d)) _candidates.Add(d);
                // WO-1047: pass the STRUCK COLLIDER's GameObject, not the damageable's — when the
                // two differ, a child collider admitted an ancestor (or a prop is parented under a
                // damageable root), and that difference is the finding.
                TraceAdmission(d, c.gameObject, "physics sweep (mask=Enemy|Structure)");
            }

            // Stable nearest-first order so Tab cycles outward predictably.
            Vector3 me = transform.position;
            _candidates.Sort((a, b) =>
                (a.WorldPosition - me).sqrMagnitude.CompareTo((b.WorldPosition - me).sqrMagnitude));
        }

        // ── WO-1047 §3 — NAME THE OBJECT (§12: instrument first, conclude from data) ──────────
        /// <summary>
        /// Log, ONCE per distinct object, exactly what just entered the HOSTILE candidate set and
        /// by which route. A prop wearing the reticle is unfixable while it is "an orange cube";
        /// this turns one run into a name, a spawner and a registration path.
        /// <para>
        /// The dump deliberately answers all three of WO-1047 §3's questions in ONE line:
        /// (1) WHAT it is — hierarchy path, layer, tag, position, component list;
        /// (2) HOW it got hostile — the concrete IDamageable implementor, the GameObject that owns
        /// it (vs the collider that admitted it), the faction it reports and the source that found
        /// it; (3) WHAT IT LOOKS LIKE — mesh, shader, base colour (the orange cube's visual owner)
        /// and the child list (does the shield glyph / ground ring belong to this object, or is it
        /// a separate marker parked on top of it?).
        /// </para>
        /// ⚠ This is a PURE OBSERVER. It changes no filter and no candidate — a guessed filter
        /// patch here is exactly what §4 of the ticket forbids; the fix belongs at the registration
        /// source, once the data says which source that is.
        /// </summary>
        private void TraceAdmission(IDamageable d, GameObject admittedVia, string source)
        {
            var mb = d as MonoBehaviour;
            if (mb == null) return;                          // interface on a non-Unity object: nothing to name
            var go = mb.gameObject;
            if (go == null) return;
            if (!_admissionDumped.Add(go.GetInstanceID())) return;   // once per object, ever

            bool isEnemy = go.GetComponentInParent<Enemy>() != null;
            string implementor = d.GetType().FullName;
            string viaName = admittedVia != null ? DescribeHierarchy(admittedVia.transform) : "(unknown)";
            bool viaDiffers = admittedVia != null && admittedVia != go;

            if (isEnemy)
            {
                // Compact line: proves real enemies still reach the reticle after any hostile-set
                // change (WO-1047 acceptance criterion 6 — do not only prove the prop stopped).
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                    $"[hostile-admit] ENEMY '{go.name}' impl={implementor} via {source}"
                    + (viaDiffers ? $" (collider '{viaName}' != damageable owner)" : ""));
                return;
            }

            // ── WO-1458 — CLASSIFY BY FACTION, NOT BY "does it carry an Enemy component?" ──────
            // The WO-1047 classifier above asked only `GetComponentInParent<Enemy>() != null`, so
            // EVERY structure fell through to the Warn below — including the ones a raid exists to
            // let you break. Measured, not inferred: 320 `[hostile-admit] NON-ENEMY ADMITTED` lines
            // in one device session, every one of them a `RaidBase_.../Wall_Outer_*` (WallSegment,
            // Faction => SceneOwnership.IsEnemyOwned ? Hostile : Friendly, WallSegment.cs:219) or
            // `RaidSpire/Crown` (RaidSpire.Faction => Hostile, RaidSpire.cs:217). Those are the
            // raid's walls and its win-condition objective. Admitting them is the FEATURE.
            //
            // ⛔ AND THEIR LAYERS ARE NOT THE BUG EITHER — do not "fix" them:
            //   * `RaidSpire/Crown` sits on `Enemy` because RaidSpire.EnsureHittable (:168-208)
            //     PUTS it there on purpose; the hero's melee/ability sweep is masked to `Enemy`,
            //     so relayering the crown makes the raid objective unkillable.
            //   * `Wall_Outer_*` sits on `Structure` (RaidBaseGenerator.cs:1018-1019) and reaches
            //     this sweep because Awake ORs `Structure` onto _enemyMask for WO-853; that layer
            //     is also every tower's line-of-sight blocker mask.
            // The layer answers "what does physics return?". Only faction answers "whose is it?".
            //
            // So a hostile structure now logs the same compact Step a real Enemy does — proof the
            // reticle still acquires raid geometry — and the Warn below is reserved for what it was
            // always FOR: something reaching the hostile set that the hero may NOT attack.
            if (!CombatFactionRules.IsFriendlyFire(HeroFaction, d))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                    $"[hostile-admit] HOSTILE STRUCTURE '{go.name}' impl={implementor} "
                    + $"faction={d.Faction} via {source}"
                    + (viaDiffers ? $" (collider '{viaName}' != damageable owner)" : ""));
                return;
            }

            // The hero's OWN faction, admitted to the HOSTILE set. This is the WO-1047 suspect and
            // after WO-1458 it is also an INVARIANT BREACH: every admit site above now gates on
            // CombatFactionRules.MayAttack(HeroFaction, d), which cannot pass a Friendly target.
            // A line here therefore means a FOURTH, uncontrolled route into _candidates exists —
            // dump everything about it.
            string mesh = "(no MeshFilter)";
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) mesh = mf.sharedMesh.name;

            string shader = "(no renderer)";
            string colour = "(n/a)";
            var r = go.GetComponentInChildren<Renderer>();
            if (r != null && r.sharedMaterial != null)
            {
                var m = r.sharedMaterial;
                shader = m.shader != null ? m.shader.name : "(null shader)";
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                        : (m.HasProperty("_Color") ? m.color : Color.clear);
                colour = $"({c.r:F2},{c.g:F2},{c.b:F2})";
            }

            DeNelle.Core.Diagnostics.FlowTrace.Warn("Reticle",
                "[hostile-admit] NON-ENEMY ADMITTED TO THE HOSTILE TARGET SET (WO-1047). "
                + $"path='{DescribeHierarchy(go.transform)}' impl={implementor} "
                + $"faction={d.Faction} alive={d.IsAlive} pos={go.transform.position:F2} "
                + $"layer='{LayerMask.LayerToName(go.layer)}' tag='{go.tag}' source={source} "
                + (viaDiffers
                    ? $"ADMITTED-VIA='{viaName}' layer='{LayerMask.LayerToName(admittedVia.layer)}' "
                      + "(a CHILD/other collider admitted this object - the reticle rides the ancestor) "
                    : "admittedVia=self ")
                + $"components=[{DescribeComponents(go)}] children=[{DescribeChildren(go.transform)}] "
                + $"mesh='{mesh}' shader='{shader}' baseColor={colour}. "
                + "^ THIS is the object the reticle can lock onto. Fix its registration at the "
                + "SOURCE (its IDamageable/faction or its layer), never with a filter here.");
        }

        private static string DescribeHierarchy(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new System.Text.StringBuilder(t.name);
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 12)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        private static string DescribeComponents(GameObject go)
        {
            if (go == null) return "(null)";
            var comps = go.GetComponents<Component>();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < comps.Length; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(comps[i] != null ? comps[i].GetType().Name : "(missing script)");
            }
            return sb.Length == 0 ? "(none)" : sb.ToString();
        }

        private static string DescribeChildren(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new System.Text.StringBuilder();
            int n = Mathf.Min(t.childCount, 8);
            for (int i = 0; i < n; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t.GetChild(i) != null ? t.GetChild(i).name : "(null)");
            }
            if (t.childCount > n) sb.Append(", +").Append(t.childCount - n).Append(" more");
            return sb.Length == 0 ? "(none)" : sb.ToString();
        }

        // ── WO-1105 R2 — RANGE MUST BE LEGIBLE (owner's SECOND shape, no new art) ────────────
        // Owner verbatim: "either we add a distance ring for archer range, or only after we get
        // within range does it auto target." The second shape is implemented: for a class with a
        // RANGED primary, auto-acquire engages ONLY once the foe is inside that ability's AUTHORED
        // range — so "it locked on" IS the range feedback, and the marker appearing is the moment
        // the shot becomes possible.
        //
        // THE RADIUS IS READ OFF AbilityDef.Range, NEVER A METRE LITERAL (WO-1035 units bug).
        // Melee classes are UNCHANGED: no ranged primary -> this returns the 45 m _acquireRange the
        // reticle has always used, so the Knight's acquisition is byte-identical.
        // A MANUAL lock is deliberately NOT gated by this — a tap is explicit player intent (R1),
        // and it still releases the moment the foe leaves the acquire ring or dies.
        private float AutoEngageRange()
        {
            if (_abilities == null) _abilities = GetComponent<HeroAbilities>();
            if (_abilities == null) return _acquireRange;
            if (_attack == null) _attack = GetComponent<PlayerAttackController>();
            // The melee reach is the MEASURED discriminator TryGetRangedPrimary compares against;
            // with no attack controller present there is no swing to outreach, so pass 0 and let
            // the authored range decide alone.
            float meleeReach = _attack != null ? _attack.AttackRange : 0f;
            float r = _abilities.RangedPrimaryRange(meleeReach);
            return r > 0f ? Mathf.Min(r, _acquireRange) : _acquireRange;
        }

        // ── WO-1105 R1 — the "it is targeted" read, reusing the owner's pick VERBATIM ────────
        // CastingTelegraphVfx.TryBeginTargetMarker is the seam that already puts the owner-picked
        // Hovl "Marker 2 Pointer Loop" on a cast target, unit-parented, with an auto-destroy safety
        // net. It is reused as-is — no second marker system, no new prefab, no owner VFX pick made
        // by a CLI. The marker carries the meaning by SHAPE and position, never by colour.
        private void UpdateTargetMarker()
        {
            bool wantMarker = CurrentTarget != null
                              && (CurrentTarget as UnityEngine.Object) != null
                              && CurrentTarget.IsAlive;
            var targetMb = wantMarker ? CurrentTarget as MonoBehaviour : null;
            if (targetMb == null) wantMarker = false;

            if (!wantMarker)
            {
                if (_marker != null) CastingTelegraphVfx.EndTargetMarker(_marker, "target lost");
                _marker = null;
                _markerTarget = null;
                return;
            }

            if (!ReferenceEquals(CurrentTarget, _markerTarget))
            {
                if (_marker != null) CastingTelegraphVfx.EndTargetMarker(_marker, "target changed");
                _marker = null;
                _markerTarget = CurrentTarget;
                _markerRefreshAt = 0f;   // spawn on this frame
            }

            // The refresh clock advances even when the spawn returns null (telegraph flag off /
            // mirror prefab absent). Without that, a missing prefab would drive a Resources.Load
            // EVERY FRAME for as long as anything is targeted — a silent frame-rate sink behind a
            // feature that is merely unavailable.
            if (Time.time < _markerRefreshAt) return;
            if (_marker != null) CastingTelegraphVfx.EndTargetMarker(_marker, "marker refresh");
            _marker = CastingTelegraphVfx.TryBeginTargetMarker(
                this, targetMb.transform, null,
                _locked != null ? "target lock (player pick)" : "target lock (auto)",
                MarkerRefreshSeconds);
            _markerTarget = CurrentTarget;
            _markerRefreshAt = Time.time + MarkerRefreshSeconds;
        }

        /// <summary>
        /// WO-1734 — the hero gets the SAME rule the owner already gave the troops: any acquirable
        /// hostile UNIT outranks a wall, always. Walls stay targetable when no unit is available.
        /// </summary>
        /// <remarks>
        /// Owner ruling 2026-09-15, restating her WO-1730 ruling for the hero: *"should never
        /// default to target wall, should always default to aggresive targets nearby first asnd
        /// then only then wall"*.
        ///
        /// ⭐ THE PRIORITY IS ONE GATE IN FRONT OF THE EXISTING SELECTION, DELIBERATELY — the
        /// nearest-wins body below is NOT restructured, exactly as WO-1719 did it for the troop
        /// side (`RaidAssaultAi.SelectFocusBreach`, which explains the same reasoning at
        /// `RaidAssaultAi.cs:230-241`). The fallback is then literally the code it always was,
        /// which is why a regression can still pin "nearest wins among equals" against an
        /// unchanged body.
        ///
        /// WHY IT WAS BROKEN: acquisition was pure nearest-wins (the sort at RebuildCandidates
        /// compares squared distance only, with no type term) while enemy-owned walls are admitted
        /// as legitimate hostiles — WallSegment is Hostile when the scene is enemy-owned, and Awake
        /// ORs `Structure` onto `_enemyMask`. So a ~4 m wall panel 2 m away outranked a mob at 5 m.
        /// Captured on device (build 370139): five
        /// `[Flow:Reticle] TARGET ACQUIRED (auto) -> 'Wall_Outer_*'` inside 626 ms.
        ///
        /// CLASSIFICATION mirrors the troop side verbatim — `is IDamageableStructure`
        /// (`TroopController.IsHostileStructure`). Walls, gates, towers and the raid spire
        /// dual-implement it; `EnemyDamageable` and `DragonBoss` do not. ⚠ That means a garrison
        /// unit also outranks the raid SPIRE, which is the troop rule applied consistently — the
        /// spire stays acquirable the moment no unit stands.
        ///
        /// The second half is the OSCILLATION (a separate defect, also owner-felt): a straight
        /// SS_5 ⇄ SS_6 flip 30 ms apart. That is handled by <see cref="HoldsCurrentAutoTarget"/>,
        /// AFTER the priority gate, so stickiness can never delay a unit beating a wall.
        /// </remarks>
        private IDamageable NearestCandidate()
        {
            // ── THE GATE: a unit first, if any unit at all is acquirable this frame. ──
            var pick = NearestCandidateOfClass(unitsOnly: true);
            bool unitWon = pick != null;
            if (!unitWon) pick = NearestCandidateOfClass(unitsOnly: false);

            pick = ApplyAutoSwitchHysteresis(pick);

            if (!ReferenceEquals(pick, _tracedAutoPick))
            {
                _tracedAutoPick = pick;
                var pmb = pick as MonoBehaviour;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                    "AUTO PICK '" + (pmb != null ? pmb.gameObject.name.Replace("(Clone)", "").Trim() : "none")
                    + "' WHY=" + (pick == null
                        ? "no acquirable hostile in the engage arc"
                        : unitWon
                            ? "unit-over-wall (WO-1734 priority gate: a hostile UNIT was acquirable)"
                            : "nearest (no unit acquirable; "
                              + (pick is IDamageableStructure ? "structure" : "unit") + " wins on distance)")
                    + ".");
            }

            return pick;
        }

        /// <summary>
        /// Pure, side-effect-free stickiness rule used by the auto-acquire hysteresis and pinned by
        /// regression coverage. TRUE = keep the target already held.
        /// </summary>
        /// <remarks>
        /// WO-1734: sized deliberately SMALL — it exists to kill a 30 ms SS_5 ⇄ SS_6 flip between
        /// two adjacent wall panels, not to make the reticle sluggish. Two independent holds:
        /// a minimum dwell (a pick younger than <paramref name="minDwellSeconds"/> is never
        /// displaced) and a distance margin (a rival must be closer by more than
        /// <paramref name="stickinessMeters"/> to win).
        ///
        /// ⛔ <paramref name="sameClass"/> IS THE WHOLE SAFETY PROPERTY. Stickiness applies ONLY
        /// between two targets of the same kind, so the WO-1734 priority gate can never be delayed
        /// by it: a unit displacing a held wall crosses classes and is exempt.
        /// </remarks>
        public static bool HoldsCurrentAutoTarget(
            bool currentStillAcquirable, bool sameClass, float currentDistance, float candidateDistance,
            float heldSeconds, float stickinessMeters, float minDwellSeconds)
        {
            if (!currentStillAcquirable) return false;
            if (!sameClass) return false;
            if (heldSeconds < minDwellSeconds) return true;
            return candidateDistance > currentDistance - Mathf.Max(0f, stickinessMeters);
        }

        /// <summary>
        /// WO-1734 — would <paramref name="cand"/> still be ACQUIRED by
        /// <see cref="NearestCandidateOfClass"/> right now? The stickiness may only hold a target
        /// the selection itself would still accept.
        /// </summary>
        /// <remarks>
        /// ⛔ `_candidates.Contains(...)` ALONE IS NOT THE TEST, and getting that wrong is a real
        /// bug rather than a nicety. `RebuildCandidates` applies only `_acquireRange` + faction +
        /// line-of-sight; the selection body applies TWO more gates on top — the WO-1105 R2
        /// `AutoEngageRange()` ring and the DEF-269 forward arc. Holding on `Contains` alone means:
        /// acquire wall A, turn 180 degrees, and A is still "held" while standing BEHIND the hero,
        /// which is precisely the spam-at-your-back that DEF-269's own header says this method
        /// returns null to prevent — and it would bite hardest while turning inside a raid gate,
        /// the exact moment this ticket exists to fix. For a ranged class it would also hold a foe
        /// inside `_acquireRange` but outside the authored engage range.
        /// </remarks>
        private bool IsStillAutoAcquirable(IDamageable cand)
        {
            if (cand == null || (cand as UnityEngine.Object) == null || !cand.IsAlive) return false;
            if (!_candidates.Contains(cand)) return false;

            Vector3 me = transform.position;
            float engage = AutoEngageRange();
            if ((cand.WorldPosition - me).sqrMagnitude > engage * engage) return false;

            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude <= 0.0001f) return true;   // degenerate facing -> don't gate (as the body does)
            fwd.Normalize();

            Vector3 to = cand.WorldPosition - me;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return true;     // on top of the hero -> in range
            return Vector3.Dot(fwd, to.normalized) >= _facingDot;
        }

        // Apply HoldsCurrentAutoTarget to the frame's raw pick. The held target must still be a
        // target the SELECTION would accept (IsStillAutoAcquirable — range + engage ring + arc), so
        // a dead, departed, out-of-engage or behind-the-hero foe can never be held by stickiness.
        private IDamageable ApplyAutoSwitchHysteresis(IDamageable pick)
        {
            if (ReferenceEquals(pick, _autoPick))
                return pick;

            if (_autoPick != null && pick != null)
            {
                bool held = IsStillAutoAcquirable(_autoPick);
                bool sameClass = (_autoPick is IDamageableStructure) == (pick is IDamageableStructure);
                Vector3 me = transform.position;
                if (held && HoldsCurrentAutoTarget(
                        true, sameClass,
                        Vector3.Distance(_autoPick.WorldPosition, me),
                        Vector3.Distance(pick.WorldPosition, me),
                        Time.time - _autoPickAt,
                        _autoSwitchStickinessMeters, _autoSwitchMinDwellSeconds))
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("Reticle", "auto-switch-held", 2f,
                        "AUTO SWITCH HELD - keeping the current target; the rival is not closer by more than "
                        + _autoSwitchStickinessMeters.ToString("0.##") + "m and the hold is "
                        + (Time.time - _autoPickAt).ToString("0.##") + "s old (WO-1734 anti-oscillation).");
                    return _autoPick;
                }
            }

            _autoPick = pick;
            _autoPickAt = Time.time;
            return pick;
        }

        // DEF-269: the AUTO target is the nearest candidate the hero is FACING. _candidates
        // is sorted nearest-first, so walk it and return the first one inside the forward arc.
        // Returns null when every hostile in range is behind the hero — so running away ends
        // the engagement instead of letting the hero spam attacks at a target at their back.
        // (Manual Tab-locks are applied in Update/LateUpdate before this is ever consulted.)
        //
        // WO-1734: `unitsOnly` is the ONLY change to this body — one `continue`, below. With it
        // false the method is byte-for-byte the DEF-269 / WO-1105 R2 rule it has always been.
        private IDamageable NearestCandidateOfClass(bool unitsOnly)
        {
            Vector3 me = transform.position;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            // Degenerate forward (hero hasn't oriented yet) → don't gate, fall back to nearest.
            bool gate = fwd.sqrMagnitude > 0.0001f;
            if (gate) fwd.Normalize();

            // WO-1105 R2: auto-acquire only inside the primary's authored range (identity at
            // _acquireRange for every melee class — see AutoEngageRange).
            float engage = AutoEngageRange();
            float engageSqr = engage * engage;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var cand = _candidates[i];
                // Unity-aware null check: an IDamageable held via the INTERFACE static type bypasses
                // Unity's overloaded ==, so `cand == null` MISSES a destroyed MonoBehaviour. A candidate
                // killed by a SCENE UNLOAD (e.g. an Outpost crate when single-loading into the Dungeon)
                // keeps _broken=false, so IsAlive lies 'true' and WorldPosition (transform) then throws
                // NRE (owner F8 2026-06-30, ×8/frame in the Dungeon). Cast to UnityEngine.Object so the
                // == overload catches the dead object, and skip it.
                if (cand == null || (cand as UnityEngine.Object) == null || !cand.IsAlive) continue;
                // WO-1734: the unit-first pass. Same classifier as the troop side
                // (TroopController.IsHostileStructure) — walls/gates/towers/spire dual-implement
                // IDamageableStructure; pure hostile units do not.
                if (unitsOnly && cand is IDamageableStructure) continue;
                // R2 range gate. _candidates is sorted nearest-first, so the first one out of
                // engage range means every remaining one is too — stop, do not auto-acquire.
                if ((cand.WorldPosition - me).sqrMagnitude > engageSqr)
                {
                    // WO-1734: the unit-first pass returning null is NORMAL (the all-classes pass
                    // runs next), so it must not print "auto-acquire HELD" — that line means the
                    // hero acquired nothing at all, and a false one would send the next triage
                    // hunting a range bug that is not there.
                    if (unitsOnly) return null;
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("Reticle", "auto-out-of-range", 2f,
                        "auto-acquire HELD: nearest hostile is "
                        + Vector3.Distance(cand.WorldPosition, me).ToString("0.##")
                        + "m away, outside the primary's authored engage range "
                        + engage.ToString("0.##") + "m (WO-1105 R2 - closing the distance IS the cue).");
                    return null;
                }
                if (!gate) return cand;   // can't determine facing → nearest wins
                Vector3 to = cand.WorldPosition - me;
                to.y = 0f;
                if (to.sqrMagnitude < 0.0001f) return cand;   // on top of the hero → in range
                if (Vector3.Dot(fwd, to.normalized) >= _facingDot) return cand;
            }
            return null;
        }

        // WO-449: true when the hero has a clear line-of-sight to the target (no wall/
        // structure between them) — so the hero can't lock + cast THROUGH a wall. We
        // linecast from an eye point on the hero to a TORSO point on the target (not feet)
        // so the ground/terrain doesn't false-block a target on a slope or a curb.
        // DEGRADE: an unset mask (value == 0) means "no blockers configured" → treat LoS as
        // always clear (fall back to range + arc only) so a misconfig never blanks targeting.
        private bool HasLoS(IDamageable target)
        {
            if (target == null) return false;
            if (_losMask.value == 0) return true;   // degrade: LoS gate disabled
            Vector3 eye = transform.position + Vector3.up * 1.4f;
            Vector3 torso = target.WorldPosition + Vector3.up * 1.0f;
            // Clear when the linecast hits NOTHING between eye and torso.
            if (!Physics.Linecast(eye, torso, out RaycastHit hit, _losMask, QueryTriggerInteraction.Ignore))
                return true;
            // WO-853 SELF-HIT EXEMPTION — also clear when the first thing the cast hits IS the
            // target. A wall or gate lives ON the "Structure" layer this cast is masked to, and
            // the torso point sits inside that wall's own collider, so the cast always reports
            // the wall as occluding itself and the reticle could never acquire or lock one.
            // Nothing can occlude a wall from itself.
            // NOT a targeting-through-walls hole: Physics.Linecast reports the CLOSEST hit, so
            // "the first blocker is the target" proves nothing else stands in front of it. A
            // wall behind a DIFFERENT wall reports that other wall here and stays blocked.
            // Byte-identical for every target NOT on a _losMask layer (all enemies): its
            // collider can never be the reported hit, so this collapses to the old expression.
            return ResolvesTo(hit.collider, target);
        }

        /// <summary>
        /// True when <paramref name="col"/> belongs to <paramref name="target"/> — resolved the
        /// same way RebuildCandidates resolves a collider to its damageable
        /// (<c>GetComponentInParent</c>), so a hit on a child collider still counts as the target.
        /// </summary>
        private static bool ResolvesTo(Collider col, IDamageable target)
        {
            if (col == null) return false;
            return ReferenceEquals(col.GetComponentInParent<IDamageable>(), target);
        }

        private void SetVisible(bool on)
        {
            if (_reticle != null && _reticle.gameObject.activeSelf != on)
            {
                _reticle.gameObject.SetActive(on);
                // WO-512 slice 1 INSTRUMENT: log every visibility flip + which target it
                // was shown/hidden for, so we can correlate "lock fired" with "reticle on".
                var tmb = CurrentTarget as MonoBehaviour;
                string tn = tmb != null ? tmb.gameObject.name.Replace("(Clone)", "").Trim() : "none";
                DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                    "SetVisible on=" + on + " target='" + tn + "'.");
            }
        }

        // DEF-206: flag the target's floating HP bar as the player's current target
        // so it stays revealed while locked (and lingers when released). The
        // IDamageable impl (EnemyDamageable) is a MonoBehaviour on the Enemy root,
        // which is where FloatingHealthBar lives — resolve it via .gameObject.
        private static void SetBarTargeted(IDamageable target, bool targeted)
        {
            var mb = target as MonoBehaviour;
            if (mb == null) return;
            FloatingHealthBar.SetTargetedOn(mb.gameObject, targeted);
        }

        // ── Visual build (no art asset; runtime ring texture + transparent quad) ──

        private void BuildReticle()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "HeroTargetReticle";
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);
            quad.transform.localScale = new Vector3(_size, _size, _size);

            var mr = quad.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                                ?? Shader.Find("Sprites/Default")
                                ?? Shader.Find("Unlit/Transparent");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    Texture2D ring = RingTexture();
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", ring);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", ring);
                    mat.mainTexture = ring;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", _autoTint);
                    mat.color = _autoTint;

                    // Transparent blend — mirrors PetDeployer.BuildSpriteBillboard (WebGL-safe).
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
                    if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (mat.HasProperty("_ZWrite"))   mat.SetInt("_ZWrite", 0);
                    if (mat.HasProperty("_Cull"))     mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    mr.sharedMaterial = mat;
                    _reticleMat = mr.material;   // instance we recolour per lock state
                }
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            _reticle = quad.transform;
            _reticle.gameObject.SetActive(false);

            // WO-512 slice 1 INSTRUMENT (no geometry change): prove what the reticle
            // actually built. Log the shader, whether the material/renderer resolved,
            // the layer, and the world size — so the NEXT run shows if the ring renders
            // at all (vs null material / wrong layer / edge-on / tiny). ASCII only.
            string shaderName = (_reticleMat != null && _reticleMat.shader != null)
                ? _reticleMat.shader.name : "NULL";
            var mrCheck = quad.GetComponent<MeshRenderer>();
            DeNelle.Core.Diagnostics.FlowTrace.Step("Reticle",
                "BuildReticle shader='" + shaderName + "' matNull=" + (_reticleMat == null)
                + " rendererNull=" + (mrCheck == null) + " layer=" + quad.layer
                + " size=" + _size.ToString("0.00") + ".");
        }

        /// <summary>A cached 64×64 soft ring (target-bracket) texture, drawn once.</summary>
        private static Texture2D RingTexture()
        {
            if (_ringTex != null) return _ringTex;

            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (S - 1) * 0.5f;
            // Small "target" reticle: a THIN outer ring + a solid centre pip — reads as a
            // crosshair/target that pinpoints the foe, not a big filled halo.
            const float ringOuter = 30f, ringInner = 27f;  // thin rim band
            const float dotRadius = 6f;                     // centre pip
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.InverseLerp(ringOuter, ringOuter - 2f, r) * Mathf.InverseLerp(ringInner, ringInner + 2f, r);
                    float dot  = Mathf.InverseLerp(dotRadius, dotRadius - 2f, r);
                    float a = Mathf.Clamp01(Mathf.Max(ring, dot));
                    t.SetPixel(x, y, a > 0f ? new Color(1f, 1f, 1f, a) : clear);
                }
            }
            t.Apply();
            _ringTex = t;
            return t;
        }
    }
}
