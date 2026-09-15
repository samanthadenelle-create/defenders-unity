// =============================================================================
// PlayerAttackController — DEF-47: Player attack with perfect-hit timing window.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Gives player attacks timing depth — a "Perfect Hit" window on each swing that
// deals bonus damage and triggers dramatic feedback. Rewards skilled play without
// requiring complex input chains.
//
// ADAPTION NOTES (from spec against this codebase):
//   • IDamageable.TakeDamage(float, DamageElement) — correct signature here.
//   • DamageNumberSpawner.Spawn() / SpawnLabel() — replaces FloatingTextSpawner
//     which doesn't exist; DamageNumberSpawner is the project's equivalent.
//   • CombatFeedbackManager.Hit(worldPos, damage) — static helper, no Instance call.
//   • No HitIntensity enum in this project; bonus damage is the sole feedback signal.
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;   // WO-449 §12: FlowTrace on the melee LoS gate
using DeNelle.Core.UI;            // P23: CombatText — pooled/capped/deduped stamps (§1.8)

namespace DeNelle.Village
{
    /// <summary>
    /// Handles player melee attacks with a timing-based perfect-hit window.
    /// Attach to the Hero root alongside <see cref="HeroLocomotion"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAttackController : MonoBehaviour
    {
        [Header("Base Attack")]
        // P1-6 (2026-08-02): raised 30 -> 52.5 as part of making the perfect-hit mechanic REAL.
        // 52.5 is not a buff — it is the damage the hero has ALWAYS dealt. The old perfect-hit
        // check compared elapsed time against a FIXED 0.13 s coroutine delay with no player input
        // anywhere in the loop, so `isPerfect` was unconditionally true above ~20 FPS and the
        // 1.75x multiplier applied to EVERY swing (30 x 1.75 = 52.5). The stated 30 was a lie, and
        // on a frame hitch > 50 ms the player silently lost 43% damage for no reason they could
        // see. The number is now honest and the multiplier is EARNED. See ResolveAttack.
        [Tooltip("Flat damage per hit before any talent multipliers.")]
        [SerializeField] private float _baseDamage = 52.5f;

        [Tooltip("Radius of the OverlapSphere damage check around the hero (fallback when " +
                 "the equipped weapon sets no reach).")]
        [SerializeField, Min(0.1f)] private float _attackRange = 3.2f;

        /// <summary>
        /// Effective melee hitbox radius (m) — read by HeroReachRing to draw the reach ring,
        /// and by the swing's OverlapSphere. Weapon-driven: a melee weapon with reach &gt; 0
        /// (Knight's greatsword/polearm/axe outreach a dagger) overrides the fixed
        /// <see cref="_attackRange"/>. Ranged classes never set reach, so they keep the fixed range.
        /// <para>
        /// ⭐ WO-1105 REVISION (owner ruling 2026-08-16, verbatim: "change the bow and arrow attack
        /// to the action bar and leave the attack as the dagger attack") — THE PRIMARY ATTACK IS THE
        /// MELEE SWEEP FOR EVERY CLASS, INCLUDING THE RANGER. The earlier WO-1105 pass re-seated the
        /// primary input on the class's ranged basic; the owner reversed that. Sylas's primary verb
        /// is his OFFHAND DAGGER — this sweep — and his BOW is an action-bar ability (the Q slot,
        /// which is exactly where the authored ranger.q Quick Shot already lives), fired
        /// deliberately and greying out under the standard ability cooldown like every other skill.
        /// So this radius is once again the one primary verb, class-agnostic, with no branch on it.
        /// </para>
        /// </summary>
        public float AttackRange => EffectiveRange();

        // Gear v1: lazily-resolved equipped-gear loadout — its EquippedWeapon.reach (when >0)
        // overrides the fixed melee range. Lazily attached so every hero gets gear with no
        // builder change; graceful — no loadout / no weapon / reach 0 leaves the fixed range.
        private GearLoadout _gear;

        // WO-1503 — the hero's OWN side, read rather than assumed, mirroring Enemy.SelfFaction.
        // HeroHealth declares it EXPLICITLY on the interface (HeroHealth.cs:1630 —
        // `CombatFaction IDamageableStructure.Faction => CombatFaction.Friendly;`), so it is only
        // reachable through an interface-typed reference; a plain GetComponent<HeroHealth>().Faction
        // does not compile. Falls back to Friendly so a rig without HeroHealth still swings.
        // Lazily resolved and CACHED, matching _gear above and Enemy's SelfFaction cache
        // (Enemy.cs:3034) — the melee sweep reads this twice per collider per swing, and an
        // uncached GetComponentInParent there is a per-frame allocation-free but non-trivial
        // hierarchy walk. _selfResolved (not a null check) so a rig genuinely without the
        // component does not re-walk every swing.
        private IDamageableStructure _self;
        private bool _selfResolved;

        private CombatFaction HeroFaction
        {
            get
            {
                if (!_selfResolved)
                {
                    _self = GetComponentInParent<IDamageableStructure>();
                    _selfResolved = true;
                }
                return _self != null ? _self.Faction : CombatFaction.Friendly;
            }
        }

        /// <summary>
        /// The melee reach to use this swing: the equipped weapon's reach when set (&gt; 0),
        /// otherwise the serialized fixed <see cref="_attackRange"/>. Preserves today's
        /// behaviour whenever no weapon reach is authored (all ranged classes, and any melee
        /// weapon without a reach value).
        /// </summary>
        private float EffectiveRange()
        {
            if (_gear == null) _gear = GetComponent<GearLoadout>();
            var w = _gear != null ? _gear.EquippedWeapon : null;
            float r = (w != null && w.reach > 0f) ? w.reach : _attackRange;
            // WO-910: talent range multiplies reach (identity when none).
            string cls = _abilities != null ? _abilities.HeroClass : null;
            r *= DeNelle.Village.Talents.HeroTalentModifiers.RangeMultiplier(cls);
            return r;
        }

        [Tooltip("Minimum seconds between attacks.")]
        [SerializeField, Min(0.1f)] private float _attackCooldown = 0.6f;

        [Tooltip("Layer mask covering enemy colliders. Awake adds the Structure layer on top " +
                 "(WO-853) so the swing can also reach walls, gates and enemy turrets.")]
        [SerializeField] private LayerMask _enemyLayer;

        // WO-449: line-of-sight gate for the MELEE swing. The swing's damage OverlapSphere
        // (ResolveAttack) hit EVERY hostile in radius — including one on the far side of a
        // wall the hero is standing against — because it was a pure radius test, no LoS.
        // This mask names the blockers that should occlude an attack (wall/structure geometry,
        // built onto the dedicated "Structure" layer by CastleWallsFromRecipe / CastleHubBuilder.
        // BuildInnerWallRing). It must NOT include the Enemy or Player layers, or the target's
        // own collider (or the hero) would self-block the linecast. When unset (value == 0) the
        // gate degrades OFF (HasLoS returns true) so a misconfigured mask never makes the hero
        // unable to hit ANYTHING. Mirrors HeroTargetIndicator's proven WO-449 pattern.
        [Tooltip("WO-449: layers that BLOCK a melee swing's line-of-sight (walls/structures on " +
                 "the 'Structure' layer). Do NOT include Enemy or Player. Empty disables the gate.")]
        [SerializeField] private LayerMask _losMask;

        [Header("Attack Weight (WO-217: anticipation → impact → recovery)")]
        [Tooltip("When ON, the swing's tempo is shaped via Animator.speed: a brief slow wind-up " +
                 "(anticipation), a fast snap through the contact frame (impact), then a settle " +
                 "back to normal (recovery) — so the swing reads with weight instead of flat.")]
        [SerializeField] private bool _shapeAttackTempo = true;

        [Tooltip("Seconds of slow wind-up before the strike (the coil).")]
        [SerializeField, Range(0f, 0.4f)] private float _anticipationDuration = 0.07f;

        [Tooltip("Animator.speed during the wind-up. <1 = slower/heavier coil.")]
        [SerializeField, Range(0.2f, 1f)] private float _windUpSpeed = 0.55f;

        [Tooltip("Seconds the fast contact speed is held through the impact frame (the snap).")]
        [SerializeField, Range(0f, 0.25f)] private float _impactHold = 0.05f;

        [Tooltip("Animator.speed at the contact frame. >1 = snappy, punchy strike.")]
        [SerializeField, Range(1f, 3f)] private float _impactSpeed = 1.9f;

        [Tooltip("Seconds to ease back to normal speed after impact (the follow-through settle).")]
        [SerializeField, Range(0f, 0.5f)] private float _recoveryDuration = 0.16f;

        [Tooltip("Seconds after swing input when the weapon CONTACTS the target — the damage lands " +
                 "here so the hit syncs to the impact frame, not the swing start. When 0, falls back " +
                 "to the perfect-hit window start (legacy behaviour).")]
        [SerializeField, Min(0f)] private float _impactFrameDelay = 0.13f;

        [Header("Perfect Hit Window")]
        // P1-6 (2026-08-02): the window is now a REAL input window — the player must press attack
        // a SECOND time during the wind-up (see RegisterPerfectTap). It therefore has to CLOSE at
        // or before the impact frame, because that is when ResolveAttack applies the damage: a tap
        // arriving after the hit has already landed cannot retroactively empower it. WO-217 pins the
        // damage to the impact frame and that is not negotiable, so the window is bounded by it
        // (and clamped in code, so an authored end past the impact frame can never again be
        // silently unreachable). Start pulled 0.08 -> 0.03 to make the achievable band as wide as
        // the impact frame allows: [0.03, 0.13] = a 100 ms second-tap window.
        [Tooltip("Seconds after swing input when the perfect-hit window opens.")]
        [SerializeField, Min(0f)] private float _perfectHitWindowStart = 0.03f;

        [Tooltip("Seconds after swing input when the perfect-hit window closes. Clamped at runtime " +
                 "to the impact-frame delay - the hit resolves there, so a later tap cannot count.")]
        [SerializeField, Min(0f)] private float _perfectHitWindowEnd = 0.13f;

        // P1-6: 1.75 -> 1.25. The old 1.75 was an always-on multiplier masquerading as a skill
        // bonus; it has been folded into _baseDamage (30 x 1.75 = 52.5). What remains is a genuine,
        // earned bonus on top of the honest base. Deliberately a BONUS-ONLY design: missing the
        // window costs the player nothing versus today's damage, so a tight window is pure upside
        // for skilled play and never a hidden punishment.
        [Tooltip("Damage multiplier applied when the player lands a real second tap in the perfect window.")]
        [SerializeField, Min(1f)] private float _perfectHitMultiplier = 1.25f;

        [Tooltip("Sound played on a perfect hit (optional).")]
        [SerializeField] private AudioClip _perfectHitSound;

        [Header("Weapon Whoosh")]
        [Tooltip("Pool of whoosh sounds — one is chosen at random per swing.")]
        [SerializeField] private AudioClip[] _whooshSounds;

        [Tooltip("Pitch variation range for the whoosh sample.")]
        [SerializeField] private Vector2 _whooshPitchRange = new Vector2(0.9f, 1.1f);

        // WO-VFX-WEAPON-TRAILS: the swing-trail VFX (WO-219 / WO-504 s3) MOVED OUT of this
        // controller into the reusable DeNelle.Village.WeaponTrailController, which flashes the
        // rarity-tinted blade trail off ActorAnimator.AttackStarted — so hero swings, casts, and
        // ENEMY swings (shared rig) all get a trail with no per-ability wiring. This controller
        // only ENSURES the component is present (Awake) and forwards the headless test seam.

        // ── Runtime ───────────────────────────────────────────────────────────

        private AudioSource  _audioSource;
        private float        _nextAttackTime;
        private float        _swingStartTime;
        private bool         _isInSwing;

        // Owner rule (2026-06-24): combat moves (attack/block/parry) only process while a
        // battle is live (BattleLock.IsInBattle()). This latch tracks the suppressed<->live
        // transition so the FlowTrace fires ONCE per transition (not every frame in town).
        private bool         _combatInputSuppressed;

        // WO-VFX-WEAPON-TRAILS: the shared blade-trail component (ensured in Awake). Cached only to
        // forward the headless ArenaCombatOracle test seam (ApplyWeaponTrailVfxForTest) onto it.
        private WeaponTrailController _trailController;

        // WO-284/285: animation now routes through the canonical ActorAnimator driver
        // (no local StringToHash). PlayAttack cycles a combo for melee classes; casters
        // route to PlayCast so they cast instead of swinging.
        private ActorAnimator _actor;
        private HeroAbilities _abilities;   // class source (knight = combo, casters = cast)

        // WO-423: face-the-target on swing. The hero used to fire its melee/360° hit toward
        // its last MOVE direction, so a stationary attack faced the wrong way. StartAttack
        // resolves the intended target (the reticle-locked one, else the nearest hostile in
        // reach) and asks HeroLocomotion — the sole rotation writer — to yaw-slew toward it
        // before the swing lands; the impact delay covers the turn. Both components sit on
        // the hero root, so they're plain sibling GetComponent lookups.
        private HeroLocomotion     _loco;
        private HeroTargetIndicator _targetIndicator;

        // WO-497 cheap wire: rumble on LANDING a melee hit. HeroImpactFeedback.PlayHaptic
        // previously fired only when the hero TOOK damage (HeroHealth). Resolved lazily +
        // null-safe (no component / no gamepad = silent no-op).
        private HeroImpactFeedback _impactFeedback;
        private int   _comboIndex;
        private float _lastSwingTime;
        private const int   ComboLength    = 3;     // Knight melee combo swings
        private const float ComboResetGap  = 1.1f;  // seconds idle before the combo resets to 0

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (!TryGetComponent(out _actor)) _actor = gameObject.AddComponent<ActorAnimator>();
            _abilities   = GetComponent<HeroAbilities>();
            _loco            = GetComponent<HeroLocomotion>();          // WO-423: sole rotation writer
            _targetIndicator = GetComponent<HeroTargetIndicator>();     // WO-423: reticle-locked target
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 0f; // 2D — punchy response without attenuation

            // Bug-fix (audit 2026-05-30): an unset _enemyLayer (mask 0 = "Nothing") makes every
            // OverlapSphere return empty, so a runtime-built hero's melee silently hits nothing.
            // Default to the Enemy layer, then to Everything — matching HeroHealth/HeroAbilities.
            if (_enemyLayer == 0) _enemyLayer = LayerMask.GetMask("Enemy");
            if (_enemyLayer == 0) _enemyLayer = ~0;

            // WO-853: the swing must also reach STRUCTURES. Walls and gates stay on the
            // "Structure" layer (it is the tower line-of-sight blocker mask — relayering them
            // onto Enemy would make towers shoot through walls), so the only way the melee
            // sweep can return one is to include that layer. Applied AFTER the two fallbacks
            // above so those still decide the base mask; GetMask returns 0 for an undeclared
            // layer, making the OR a no-op that leaves the ~0 fallback byte-identical.
            // Safe because ResolveAttack / FaceNearestHostile reject any target whose
            // Faction is not Hostile — the player's own perimeter reports Friendly.
            _enemyLayer = _enemyLayer.value | LayerMask.GetMask("Structure");

            // WO-449: activate the melee LoS gate out-of-the-box against the dedicated
            // "Structure" wall layer (same layer HeroTargetIndicator's reticle gate uses, set on
            // the wall geometry by CastleWallsFromRecipe / CastleHubBuilder.BuildInnerWallRing).
            // Only seed when unset in the inspector; if "Structure" doesn't exist GetMask returns
            // 0 and HasLoS's degrade rule (value == 0 → clear) keeps the swing able to hit.
            if (_losMask.value == 0) _losMask = LayerMask.GetMask("Structure");

            if (_gear == null) _gear = GetComponent<GearLoadout>();

            // WO-VFX-WEAPON-TRAILS: ensure the shared blade-trail component is present on this rig,
            // so the hero always gets a swing/cast trail (it self-drives off ActorAnimator.
            // AttackStarted). DisallowMultipleComponent makes a double-add a no-op; the trail's own
            // ApplyWeaponTrailVfx re-tints per swing, so no OnGearChanged subscription is needed here.
            _trailController = TryGetComponent<WeaponTrailController>(out var tc)
                            ? tc : gameObject.AddComponent<WeaponTrailController>();
        }

