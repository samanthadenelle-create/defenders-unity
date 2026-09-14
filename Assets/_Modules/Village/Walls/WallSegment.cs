// =============================================================================
// WallSegment — one wall-ring section MonoBehaviour (Week-3 skeleton).
// -----------------------------------------------------------------------------
// Port spec Part 3 row: src/modules/village/walls/KayWalls.tsx -> WallSegment.cs.
//
// One MonoBehaviour per wall section. TWO spawners produce them:
//   * VillageController — one per WallLayout.Segments entry, then Configure() to wire
//     the section's identity + footprint (the PLAYER's Elarion perimeter).
//   * RaidBaseGenerator.PlaceSegment (Editor/WallTools, :982) — one per raid-base wall
//     panel, WITHOUT calling Configure() (it authors the BoxCollider + Structure layer
//     itself). These are ENEMY walls and they bake into the RaidBase_* scenes.
// The second spawner is why every ownership question below is answered from
// SceneOwnership rather than assumed player-owned (see StructureToughnessReduction).
//
// The actual KayKit straight-wall mesh is supplied as a child by the scene
// builder; this component just owns the section's data + collider sizing.
//
// WHY IT IMPLEMENTS *TWO* DAMAGE INTERFACES (WO-853, mirroring the RaidSpire essay at
// World/Camps/RaidSpire.cs:8-31):
//   IDamageableStructure — the seam ENEMIES use (Enemy.ProbeForStructure ->
//                          ApplyContactDamage). A wall only ever had this one, so it
//                          could be HIT but never FOUND by a search.
//   IDamageable          — the seam the PLAYER and TROOPS use. PlayerAttackController.
//                          ResolveAttack and TroopController.NearestHostile sweep for
//                          GetComponentInParent<IDamageable>() and reject
//                          Faction != Hostile. Without this a raid wall is indestructible.
// Both entry points funnel into the single private ApplyDamage() so the enemy path and
// the player path can never diverge.
//
// LAYER — DELIBERATELY *NOT* THE RaidSpire TRICK. RaidSpire makes itself findable by
// moving onto the "Enemy" layer. (BreakableContainer used to do the same and was named
// here; WO-1132 removed that relayer when the container became an openable chest —
// precisely because it made every crate a hostile-reticle target, WO-1047. So the trick
// now has ONE user, not two, which is itself the argument against it.)
// A wall MUST NOT copy it: "Structure" is
// the line-of-sight BLOCKER mask every tower linecasts against (DefenseTower.
// BlockedByWall, TowerCombat, ArcaneTower, PlayerAttackController, HeroTargetIndicator).
// Relayering a wall would make towers shoot through walls again. Walls stay on
// "Structure" and the target masks widen instead.
// =============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;             // NavMeshObstacle — dropped on collapse (see Collapse)
using DeNelle.Core.Catalog;       // RepoProps.MaxStructureLevel — the SINGLE structure ceiling
using DeNelle.Core.Combat;        // IDamageable / IDamageableStructure / DamageElement
using DeNelle.Core.Diagnostics;   // FlowTrace / Guard (CLAUDE.md S12)

