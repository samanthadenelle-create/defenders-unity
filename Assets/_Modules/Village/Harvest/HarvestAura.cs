// =============================================================================
// HarvestAura - the ONE held loop slot on a harvest surface (node or collector),
// plus the shared nearest-N budget that stops a town of them from eating the
// global loop cap.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// ## WHY THIS EXISTS (WO-890)
//
// Six harvest recipes were built, measured and committed on 2026-08-05
// (Harvest_Iron / _Wood / _Food / _Crystal / _Gold + Collector_Ready). Verified at
// source, they had ZERO runtime consumers - the art shipped and nothing ever played
// it. This component is the consumer.
//
// The recipes are split by MOTION VECTOR, never by hue, because the owner is
// red/green colourblind. MEASURED off the committed prefabs (2026-08-05):
//   Harvest_Iron    5 layers, root 35/sec + spark layers 28 / 3.5 / 3.5, gravity 0.15 -> dust SETTLES + glints
//   Harvest_Wood    1 layer,  35/sec, gravity 0                        -> chip motes drift FLAT
//   Harvest_Food    2 layers, 5/sec + 1/sec                            -> sparse pollen RISES
//   Harvest_Crystal 2 layers, 16/sec + 3.2/sec, startSpeed x0.15       -> dense twinkle, NO travel
//   Harvest_Gold    4 layers, 5 / 5 / 40/sec, gravity 0.4, life x0.6   -> short glint pops that FALL
//   Collector_Ready 2 layers, 6/sec + 1.2/sec                          -> rising bob = "come pick me up"
// Every one measures CONTINUOUS at its root (rateOverTime > 0, looping) and is
// catalogued IsLoop=true, so every one of them is a PERSISTENT LOOP.
//
// ## THE HALF THAT MATTERS MORE THAN THE ART
//
// A loop played fire-and-forget permanently consumes one of VFXManager's 20 global
// slots and every later aura in the session is silently dropped. So, mirroring
// HeroHpStateAura (commit 1534dffb), which is the worked example:
//
//   * ONE handle field. Not a list, not a dictionary - ONE. "Harvesting" and "ready"
//     are therefore MUTUALLY EXCLUSIVE BY CONSTRUCTION: there is no second field in
//     which a second loop could be held, so "node harvesting and collector ready both
//     running on one host" is not a bug that can occur, it is a state that cannot be
//     represented. Apply() stops before it starts.
//   * EVERY exit path stops it: beat change, the source going quiet (idle / depleted /
//     collected / broken), the source delegate returning None, a destroyed host, a
//     denied budget permit, OnDisable, OnDestroy and sceneUnloaded.
//   * NO WATCHDOG IS NEEDED HERE, and that is deliberate rather than an omission.
//     HeroHpStateAura is PUSH-driven (HeroHealth calls Drive), so a silent driver is a
//     leak and it carries a watchdog for exactly that. This component is PULL-driven:
//     it reads its host's state from a delegate in its own Update, so there is no
//     external driver that can go silent while a loop is held. If the host object dies
//     the component dies with it (OnDestroy stops); if it is deactivated OnDisable
//     stops; if the delegate throws or reports nothing the beat resolves to None and
//     Apply stops. The "driver went silent" class of leak cannot occur by shape.
//
// ## THE NEAREST-N BUDGET (WO-890's stated WO-889 dependency, which does not exist)
//
// WO-890 depends on "WO-889 nearest-N guard". VERIFIED AT SOURCE: no such guard exists
// anywhere in the tree (no nearest-N / budget / arbiter for VFX loops). Rather than
// wire N harvest loops into a 20-slot cap and hope, the budget is implemented HERE, in
// the one place that needs it, as a static arbiter over every live instance:
//   * At most MaxConcurrent instances may hold a loop at once.
//   * Permits go to READY beacons first (a full collector is ACTIONABLE - the player
//     can go tap it), then to the nearest harvesting auras (ambience). Priority is by
//     meaning then by distance, never by who happened to start first.
//   * Re-arbitrated on a throttle, so an instance that loses its permit stops and one
//     that gains it starts. The set self-heals as the camera moves.
// An instance that updates earlier in the same frame than the arbitration tick applies
// a permit up to one interval stale. That is intentional and harmless at 4 Hz.
//
// POOLED-INSTANCE CONTAMINATION: VFXManager.ReturnToPool resets nothing it did not
// itself change, so a caller that modulated an instance would hand the NEXT user of
// that pool slot a modulated effect forever. Every start therefore re-baselines the
// instance's modulation through VfxLoopModulator before seating its own scale.
//
// LANDSCAPE PHONE (2670x1200): these are demo-scene room-fill ambiences at root scale
// 1 (MEASURED - all six ship localScale 1,1,1). An effect that grows UPWARD spends the
// scarce axis and crops, so each beat is seated CLOSE to its object with a scale
// multiplier and a low offset. Every number below is a felt-tunable bone.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// One harvest surface's single held VFX loop: the per-resource harvest aura while
    /// it is producing, or the ready-to-collect beacon while it is full. Pull-driven
    /// from a host-supplied state delegate; budgeted nearest-N across all instances.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestAura : MonoBehaviour
    {
        /// <summary>Which single read is live. There is no combination - see the header.</summary>
        public enum Beat
        {
            /// <summary>Nothing held (idle, depleted, broken, or empty).</summary>
            None,
            /// <summary>Producing / being worked: the per-resource motion aura.</summary>
            Harvesting,
            /// <summary>Full / ready: the rising-bob "come pick me up" beacon.</summary>
            Ready,
        }

        // =====================================================================
        //  TUNABLE BONES - felt-tunable by the owner. They encode the READ.
        // =====================================================================

        /// <summary>
        /// How many harvest loops may be held at once, out of VFXManager's 20 global
        /// slots. Deliberately a small share: the hero HP aura, structure burn loops,
        /// projectile trails, pet auras and the Heart's two ambient loops all draw on
        /// the same 20, and six captured sessions showed that cap saturated.
        /// </summary>
        private const int MaxConcurrent = 4;

        /// <summary>How often the nearest-N permits are recomputed, for all instances.</summary>
        private const float ArbitrateInterval = 0.25f;

        /// <summary>How often each instance re-reads its host's state. Cheap either way.</summary>
        private const float PollInterval = 0.2f;

        /// <summary>
        /// Beyond this distance from the camera an instance does not even ask for a
        /// permit. A harvest aura you cannot resolve on a 2670x1200 phone screen is
        /// pure cost, and asking would push a nearer surface out of the budget.
        /// </summary>
        private const float MaxVisibleDistance = 45f;

        /// <summary>Retry throttle after a refused start (loop cap / quality gate / no manager).</summary>
        private const float StartRetrySeconds = 0.5f;

        // Body-seating scale multipliers against each recipe's AUTHORED scale (MEASURED:
        // all six committed prefabs ship root scale 1, i.e. the pack's room-fill size).
        // Seating them onto a single node / collector is what keeps them readable and
        // stops them growing up out of a landscape frame.
        private const float ScaleMulHarvesting = 0.60f;
        private const float ScaleMulReady      = 0.80f;   // the beacon is meant to read from across town

        // How high above the host each beat sits. Both are deliberately LOW - a
        // ground-plane read survives the landscape frame; a column does not.
        private const float SeatHeightHarvesting = 0.45f;
        private const float SeatHeightReady      = 1.10f;

        // =====================================================================
        //  Shared budget state
        // =====================================================================

        private static readonly List<HarvestAura> s_live    = new List<HarvestAura>(16);
        private static readonly List<HarvestAura> s_ranked  = new List<HarvestAura>(16);
        private static float s_nextArbitrate;

        /// <summary>Live instances right now (headless verification / tests).</summary>
        public static int LiveCount => s_live.Count;

        /// <summary>Instances actually holding a pooled loop right now (headless verification).</summary>
        public static int HoldingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < s_live.Count; i++)
                    if (s_live[i] != null && s_live[i].IsHolding) n++;
                return n;
            }
        }

        // =====================================================================
        //  Instance state - ONE beat, ONE handle. The exclusion is this shape.
        // =====================================================================

        private Func<Beat>     _beatSource;    // what the host wants right now
        private Func<VFXType>  _harvestType;   // which per-resource aura the host maps to
        private string         _label = "harvest";

        private Beat      _held;               // what is currently HELD (None whenever _handle is null)
        private Beat      _want;               // what the host asked for at the last poll
        private VFXHandle _handle;             // THE one held loop. There is deliberately no second field.
        private bool      _permitted;          // granted a slot by the shared arbiter
        private float     _nextPoll;
        private float     _nextStartAttempt;

        /// <summary>The beat currently held. Exposed for headless verification / tests.</summary>
        public Beat Current => _held;

        /// <summary>True while a real pooled loop is held (false when a start was refused).</summary>
        public bool IsHolding => _handle != null && _handle.IsAlive;

        // =====================================================================
        //  Wiring
        // =====================================================================

        /// <summary>
        /// Attach (or reuse) the aura slot on <paramref name="host"/> and point it at the
        /// host's state. Idempotent - a second call re-points the SAME slot rather than
        /// adding a second one, which is what keeps "one loop per surface" true even if
        /// two bootstraps both wire the same collector.
        /// </summary>
        /// <param name="host">The node / collector GameObject the aura is seated on.</param>
        /// <param name="beatSource">Reads the host's live state. Must be null-safe.</param>
        /// <param name="harvestType">The per-resource aura VFXType for the Harvesting beat.</param>
        /// <param name="label">Trace label (building id / resource name).</param>
        public static HarvestAura Attach(GameObject host, Func<Beat> beatSource,
                                         Func<VFXType> harvestType, string label)
        {
            if (host == null || beatSource == null || harvestType == null) return null;
            var a = host.GetComponent<HarvestAura>();
            if (a == null) a = host.AddComponent<HarvestAura>();
            a._beatSource  = beatSource;
            a._harvestType = harvestType;
            a._label       = string.IsNullOrEmpty(label) ? "harvest" : label;
            return a;
        }

        // =====================================================================
        //  Lifecycle - every one of these is an EXIT PATH and stops the loop.
        // =====================================================================

        private void OnEnable()
        {
            if (!s_live.Contains(this)) s_live.Add(this);
            // A scene unload can tear down the VFXManager (and its pool) while this host
            // survives, stranding the held instance. Stop on the way out, always.
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            s_live.Remove(this);
            StopHeld(immediate: true, "OnDisable");
        }

        private void OnDestroy()
        {
            s_live.Remove(this);
            StopHeld(immediate: true, "OnDestroy");
        }

        private void OnSceneUnloaded(Scene _) => StopHeld(immediate: true, "sceneUnloaded");

        private void Update()
        {
            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + PollInterval;
                _want = ResolveWant();
            }

            ArbitrateIfDue();
            Apply(_permitted ? _want : Beat.None);
        }

        // =====================================================================
        //  Internals
        // =====================================================================

        /// <summary>
        /// What the host wants, guarded. A delegate whose target has been destroyed, or
        /// that throws, resolves to None and therefore STOPS the loop - a surface that
        /// can no longer report its state must never keep a slot. Silent failure is
        /// forbidden (CLAUDE.md section 12), so the throw is traced.
        /// </summary>
        private Beat ResolveWant()
        {
            if (_beatSource == null) return Beat.None;
            try
            {
                return _beatSource();
            }
            catch (Exception e)
            {
                FlowTrace.Fail("HarvestAura",
                    $"'{_label}': beat source threw ({e.Message}) - resolving to None and releasing " +
                    "the loop slot. A surface that cannot report its state does not get to hold one.");
                _beatSource = null;
                return Beat.None;
            }
        }

        /// <summary>
        /// Recompute every instance's permit. Runs at most once per
        /// <see cref="ArbitrateInterval"/> no matter how many instances call it.
        /// READY outranks HARVESTING (actionable beats ambient), then nearest-first.
        /// </summary>
        private static void ArbitrateIfDue()
        {
            if (Time.time < s_nextArbitrate) return;
            s_nextArbitrate = Time.time + ArbitrateInterval;

            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            float maxSqr = MaxVisibleDistance * MaxVisibleDistance;

            s_ranked.Clear();
            for (int i = 0; i < s_live.Count; i++)
            {
                var a = s_live[i];
                if (a == null) continue;
                a._permitted = false;
                if (a._want == Beat.None) continue;
                // Off-screen-far surfaces do not compete: asking would evict a nearer one.
                if (cam != null && (a.transform.position - eye).sqrMagnitude > maxSqr) continue;
                s_ranked.Add(a);
            }

            if (s_ranked.Count > 1)
            {
                s_ranked.Sort((x, y) =>
                {
                    // Ready first - "come collect me" is a thing the player can act on;
                    // a harvest shimmer is ambience and yields to it.
                    bool xr = x._want == Beat.Ready, yr = y._want == Beat.Ready;
                    if (xr != yr) return xr ? -1 : 1;
                    float dx = (x.transform.position - eye).sqrMagnitude;
                    float dy = (y.transform.position - eye).sqrMagnitude;
                    return dx.CompareTo(dy);
                });
            }

            int grant = Mathf.Min(s_ranked.Count, MaxConcurrent);
            for (int i = 0; i < grant; i++) s_ranked[i]._permitted = true;

            if (s_ranked.Count > MaxConcurrent)
                FlowTrace.Throttle("HarvestAura", "budget", 5f,
                    $"harvest loop budget: {s_ranked.Count} surfaces want a loop, cap={MaxConcurrent} " +
                    "(ready beacons first, then nearest) - the rest are silent by design, not dropped by " +
                    "the global 20-slot cap.");
        }

        /// <summary>
        /// Swap the ONE held loop to <paramref name="want"/>. Stops before it starts,
        /// always - which is why two harvest loops can never be live on one surface.
        /// </summary>
        private void Apply(Beat want)
        {
            // A handle whose host died under us (pool torn down / manager destroyed) reads
            // as not-alive: drop it so the state machine can re-acquire, never sit on a corpse.
            if (_handle != null && !_handle.IsAlive) { _handle = null; _held = Beat.None; }

            if (want == _held && (_held == Beat.None || _handle != null)) return;

            StopHeld(immediate: false, "beat change -> " + want);   // graceful: let the tail die out
            _held = Beat.None;

            if (want == Beat.None) return;

            // Do not hammer the manager while a start is being refused (cap / quality gate).
            if (Time.time < _nextStartAttempt) return;

            var mgr = VFXManager.Instance;
            if (mgr == null)
            {
                _nextStartAttempt = Time.time + StartRetrySeconds;
                return;
            }

            VFXType type = TypeFor(want);
            if (type == VFXType.None) return;

            // Seated on a child-free local offset by parenting to the host: the effect
            // tracks a node that despawns or a collector that is re-placed, and is torn
            // down with it. Low seat height on purpose (landscape frame).
            _handle = mgr.PlayAura(type, transform);
            if (_handle == null)
            {
                // REFUSED, not latched: _held stays None so the next tick retries and the
                // surface self-heals the moment a slot frees. VFXManager already throttle-
                // reports the cause; this line says WHICH surface was the casualty.
                _nextStartAttempt = Time.time + StartRetrySeconds;
                FlowTrace.Throttle("HarvestAura", "start-refused", 2f,
                    $"'{_label}': '{want}' loop ('{type}') REFUSED by VFXManager (global loop cap or " +
                    "quality gate) even though the harvest budget granted a permit. Retrying.");
                return;
            }

            _held = want;
            _handle.SetPosition(transform.position + Vector3.up * SeatFor(want));

            // Re-baseline this pooled instance BEFORE seating our own scale: whatever the
            // previous user of this pool slot modulated must not be inherited.
            var mod = _handle.Modulator;
            if (mod != null)
            {
                mod.SetEmissionScale(1f);
                mod.SetSimulationSpeed(1f);
                mod.SetScaleMul(ScaleMulFor(want));
            }

            FlowTrace.Step("HarvestAura",
                $"'{_label}': HELD '{want}' -> '{type}' (one slot; budget {HoldingCount}/{MaxConcurrent}, " +
                $"scaleMul={ScaleMulFor(want):0.00}, seat=+{SeatFor(want):0.00}m).");
        }

        /// <summary>Stop and release THE held loop. Idempotent; safe with nothing held.</summary>
        private void StopHeld(bool immediate, string reason)
        {
            if (_handle == null) { _held = Beat.None; return; }

            var beat = _held;
            _handle.Stop(immediate);   // Stop restores the instance's modulation before pooling
            _handle = null;
            _held   = Beat.None;

            FlowTrace.Step("HarvestAura",
                $"'{_label}': released '{beat}' loop (reason={reason}, immediate={immediate}) - slot returned.");
        }

        /// <summary>
        /// Beat -> the landed VFXType. Reference values only; the enum append is Grok's
        /// single-owner edit (WO-884 section 0.2) and these six landed on 2026-08-05.
        /// The Harvesting type comes from the HOST, because only the host knows which
        /// resource it yields - that indirection is what makes the five motion recipes
        /// selectable without this component knowing anything about the economy.
        /// </summary>
        private VFXType TypeFor(Beat beat)
        {
            switch (beat)
            {
                case Beat.Ready: return VFXType.Collector_Ready;
                case Beat.Harvesting:
                    try { return _harvestType != null ? _harvestType() : VFXType.None; }
                    catch (Exception e)
                    {
                        FlowTrace.Fail("HarvestAura",
                            $"'{_label}': harvest-type source threw ({e.Message}) - no loop started.");
                        _harvestType = null;
                        return VFXType.None;
                    }
                default: return VFXType.None;
            }
        }

        // =====================================================================
        //  Resource -> motion recipe. ONE home for the rule.
        // =====================================================================
        //
        // The game carries TWO resource enums - MineResource on world nodes and
        // HarvestResource on town collectors - and they are NOT the same type or the
        // same ordinals. Mapping them in each host would be two copies of one rule, and
        // a second copy of a shared rule is precisely what caused the IsLoop P0 fixed in
        // bd532d5b. Both overloads live here, next to the MEASURED motion notes in the
        // header, so "iron settles, wood drifts flat, food rises, crystal hangs, gold
        // falls" is stated exactly once.
        //
        // GOLD HAS NO SOURCE. Harvest_Gold is built, measured and catalogued, but
        // VERIFIED AT SOURCE there is no gold harvestable in this game: MineResource is
        // { Iron, Wood, Food, AetherCrystal } and HarvestResource is { Crystals, Food,
        // Wood, Iron }. The only "Gold" in the tree is a HUD display field
        // (Core/HudModel/HudModels.cs) and a buildingTier goldCost - neither is harvested
        // from anything. So no call site can select it, and inventing a gold resource
        // would be economy DESIGN, which belongs to the owner. Reported, not faked.

        /// <summary>World-node resource -> its measured motion recipe.</summary>
        public static VFXType TypeForResource(MineResource res)
        {
            switch (res)
            {
                case MineResource.Iron:          return VFXType.Harvest_Iron;
                case MineResource.Wood:          return VFXType.Harvest_Wood;
                case MineResource.Stone:          return VFXType.Harvest_Food;
                case MineResource.AetherCrystal: return VFXType.Harvest_Crystal;
                default:                         return VFXType.None;
            }
        }

        /// <summary>Town-collector resource -> its measured motion recipe.</summary>
        public static VFXType TypeForResource(
            DeNelle.Village.Buildings.Progression.HarvestResource res)
        {
            switch (res)
            {
                case DeNelle.Village.Buildings.Progression.HarvestResource.Iron:
                    return VFXType.Harvest_Iron;
                case DeNelle.Village.Buildings.Progression.HarvestResource.Wood:
                    return VFXType.Harvest_Wood;
                case DeNelle.Village.Buildings.Progression.HarvestResource.Stone:
                    return VFXType.Harvest_Food;
                case DeNelle.Village.Buildings.Progression.HarvestResource.Crystals:
                    return VFXType.Harvest_Crystal;
                default:
                    return VFXType.None;
            }
        }

        private static float ScaleMulFor(Beat beat)
            => beat == Beat.Ready ? ScaleMulReady : ScaleMulHarvesting;

        private static float SeatFor(Beat beat)
            => beat == Beat.Ready ? SeatHeightReady : SeatHeightHarvesting;
    }
}