        private void Update()
        {
            // WO-1483: town frame path — the hero's attack/input tick, live in an empty town.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "PlayerAttackController.Update", 4f, 1f);

            // WO-377: no melee swings / blocks while a Yarn dialogue is on screen (a click
            // on the dialogue box used to fall through to a swing). HeroLocomotion owns the
            // gate. If a block was being held when the dialogue opened, drop it cleanly so
            // the hero doesn't freeze mid-block through the conversation.
            if (HeroLocomotion.InputSuppressed)
            {
                if (_blocking)
                {
                    _blocking = false;
                    _actor?.SetBlocking(false);
                }
                return;
            }

            // OWNER RULE 2026-06-24 ("in town / non-combat, NO button creates combat moves"):
            // attack / block / parry are COMBAT moves and must only process while a battle is
            // actually live (in the BattleArena). Gate on the canonical, assembly-neutral
            // BattleLock.IsInBattle() — the single source of truth every battle owner
            // (BattleArena, ArenaMode, ATBCombatManager) registers its in-progress probe into.
            // When NOT in battle (town MainCastle_Hall / overworld walk): suppress the swing,
            // drop any held block, and skip UpdateBlock so RMB/Shift can't raise a shield or
            // fire the parry-ward nova. Movement, camera, interaction, build-mode and NPC-talk
            // are UNTOUCHED — only the combat inputs are gated here.
            if (!BattleLock.IsInBattle())
            {
                if (_blocking)
                {
                    _blocking = false;
                    _actor?.SetBlocking(false);
                }
                if (!_combatInputSuppressed)
                {
                    _combatInputSuppressed = true;
                    FlowTrace.Step("Combat", "input gated: not in battle (town/overworld) - combat moves suppressed");
                }
                // §12 outgoing-attack trace (2026-06-30 "0 damage in dungeon"): PROVE a melee swing was
                // pressed but SUPPRESSED because BattleLock is false — the exact dungeon 0-damage cause
                // (in-place hollows staged no battle). Should stop once a hollow engages (HeroCombatEngagement).
                bool meleePressed =
                    (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                    (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) ||
                    UnityEngine.Input.GetMouseButtonDown(0);
                if (meleePressed)
                    FlowTrace.Throttle("Combat", "melee-suppressed", 1f,
                        "MELEE swing pressed but SUPPRESSED — BattleLock.IsInBattle()=false (no active battle). " +
                        "Hero deals 0 damage here until a battle is staged OR a hero-only enemy engages.");
                return;
            }
            if (_combatInputSuppressed)
            {
                _combatInputSuppressed = false;
                FlowTrace.Step("Combat", "input live: battle started - combat moves enabled");
            }

            bool attackPressed = false;

            // New Input System path.
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) attackPressed = true;

            var gp = Gamepad.current;
            if (gp != null && gp.buttonSouth.wasPressedThisFrame) attackPressed = true;

            // Legacy Input fallback.
            if (!attackPressed && UnityEngine.Input.GetMouseButtonDown(0)) attackPressed = true;

            if (attackPressed && !_isInSwing && Time.time >= _nextAttackTime)
                StartAttack();          // WO-1105 REVISION: the primary verb is the melee/dagger swing
            else if (attackPressed && _isInSwing)
                RegisterPerfectTap();   // P1-6: the SECOND tap — the perfect-hit input

            UpdateBlock();
        }

