// =============================================================================
// OutpostDefender — recruitable AI defensive troop (Tank / DPS / Healer).
// -----------------------------------------------------------------------------
// Spawned by OutpostHub after successful Economy.TrySpend.
// - Guards a post position.
// - Attacks nearby hostile targets (reuses IDamageable + basic contact or range).
// - Roles have different stats (Tank = high HP/low dmg, DPS = glass cannon,
//   Healer = supports by occasionally "healing" the hub or other defenders via
//   a small positive Grant or HP restore on friendlies — simplified here).
// - Light upkeep is handled by the owning hub (periodic Economy drain).
// - Contributes to defending harvest sites and the outpost itself.
// - Scaling stats applied at spawn time by the hub (distance / threat).
// =============================================================================
using UnityEngine;
using UnityEngine.AI;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village.World
{
    public sealed class OutpostDefender : MonoBehaviour, IDamageableStructure
    {
        public DefenderRole Role = DefenderRole.DPS;
        public Vector3 GuardPost;
        public float GuardRadius = 9f;
        public OutpostHub Hub;

        public float MaxHp = 40f;
        public float Damage = 10f;
        public float AttackRange = 4.5f;
        public float AttackCooldown = 1.1f;

        private float _hp;
        private float _nextAttack;
        private Transform _currentTarget;
        private int _enemyMask;
        private static readonly Collider[] _scanBuf = new Collider[48];

        public bool IsAlive => _hp > 0f;

        /// <summary>
        /// WO-1439 — a recruited outpost defender is bought by the player and guards the
        /// player's hub. Constant Friendly, so hostiles still engage it and it is never
        /// mistaken for one of their own.
        /// </summary>
        public CombatFaction Faction => CombatFaction.Friendly;

        public void ApplyContactDamage(float amount)
        {
            if (_hp <= 0f) return;
            _hp = Mathf.Max(0f, _hp - Mathf.Abs(amount));
            if (_hp <= 0f)
            {
                // U (TGVRU-U): death was Debug.Log'd (uncaptured). Route through FlowTrace so a
                // defender's death lands in the F8 break-log (who fell, where) — previously untraced.
                FlowTrace.Step("Outpost", $"OutpostDefender {Role} fell at {transform.position} — destroyed.");
                Destroy(gameObject);
            }
        }

        private void Awake()
        {
            _hp = MaxHp;
            if (GuardPost == Vector3.zero) GuardPost = transform.position;
            _enemyMask = LayerMask.GetMask("Enemy");
            if (_enemyMask == 0) _enemyMask = ~0;   // Enemy layer undefined -> fall back to all
        }

        private void Update()
        {
            if (_hp <= 0f) return;

            // Stay near guard post.
            float distToPost = Vector3.Distance(transform.position, GuardPost);
            if (distToPost > GuardRadius * 0.6f)
            {
                Vector3 toPost = (GuardPost - transform.position).normalized;
                transform.position += toPost * 3.5f * Time.deltaTime;
            }

            // V (TGVRU-V): this defender moves by RAW transform (no NavMeshAgent), so it can walk
            // off the baked navmesh / through geometry with nothing ever flagging it. Sample the
            // navmesh under its current position and Throttle-Warn (~1/sec) when it has drifted off
            // a walkable surface — the "raw-transform move never navmesh-verified" gap, now self-
            // reporting in the capture. Purely diagnostic; movement is unchanged.
            if (!NavMesh.SamplePosition(transform.position, out _, 2f, NavMesh.AllAreas))
                FlowTrace.Throttle("Outpost", $"offmesh-{GetInstanceID()}", 1f,
                    $"OutpostDefender {Role} is OFF the navmesh at {transform.position} (raw-transform move drifted off the walkable surface).");

            // Find hostile (simple broad search — real version would use a team mask / Threat system).
            if (_currentTarget == null || !IsValidTarget(_currentTarget))
            {
                _currentTarget = FindNearestHostile(AttackRange * 1.6f);
            }

            if (_currentTarget != null)
            {
                // Face and move a bit closer.
                Vector3 dir = (_currentTarget.position - transform.position).normalized;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 0.3f);

                float d = Vector3.Distance(transform.position, _currentTarget.position);
                if (d > AttackRange * 0.7f)
                {
                    transform.position += dir * 4f * Time.deltaTime;
                }

                if (Time.time >= _nextAttack && d <= AttackRange)
                {
                    Attack();
                    _nextAttack = Time.time + AttackCooldown;
                }
            }
        }

        private void Attack()
        {
            if (_currentTarget == null) return;

            // Apply damage to target if it is damageable.
            var dmgTarget = _currentTarget.GetComponent<IDamageableStructure>();
            if (dmgTarget != null && dmgTarget.IsAlive)
            {
                dmgTarget.ApplyContactDamage(Damage);
            }
            else
            {
                // Fallback: target carries no IDamageableStructure — it took no damage. Throttle so
                // a "hitting something un-damageable" mis-target self-reports without spamming.
                FlowTrace.Throttle("Outpost", $"nodmg-{GetInstanceID()}", 1f,
                    $"OutpostDefender {Role} swung at '{_currentTarget.name}' but it has no IDamageableStructure — no damage applied.");
            }

            // Healer role occasionally "supports" by giving a tiny resource bump or healing the hub.
            if (Role == DefenderRole.Healer && Hub != null && Random.value < 0.35f)
            {
                // Symbolic support: small positive grant to the economy (represents "inspired gathering"
                // or minor supply line). Real healer would restore HP on friendlies.
                EconomyService.Instance?.Grant(stone: 1);
            }
        }

        private Transform FindNearestHostile(float radius)
        {
            // NonAlloc scan of the Enemy layer (was a full-scene OverlapSphere with no mask +
            // a per-call array alloc). Nearest valid hostile by sqrMagnitude.
            int n = Physics.OverlapSphereNonAlloc(transform.position, radius, _scanBuf, _enemyMask, QueryTriggerInteraction.Collide);
            Transform best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < n; i++)
            {
                var hit = _scanBuf[i];
                if (hit == null || hit.transform == transform) continue;
                if (!IsValidTarget(hit.transform)) continue;

                float sqr = (hit.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = hit.transform;
                }
            }
            return best;
        }

        private bool IsValidTarget(Transform t)
        {
            if (t == null) return false;
            // The scan is masked to the Enemy layer, so a hit on that layer already qualifies.
            // Keep the raider-name check as a fallback (drops the slow string-based GetComponent).
            if (((1 << t.gameObject.layer) & _enemyMask) != 0) return true;

            // ⛔ REMOVED 2026-08-17 (WO-1038): `if (t.CompareTag("Enemy")) return true;`
            // "Enemy" is a LAYER in this project, NOT a tag — ProjectSettings/TagManager.asset
            // declares exactly four tags (Tower, Building, HeartTarget, Player). CompareTag THROWS
            // UnityException on an undeclared tag, so that line threw on every call that reached it.
            // It was also DEAD: the line directly above already returns true for anything on the
            // Enemy layer, which is the same set it was trying to match. A throwing line that could
            // only ever duplicate the check above it.
            //
            // ⚠ Do NOT "fix" this by declaring an Enemy TAG. Enemy is a layer by design (the mask
            // above is the intended mechanism); adding a same-named tag creates two authorities over
            // "is this thing hostile" and they will drift. If a non-layer hostile ever needs matching,
            // give it the layer.
            if (t.name.Contains("Raider") || t.name.Contains("HarvestRaider")) return true;
            return false;
        }

        private void OnDestroy()
        {
            if (Hub != null && Hub.Defenders != null)
            {
                // The hub will clean the list in its Update.
            }
        }
    }
}