namespace DeNelle.Village
{
    /// <summary>
    /// A single section of the square wall ring. Holds the section's stable
    /// damage id, its <see cref="WallLayout"/> source data, and (Week 4+) its
    /// damage HP. Instantiated by <see cref="VillageController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallSegment : MonoBehaviour, IDamageable, IDamageableStructure
    {
        private const string Sys = "WallSegment";

        [Header("Identity")]
        [Tooltip("Stable damage id from WallLayout -- wall-<index>.")]
        [SerializeField] private string _segmentId;

        [Tooltip("Ordinal index in the generated WallLayout.Segments list.")]
        [SerializeField] private int _segmentIndex;

        [Tooltip("True for the four corner pieces (short square block, stands taller).")]
        [SerializeField] private bool _isCorner;

        [Header("Footprint")]
        [Tooltip("Length (world units) of this section along its side.")]
        [SerializeField] private float _length = 1f;

        [Tooltip("Thickness (world units) -- the section's short radial axis.")]
        [SerializeField] private float _thickness = WallLayout.WallThickness;

        [Tooltip("Wall height (world units). Set per tier by VillageController.")]
        [SerializeField] private float _height = 3f;

        [Header("State (Week 4+)")]
        [Tooltip("Accumulated damage 0-100. 100 = collapsed to rubble. Wired in Week 4.")]
        [SerializeField, Range(0f, 100f)] private float _damage;

        [Tooltip("Box collider blocking the hero / enemies on this section.")]
        [SerializeField] private BoxCollider _blocker;

        [Header("Tier (S5 — wood→stone→reinforced→…)")]
        [Tooltip("Upgrade tier (1..RepoProps.MaxStructureLevel). Higher tiers absorb contact damage " +
                 "more slowly (effective HP scales x1.6 per tier) — the build-mode wall-tier sink.")]
        [SerializeField] private int _tier = 1;

        [Header("Collapse (WO-853)")]
        [Tooltip("Seconds the razed section takes to slump into the ground. Readability only — " +
                 "the collider drop and the Collapsed event both fire on frame 0.")]
        [SerializeField, Min(0.01f)] private float _collapseSeconds = 0.9f;

        // WO-1480 — the effective-HP multiplier is DERIVED, not tabled. It used to be the
        // literal array { 1f, 1f, 1.6f, 2.56f }, which defined a divisor for tiers 1..3 ONLY;
        // paired with SetTier's literal 1..3 clamp that was a NINTH hardcoded structure ceiling
        // (WO-1108b replaced eight of them with RepoProps.MaxStructureLevel = 6 and missed this
        // one). A geometric step reproduces the old numbers EXACTLY at the tiers that existed
        // (x1 / x1.6 / x2.56) and keeps every level the ceiling now admits defined, so a level-4
        // wall can never silently take level-3 damage reduction.
        private const float TierToughnessStep = 1.6f;

        /// <summary>Full health on the wall's inverted 0-100 damage track (Damage 0 == MaxHp).</summary>
        public const float MaxHp = 100f;

        // Fraction of the section's own height it sinks on collapse. Below 1 on purpose:
        // a stub of rubble stays above ground so the razed section READS as rubble rather
        // than as a piece that silently vanished.
        private const float SinkFraction = 0.85f;

        // Shader property the Gate's ForceFieldGate.shader ramps on destruction
        // (Gate.cs:81). Pushed here on the same MaterialPropertyBlock shape so a wall
        // whose material declares _Collapse tears the same way. On a plain URP/Lit wall
        // material the property does not exist and SetFloat is a silent no-op — which is
        // exactly why the sink below, not this ramp, is the tell that always reads.
        private static readonly int CollapseId = Shader.PropertyToID("_Collapse");

        // ---- Collapse runtime ------------------------------------------------
        private MaterialPropertyBlock _mpb;
        private bool _collapsed;

        /// <summary>Stable damage id -- <c>wall-&lt;index&gt;</c>.</summary>
        public string SegmentId => _segmentId;

        /// <summary>Ordinal index in <see cref="WallLayout.Segments"/>.</summary>
        public int SegmentIndex => _segmentIndex;

        /// <summary>True for the four square corner pieces.</summary>
        public bool IsCorner => _isCorner;

        /// <summary>Length (world units) of this section along its side.</summary>
        public float Length => _length;

        /// <summary>Wall height (world units) for the current tier.</summary>
        public float Height => _height;

        /// <summary>Accumulated damage, 0-100. 100 = destroyed (Week 4+).</summary>
        public float Damage => _damage;

        /// <summary>True once the section has taken full damage (Week 4+).</summary>
        public bool IsDestroyed => _damage >= 100f;

        /// <summary>Upgrade tier (1..<see cref="MaxTier"/>). Higher = tougher (S5 wall-tier sink).</summary>
        public int Tier => _tier;

        /// <summary>
        /// WO-1480 — the highest tier a wall may hold, read from the SINGLE structure ceiling
        /// (<see cref="RepoProps.MaxStructureLevel"/>) rather than restated as a literal. A row
        /// still opts in with its own <c>repo.maxLevel</c> (<c>wall_wood</c> authors 2 today);
        /// this is only the hard bound the clamp may never exceed.
        /// </summary>
        public static int MaxTier => RepoProps.MaxStructureLevel;

        /// <summary>
        /// WO-1480 — the effective-HP divisor for a tier, defined for EVERY tier the clamp
        /// admits (1..<see cref="MaxTier"/>) instead of for a tabled 1..3. Tier 1 is x1 and each
        /// step multiplies by <see cref="TierToughnessStep"/> (1.6), which reproduces the old
        /// table exactly: 1 → x1, 2 → x1.6, 3 → x2.56.
        /// </summary>
        public static float ToughnessFor(int tier)
        {
            int t = Mathf.Clamp(tier, 1, MaxTier);
            return Mathf.Pow(TierToughnessStep, t - 1);
        }

        /// <summary>
        /// S5 — set the wall's upgrade tier (clamped 1..<see cref="MaxTier"/>). Higher tiers divide incoming
        /// contact damage by a per-tier toughness factor (~x1.6 effective HP per tier), so a
        /// reinforced wall wears down far slower. The tier accent tint is owned by
        /// StructureTierVisual; this is the gameplay (durability) half of the upgrade.
        /// WO-948: the tier also pulls its wall HEIGHT from walls.json (targetHeight for
        /// ladder level tier-1) into the blocker collider — see ApplyTierBlockerHeight.
        /// </summary>
        public void SetTier(int tier)
        {
            _tier = Mathf.Clamp(tier, 1, MaxTier);
            ApplyTierBlockerHeight();
        }

        /// <summary>
        /// WO-948 — data-driven wall height. walls.json authors a targetHeight per ladder
        /// level (L0 wood 3.0 → L1 stone 3.8 → ...); a tier step raises the blocker collider
        /// to match, HEIGHT ONLY: for a build-mode placed wall the collider on this root is
        /// the footprint blocker (BaseLayoutLoader.AddFootprintBlocker), whose X/Z are
        /// footprint-derived and must never be touched (resizing them re-opens the
        /// "towers shoot through walls" / pathable-gap class). Ladder level = tier - 1
        /// (matches the existing toughness semantics, where a placed row's L1 is its base
        /// tier). No collider = traced no-op (a bare edit-mode test object), never a throw.
        /// </summary>
        private void ApplyTierBlockerHeight()
        {
            // SCOPE: player build-mode walls ONLY (PlacedStructure marker). SetTier is also
            // called by the editor bake tools (RaidBaseGenerator :989, PerimeterWallGenerator,
            // GridWallBuilder), whose walls author their own colliders — a data-height override
            // there would silently re-size baked raid/perimeter scenes.
            if (GetComponent<PlacedStructure>() == null) return;

            // WO-1480: no literal bound here either. WallDefense.TargetHeight already clamps the
            // ladder level into its OWN authored walls.json table (0..tiers-1), so a tier the
            // structure ceiling now admits above that table keeps the top authored height rather
            // than reading a restated-and-drifting 3 from this side.
            float h = Walls.WallDefense.TargetHeight(_tier - 1);
            if (h <= 0f || Mathf.Approximately(h, _height)) return;

            var box = _blocker != null ? _blocker : GetComponent<BoxCollider>();
            if (box == null)
            {
                FlowTrace.Warn(Sys,
                    $"WallSegment '{name}' tier {_tier}: no BoxCollider to apply walls.json targetHeight {h:0.0}m to — height unchanged.");
                return;
            }
            _height = h;
            var size = box.size;
            var center = box.center;
            size.y = h;
            center.y = h * 0.5f;
            box.size = size;
            box.center = center;
            FlowTrace.Step(Sys,
                $"WallSegment '{name}' tier {_tier}: blocker height -> {h:0.0}m (walls.json targetHeight, level {_tier - 1}; footprint X/Z untouched).");
        }

        /// <summary>
        /// True while the section still stands and can be attacked. Satisfies BOTH
        /// <see cref="IDamageableStructure"/> (the enemy contact seam) and
        /// <see cref="IDamageable"/> (the player/troop seam) — one liveness answer, so
        /// the two contracts can never disagree about whether this wall is a target.
        /// </summary>
        public bool IsAlive => _damage < 100f;

        // =====================================================================
        //  IDamageable — the PLAYER + TROOP attack seam (WO-853)
        // =====================================================================

        /// <summary>
        /// DERIVED from who owns the loaded scene, never serialized: a wall in an
        /// enemy-owned scene (a baked RaidBase_*, flipped by RaidGarrisonSpawner via
        /// <see cref="SceneOwnership.SetEnemyOwned"/>) is Hostile so the hero and troops
        /// will acquire it; the player's own Elarion perimeter reads Friendly and is
        /// rejected by the Faction != Hostile gate at every sweep site. A serialized
        /// field would let a prefab or a stale scene lie about allegiance.
        /// </summary>
        public CombatFaction Faction =>
            SceneOwnership.IsEnemyOwned ? CombatFaction.Hostile : CombatFaction.Friendly;

        /// <summary>World position — used by range / nearest-target queries.</summary>
        public Vector3 WorldPosition => transform.position;

        /// <summary>
        /// Remaining health on the 0-100 scale every other IDamageable uses. This
        /// component's stored model is INVERTED (<see cref="Damage"/> counts UP from 0 to
        /// 100 and there is no HP field), so this is the reading of that same single
        /// track from the other end: <c>MaxHp - Damage</c>. Nothing is double-booked —
        /// writing Damage moves Hp and vice versa.
        /// </summary>
        public float Hp => Mathf.Max(0f, MaxHp - _damage);

        /// <summary>Remaining health as 0..1 (0 once collapsed).</summary>
        public float HpFraction => Mathf.Clamp01(Hp / MaxHp);

        /// <summary>
        /// <see cref="IDamageable"/> attack entry — hero melee / abilities / troops /
        /// pets. Element is ignored: stone carries no elemental resists. Routes into the
        /// same <see cref="ApplyDamage"/> the enemy contact path uses.
        /// </summary>
        public void TakeDamage(float amount, DamageElement element) => ApplyDamage(amount, "attack");

        /// <summary>
        /// <see cref="IDamageable"/> — a no-op. A wall does not move, so Slow/Freeze have
        /// nothing to act on, and Burn is owned by StructureBurn's own contact ticks.
        /// </summary>
        public void ApplyStatus(StatusEffect effect, float seconds) { /* a wall cannot be slowed, frozen or re-burned */ }

        /// <summary>
        /// Raised whenever the section's accumulated damage changes — carries
        /// the new 0-100 damage value. HUD / rubble swap subscribe.
        /// </summary>
        public event Action<float> DamageChanged;

        /// <summary>Raised once when the section's damage reaches 100 (collapsed to rubble).</summary>
        public event Action<WallSegment> Collapsed;

        /// <summary>
        /// Wires this section from a <see cref="WallSegmentData"/> layout record.
        /// Called by <see cref="VillageController"/> right after instantiation.
        /// Sizes the box collider to the section footprint.
        /// </summary>
        /// <param name="data">The <see cref="WallLayout"/> record this section renders.</param>
        /// <param name="height">Wall height for the current tier (world units).</param>
        public void Configure(WallSegmentData data, float height)
        {
            _segmentId = data.Id;
            _segmentIndex = data.Index;
            _isCorner = data.Corner;
            _length = data.Length;
            _thickness = WallLayout.WallThickness;
            _height = height;
            RebuildCollider();
        }

        /// <summary>
        /// <see cref="IDamageableStructure"/> contact-attack entry point — an
        /// enemy in melee contact with this section routes its hit here.
        /// Accumulates onto <see cref="Damage"/> (clamped 0-100); at 100 the
        /// section is rubble and <see cref="Collapsed"/> fires. Closes
        /// week4-waves.md integration item 5 — enemies can wear walls down
        /// instead of pathing straight through (port spec Week 4).
        /// </summary>
        /// <param name="amount">Damage to accumulate. Non-positive values are ignored.</param>
        public void ApplyContactDamage(float amount) => ApplyDamage(amount, "contact");

        /// <summary>
        /// THE single damage method. Both damage seams land here — the enemy contact path
        /// (<see cref="ApplyContactDamage"/>) and the player/troop path
        /// (<see cref="TakeDamage(float, DamageElement)"/>) — so tier toughness, the BULWARK
        /// reduction, the clamp, the events and the collapse can never differ between them.
        /// </summary>
        /// <param name="amount">Damage before tier / talent reduction. Non-positive is ignored.</param>
        /// <param name="via">Trace label for which seam delivered the hit.</param>
        private void ApplyDamage(float amount, string via)
        {
            // §12 — THE SILENT REJECT, MADE LOUD. This guard used to `return` with no line at
            // all, which makes "the hit landed and was refused" byte-identical in the log to
            // "the swing never reached this wall" — the exact ambiguity behind the reported
            // "attacking a segment shows no damage" symptom. A reject here with IsDestroyed=true
            // while the player still SEES a standing wall is the collapse-without-visual case;
            // a reject with a non-positive amount is an upstream damage-calc fault instead.
            // Throttled per instance: a warband chewing one dead panel would otherwise emit a
            // line per troop per tick (memory: logcat-ring-buffer-destroys-evidence).
            if (amount <= 0f || IsDestroyed)
            {
                string rejectReason = IsDestroyed ? "already-destroyed" : "non-positive-amount";
                FlowTrace.Throttle(Sys, $"wall-reject:{GetInstanceID()}", 1f,
                    $"WallSegment '{name}' REFUSED {amount:0.##} damage ({via}) - {rejectReason}; " +
                    $"damage={_damage:0}/100 collapsed={_collapsed} faction={Faction}.");
                return;
            }

            // S5 — higher tiers absorb the hit more slowly (effective-HP scaling on the
            // shared 0-100 track). The collapse threshold stays 100; only the rate changes.
            // WO-1480: the divisor is derived across the WHOLE admissible range, so a wall the
            // clamp now lets reach level 4+ no longer takes level-3 reduction off a 1..3 table.
            int t = Mathf.Clamp(_tier, 1, MaxTier);
            float effective = amount / ToughnessFor(t);

            // WO-676 (BULWARK): Hardened Ramparts (structureToughness, always-on) +
            // Warden of Elarion (structureToughnessWave, only while the wave phase is
            // Active) reduce the intake ON TOP of the tier divide. Σ=0 → ×1 (unchanged).
            // WO-853 §9 — GATED ON FACTION. The hero's own defensive talents must protect
            // only the hero's own walls; on an enemy raid wall (Faction == Hostile) they
            // used to make the target up to 50% tougher, so investing in defence made
            // raiding harder.
            // §12: the reduction is CAPTURED, not just applied, so the trace below can show
            // whether a hit was eaten by the BULWARK gate rather than by the tier divide.
            // Behaviour is byte-identical — the reader is still called only when Friendly.
            float bulwark = 0f;
            if (Faction == CombatFaction.Friendly)
            {
                bulwark = StructureToughnessReduction("WallSegment");
                effective *= 1f - bulwark;
            }

            _damage = Mathf.Clamp(_damage + effective, 0f, 100f);
            DamageChanged?.Invoke(_damage);
            // §12 — carries RAW amount -> effective, so a hit that arrives fat and lands thin
            // names its own attenuator (tier divisor vs BULWARK) instead of needing a second line.
            FlowTrace.Throttle(Sys, $"wall-hit:{GetInstanceID()}", 1f,
                $"WallSegment '{name}' took {amount:0.##} raw -> {effective:0.##} effective " +
                $"({via}, tier {t}, toughDiv {ToughnessFor(t):0.##}, bulwark {bulwark:P0}, {Faction}) -> " +
                $"damage {_damage:0}/100 ({HpFraction:P0} standing).");

            if (_damage >= 100f) Collapse();
        }

        /// <summary>
        /// Razes the section: drops every solid collider and any carving NavMeshObstacle so
        /// it stops blocking BOTH tower line-of-sight (the Structure-mask linecasts) and
        /// NavMeshAgent pathing, raises <see cref="Collapsed"/>, then slumps the ruin so the
        /// kill reads. Runs exactly once — a razed section is never re-armed (WO-753:
        /// destroyed is destroyed; <see cref="Repair"/> already refuses to revive it).
        /// </summary>
        private void Collapse()
        {
            if (_collapsed) return;
            _collapsed = true;

            // Stop blocking BEFORE the event fires, so any subscriber that re-queries
            // physics or the navmesh already sees the opening.
            int colliders = 0;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                c.enabled = false;
                colliders++;
            }

            // WallNavObstacleInstaller / the raid bakes fit CARVING NavMeshObstacles to wall
            // pieces so they block agents without a rebake. Disabling the obstacle hands the
            // carved navmesh back, which is what actually lets an agent walk through the gap
            // — a collider alone never carved anything. Zero found is normal for a segment
            // whose barrier obstacle lives on a sibling object.
            int obstacles = 0;
            foreach (var o in GetComponentsInChildren<NavMeshObstacle>(true))
            {
                if (o == null || !o.enabled) continue;
                o.enabled = false;
                obstacles++;
            }

            FlowTrace.Step(Sys, $"WallSegment '{name}' ({Faction}) COLLAPSED: {colliders} solid collider(s) " +
                                $"and {obstacles} carving obstacle(s) dropped - it no longer blocks tower " +
                                "line-of-sight or agent pathing.");

            Collapsed?.Invoke(this);

            // Readability only, and guarded so a presentation fault can never swallow the
            // collapse above. Skipped outside play mode: the edit-mode regression harness
            // drives walls to 100 damage on bare GameObjects, where StartCoroutine cannot run.
            if (!Application.isPlaying) return;
            Guard.Try(Sys, "wall collapse visual", () => StartCoroutine(CollapseRoutine()));
        }

