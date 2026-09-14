// =============================================================================
// Gate — one cardinal force-field gate MonoBehaviour (Week 4).
// -----------------------------------------------------------------------------
// Port spec Part 3 row: src/modules/village/walls/Gate.tsx -> Gate.cs.
//
// One MonoBehaviour per cardinal gate (N / E / S / W). VillageController
// instantiates one per WallLayout.Gates entry and calls Configure().
//
// WEEK 4 — this file gains the gameplay layer over the Week-3 skeleton:
//   - the gate TAKES DAMAGE (TakeDamage / Repair),
//   - the force field visibly COLLAPSES below 25% HP — the ForceFieldGate
//     shader's _Collapse property eases 0 -> 1 as HP falls through the
//     threshold band, tearing the violet sheet apart,
//   - the blocker collider TOGGLES OFF on collapse so the Hollow Ones can
//     pour through the opening (port spec Part 5 Week 4).
//
// Geometry comes from docs/four-cardinal-gates-spec.md: on the SQUARE wall a
// gate sits centred in one side, the wall sections meet the pillars flush, and
// gate HP rides on the buildingDamage map as gate-0 .. gate-3.
//
// WO-853 — WHY IT IMPLEMENTS *TWO* DAMAGE INTERFACES (the RaidSpire essay at
// World/Camps/RaidSpire.cs:8-31 is the reference):
//   IDamageableStructure — the seam ENEMIES use (Enemy.ProbeForStructure ->
//                          ApplyContactDamage).
//   IDamageable          — the seam the PLAYER and TROOPS use (PlayerAttackController.
//                          ResolveAttack / TroopController.NearestHostile sweep for
//                          GetComponentInParent<IDamageable>() and reject
//                          Faction != Hostile).
// All three damage entry points — TakeDamage(float), TakeDamage(float,DamageElement)
// and ApplyContactDamage — funnel into the single private ApplyDamage(), so the HP
// write, the collapse ramp, the blocker toggle and HpChanged can never diverge.
//
// SCOPE (WO-853 §6): there is NO Gate component in any RaidBase_* scene — a raid
// "gate" is a literal skipped wall panel. Gates matter for the PLAYER's own perimeter,
// where Faction reads Friendly and the player/troop sweeps correctly ignore them. The
// IDamageable half exists so the contract holds if a gate ever stands in an enemy scene,
// not because anything hostile owns one today.
//
// LAYER: untouched here. Gate pieces are put on "Structure" by their spawners
// (Village2Generator / BaseLayoutLoader) because that layer is the towers'
// line-of-sight blocker mask. Never move a gate to "Enemy" to make it findable.
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.Combat;        // IDamageable / IDamageableStructure / DamageElement
using DeNelle.Core.Diagnostics;   // FlowTrace (CLAUDE.md §12)