        // WO-285 (D): shield classes (Knight) hold a block while RMB / Left-Shift is
        // held. Routed through the canonical driver (Block bool); a guarded no-op for
        // classes/controllers without a Block state. Resolved live so it follows the
        // body swap. Only the Knight blocks (sword-and-shield); casters/ranged don't.
        private bool _blocking;

        // ── Perfect parry / riposte ───────────────────────────────────────────
        // Raising block opens a brief PARRY WINDOW; an enemy hit landing inside it is negated
        // (see HeroHealth) → a slow-time beat + the next swing becomes a big RIPOSTE. OpenParryWindow()
        // is the public seam a caster's magical deflect spell reuses for the same payoff.
        private float _parryWindowUntil;
        private float _riposteArmedUntil;
        private const float ParryWindow       = 0.25f;  // sec after block-raise a hit is parried
        private const float RiposteWindow     = 2.0f;   // sec after a parry the next swing is empowered
        private const float RiposteMultiplier = 3.0f;   // counter-swing damage multiplier

        private void UpdateBlock()
        {
            string cls = _abilities != null ? _abilities.HeroClass : null;
            bool canBlock = cls == "knight";

            bool held = false;
            if (canBlock)
            {
                var kb = Keyboard.current;
                if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) held = true;
                if (!held && UnityEngine.Input.GetMouseButton(1)) held = true;   // RMB
                var gp = Gamepad.current;
                if (!held && gp != null && gp.leftTrigger.isPressed) held = true;
            }