        /// <summary>
        /// The visible tell. Mirrors the shape of the one real destruction tell in the game
        /// — Gate's eased <c>_Collapse</c> ramp pushed through a MaterialPropertyBlock
        /// (Gate.cs:285-339) — and adds an accelerating sink, because a wall's KayKit
        /// material has no <c>_Collapse</c> property for the ramp alone to drive.
        /// </summary>
        private IEnumerator CollapseRoutine()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);

            // §12 — Guard.Try around StartCoroutine catches only a throw BEFORE the first yield;
            // a fault on any later frame is silent, and so is "zero renderers", which sinks the
            // transform while leaving nothing visibly moving. Logging the renderer count at the
            // START and a settle line at the END makes the visual half of the collapse
            // falsifiable: COLLAPSED with no `ruin settled` line = the tell died mid-routine,
            // which is a DIFFERENT bug from the collapse never firing. Once per segment.
            int rendererCount = renderers != null ? renderers.Length : 0;
            FlowTrace.Once(Sys, $"wall-collapse-visual:{GetInstanceID()}",
                $"WallSegment '{name}' collapse tell STARTED over {rendererCount} renderer(s), " +
                $"{_collapseSeconds:0.##}s sink.");

            // Sink distance from the actual art bounds when there is art, else the
            // configured tier height (raid walls never get Configure()'d, so _height
            // sits at its serialized default for them).
            float span = Mathf.Max(1f, _height);
            if (renderers != null && renderers.Length > 0 && renderers[0] != null)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    if (renderers[i] != null) b.Encapsulate(renderers[i].bounds);
                span = Mathf.Max(1f, b.size.y);
            }

            Vector3 from = transform.position;
            Vector3 to = from + Vector3.down * (span * SinkFraction);
            float dur = Mathf.Max(0.01f, _collapseSeconds);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                transform.position = Vector3.Lerp(from, to, k * k);   // accelerating fall
                PushCollapseRamp(renderers, k);
                yield return null;
            }
            transform.position = to;
            PushCollapseRamp(renderers, 1f);

            // §12 — the terminal proof. Absence of this line on a segment that logged COLLAPSED
            // is the collapse-without-visual desync; presence means the ruin really did sink and
            // the symptom lies elsewhere (e.g. the art is a sibling the sink never moved).
            FlowTrace.Once(Sys, $"wall-collapse-settled:{GetInstanceID()}",
                $"WallSegment '{name}' ruin SETTLED at y={to.y:0.##} (fell {(from.y - to.y):0.##}m).");
        }

        /// <summary>
        /// Writes the 0..1 collapse value into every renderer's MaterialPropertyBlock.
        /// GetPropertyBlock first so StructureTierVisual's tier tint on the same renderer
        /// survives; SetFloat on a shader without the property is a no-op, never an error.
        /// </summary>
        private void PushCollapseRamp(Renderer[] renderers, float value)
        {
            if (renderers == null) return;
            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(CollapseId, value);
                r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>
        /// WO-676 (BULWARK) — the hero's structure-toughness talents, read at the damage
        /// INTAKE choke point the way HeroHealth.TakeDamage consumes HeroTalentModifiers:
        /// `structureToughness` (Hardened Ramparts) is always-on; `structureToughnessWave`
        /// (Warden of Elarion) is added ONLY while <see cref="WaveManager.Phase"/> is
        /// <see cref="WavePhase.Active"/> (null-safe instance check — mirrors
        /// OfflineHarvestService.IsCombatActive). Total reduction capped at 0.5 (WO-676 G2).
        /// Returns 0 with no service / no unlocked nodes — byte-identical baseline.
        ///
        /// OWNERSHIP IS THE CALLER'S JOB. This reader answers "what is the hero's talent
        /// reduction", nothing more. Its prior doc claimed enemy strongholds do not use
        /// WallSegment/Gate and therefore needed no ownership gate — that was FALSE
        /// (RaidBaseGenerator.PlaceSegment adds a WallSegment to every raid wall panel), and
        /// the missing gate meant BULWARK made enemy walls up to 50% tougher. Every caller
        /// now checks <see cref="Faction"/> first; do not drop that check (WO-853 §9).
        /// </summary>
        internal static float StructureToughnessReduction(string traceSystem)
        {
            var hero = HeroHealth.Instance;
            var abilities = hero != null ? hero.GetComponent<HeroAbilities>() : null;
            string heroClass = abilities != null ? abilities.HeroClass : "knight";

            var wm = WaveManager.Instance;
            bool waveActive = wm != null && wm.Phase == WavePhase.Active;
            float reduction = Talents.HeroTalentModifiers.StructureToughnessReduction(
                heroClass, waveActive);

            if (reduction > 0f)
                DeNelle.Core.Diagnostics.FlowTrace.Once(traceSystem, "talent-structureToughness",
                    $"BULWARK structureToughness applied: -{reduction:P0} intake " +
                    $"(canonical talent authority, waveActive={waveActive}, cap 0.5).");
            return reduction;
        }

        /// <summary>
        /// Repairs the section, reducing accumulated damage (the village repair
        /// flow). Clamped at 0.
        /// </summary>
        /// <param name="amount">Damage to remove. Non-positive values are ignored.</param>
        // Saved condition is already post-mitigation. Reconstruction must not apply talents twice.
        public void RestoreOwnedTownCondition(float condition)
        {
            if (gameObject.scene.name != DeNelle.Village.World.Camps.OwnedTownScenePose.SceneName &&
                gameObject.scene.name != DeNelle.Core.Combat.PracticeCombatPolicy.SceneName)
                throw new System.InvalidOperationException("Owned-town restoration requires its isolated scene.");
            if (float.IsNaN(condition) || float.IsInfinity(condition) || condition < 0 || condition > 1 || _collapsed)
                throw new System.InvalidOperationException("Owned-town condition requires a fresh valid structure.");
            _damage = (1f - condition) * 100f;
            DamageChanged?.Invoke(_damage);
            if (_damage >= 100f) Collapse();
        }

        public void Repair(float amount)
        {
            // WO-753 ruling (owner 2026-07-19, SUPERSEDES WO-672's repair-back-online): a DESTROYED
            // (collapsed-to-rubble) section is LOST - it returns ONLY via a full-cost build-mode
            // placement, never an in-place repair. Mirrors the guard Building.Repair already carries.
            if (IsDestroyed) return;
            if (amount <= 0f) return;
            _damage = Mathf.Clamp(_damage - amount, 0f, 100f);
            DamageChanged?.Invoke(_damage);
        }

        private void Awake()
        {
            if (_blocker == null) _blocker = GetComponent<BoxCollider>();
        }

        /// <summary>Sizes the box collider to the section's box footprint.</summary>
        private void RebuildCollider()
        {
            if (_blocker == null) _blocker = GetComponent<BoxCollider>();
            if (_blocker == null) _blocker = gameObject.AddComponent<BoxCollider>();
            // Long axis is local X (matches the WallLayout rotation rule).
            _blocker.size = new Vector3(_length, _height, _thickness);
            _blocker.center = new Vector3(0f, _height * 0.5f, 0f);

            // "towers shoot through walls" fix (owner 2026-07): put the wall on the "Structure"
            // physics layer so the towers' line-of-sight linecast (TowerCombat.BlockedByWall +
            // DefenseTower/ArcaneTower) — which is masked to "Structure" — actually HITS this
            // collider. Without it the shot passes straight through. GUARD: NameToLayer returns
            // -1 when the layer is absent; only assign a real layer so a misconfigured project
            // is left untouched rather than moved to layer 0.
            int structureLayer = LayerMask.NameToLayer("Structure");
            if (structureLayer >= 0) gameObject.layer = structureLayer;
        }
    }
}
