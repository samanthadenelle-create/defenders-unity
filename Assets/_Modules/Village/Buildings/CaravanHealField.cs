// =============================================================================
// CaravanHealField — WO-991 slice 2: the Healing Caravan's healing aura.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// OWNER RULING (2026-08-15): the caravan is a glass support unit. The heal field
// is its payoff — "win a hard wave by parking it behind a wall and fighting in
// its radius" — with REDUCED strength while rolling (follow mode stays useful
// without becoming a mobile fortress).
//
// WO-1424 (owner ruling 2026-09-06, "it slow follows as a combat attack item as
// defensive item it stationary") gives that reduction its full meaning. The
// caravan now follows ONLY in an enemy-owned scene and is stationary in the
// player's town, so MovingHealMultiplier is no longer a quirk of whether the
// crawler happens to be catching up — it is the deliberate TRADE between the two
// modes: the offensive escort buys mobility at HALF the heal rate, while the
// defensive town caravan gives up mobility and heals at FULL rate for it. The
// value itself is unchanged and is the owner's to re-tune, not ours.
//
// OWNER VFX PICK (2026-08-16, VERBATIM TAG): the aura-range ring is
//   "Assets/Hovl Studio/Map track markers VFX/Prefabs/Marker 8 Safe zone Loop.prefab"
// That pack is GITIGNORED, so the ring resolves via a TRACKED Resources mirror
// (RingResourcePath below — the committer's mirror lane copies the prefab there).
// Graceful fallback when the mirror is absent: FlowTrace.Warn + a simple
// code-built ground ring. NEVER an error, NEVER a substitute effect.
//
// LOOP DISCIPLINE (WO-955/983 lesson): the ring is ONE retained instance in ONE
// field, instantiated directly from the mirror (not a VFXManager pooled loop, so
// it spends none of the 20 global slots), and EVERY exit releases it: field off,
// caravan death, OnDisable, OnDestroy. Never one per tick, never one per target.
// The per-tick contact flash on troops is Family B (VFXManager.Play one-shot).
//
// COLOURBLIND (owner is red/green): the field reads by SHAPE (the ground ring)
// and RHYTHM (the tick cadence + contact flashes) — never by hue alone.
//
// GAMEPLAY MODEL (mirrors SupportFieldStructure.TryHeal, verified at source):
//   hero      -> HeroHealth.Heal (plays its OWN Impact_Heal; no double burst here)
//   troops    -> TroopController.Heal + contact flash
//   companion -> StoryCompanion.Heal + contact flash
// HP is logged CREDITED-NOT-REQUESTED: measured off the unit's Hp delta, so a
// clamped heal (near-full unit) reports what actually landed.
//
// Attached at runtime by HealingCaravanMobility.Start behind ff.caravanmobile —
// zero scene wiring. The heal-UNLOCK gate (tier/research) is a later slice; this
// slice ships the field itself so the owner can feel the tagged ring live.
// =============================================================================

