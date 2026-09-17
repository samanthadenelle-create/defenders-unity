// =============================================================================
// RaidSpire - the RAID OBJECTIVE: a high-HP central structure that, when it
// falls, WINS the raid. Replaces "kill every body in the camp" as the win
// condition (owner concept 2026-08-02).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// WHY IT IMPLEMENTS *TWO* INTERFACES (this is the load-bearing detail):
//
//   IDamageableStructure  is the seam ENEMIES use to hurt things (Enemy.TryAttack,
//                         DragonBoss.DealStrike, EnemyOwned DefenseTower.FireAtParty
//                         -> target.ApplyContactDamage). It is NOT how the PLAYER
//                         hurts anything.
//   IDamageable           is the seam the PLAYER uses. Verified in source:
//                         PlayerAttackController.ResolveAttack (:592-611) does
//                         Physics.OverlapSphere(pos, reach, _enemyLayer) then
//                         col.GetComponentInParent<IDamageable>() and REJECTS
//                         anything whose Faction != CombatFaction.Hostile.
//                         TroopController.NearestHostile (:449-470) does the same
//                         with its own enemy LayerMask.
//
// So a spire that implemented ONLY IDamageableStructure would be UNKILLABLE by the
// hero and by every deployed troop - i.e. the raid would be unwinnable. Both are
// implemented here. The single public IsAlive satisfies both contracts.
//
// ⚠ THE PRECEDENT THIS USED TO CITE IS GONE. This paragraph named BreakableContainer
// as "the ONE existing precedent for a player-destructible world object" (dual-contract
// + Enemy layer). WO-1132 (owner ruling 2026-08-21) turned that container into an
// OPENABLE CHEST: it implements neither damage interface now and is not on the Enemy
// layer. The spire is a genuine combat target and KEEPS both contracts — but it is now
// the precedent, not the follower, and a seat reading BreakableContainer.cs for the
// pattern will not find it there any more.
//
// LAYER (equally load-bearing): the hero's sweep is MASKED to the "Enemy" layer, so
// the spire's solid collider is moved onto it in Awake. Without that the hero's
// OverlapSphere never returns the spire no matter what interfaces it carries.
//
// NOT A BESPOKE DAMAGE CLASS: it owns no damage maths of its own - it is a plain HP
// bucket on the two shipped seams. Everything that can already hurt a structure or
// an enemy hurts the spire for free (hero melee, hero abilities, troops, pets, DoT,
// StructureBurn).
//
// LIFECYCLE: at 0 HP it fires OnDestroyed (RaidVictoryController wins the raid off
// it), sinks into the ground over CollapseSeconds so the kill READS, and stops being
// a target. It is never re-armed - a razed spire stays razed (owner ruling WO-753:
// destroyed is destroyed).
//
// Instrumented per CLAUDE.md S12: Step on configure + on the kill, Throttle on hits.
// ASCII-only runtime strings. Canon: the village is Elarion (never Avalon).
// =============================================================================