namespace DeNelle.Village
{
    /// <summary>Cardinal direction of a gate -- parallel to <c>GATE_DIRECTIONS</c> in gate.ts.</summary>
    public enum GateDirection
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    /// <summary>
    /// A single cardinal force-field gate centred in one side of the square
    /// wall. Holds its stable damage id (<c>gate-0</c> .. <c>gate-3</c>),
    /// direction, force-field HP, the shader-driven collapse state, and the
    /// blocker collider that toggles on collapse. Instantiated by
    /// <see cref="VillageController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Gate : MonoBehaviour, IDamageable, IDamageableStructure
    {
        /// <summary>
        /// §12 trace system tag. Deliberately NOT "WallSegment": the whole point of the damage
        /// traces below is that a gate and the wall beside it are indistinguishable to a player
        /// aiming at the perimeter, so their log lines must be distinguishable to us.
        /// </summary>
        private const string TraceSys = "Gate";

        [Header("Identity")]
        [Tooltip("Stable damage id -- gate-0 (N) .. gate-3 (W). From WallLayout.Gates.")]
        [SerializeField] private string _gateId;

        [Tooltip("Cardinal direction -- drives HUD copy / repair-modal headlines.")]
        [SerializeField] private GateDirection _direction = GateDirection.North;

        [Tooltip("Cardinal heading (radians) from village centre toward this gate's side.")]
        [SerializeField] private float _angle;

        [Header("Force field")]
        [Tooltip("Gate HP, 0-100. The force field collapses below 25%, letting Hollow Ones through.")]
        [SerializeField, Range(0f, 100f)] private float _hp = 100f;

        [Tooltip("Max gate HP, 0-100.")]
        [SerializeField, Range(0f, 100f)] private float _maxHp = 100f;

        [Tooltip("Collider that blocks enemies while the force field is up. Toggled off on collapse.")]
        [SerializeField] private BoxCollider _forceFieldCollider;

        [Tooltip("Renderer of the force-field sheet — its material runs ForceFieldGate.shader.")]
        [SerializeField] private Renderer _forceFieldRenderer;

        [Tooltip("Half-width (world units) of the gate opening. Matches WallLayout.GateHalfWidth.")]
        [SerializeField] private float _halfWidth = WallLayout.GateHalfWidth;

        [Header("Collapse tuning")]
        [Tooltip("Seconds the shader _Collapse property eases toward its target after an HP change.")]
        [SerializeField] private float _collapseEaseSeconds = 0.35f;

        /// <summary>HP fraction below which the force field collapses (Week 4 gameplay).</summary>
        public const float CollapseThreshold = 0.25f;

        /// <summary>Shader property id for the ForceFieldGate.shader collapse driver.</summary>
        private static readonly int CollapseId = Shader.PropertyToID("_Collapse");

        /// <summary>Fired when this gate's HP changes — HUD repair prompts subscribe.</summary>
        public event Action<Gate> HpChanged;

        /// <summary>Fired once when the force field collapses (HP crosses below 25%).</summary>
        public event Action<Gate> ForceFieldCollapsed;

        /// <summary>Fired once when a collapsed force field is restored above 25%.</summary>
        public event Action<Gate> ForceFieldRestored;

        // --- runtime ---
        private MaterialPropertyBlock _mpb;
        private float _collapseDisplayed;   // shader value, eased toward _collapseTarget
        private float _collapseTarget;      // 0 = full strength, 1 = collapsed
        private bool _wasForceFieldUp = true;
        // WO-08: set while the hero is inside a GateProximityOpener radius. An
        // ADDITIONAL reason to drop the field (the hero walks through), layered
        // on top of the existing HP-derived collapse — enemies never set it.
        private bool _isOpenForHero;

        /// <summary>Stable damage id -- <c>gate-0</c> .. <c>gate-3</c>.</summary>
        public string GateId => _gateId;

        /// <summary>Cardinal direction of this gate.</summary>
        public GateDirection Direction => _direction;

        /// <summary>Cardinal heading (radians) toward this gate's square side.</summary>
        public float Angle => _angle;

        /// <summary>Gate HP, 0-100.</summary>
        public float Hp => _hp;

        /// <summary>Max gate HP, 0-100.</summary>
        public float MaxHp => _maxHp;

        /// <summary>Gate HP as a 0..1 fraction.</summary>
        public float HpFraction => _maxHp > 0f ? Mathf.Clamp01(_hp / _maxHp) : 0f;

        /// <summary>True while the force field still blocks enemies (HP above the collapse threshold).</summary>
        public bool IsForceFieldUp => HpFraction > CollapseThreshold;

        /// <summary>
        /// True while the gate still has HP to attack. Satisfies BOTH
        /// <see cref="IDamageableStructure"/> (the enemy contact seam) and
        /// <see cref="IDamageable"/> (the player/troop seam) — one liveness answer, so the
        /// two contracts can never disagree about whether this gate is a target. Once HP
        /// hits zero the field is fully torn and enemies stop attacking and path through
        /// the opening. (The force field stops *blocking* earlier, at the 25% collapse
        /// threshold — see <see cref="IsForceFieldUp"/>.)
        /// </summary>
        public bool IsAlive => _hp > 0f;

        // =====================================================================
        //  IDamageable — the PLAYER + TROOP attack seam (WO-853)
        //  Hp / IsAlive above already satisfy the rest of the contract.
        // =====================================================================

        /// <summary>
        /// DERIVED from who owns the loaded scene, never serialized: a gate in an
        /// enemy-owned scene reads Hostile so the hero and troops would acquire it; the
        /// player's own Elarion perimeter reads Friendly and is rejected by the
        /// Faction != Hostile gate at every sweep site — which is what keeps a deployed
        /// troop from chewing on the player's own gate. A serialized field would let a
        /// prefab or a stale scene lie about allegiance.
        /// </summary>
        public CombatFaction Faction =>
            SceneOwnership.IsEnemyOwned ? CombatFaction.Hostile : CombatFaction.Friendly;

        /// <summary>World position — used by range / nearest-target queries.</summary>
        public Vector3 WorldPosition => transform.position;

        /// <summary>
        /// <see cref="IDamageable"/> attack entry — hero melee / abilities / troops / pets.
        /// Element is ignored: the force field models no elemental resists. Routes into the
        /// same <see cref="ApplyDamage"/> as the other two entry points, so it drives the
        /// shader collapse and the blocker toggle identically.
        /// </summary>
        public void TakeDamage(float amount, DamageElement element)
            => ApplyDamage(amount, applyToughness: false);

        /// <summary>
        /// <see cref="IDamageable"/> — a no-op. A gate does not move, so Slow/Freeze have
        /// nothing to act on, and Burn is owned by StructureBurn's own contact ticks.
        /// </summary>
        public void ApplyStatus(StatusEffect effect, float seconds) { /* a gate cannot be slowed, frozen or re-burned */ }

        /// <summary>
        /// Wires this gate from a <see cref="GateGap"/> layout record. Called by
        /// <see cref="VillageController"/> right after instantiation.
        /// </summary>
        /// <param name="gap">The <see cref="WallLayout"/> gate-gap record this gate fills.</param>
        public void Configure(GateGap gap)
        {
            _gateId = gap.Id;
            _direction = (GateDirection)Mathf.Clamp(gap.Index, 0, 3);
            _angle = gap.Angle;
            _halfWidth = WallLayout.GateHalfWidth;
            RebuildCollider();
            RefreshCollapseTarget(snap: true);
            ApplyForceFieldState();
        }

        /// <summary>
        /// Sets the gate's HP directly (e.g. restoring from a save). Drives the
        /// collapse visuals and the blocker toggle.
        /// </summary>
        public void SetHp(float hp, float maxHp = -1f)
        {
            if (maxHp > 0f) _maxHp = Mathf.Clamp(maxHp, 1f, 100f);
            _hp = Mathf.Clamp(hp, 0f, _maxHp);
            RefreshCollapseTarget(snap: true);
            ApplyForceFieldState();
            HpChanged?.Invoke(this);
        }

        /// <summary>
        /// Applies <paramref name="amount"/> damage to the gate. Below 25% HP
        /// the force field collapses — the blocker drops and the shader tears
        /// the violet sheet apart so enemies can pour through.
        /// </summary>
        /// <param name="amount">Damage on the 0–100 HP scale.</param>
        public void TakeDamage(float amount) => ApplyDamage(amount, applyToughness: false);

        /// <summary>
        /// THE single damage method. Every entry point lands here — the direct/scripted
        /// <see cref="TakeDamage(float)"/>, the player/troop
        /// <see cref="TakeDamage(float, DamageElement)"/>, and the enemy
        /// <see cref="ApplyContactDamage"/> — so the HP write, the collapse ramp, the
        /// blocker toggle and <see cref="HpChanged"/> can never differ between them.
        /// </summary>
        /// <param name="amount">Damage on the 0-100 HP scale. Non-positive is ignored.</param>
        /// <param name="applyToughness">
        /// True only on the ENEMY contact path: the hero's BULWARK structure-toughness
        /// talents are a defensive bonus against a siege, not a global damage resist, so
        /// the direct and player-attack paths deliberately pass false — matching the
        /// behaviour these two paths already had before they were unified.
        /// </param>
        private void ApplyDamage(float amount, bool applyToughness)
        {
            // §12 — a Gate and a WallSegment look IDENTICAL to a player aiming at the perimeter,
            // and they take damage under different rules (a gate has real HP and no tier divide).
            // This path carried NO trace at all, so a hit that landed on a gate instead of the
            // wall beside it was invisible in the log. Both the reject and the hit are now named,
            // throttled per instance, and say GATE explicitly so the two can never be confused.
            if (amount <= 0f || _hp <= 0f)
            {
                string rejectReason = _hp <= 0f ? "already-breached" : "non-positive-amount";
                FlowTrace.Throttle(TraceSys, $"gate-reject:{GetInstanceID()}", 1f,
                    $"Gate '{name}' REFUSED {amount:0.##} damage - {rejectReason}; " +
                    $"hp={_hp:0.#}/{_maxHp:0.#} faction={Faction}.");
                return;
            }

            float effective = amount;
            // WO-853 §9 — the BULWARK reduction is GATED ON FACTION. The hero's own
            // defensive talents must protect only the hero's own perimeter; applied to a
            // Hostile gate they would make an enemy structure up to 50% tougher, so
            // investing in defence would make attacking harder.
            if (applyToughness && Faction == CombatFaction.Friendly)
                effective *= 1f - WallSegment.StructureToughnessReduction("Gate");

            _hp = Mathf.Max(0f, _hp - effective);
            FlowTrace.Throttle(TraceSys, $"gate-hit:{GetInstanceID()}", 1f,
                $"Gate '{name}' took {amount:0.##} raw -> {effective:0.##} effective " +
                $"(toughness {applyToughness}, {Faction}) -> hp {_hp:0.#}/{_maxHp:0.#}.");
            RefreshCollapseTarget(snap: false);
            ApplyForceFieldState();
            HpChanged?.Invoke(this);
        }

        /// <summary>
        /// Repairs the gate by <paramref name="amount"/> HP (the village repair
        /// flow / a Workshop crew). Restores the force field if HP climbs back
        /// above the collapse threshold.
        /// </summary>
        /// <param name="amount">HP to restore on the 0–100 scale.</param>
        public void Repair(float amount)
        {
            if (amount <= 0f) return;
            _hp = Mathf.Min(_maxHp, _hp + amount);
            RefreshCollapseTarget(snap: false);
            ApplyForceFieldState();
            HpChanged?.Invoke(this);
        }

        /// <summary>
        /// <see cref="IDamageableStructure"/> contact-attack entry point — a
        /// Hollow One in melee contact with the force field routes its hit here.
        /// A thin adapter onto the shared <see cref="ApplyDamage"/>, which drives
        /// the shader collapse + blocker toggle. Once the field collapses below
        /// 25% the blocker drops and enemies pour through (port spec Week 4).
        /// WO-676 (BULWARK): the ENEMY intake is reduced by the hero's structure-
        /// toughness talents (Hardened Ramparts always-on + Warden of Elarion while
        /// the wave phase is Active, capped 0.5) via the shared
        /// <see cref="WallSegment.StructureToughnessReduction"/> reader — ×1 at Σ=0, and
        /// (WO-853 §9) only when <see cref="Faction"/> is Friendly. The direct
        /// <see cref="TakeDamage(float)"/> / <see cref="Repair"/> paths are untouched.
        /// </summary>
        public void ApplyContactDamage(float amount) => ApplyDamage(amount, applyToughness: true);

        /// <summary>True while a hero is inside this gate's proximity radius.</summary>
        public bool IsOpenForHero => _isOpenForHero;

        /// <summary>
        /// WO-08 — opens the force field for an approaching hero: eases the
        /// collapse to fully passable and drops the blocker so the hero walks
        /// through. Layered ON TOP of the HP-derived state, so the existing
        /// damage→collapse mechanic is untouched. Idempotent; called by
        /// <see cref="GateProximityOpener"/> on hero approach. Only the hero
        /// ever triggers this — enemies do not.
        /// </summary>
        public void RequestOpen()
        {
            if (_isOpenForHero) return;
            _isOpenForHero = true;
            RefreshCollapseTarget(snap: false);
            ApplyForceFieldState();
        }

        /// <summary>
        /// WO-08 — closes the force field again once the hero leaves the
        /// proximity radius: the collapse target reverts to whatever HP dictates
        /// and the blocker re-enables IF HP also wants the field up (a gate the
        /// enemies already battered below 25% stays open). Idempotent.
        /// </summary>
        public void RequestClose()
        {
            if (!_isOpenForHero) return;
            _isOpenForHero = false;
            RefreshCollapseTarget(snap: false);
            ApplyForceFieldState();
        }

        private void Awake()
        {
            if (_forceFieldCollider == null) _forceFieldCollider = GetComponent<BoxCollider>();
            if (_forceFieldRenderer == null) _forceFieldRenderer = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            ApplyFieldColor();          // BUG-016
            RefreshCollapseTarget(snap: true);
            ApplyForceFieldState();
        }

        // BUG-016: the force-field stand-in rendered WHITE — a fallback material
        // reads _BaseColor/_Color, which the ForceFieldGate.shader's violet
        // (_FieldColor) never sets. Push the canon violet to all three via the
        // shared MPB so the field is violet on the real shader AND any fallback
        // material; SetColor on a property the material's shader lacks is a no-op.
        private void ApplyFieldColor()
        {
            if (_forceFieldRenderer == null) return;
            var violet = new Color(0.486f, 0.227f, 0.929f, 1f); // ForceFieldGate.shader default
            _forceFieldRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_FieldColor", violet);
            _mpb.SetColor("_BaseColor", violet);
            _mpb.SetColor("_Color", violet);
            _forceFieldRenderer.SetPropertyBlock(_mpb);
        }

        private void Update()
        {
            // Ease the shader's _Collapse value toward its HP-derived target so
            // the field tears/heals smoothly rather than snapping.
            if (!Mathf.Approximately(_collapseDisplayed, _collapseTarget))
            {
                float step = _collapseEaseSeconds > 0f
                    ? Time.deltaTime / _collapseEaseSeconds
                    : 1f;
                _collapseDisplayed = Mathf.MoveTowards(_collapseDisplayed, _collapseTarget, step);
                PushCollapseToShader();
            }
        }

        /// <summary>
        /// Recomputes the shader collapse target from current HP. Below 25% HP
        /// the field eases toward fully collapsed (1); above it, toward 0.
        /// Between 25% and 0% the collapse ramps so the field looks like it is
        /// failing progressively, not popping off in one frame.
        /// </summary>
        private void RefreshCollapseTarget(bool snap)
        {
            float frac = HpFraction;
            if (_isOpenForHero)
            {
                // WO-08: hero in proximity — fully passable regardless of HP.
                _collapseTarget = 1f;
            }
            else if (frac > CollapseThreshold)
            {
                _collapseTarget = 0f;
            }
            else
            {
                // frac in [0, 0.25] -> collapse in [1, 0]; 0 HP = fully torn.
                _collapseTarget = 1f - Mathf.Clamp01(frac / CollapseThreshold);
            }

            if (snap)
            {
                _collapseDisplayed = _collapseTarget;
                PushCollapseToShader();
            }
        }

        /// <summary>
        /// Toggles the blocker collider with the force-field state and fires the
        /// collapse / restore events on the rising / falling edge.
        /// </summary>
        private void ApplyForceFieldState()
        {
            // WO-08: the blocker is up only when the HP-derived field is up AND no
            // hero is in proximity. Combined state, so the collapse/restore events
            // fire on the rising/falling edge of "blocking enemies right now".
            bool up = !_isOpenForHero && IsForceFieldUp;
            if (_forceFieldCollider != null)
                _forceFieldCollider.enabled = up; // blocker drops on collapse

            if (up != _wasForceFieldUp)
            {
                if (up) ForceFieldRestored?.Invoke(this);
                else ForceFieldCollapsed?.Invoke(this);
                _wasForceFieldUp = up;
            }
        }

        /// <summary>Writes the eased collapse value into the force-field material.</summary>
        private void PushCollapseToShader()
        {
            if (_forceFieldRenderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _forceFieldRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(CollapseId, _collapseDisplayed);
            _forceFieldRenderer.SetPropertyBlock(_mpb);
        }

        /// <summary>Sizes the force-field collider to span the gate opening.</summary>
        private void RebuildCollider()
        {
            if (_forceFieldCollider == null) _forceFieldCollider = GetComponent<BoxCollider>();
            if (_forceFieldCollider == null) _forceFieldCollider = gameObject.AddComponent<BoxCollider>();
            // Span runs along local X (matches the gate's WallLayout rotation rule).
            _forceFieldCollider.size = new Vector3(_halfWidth * 2f, 4f, WallLayout.WallThickness);
            _forceFieldCollider.center = new Vector3(0f, 2f, 0f);
            // A solid blocker while the field is up; ApplyForceFieldState()
            // toggles .enabled off when the field collapses.
            _forceFieldCollider.isTrigger = false;
        }
    }
}