            if (held != _blocking)
            {
                _blocking = held;
                _actor.SetBlocking(_blocking);
                if (held)
                {
                    _parryWindowUntil = Time.time + ParryWindow; // raising block opens the parry window
                    // WO-359: parry-READY visual cue. A brief cool-steel flash at the shield the
                    // instant the window opens, so the player can SEE the (otherwise invisible)
                    // 0.25 s timing window they're aiming for. Reuses the central VFXManager
                    // (Impact_ShockwaveRing → procedural shield-ward nova); static + null-safe, so
                    // it's a silent no-op when no VFXManager is present. No new VFX subsystem.
                    VFXManager.Play(VFXType.Impact_ShockwaveRing, transform.position + Vector3.up * 1.2f);
                }
            }
        }

        // ── Parry API (HeroHealth calls these on incoming hits; the deflect seam is public) ──

        /// <summary>True (and consumes the window) if an enemy hit lands during the parry window.</summary>
        public bool TryConsumeParry()
        {
            if (Time.time > _parryWindowUntil) return false;
            _parryWindowUntil = 0f;   // consume so one block-raise parries one hit
            return true;
        }

        /// <summary>Open a parry window NOW — the seam a caster's magical deflect spell calls so it
        /// routes through the same parry → slo-mo → riposte payoff (just with magic VFX/anim).</summary>
        public void OpenParryWindow(float seconds = ParryWindow)
        {
            _parryWindowUntil = Time.time + Mathf.Max(0.05f, seconds);
        }

        /// <summary>Payoff for a successful parry: slow-time beat + deflect clang + arm the riposte.</summary>
        public void OnParrySuccess(Vector3 pos)
        {
            CombatFeedbackManager.Parry(pos);
            GameSfx.PlaySwordClash();   // metallic deflect clang
            _riposteArmedUntil = Time.time + RiposteWindow;
            // P23 §0 fix (HUD_OBSIDIAN §1.8): the old DamageNumberSpawner.SpawnLabel (:140) was
            // pooled but UNCAPPED + UN-DEDUPED world-space TextMesh at 1.4x — every parried hit
            // inside the 0.25s window stacked another giant label. CombatText is the pooled(6)/
            // capped/0.5s-deduped screen-space stamp: repeats become ONE "PARRY! x N".
            CombatText.Show(CombatTextKind.Parry, "PARRY!", pos + Vector3.up * 1.4f);
        }

        // ── Attack flow ───────────────────────────────────────────────────────

        /// <summary>
        /// HUD seam (Option 1, owner 2026-06-27): fire ONE basic-attack swing from a UI button —
        /// the big bottom-right "basic attack" button — exactly as a Space / LMB / gamepad-South
        /// press would in <see cref="Update"/>. Honors the SAME gates: only while a battle is live
        /// (BattleLock.IsInBattle), input not suppressed, not already mid-swing, and off the swing
        /// cooldown. Returns true when a swing actually started. The keyboard/mouse path is untouched.
        /// </summary>
        public bool TriggerBasicAttack()
        {
            // ── WO-1750 — A DOWNED HERO REFUSES THE SWING, AND SAYS SO ────────────────────
            // This is a DIRECT-CALL entry point. HudKitCommandBridge (the phone's one attack
            // button) reaches it via Object.FindAnyObjectByType<PlayerAttackController>(),
            // which filters on GameObject ACTIVE state and NOT on component ENABLED state —
            // so HeroHealth.EnterDeathFreeze's `_pac.enabled = false` stops this component's
            // Update() and leaves this method fully callable. The keyboard/mouse path goes
            // through Update and was therefore already covered, which is why the owner only
            // ever felt this on the Seeker (owner felt-test 2026-09-15, RaidBase_IronBastion:
            // hero mid-swing with an empty HP bar while the enemy brain logged
            // "still steered at the hero ... while HeroHealth.IsAlive=false").
            //
            // The predicate lives on HeroHealth (EvaluateInputRefusedForDeath) so the refusal
            // and the death latch are ONE rule, not two copies. It is also the instrument §12
            // asks for: while a down hero is still being driven, this Throttle names the
            // surface once a second instead of leaving a silent refusal.
            if (HeroHealth.InputRefusedForDeath(gameObject))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("HeroDeath", "input-while-down-attack", 1f,
                    "basic attack REFUSED: the hero is down (HeroHealth.IsAlive=false or the death " +
                    "latch is set) but a direct TriggerBasicAttack call still arrived - component " +
                    "enabled=" + enabled + " activeInHierarchy=" + gameObject.activeInHierarchy +
                    ". A caller is reaching this method past the death freeze's component disable " +
                    "(WO-1750); the swing is refused here so the health model and what the player " +
                    "can do agree.");
                return false;
            }
            if (HeroLocomotion.InputSuppressed) return false;
            if (!BattleLock.IsInBattle()) return false;
            // P1-6: a press that arrives MID-SWING is the perfect-hit second tap, not a dropped
            // input. This is what makes the mechanic reachable on TOUCH — the mobile HUD's
            // basic-attack button is the only attack input on a phone, so tapping it twice is the
            // gesture. Still returns false (no NEW swing started), so every existing caller's
            // contract is unchanged.
            if (_isInSwing) { RegisterPerfectTap(); return false; }
            // ⭐ WO-1105 REVISION (owner 2026-08-16): the bow does NOT live here. The primary input
            // is the melee/dagger swing for every class; the archer's bow is an ACTION-BAR ABILITY
            // (the Q slot -> HeroAbilities.TryCast), so this button never spends an arrow.
            if (Time.time < _nextAttackTime) return false;
            StartAttack();
            return true;
        }

        // ── ⭐ WO-1105 REVISION (owner ruling 2026-08-16) — THE BOW IS NOT THE PRIMARY ATTACK ────
        //
        // Owner, verbatim: "change the bow and arrow attack to the action bar and leave the attack
        // as the dagger attack."
        //
        // WHAT WAS HERE AND WHY IT IS GONE: the first WO-1105 pass added a bow-first primary path
        // (a ranged-fire helper, its own target resolver, and a HUD face resolver) which made the
        // PRIMARY attack input resolve through the class's ranged basic (ranger.q Quick Shot) for
        // any class whose basic is a ranged one. The owner reversed that. The primary attack is the
        // melee sweep again — class-agnostic, no branch, byte-equivalent to the pre-WO-1105 path
        // for EVERY class including the ranger, whose sweep is Sylas's offhand DAGGER.
        //
        // WHERE THE BOW WENT: nowhere new. `ranger.q` is the class's LOCKED Q def, and the Q
        // action-bar medallion already resolves + casts exactly that def
        // (HudModelProducers.ResolveSlotDef -> HeroAbilities.ResolvedDef(Q); the medallion tap runs
        // VillageHudController.AbilityRequested -> HeroAbilitiesHudBridge.OnAbilityClicked ->
        // HeroAbilities.TryCast(Q)). So the bow is fired deliberately from the action bar, on the
        // standard ability cooldown, and the slot greys out while it cools like every other ability
        // — there is no special case left anywhere on this path.
        //
        // WHAT SURVIVES, deliberately: HeroAbilities.TryGetRangedPrimary is still the one derived
        // "does this class shoot?" test. HeroTargetIndicator gates auto-acquire + the sticky tap
        // override (R1/R2) on it, and the Focus no-double-refund rule in ResolveAttack reads it, so
        // the targeting work and the resource economy are unchanged by this revision.

        // ── P1-6: the perfect-hit input ───────────────────────────────────────

        // Seconds from swing start at which the player's second tap landed; < 0 = no tap this
        // swing. Recorded, not evaluated, at press time — ResolveAttack decides.
        private float _perfectTapElapsed = -1f;

        /// <summary>
        /// P1-6: record the perfect-hit SECOND TAP for the swing in flight. Public so any input
        /// surface (keyboard/gamepad/mouse in <see cref="Update"/>, the HUD basic-attack button via
        /// <see cref="TriggerBasicAttack"/>, a future virtual stick) feeds the same one seam.
        /// First tap wins — mashing after the window has closed cannot overwrite a good tap with a
        /// bad one, and (because the window has a lower bound) mashing from frame zero does not
        /// guarantee a perfect either. No-op when no swing is in flight.
        /// </summary>
        public void RegisterPerfectTap()
        {
            if (!_isInSwing) return;
            if (_perfectTapElapsed >= 0f) return;          // first tap wins
            _perfectTapElapsed = Time.time - _swingStartTime;
        }

        private void StartAttack()
        {
            // WO-910: attackSpeed talents shorten the swing cooldown (1 + ΣattackSpeed).
            string atkClass = _abilities != null ? _abilities.HeroClass : null;
            float atkSpd = DeNelle.Village.Talents.HeroTalentModifiers.AttackSpeedMultiplier(atkClass);
            _nextAttackTime = Time.time + (_attackCooldown / Mathf.Max(0.25f, atkSpd));
            _swingStartTime = Time.time;
            _isInSwing      = true;
            _perfectTapElapsed = -1f;   // P1-6: each swing needs its OWN second tap

            // #51: the whoosh on the swing itself (the "swish" before the clash). The clash plays
            // later only when ResolveAttack connects, so swing + hit read as two distinct beats.
            GameSfx.PlaySwordSwing();

            // WO-423: turn to face the intended target BEFORE the swing plays, so the
            // hit (a 360° OverlapSphere in ResolveAttack) and any weapon VFX read as
            // facing the foe instead of the last move direction. Prefer the reticle's
            // locked target; else the nearest hostile inside attack reach. HeroLocomotion
            // (the sole rotation writer) slews the yaw; the _impactFrameDelay covers it.
            FaceAttackTarget();

            // WO-285: melee classes swing a cycling combo; casters (Mage/Cleric) cast
            // instead of swinging. Class resolved live from HeroAbilities (set after the
            // body swap). The combo index resets after a short idle gap so consecutive
            // hits flow Attack0→1→2 but a paused-then-resumed attack starts fresh.
            string cls = _abilities != null ? _abilities.HeroClass : null;
            bool caster = cls == "mage" || cls == "cleric";
            if (caster)
            {
                _actor.PlayCast();
            }
            else
            {
                if (Time.time - _lastSwingTime > ComboResetGap) _comboIndex = 0;
                _actor.PlayAttack(_comboIndex);
                _comboIndex    = (_comboIndex + 1) % ComboLength;
                _lastSwingTime = Time.time;
            }
            // WO-217: shape the swing's tempo so it reads anticipation → impact →
            // recovery instead of a flat uniform swing. Drives Animator.speed through
            // the canonical driver (a global multiplier, NOT a guarded param). Data
            // -driven via the serialized timing fields; restores speed = 1 on settle.
            if (_shapeAttackTempo && _actor != null)
                _actor.ShapeAttackTempo(_anticipationDuration, _windUpSpeed,
                                        _impactHold, _impactSpeed, _recoveryDuration);

            PlayWhoosh();

            // WO-VFX-WEAPON-TRAILS: the blade trail now lights up via WeaponTrailController's
            // subscription to _actor.AttackStarted (fired inside PlayAttack/PlayCast above) — no
            // explicit call here.

            StartCoroutine(ResolveAttack());
        }

        /// <summary>
        /// WO-423: resolve the intended swing target and ask HeroLocomotion to yaw-slew
        /// toward it. Target priority: the reticle-locked <see cref="HeroTargetIndicator.CurrentTarget"/>,
        /// else the nearest living hostile inside the effective attack reach. Null-guarded —
        /// no locomotion or no target leaves facing untouched (attack proceeds as before).
        /// </summary>
        private void FaceAttackTarget()
        {
            if (_loco == null) return;

            // 1) reticle-locked target (registry — exactly what the ring shows). The reticle
            //    already applies its own WO-449 LoS gate (HeroTargetIndicator.HasLoS), so a
            //    locked target is guaranteed visible — no wall between hero and it.
            IDamageable target = _targetIndicator != null ? _targetIndicator.CurrentTarget : null;
            if (target != null && !target.IsAlive) target = null;

            // 2) fall back to the nearest hostile inside the swing's reach WITH a clear
            //    line-of-sight (WO-449) — so the hero never turns to face / swing at a foe
            //    walled off from them.
            if (target == null)
            {
                float range = EffectiveRange();
                Collider[] hits = Physics.OverlapSphere(transform.position, range, _enemyLayer);
                float bestSqr = float.MaxValue;
                foreach (var col in hits)
                {
                    if (col == null) continue;
                    var d = col.GetComponentInParent<IDamageable>();
                    if (d == null || !d.IsAlive || d.Faction != CombatFaction.Hostile) continue;
                    if (!HasLoS(d)) continue;   // WO-449: skip walled-off foes
                    float sqr = (d.WorldPosition - transform.position).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; target = d; }
                }
            }

            if (target == null) return;   // nothing to face — swing as before
            FlowTrace.Step("Combat", "PlayerAttack: facing melee target (LoS-cleared, WO-449)");
            _loco.FaceToward(target.WorldPosition);
        }

        /// <summary>
        /// WO-449: true when the hero has a clear line-of-sight to <paramref name="target"/>
        /// (no wall/structure between them) — so a melee swing can't damage an enemy through a
        /// wall the hero is standing against. Linecasts from an eye point on the hero to a TORSO
        /// point on the target (not feet) so a slope/curb under either doesn't false-block.
        /// DEGRADE: an unset mask (value == 0) → treat LoS as always clear (radius-only, legacy
        /// behaviour) so a misconfigured mask never makes the hero unable to hit anything.
        /// Mirrors HeroTargetIndicator.HasLoS (the reticle's proven WO-449 gate).
        /// </summary>
        private bool HasLoS(IDamageable target)
        {
            if (target == null) return false;
            if (_losMask.value == 0) return true;   // degrade: LoS gate disabled
            Vector3 eye   = transform.position + Vector3.up * 1.4f;
            Vector3 torso = target.WorldPosition + Vector3.up * 1.0f;
            // Clear when the linecast hits NOTHING between eye and torso.
            if (!Physics.Linecast(eye, torso, out RaycastHit hit, _losMask, QueryTriggerInteraction.Ignore))
                return true;
            // WO-853 SELF-HIT EXEMPTION — also clear when the first thing the cast hits IS the
            // target. A wall or gate lives ON the "Structure" layer this cast is masked to, and
            // the torso point sits inside that wall's own collider, so the cast always reports
            // the wall as occluding itself and the hero could never swing at one. Nothing can
            // occlude a wall from itself.
            // NOT a shoot-through-wall hole: Physics.Linecast reports the CLOSEST hit, so "the
            // first blocker is the target" proves nothing else stands in front of it. A wall
            // behind a DIFFERENT wall reports that other wall here and stays blocked.
            // Byte-identical for every target NOT on a _losMask layer (all enemies): its
            // collider can never be the reported hit, so this collapses to the old expression.
            return ResolvesTo(hit.collider, target);
        }

        /// <summary>
        /// True when <paramref name="col"/> belongs to <paramref name="target"/> — resolved the
        /// same way the melee sweep resolves a collider to its damageable
        /// (<c>GetComponentInParent</c>), so a hit on a child collider still counts as the target.
        /// </summary>
        private static bool ResolvesTo(Collider col, IDamageable target)
        {
            if (col == null) return false;
            return ReferenceEquals(col.GetComponentInParent<IDamageable>(), target);
        }

        private IEnumerator ResolveAttack()
        {
            // WO-217: land the hit on the IMPACT FRAME (when the weapon contacts the
            // target) rather than at the swing start, so the damage + "connect" feel
            // sync to the snap of the animation, not the wind-up. Data-driven via
            // _impactFrameDelay; falls back to the perfect-window start when unset (0).
            float hitDelay = _impactFrameDelay > 0f ? _impactFrameDelay : _perfectHitWindowStart;
            yield return new WaitForSeconds(hitDelay);

            // P1-6 (2026-08-02) — THE PERFECT HIT IS NOW A REAL INPUT.
            // What this used to be:
            //     float elapsed  = Time.time - _swingStartTime;
            //     bool isPerfect = elapsed >= 0.08f && elapsed <= 0.18f;
            // `elapsed` was just the coroutine's own FIXED 0.13 s wait read back. 0.13 sits dead
            // centre of [0.08, 0.18] and there was NO second player input anywhere in this method,
            // so isPerfect was unconditionally TRUE at any frame rate above ~20 FPS. Consequences:
            // the 1.75x multiplier applied to every swing (so the hero's "30" base melee was really
            // 52.5), the gold PERFECT stamp fired on every single hit and therefore meant nothing,
            // and a frame hitch over 50 ms randomly cost the player 43% damage with no cue.
            // What it is now: the player must press attack a SECOND time during the wind-up
            // (RegisterPerfectTap — keyboard/gamepad/mouse, or a second tap of the mobile HUD
            // button). The multiplier moved into _baseDamage so a missed window deals exactly the
            // damage the hero dealt before this change; a landed tap is a real +25% on top.
            // The window is CLAMPED to the impact frame: WO-217 pins damage to the impact frame, so
            // any authored window past it would be unreachable — the clamp makes that structural
            // instead of a silent lie, and self-reports once if the authoring disagrees.
            float windowStart = Mathf.Max(0f, _perfectHitWindowStart);
            float windowEnd   = Mathf.Min(_perfectHitWindowEnd, hitDelay);
            if (_perfectHitWindowEnd > hitDelay)
                FlowTrace.Once("Combat", "perfect-window-clamped",
                    $"perfect-hit window end {_perfectHitWindowEnd:F3}s is AFTER the impact frame " +
                    $"{hitDelay:F3}s where damage resolves - clamped to {windowEnd:F3}s. A tap in the " +
                    "clamped-off tail could never have counted; widen _impactFrameDelay instead if a " +
                    "longer window is wanted.");

            bool isPerfect  = _perfectTapElapsed >= windowStart
                           && _perfectTapElapsed <= windowEnd;
            bool riposte    = Time.time <= _riposteArmedUntil;   // empowered counter after a parry

            // §12: PROVE the timing input, so "perfect never fires" / "perfect always fires" is a
            // data read, not a theory. Throttled — this is one line per swing otherwise.
            FlowTrace.Throttle("Combat", "perfect-window", 1f,
                $"perfect-hit eval: tap={(_perfectTapElapsed < 0f ? "NONE" : _perfectTapElapsed.ToString("F3") + "s")} " +
                $"window=[{windowStart:F3}, {windowEnd:F3}]s -> perfect={isPerfect}");

            Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveRange(), _enemyLayer);

            // Gear v1: the equipped weapon's damageMult multiplies the melee swing — the SAME
            // scalar HeroAbilities folds into ability casts (base x talent x level x timing x
            // WEAPON). Previously the melee basic attack only read the weapon for reach, so a
            // better blade changed range but not damage; now equipping a stronger weapon is
            // FELT on every swing (e.g. Iron Longsword's 1.25 = +25% melee damage vs starter).
            // Same field, same lazy _gear cache used by EffectiveRange — no new stat system.
            // Graceful: no loadout / no weapon = 1.0, so combat is unchanged.
            if (_gear == null) _gear = GetComponent<GearLoadout>();
            float weaponMult = _gear != null ? _gear.WeaponMult : 1f;

            bool anyHit = false;
            float lastHitDamage = 0f;   // WO-497: scales the landing rumble

            // §12 — WHY-REJECTED ACCOUNTING. The whiff line below already reported `candidates=N`,
            // but N>0 with no hit is the ambiguous case: the sweep TOUCHED something and refused
            // it, and the log never said which refusal. These three counters split that one
            // number into the three hypotheses for "I hit the wall and nothing happened":
            //   noDamageable  — collider found, but no IDamageable up the parent chain. That is a
            //                   collider/mesh or hierarchy mismatch (the art is not under the
            //                   component), NOT a mask problem.
            //   factionReject — found and alive, but CombatFactionRules.MayAttack said no. That is
            //                   the faction-mislabel case (an enemy wall reading Friendly).
            //   losReject     — the WO-449 wall-blocks-line-of-sight refusal, already logged
            //                   individually; counted here so the whiff line totals to `candidates`.
            // Ints on the stack, folded into the EXISTING throttled miss line — no new hot-path log.
            int noDamageable = 0, factionReject = 0, losReject = 0;
            foreach (var col in hits)
            {
                if (col == null) continue;
                var damageable = col.GetComponentInParent<IDamageable>();
                if (damageable == null) noDamageable++;
                // WO-1503: route the friend-or-foe test through the ONE authority instead of the
                // inline `Faction != CombatFaction.Hostile` copy that used to live here — the exact
                // duplication CombatFactionRules' header forbids. MayAttack folds in null + IsAlive,
                // so this single call is the whole guard. `damageable` is IDamageable-typed, which
                // picks the IDamageable overload unambiguously (see that file's overload trap note).
                if (!CombatFactionRules.MayAttack(HeroFaction, damageable))
                {
                    // §12: counted only when a damageable WAS found, so the null case stays
                    // attributed to noDamageable above and the two buckets never double-count.
                    if (damageable != null) factionReject++;
                    continue;
                }

                // WO-449: reject a hit when a wall/structure blocks the swing's line-of-sight,
                // so the hero standing against a wall can't damage an enemy on the far side.
                // Degrades clear when the mask is unset (HasLoS) so a misconfig never no-ops melee.
                if (!HasLoS(damageable))
                {
                    losReject++;
                    FlowTrace.Warn("Combat", "PlayerAttack: hostile in melee radius REJECTED — wall blocks line-of-sight (WO-449)");
                    continue;
                }

                float damage = _baseDamage * weaponMult;
                if (isPerfect) damage *= _perfectHitMultiplier;
                if (riposte)   damage *= RiposteMultiplier;   // parry counter — big hit on the tank
                // Hunter's Mark is applied ONCE inside Enemy.TakeDamageFrom (2026-08-15 review,
                // CombatMark GameObject-key fix) — scaling here too would double-apply.

                // WO-910: critChance talent roll (additive). Distinct from perfect-hit timing.
                string critClass = _abilities != null ? _abilities.HeroClass : null;
                float critChance = DeNelle.Village.Talents.HeroTalentModifiers.CritChanceBonus(critClass);
                bool isCrit = critChance > 0f && Random.value < critChance;
                if (isCrit) damage *= 1.5f;

                Vector3 hitPos = col.transform.position + Vector3.up;

                // WO-219 reconcile: damage routes through Enemy.TakeDamageFrom, which
                // already fires the floating damage number + CombatFeedbackManager.Hit
                // (hit-stop/combo/shake) + the impact burst centrally. Calling them again
                // here double-spawned the number and restarted the hit-stop twice per
                // enemy. Drop the duplicate calls — TakeDamage is the single feedback
                // entry point. (Non-Enemy IDamageable targets simply skip the extra feel,
                // which is acceptable — only enemies are hostile melee targets.)
                // Ticket #61: mark this as a HERO strike so its combo / kill-streak / RAMPAGE
                // feedback fires (tower / pet / DoT never stamp -> never feed the combo).
                (damageable as DeNelle.Core.Combat.IHeroDamageMarkable)?.MarkNextHitFromHero();
                // §12 outgoing-attack trace (2026-06-30): PROVE the melee swing LANDS as a hero-dealt
                // hit — the counterpart to Enemy's "CombatFeedback Hit gated: dealtByHero=..." line. On
                // the next felt-test this MUST read dealtByHero=True (was never emitted while suppressed).
                // WO-1503 — NAME THE TARGET, not just its scene root. This line used to print
                // ONLY `col.transform.root.name`, and in the hub that is ALWAYS "CastleHubRoot":
                // WaveManager is a child of CastleHubRoot and spawns every wave enemy under
                // WaveEnemies/CastleHubRoot (Main_Castle_Overworld.unity — WaveManager._enemyRoot
                // 414338686 -> WaveEnemies -> CastleHubRoot; WaveManager.cs:664/2853 pass
                // _enemyRoot as the spawn parent). So a correct hit on a wave enemy read as
                // `hero MELEE hit 'CastleHubRoot'`, and it was triaged as the hero destroying its
                // own castle. It never was: the hub root carries a Transform and NOTHING else
                // (one m_Component, fileID 1385856591), so it holds no faction and cannot take a
                // hit, and the guard above refuses every non-Hostile target regardless.
                // Root stays (it locates the target in the hierarchy); identity leads.
                var targetBehaviour = damageable as MonoBehaviour;
                FlowTrace.Step("Combat",
                    $"hero MELEE hit '{(targetBehaviour != null ? targetBehaviour.name : "<non-MonoBehaviour>")}' " +
                    $"({damageable.GetType().Name}) under root '{col.transform.root.name}' " +
                    $"faction={damageable.Faction} attacker={HeroFaction} " +
                    $"dealtByHero=True amount={damage:F1} (perfect={isPerfect} riposte={riposte} crit={isCrit}).");
                DamageElement weaponElement = ElementalDamageResolver.ParseElement(
                    _gear != null && _gear.EquippedWeapon != null ? _gear.EquippedWeapon.element : null);
                damageable.TakeDamage(damage, weaponElement);

                // WO-1066: bounded catalog effect. Reuse the existing burn/slow/poison
                // consumers; a missing/unknown effect remains inert and must not be advertised.
                var effectWeapon = _gear != null ? _gear.EquippedWeapon : null;
                if (_abilities != null && effectWeapon != null && !string.IsNullOrEmpty(effectWeapon.effectKind))
                {
                    float chance = Mathf.Clamp01(effectWeapon.effectChance);
                    if (chance >= 1f || (chance > 0f && Random.value < chance))
                    {
                        float seconds = Mathf.Max(0f, effectWeapon.effectDurationSeconds);
                        float dps = seconds > 0f ? damage * Mathf.Max(0f, effectWeapon.effectDamagePct) / seconds : 0f;
                        _abilities.ApplyAmmoRider(damageable, effectWeapon.effectKind, dps, seconds, 0f);
                    }
                }
                anyHit = true;
                lastHitDamage = damage;

                // Owner ruling 2026-08-02 (F8): the BASIC melee hit carries NO impact burst —
                // the generic green "Weaponskillsword_Impact" pop and the perfect-hit
                // "KnightWeaponskill_Impact" burst are DELETED from this path (both keys stay
                // owner-tagged in VfxManualPicks.json for their ability-level uses). A
                // perfect-timed connect is announced by the gold PERFECT stamp instead
                // (TriggerPerfectHitFeedback below). ONLY an element-branded weapon still
                // bursts on hit, so an elemental blade keeps its visual read in combat.
                Guard.Try("Combat", "melee elemental on-hit vfx", () =>
                {
                    // ELEMENTAL brand on-hit (data-driven via WeaponDef.element; WeaponVfxMap is
                    // the ONE reader). Reuses the shared VFXManager pool (no raw Instantiate);
                    // every resolved key is owner-tagged in VfxManualPicks.json. No element ->
                    // null key -> nothing plays. Not hardcoded to one weapon id.
                    string elementKey = WeaponVfxMap.ElementalOnHitKey(_gear != null ? _gear.EquippedWeapon : null);
                    if (!string.IsNullOrEmpty(elementKey))
                        VFXManager.PlayKey(elementKey, hitPos, Quaternion.identity, null, null,
                                           isPerfect ? 1.25f : 0f);
                });

                // WO-566: v2 talent on-hit procs (Knight Emberbrand Strike burn). Apply each owned
                // proc as a DoT on the struck enemy, rolling its chance. Data-driven + identity
                // (no procs) until the node is learned. Enemy is the only hostile melee target.
                var procEnemy = col.GetComponentInParent<Enemy>();
                if (procEnemy != null && !procEnemy.IsDead)
                {
                    string procClass = _abilities != null ? _abilities.HeroClass : "knight";
                    Vector3 procSource = transform.position + Vector3.up * 1.0f;
                    DeNelle.Village.Talents.HeroTalentModifiers.ForEachOnHitProc(procClass, spec =>
                    {
                        if (Random.value > spec.Chance) return;
                        procEnemy.ApplyDamageOverTime(spec.Dps, spec.Duration, procSource);
                        FlowTrace.Throttle("HeroTalents", "proc-" + spec.NodeId, 1f,
                            $"on-hit proc {spec.NodeId}: {spec.Dps:F0} dps for {spec.Duration:F0}s applied to enemy.");
                    });
                }

                if (isPerfect)
                    TriggerPerfectHitFeedback(hitPos);
            }

            // §12 outgoing-attack trace (2026-06-30): the swing FIRED (BattleLock was live) but
            // connected with nothing — splits "hero can't attack (gated)" from "attacked but no
            // hostile in reach / LoS-blocked". Throttled so a flurry of empty swings doesn't spam.
            // §12 (extended): `candidates=N` alone could not tell a mask miss from a refusal, so
            // the miss now carries the three reject buckets AND the resolved layer mask. Read it as:
            //   candidates=0                       -> the OverlapSphere found NO collider at all.
            //                                         Compare `mask` against the Structure layer bit:
            //                                         a wall absent here is the layer/mask hypothesis.
            //   candidates>0, noDamageable=N       -> colliders present, component missing up the
            //                                         parent chain (collider/mesh/hierarchy mismatch).
            //   candidates>0, factionReject=N      -> found and refused on faction (mislabel).
            //   candidates>0, losReject=N          -> the WO-449 line-of-sight refusal.
            if (!anyHit)
                FlowTrace.Throttle("Combat", "melee-whiff", 1f,
                    $"hero MELEE swing FIRED but hit nothing (candidates={hits.Length}, reach={EffectiveRange():F1}m, " +
                    $"mask={_enemyLayer.value}) rejects[noDamageable={noDamageable}, faction={factionReject}, " +
                    $"los={losReject}] — in battle, but no hostile IDamageable in reach/LoS.");

            // WO-997: ranger Focus on-hit restore — a landed basic attack refunds resource
            // through the SINGLE pool on HeroAbilities (RestoreMana, clamped there). Gated on
            // the class resource block's onHitRestore > 0, so knight/mage are provably
            // untouched. Once per CONNECTED SWING (not per enemy caught in the sweep), so a
            // crowd cannot multi-refund one attack.
            //
            // WO-1105 R3 — THE DAGGER DOES NOT REFUND; THE BOW DOES. Unchanged by the 2026-08-16
            // revision that moved the bow off the primary input and onto the action bar: the CLASS
            // BASIC is still `ranger.q`, and WO-997's rule is "armed for the class basic, paid on
            // hit-confirm". HeroAbilities arms + pays that restore inside the ARROW's arrival
            // closure (IsClassBasic -> _pendingOnHitRestore -> RestoreMana on connect), so Focus
            // rides the shot the player deliberately fired from the bar. Leaving this melee restore
            // ungated would pay Focus TWICE per cycle — once on the arrow, once on the dagger — and
            // hand the ranger a second, unauthored Focus engine (the exact WO-999 failure mode: a
            // free spammable move quietly out-earning the authored 0.8/s passive). The swing is the
            // DAGGER, so it earns nothing. Byte-identical for every class WITHOUT a ranged basic
            // (knight: OnHitRestore is 0 anyway, and TryGetRangedPrimary is false for his 'dash').
            bool basicIsMelee = _abilities == null ||
                                !_abilities.TryGetRangedPrimary(EffectiveRange(), out _);
            if (anyHit && _abilities != null && _abilities.OnHitRestore > 0f && basicIsMelee)
                _abilities.RestoreMana(_abilities.OnHitRestore);
            else if (anyHit && _abilities != null && _abilities.OnHitRestore > 0f)
                FlowTrace.Throttle("Combat", "offhand-no-refund", 1f,
                    $"OFFHAND melee connected but restored 0 {_abilities.ResourceDisplayName} - the " +
                    "class basic is the BOW, so the on-hit restore rides the arrow (WO-1105 R3). " +
                    "No double refund.");

            // Impact audio — the meaty "connect" the swing was missing. Melee classes get a
            // weapon clash; casters get a spell-hit zap. (TakeDamageFrom already plays the
            // enemy's own hit grunt + the central hit-stop/shake; this is the WEAPON sound.)
            if (anyHit)
            {
                string cls = _abilities != null ? _abilities.HeroClass : null;
                if (cls == "mage" || cls == "cleric") GameSfx.PlaySpellCast();
                else GameSfx.PlaySwordClash();

                // WO-497: rumble on the swing CONNECTING (was rumble-on-take-damage only).
                // Intensity scales with the damage dealt; null-safe (no gamepad = no-op).
                if (_impactFeedback == null) _impactFeedback = GetComponent<HeroImpactFeedback>();
                _impactFeedback?.PlayHaptic(Mathf.Clamp(0.2f + lastHitDamage * 0.004f, 0.2f, 0.6f), 0.10f);

                if (riposte)
                {
                    // P23 §0 fix (§1.8): screen-space pooled stamp — never a stacking 1.6x TextMesh.
                    CombatText.Show(CombatTextKind.Riposte, "RIPOSTE!", transform.position + Vector3.up * 1.8f);
                    _riposteArmedUntil = 0f;   // one empowered counter per parry
                }
            }

            _isInSwing = false;

            // WO-VFX-WEAPON-TRAILS: trail emission is stopped by WeaponTrailController's own
            // active-window coroutine (started when it received AttackStarted) — nothing to do here.
        }

        // ---------------------------------------------------------------------
        //  HEADLESS ORACLE SEAM (WO-504 s3 -> WO-VFX-WEAPON-TRAILS) — the swing-trail
        //  build + rarity apply now live on WeaponTrailController. This forwarder keeps
        //  the ArenaCombatOracle (which calls attack.ApplyWeaponTrailVfxForTest()) compiling
        //  and exercising the SAME EnsureTrail + ApplyWeaponTrailVfx path a live swing runs.
        //  Ensures the component (the oracle AddComponents this controller in isolation, so
        //  Awake may not have run its ensure yet). Editor/QA seam only — gameplay never calls it.
        // ---------------------------------------------------------------------
        public Color ApplyWeaponTrailVfxForTest()
        {
            if (_trailController == null)
                _trailController = TryGetComponent<WeaponTrailController>(out var tc)
                                ? tc : gameObject.AddComponent<WeaponTrailController>();
            return _trailController.ApplyWeaponTrailVfxForTest();
        }

        private void TriggerPerfectHitFeedback(Vector3 hitPos)
        {
            if (_perfectHitSound != null)
                _audioSource.PlayOneShot(_perfectHitSound, 1.0f);

            // Owner ruling 2026-08-02: a perfect-timed hit shows a floating GOLD ASCII
            // "PERFECT" stamp (action-game style) through the SS1.8 pooled CombatText layer
            // (capped, per-kind deduped -> "PERFECT x3", rises + fades ~0.9s, raycast-off).
            // The WORD carries the meaning (colourblind law). Replaces both the old
            // world-space DamageNumberSpawner label (uncapped/stacking) and the deleted
            // KnightWeaponskill impact burst as the perfect-hit read.
            FlowTrace.Once("Combat", "perfect-stamp",
                "PERFECT stamp armed: perfect-timed melee hits route CombatText(Perfect) — no impact burst on basic hits (owner 2026-08-02).");
            Guard.Try("Combat", "perfect-hit stamp", () =>
                CombatText.Show(CombatTextKind.Perfect, "PERFECT", hitPos + Vector3.up * 1.2f));

            // VFX-FREE-WIN-1: the PERFECT window's ONLY visual payoff was the ASCII stamp above.
            // A stamp is read by the eye that is already looking at the enemy; a flash at the
            // contact point is read by peripheral vision, which is what a timing mechanic needs
            // to teach itself under pressure. Juice_CriticalHit is ALREADY wired in
            // VFXCatalog.asset (Lana/Burst/Flash_star, IsLoop:0 — a ONESHOT, so it takes a
            // reclaimed oneshot slot and never one of the 20 leak-prone loop slots).
            //
            // This does NOT reinstate the owner-deleted basic-hit impact burst (owner ruling
            // 2026-08-02): that fired on EVERY connect. This fires only on a landed perfect tap.
            // The meaning is carried by the star SHAPE + the sudden flash, not by a colour, so it
            // survives the red/green colourblind law. playSound:false — the perfect chime is
            // already played by _perfectHitSound above; VFXManager must not layer a second cue.
            Guard.Try("Combat", "perfect-hit burst vfx", () =>
                VFXManager.Play(VFXType.Juice_CriticalHit, hitPos,
                                Quaternion.identity, playSound: false));
        }

        // ── Audio ─────────────────────────────────────────────────────────────

        private void PlayWhoosh()
        {
            if (_whooshSounds == null || _whooshSounds.Length == 0) return;
            var clip = _whooshSounds[Random.Range(0, _whooshSounds.Length)];
            if (clip == null) return;

            _audioSource.pitch = Random.Range(_whooshPitchRange.x, _whooshPitchRange.y);
            _audioSource.PlayOneShot(clip, 0.7f);
            StartCoroutine(ResetPitchAfter(clip.length));
        }

        private IEnumerator ResetPitchAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            _audioSource.pitch = 1f;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? EffectiveRange() : _attackRange);
        }
#endif
    }
}