using System.Text;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// Radius heal tick + the owner-tagged Safe Zone range ring around the
    /// Healing Caravan (WO-991). Half strength while the caravan is rolling,
    /// full while parked. One retained ring instance; released on every exit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaravanHealField : MonoBehaviour
    {
        // =====================================================================
        //  TUNABLE — owner-balance defaults, never locks (WO-991 scope note).
        // =====================================================================

        /// <summary>TUNABLE: field radius in world units.</summary>
        private const float FieldRadius = 7f;

        /// <summary>TUNABLE: seconds between heal ticks (a support field ticks slowly).</summary>
        private const float TickIntervalSeconds = 2f;

        /// <summary>TUNABLE: HP restored per tick per unit at full (parked) strength.</summary>
        private const float HealPerTick = 6f;

        /// <summary>TUNABLE: field-strength multiplier while the caravan is rolling
        /// (owner default "Yes, reduced — e.g. 50% while rolling"). WO-1424: this is now the
        /// offensive/defensive TRADE, not an incidental penalty — a DEFENSIVE town caravan never
        /// rolls, so it always heals at full rate; only the OFFENSIVE escort pays this. Balance
        /// value deliberately UNCHANGED by WO-1424 — re-tuning it is the owner's call.</summary>
        private const float MovingHealMultiplier = 0.5f;

        /// <summary>TUNABLE: uniform scale applied to the mirrored ring prefab per metre of
        /// <see cref="FieldRadius"/>. The Hovl marker's native footprint is unknown until the
        /// mirror lands, so the spawn logs the MEASURED world bounds for one-look calibration.</summary>
        private const float RingScalePerMeter = 0.28f;

        /// <summary>Ground seat height for the ring (low + wide reads on a landscape phone).</summary>
        private const float RingSeatHeight = 0.08f;

        // Owner-tagged ring, resolved via the TRACKED mirror (the gitignored pack
        // path is in the file header for the committer's mirror list).
        private const string RingResourcePath = "VFX/Markers/Marker8_SafeZoneLoop";

        private const int OverlapBufferSize = 32;

        // =====================================================================
        //  State
        // =====================================================================

        private HealingCaravanMobility _mobility;
        private GameObject _ring;          // THE retained ring instance (one field, one exit set)
        private bool _ringIsFallback;
        private bool _fieldOn;
        private float _tickTimer;
        private int _lastInsideCount = -1; // change-only insider logging
        private bool _lastHeroInside;
        private readonly Collider[] _overlap = new Collider[OverlapBufferSize];

        /// <summary>True while the field is live (headless verification seam).</summary>
        public bool IsFieldOn => _fieldOn;

        /// <summary>Attach (or reuse) the heal field on the caravan root.</summary>
        public static CaravanHealField Attach(HealingCaravanMobility host)
        {
            if (host == null)
            {
                FlowTrace.Warn("Caravan", "CaravanHealField.Attach called with null host — no field");
                return null;
            }
            var field = host.GetComponent<CaravanHealField>();
            if (field == null) field = host.gameObject.AddComponent<CaravanHealField>();
            field._mobility = host;
            return field;
        }

        private void OnEnable()
        {
            _tickTimer = TickIntervalSeconds;   // never heal on the frame it is placed
            SetField(true, "OnEnable");
        }

        private void OnDisable() => SetField(false, "OnDisable");
        private void OnDestroy() => SetField(false, "OnDestroy");

        private void Update()
        {
            Guard.Try("Caravan", "heal field tick", TickField);
        }

        private void TickField()
        {
            // Death exit: the mobility owns the lifecycle; a dead caravan's field
            // goes out BEFORE the 0.4s corpse-linger destroy so a deleted cart
            // never keeps advertising a safe zone.
            if (_mobility == null || !_mobility.IsAlive)
            {
                SetField(false, "caravan dead");
                return;
            }
            if (!_fieldOn) return;

            _tickTimer -= Time.deltaTime;
            if (_tickTimer > 0f) return;
            _tickTimer = TickIntervalSeconds;

            bool rolling = _mobility.IsRolling;
            float amount = HealPerTick * (rolling ? MovingHealMultiplier : 1f);

            int hit = Physics.OverlapSphereNonAlloc(
                transform.position, FieldRadius, _overlap, ~0, QueryTriggerInteraction.Ignore);
            if (hit >= OverlapBufferSize)
                FlowTrace.Throttle("Caravan", "field-overlap", 10f,
                    $"heal field: overlap buffer full ({OverlapBufferSize}) — some units in radius not served this tick.");

            int inside = 0;
            bool heroInside = false;
            float credited = 0f;
            var servedNames = new StringBuilder();

            for (int i = 0; i < hit; i++)
            {
                var col = _overlap[i];
                if (col == null) continue;

                var hero = col.GetComponentInParent<HeroHealth>();
                if (hero != null)
                {
                    inside++;
                    heroInside = true;
                    if (hero.Hp > 0f && hero.Fraction < 1f)
                    {
                        float before = hero.Hp;
                        hero.Heal(amount);   // plays its own Impact_Heal (no double burst here)
                        credited += Mathf.Max(0f, hero.Hp - before);
                        Append(servedNames, "hero");
                    }
                    continue;
                }

                var troop = col.GetComponentInParent<TroopController>();
                if (troop != null)
                {
                    inside++;
                    if (troop.IsAlive && troop.Hp < troop.MaxHp)
                    {
                        float before = troop.Hp;
                        troop.Heal(amount);
                        credited += Mathf.Max(0f, troop.Hp - before);
                        // Family B contact flash — one-shot, no loop slot, never a retained handle.
                        VFXManager.Play(VFXType.Impact_Heal, troop.transform.position + Vector3.up * 1f);
                        Append(servedNames, troop.name);
                    }
                    continue;
                }

                var companion = col.GetComponentInParent<StoryCompanion>();
                if (companion != null)
                {
                    inside++;
                    if (companion.IsAlive && companion.Hp < companion.MaxHp)
                    {
                        float before = companion.Hp;
                        companion.Heal(amount);
                        credited += Mathf.Max(0f, companion.Hp - before);
                        VFXManager.Play(VFXType.Impact_Heal, companion.transform.position + Vector3.up * 1f);
                        Append(servedNames, companion.name);
                    }
                }
            }

            // Insiders: CHANGE-ONLY logging (spec item 3) so a parked fight does not firehose.
            if (inside != _lastInsideCount || heroInside != _lastHeroInside)
            {
                _lastInsideCount = inside;
                _lastHeroInside = heroInside;
                FlowTrace.Step("Caravan",
                    $"heal field insiders changed: {inside} inside (heroInside={heroInside}) radius={FieldRadius:0.#}m");
            }

            // Credited HP: MEASURED off Hp deltas — what actually landed, not what was requested.
            if (credited > 0f)
                FlowTrace.Throttle("Caravan", "field-heal", 2f,
                    $"heal tick credited={credited:0.#}hp (requested {amount:0.#}/unit, " +
                    $"{(rolling ? "ROLLING x" + MovingHealMultiplier.ToString("0.##") : "PARKED full")}) " +
                    $"served=[{servedNames}]");
        }

        private static void Append(StringBuilder sb, string name)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(name);
        }

        // =====================================================================
        //  Field on/off + THE ring (one retained instance, every exit releases it)
        // =====================================================================

        private void SetField(bool on, string reason)
        {
            if (on)
            {
                if (_fieldOn && _ring != null) return;   // already live (idempotent)
                _fieldOn = true;
                FlowTrace.Step("Caravan",
                    $"heal field ON radius={FieldRadius:0.#}m tick={TickIntervalSeconds:0.#}s " +
                    $"heal={HealPerTick:0.#}hp (x{MovingHealMultiplier:0.##} while rolling) reason={reason}");
                Guard.Try("Caravan", "spawn field ring", SpawnRing);
            }
            else
            {
                bool wasOn = _fieldOn;
                _fieldOn = false;
                DespawnRing(reason);
                if (wasOn)
                    FlowTrace.Step("Caravan", $"heal field OFF reason={reason}");
            }
        }

        /// <summary>
        /// Spawn the owner-tagged range ring from the tracked mirror; fall back to a
        /// simple code-built ground circle (shape read, not hue) when absent.
        /// </summary>
        private void SpawnRing()
        {
            if (_ring != null) return;

            var prefab = Resources.Load<GameObject>(RingResourcePath);
            if (prefab != null)
            {
                _ring = Instantiate(prefab, transform);
                _ring.name = "CaravanFieldRing";
                _ring.transform.localPosition = new Vector3(0f, RingSeatHeight, 0f);
                _ring.transform.localScale = Vector3.one * (FieldRadius * RingScalePerMeter);
                _ringIsFallback = false;

                // The purchased marker includes a filled ShockWave child. At this
                // sustained scale it renders as the solid yellow plate under the wagon.
                // Keep the readable perimeter ring; disable only the filled centre.
                var filledCentre = _ring.transform.Find("ShockWave");
                if (filledCentre != null) filledCentre.gameObject.SetActive(false);

                // Calibration line: the marker's native footprint is unknown until the
                // mirror lands — a capture shows the MEASURED world size vs FieldRadius.
                var r = _ring.GetComponentInChildren<Renderer>(true);
                string measured = r != null
                    ? $"measured bounds={r.bounds.size.x:0.#}x{r.bounds.size.z:0.#}m"
                    : "no renderer to measure";
                FlowTrace.Step("Caravan",
                    $"field ring SPAWNED from '{RingResourcePath}' (owner-tagged Marker 8 Safe zone Loop) " +
                    $"scale={FieldRadius * RingScalePerMeter:0.##} vs radius={FieldRadius:0.#}m — {measured}");
                return;
            }

            // Standard graceful fallback: Warn + a simple code-built ground ring.
            // Never an error, never a substitute effect (owner tags are verbatim).
            FlowTrace.Warn("Caravan",
                $"field ring mirror ABSENT at Resources/{RingResourcePath} — Hovl pack is gitignored; " +
                "using the plain code-built ground circle until the mirror lands (committer mirror list).");
            _ring = BuildFallbackRing();
            _ringIsFallback = true;
            FlowTrace.Step("Caravan", $"field ring FALLBACK circle spawned radius={FieldRadius:0.#}m");
        }

        /// <summary>Plain LineRenderer circle at the field radius — a SHAPE read
        /// (colourblind-safe), deliberately not imitating the tagged effect.</summary>
        private GameObject BuildFallbackRing()
        {
            const int Segments = 48;
            var go = new GameObject("CaravanFieldRing_Fallback");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, RingSeatHeight, 0f);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = Segments;
            lr.startWidth = 0.12f;
            lr.endWidth = 0.12f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            // Neutral bright-on-dark luminance, no hue coding.
            lr.startColor = new Color(0.95f, 0.93f, 0.85f, 0.55f);
            lr.endColor = lr.startColor;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * FieldRadius, 0f, Mathf.Sin(a) * FieldRadius));
            }
            return go;
        }

        /// <summary>Release THE ring. Idempotent; every field exit routes here.</summary>
        private void DespawnRing(string reason)
        {
            if (_ring == null) return;
            Destroy(_ring);
            _ring = null;
            FlowTrace.Step("Caravan",
                $"field ring DESPAWNED (fallback={_ringIsFallback}) reason={reason}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.86f, 0.45f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, FieldRadius);
        }
#endif
    }
}