using System.Collections;
using UnityEngine;
using DeNelle.Core.Combat;        // IDamageable / IDamageableStructure / DamageElement
using DeNelle.Core.Diagnostics;   // FlowTrace (CLAUDE.md S12)

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// The raid's central objective structure. Destroying it wins the raid.
    /// Implements <see cref="IDamageable"/> (the player/troop attack seam) AND
    /// <see cref="IDamageableStructure"/> (the enemy contact-damage seam) so every
    /// shipped damage source can hurt it and none of them needed changing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidSpire : MonoBehaviour, IDamageable, IDamageableStructure
    {
        private const string Sys = "Raid";

        /// <summary>Marker name the generator bakes, so scene tools can find the spire.</summary>
        public const string ObjectName = "RaidSpire";

        // ---- Authored at bake time by RaidBaseGenerator.PlaceSpire ------------

        [Header("Objective")]
        [Tooltip("Hit points. Baked from the scene-config's difficulty tier " +
                 "(Regular 1200 / Hard 2200 / Extreme 3500).")]
        [SerializeField, Min(1f)] private float _maxHp = 1200f;

        [Tooltip("scene-configs.json id this spire was generated for (trace/report only).")]
        [SerializeField] private string _configId = "";

        [Tooltip("structures-catalog id the spire's art came from (the config's centralBuilding).")]
        [SerializeField] private string _catalogId = "";

        [Tooltip("Seconds the razed spire takes to sink into the ground (readability only).")]
        [SerializeField, Min(0f)] private float _collapseSeconds = 1.4f;

        [Tooltip("Approximate visual height (m) - how far the collapse sinks it.")]
        [SerializeField, Min(1f)] private float _visualHeight = 9f;

        // ---- Runtime ---------------------------------------------------------

        private float _hp = -1f;      // <0 = not yet initialised
        private bool _destroyed;

        /// <summary>The live spire for the current raid scene (null when there is none).</summary>
        public static RaidSpire Active { get; private set; }

#if UNITY_EDITOR
        /// <summary>
        /// WO-1595 EditMode capture — scene open without Play skips Awake, so Active stays null
        /// and formation falls back to RingOffset. Batch trace bind only.
        /// Editor-only: the sole caller is Assets/Editor/Regression/RaidAssaultTraceCapture.cs.
        /// </summary>
        public static void BindActiveForEditorCapture(RaidSpire spire)
        {
            if (spire != null) Active = spire;
        }
#endif

        /// <summary>Raised once, on the frame this spire is razed. The raid is won here.</summary>
        public event System.Action<RaidSpire> OnDestroyedEvent;

        /// <summary>Max hit points (the tier's authored objective HP).</summary>
        public float MaxHp => _maxHp;

        /// <summary>Remaining HP as 0..1 (0 once razed). The HUD objective bar reads this.</summary>
        public float HpFraction => _maxHp > 0f ? Mathf.Clamp01(Hp / _maxHp) : 0f;

        /// <summary>Fraction of the spire that has been destroyed, 0..1 (1 once razed).</summary>
        public float DamagedFraction => 1f - HpFraction;

        /// <summary>True once the spire has been razed (the raid objective is complete).</summary>
        public bool IsDestroyed => _destroyed;

        /// <summary>The scene-config id this spire belongs to (trace only).</summary>
        public string ConfigId => _configId;

        /// <summary>The structures-catalog id its art came from (trace only).</summary>
        public string CatalogId => _catalogId;

        /// <summary>
        /// The height the BAKE fitted this spire to, in metres (WO-1820). Read-only accessor over the
        /// value <see cref="Configure"/> serialized into the scene — it adds no state and changes no
        /// behaviour.
        ///
        /// ⛔ THIS IS THE AUTHORED HEIGHT, AND IT WAS ALREADY THE COLLIDER'S AUTHORITY BEFORE IT WAS
        /// READABLE. <c>RaidBaseGenerator.PlaceSpire</c> clamps the catalog's <c>repo.visualHeight</c>
        /// to the monument band, fits the host to it, and passes the ACHIEVED value here
        /// (RaidBaseGenerator.cs:1219); <see cref="EnsureHittable"/> then sizes the hero's contact
        /// collider from it. WO-1820: in three of the four baked scenes the rendered art was 0.14 m
        /// while THIS said 14.40 m, so the player was swinging at a 14.4 m hit box wrapped around a
        /// 14 cm object. Exposing the number lets the audit and the gate assert that the art agrees
        /// with the collider, instead of each re-deriving the clamp from constants that are
        /// `internal` to the editor assembly (a second copy is the bug — CLAUDE.md §2/§5/§16).
        /// </summary>
        public float VisualHeight => _visualHeight;

        // =====================================================================
        //  Bake-time configuration (RaidBaseGenerator calls this in the editor;
        //  the values serialize into the RaidBase_<id>.unity scene).
        // =====================================================================

        /// <summary>
        /// Authors this spire from its scene-config. Called by the generator at BAKE
        /// time, so the values persist in the saved scene - there is no runtime
        /// config read on the objective path.
        /// </summary>
        public void Configure(string configId, string catalogId, float maxHp, float visualHeight)
        {
            _configId = configId ?? "";
            _catalogId = catalogId ?? "";
            _maxHp = Mathf.Max(1f, maxHp);
            _visualHeight = Mathf.Max(1f, visualHeight);
            _hp = _maxHp;
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_hp < 0f) _hp = _maxHp;
            Active = this;
            EnsureHittable();
            FlowTrace.Step(Sys, $"RaidSpire '{name}' online: {_maxHp:0} HP, config='{_configId}', art='{_catalogId}'. " +
                                "Destroying it WINS the raid.");
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        /// <summary>
        /// Guarantees the spire is reachable by the hero's melee/ability sweep: at least
        /// one SOLID collider, sized from the visual, sitting on the "Enemy" physics
        /// layer (the mask PlayerAttackController._enemyLayer and TroopController's
        /// _enemyMask are set to). Mirrors DefenseTower.EnsureContactCollider's bounds
        /// sizing. (It used to also cite BreakableContainer.Create's layer move; that
        /// move was REMOVED by WO-1132 when the container became an openable chest.)
        /// Idempotent.
        /// </summary>
        private void EnsureHittable()
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer < 0)
            {
                FlowTrace.Warn(Sys, "RaidSpire: project has no 'Enemy' layer - the hero's masked sweep " +
                                    "cannot return the spire, so the objective would be unkillable. Layer left untouched.");
            }

            bool hasSolid = false;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger) continue;
                hasSolid = true;
                if (enemyLayer >= 0) c.gameObject.layer = enemyLayer;
            }

            if (!hasSolid)
            {
                // No collider on the art (or the art fell back to a primitive with one
                // stripped) - build one from the renderer bounds so the swing connects.
                float height = _visualHeight, radius = 1.6f;
                Vector3 worldCentre = transform.position + Vector3.up * (height * .5f);
                var rends = GetComponentsInChildren<Renderer>(true);
                if (rends != null && rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    height = Mathf.Max(1f, b.size.y);
                    radius = Mathf.Max(0.8f, Mathf.Max(b.size.x, b.size.z) * 0.5f);
                    worldCentre = b.center;
                }
                var cap = gameObject.AddComponent<CapsuleCollider>();
                cap.isTrigger = false;
                // Renderer bounds are world-space; collider dimensions are local. Raid art
                // is fitted with a scaled root, so assigning world sizes directly shrinks
                // the hitbox a second time and hides the attack target inside its own mesh.
                Vector3 scale = transform.lossyScale;
                cap.height = height / Mathf.Max(.0001f, Mathf.Abs(scale.y));
                cap.radius = radius / Mathf.Max(.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
                cap.center = transform.InverseTransformPoint(worldCentre);
                if (enemyLayer >= 0) gameObject.layer = enemyLayer;
                FlowTrace.Step(Sys, $"RaidSpire '{name}': built a solid capsule hitbox (h={height:0.#} r={radius:0.#}) " +
                                    "- the art carried none.");
            }
            else if (enemyLayer >= 0)
            {
                gameObject.layer = enemyLayer;
            }
        }

        // =====================================================================
        //  IDamageable - the PLAYER + TROOP attack seam (the one that matters).
        // =====================================================================

        /// <summary>Hostile, so the hero's and every troop's Faction gate accepts it.</summary>
        public CombatFaction Faction => (gameObject.scene.name == OwnedTownScenePose.SceneName ||
            gameObject.scene.name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName)
            ? CombatFaction.Friendly : CombatFaction.Hostile;

        public void RestoreOwnedTownCondition(float condition)
        {
            if (gameObject.scene.name != OwnedTownScenePose.SceneName &&
                gameObject.scene.name != DeNelle.Core.Combat.PracticeCombatPolicy.SceneName)
                throw new System.InvalidOperationException("Owned-town restoration requires its isolated scene.");
            if (float.IsNaN(condition) || float.IsInfinity(condition) || condition < 0 || condition > 1 || _destroyed)
                throw new System.InvalidOperationException("Owned-town condition requires a fresh valid structure.");
            _hp = _maxHp * condition;
            if (condition == 0) Raze();
        }

        /// <summary>World position - used by range / nearest-target queries.</summary>
        public Vector3 WorldPosition => transform.position;

        /// <summary>Current hit points (lazily initialised to <see cref="MaxHp"/>).</summary>
        public float Hp => _hp < 0f ? _maxHp : _hp;

        /// <summary>True while the spire still stands. Satisfies BOTH interfaces' IsAlive.</summary>
        public bool IsAlive => !_destroyed && Hp > 0f;

        /// <summary>Hero melee / ability / troop damage entry. Element is ignored (stone has no resists).</summary>
        public void TakeDamage(float amount, DamageElement element) => ApplyDamage(amount, "attack");

        /// <summary>Crowd control is a no-op on a building.</summary>
        public void ApplyStatus(StatusEffect effect, float seconds) { /* a spire cannot be slowed */ }

        // =====================================================================
        //  IDamageableStructure - the ENEMY contact / burn / siege seam.
        // =====================================================================

        /// <summary>Contact-damage entry (StructureBurn ticks, stray siege hits).</summary>
        public void ApplyContactDamage(float amount) => ApplyDamage(amount, "contact");

        // =====================================================================
        //  Damage
        // =====================================================================

        private void ApplyDamage(float amount, string via)
        {
            if (_destroyed || amount <= 0f) return;
            if (_hp < 0f) _hp = _maxHp;

            _hp -= amount;
            FlowTrace.Throttle(Sys, $"spire-hit:{GetInstanceID()}", 1f,
                $"RaidSpire '{name}' took {amount:0.#} ({via}) -> {Mathf.Max(0f, _hp):0}/{_maxHp:0} " +
                $"({HpFraction:P0} standing).");

            if (_hp <= 0f)
            {
                _hp = 0f;
                Raze();
            }
        }

        private void Raze()
        {
            if (_destroyed) return;
            _destroyed = true;

            FlowTrace.Step(Sys, $"OBJECTIVE COMPLETE - RaidSpire '{name}' (config '{_configId}') RAZED. " +
                                "The raid is won.");

            // Stop being a target immediately (before the collapse animation) so nothing
            // keeps swinging at a dead objective.
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = false;

            OnDestroyedEvent?.Invoke(this);

            // Readability: sink the ruin. Guarded so a presentation fault can never
            // swallow the win (the event above already fired).
            Guard.Try(Sys, "raid spire collapse", () => StartCoroutine(CollapseRoutine()));
        }

        private IEnumerator CollapseRoutine()
        {
            Vector3 from = transform.position;
            Vector3 to = from + Vector3.down * Mathf.Max(1f, _visualHeight);
            float t = 0f;
            float dur = Mathf.Max(0.01f, _collapseSeconds);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                transform.position = Vector3.Lerp(from, to, k * k);   // accelerating fall
                yield return null;
            }
            transform.position = to;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = false;
        }
    }
}
