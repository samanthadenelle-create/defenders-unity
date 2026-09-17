// =============================================================================
// SafeZoneRecovery -- full HP + MP restore on entering a SAFE ZONE (Castle/Town/Base).
// -----------------------------------------------------------------------------
// Owner brief (2026-06-29, SURVIVAL RULE): Health AND Mana do NOT auto-restore
// after combat (gated by FeatureFlags.NoAutoHeal). In the field the hero relies on
// crafted potions. Full passive recovery happens ONLY at a SAFE ZONE -- the home
// hub scenes (Castle/Town/Base) enumerated by DeNelle.Core.HubScenes.IsHub
// (MainCastle_Hall, Village2, CastleHub, CastleHub_MainKeep).
//
// WHAT IT DOES (on every scene load where HubScenes.IsHub(scene.name) is true):
//   - HeroHealth.Instance.RestoreToFull()   -> full HP (clears a downed latch too)
//   - HeroAbilities.RestoreManaToFull()     -> full MP
// This runs REGARDLESS of ff.noautoheal -- safe zones ALWAYS fully heal; that is
// the design (the flag gates the POST-COMBAT field auto-heal, not this).
//
// WHY A SELF-BOOTSTRAPPING DDOL SINGLETON (not a scene edit) -- mirrors
// HubAmbientVfxInjector: re-saving a .unity carries the project's scene-resave
// corruption risk (CLAUDE.md §3 "NEVER hand-edit"). This finds the hero at runtime
// via its singleton / component, so it never touches a scene file.
//
// Village -> Core only (FeatureFlags / HubScenes / FlowTrace / Guard). No
// cross-asmdef ref, no reflection. Null-safe + Guard-wrapped throughout. ASCII only.
// =============================================================================

using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;   // WO-1773: RemoteTunables - the town-regen combat gate's two knobs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>Restores the hero to full HP + MP on entering a safe-zone (hub) scene,
    /// and while the hero STANDS inside the town/castle footprint, regens HP by tick.</summary>
    public sealed class SafeZoneRecovery : MonoBehaviour
    {
        public static SafeZoneRecovery Instance { get; private set; }

        // ── TOWN-FOOTPRINT tick regen (owner 2026-07-08 felt-test) ────────────────────
        // The on-load RestoreToFull below tops the hero off when it ENTERS a hub. But
        // combat can happen INSIDE a hub with NO scene reload — the wave-loop-in-hub, or
        // the FTUE teaching wave that floors the hero at 1 HP (HeroHealth.TakeDamage). In
        // that case OnSceneLoaded never re-fires, so the hero sits at 1 HP with no recovery
        // option (the exact bug the owner hit). This adds a CONTINUOUS while-standing-in-town
        // top-up: while the hero is in a hub scene AND inside the town/castle footprint
        // (within TownRadius of the Heart-at-origin — mirroring the HUD's HudContextEvaluator
        // radial model), regen HP by tick up to full.
        //
        // SAFE-ZONE ONLY (preserves the ff.noautoheal field difficulty): the ring test IS the
        // gate. Outside the ring / in the field / in enemy-owned raid scenes the hero never
        // regens here, so no-auto-heal-in-the-field still holds; the town footprint is the sole
        // exception (identical design to the on-load RestoreToFull, which also ignores the flag).
        //
        // ⛔ WO-1773 (2026-09-16): THIS TICK HAD NO COMBAT GATE, AND THAT WAS DEAD STEP A.
        // It ran mid-wave with an enemy in melee contact. Because it is a fraction of MaxHp it
        // scales with gear while incoming contact damage is capped by WaveScalingCurve's wave-20
        // clamp, so the gap widens with every upgrade and can never be outgrown — which is the
        // arithmetic behind an external tester's "nothing can damage him he can just stand there".
        // Update() now asks TownRegenTunables whether this frame's regen is allowed. The gate's two
        // knobs default to IDENTITY, so with no database row the behaviour below is unchanged; the
        // owner seeds the row. The FTUE 1-HP case documented above is an explicit never-gate
        // carve-out in that file, so the bug this tick was added to fix stays fixed.
        private const float TownRegenFractionPerSecond = 0.12f; // ~8s empty->full; "rest a few seconds to top up"
        private const float TownRadius     = 60f;   // matches HudContextEvaluator.TownRadius (Heart at world origin, canon §7)
        private const float TownRadiusHyst = 8f;    // matches HudContextEvaluator hysteresis so the edge doesn't chatter

        private HeroLocomotion _hero;
        private bool _inTownRing;

        // ── WO-1773: THE COMBAT GATE, and why it is cached ────────────────────────────────
        // Owner-relayed tester report, 2026-09-16: "at the level he is nothing can damage him he can
        // just stand there". WO-1773's arithmetic named THIS component as dead step A: the tick below
        // restores a fraction of MaxHp per second with no combat check of any kind, and because it
        // scales with MaxHp while incoming contact damage is capped by WaveScalingCurve's clamp, the
        // gap WIDENS with every piece of gear. On the ticket's plausible build, four Cave Trolls at
        // the engine's hard melee ceiling still lose to it. It cannot be outgrown at any level.
        //
        // The decision itself lives in TownRegenTunables (pure, oracle-testable). This holds the two
        // knob values behind a refresh interval because Update runs EVERY FRAME the hero is below
        // full, and RemoteTunables.Int costs a spec lookup plus a PlayerPrefs.GetInt per call. Six
        // seconds is short enough that an operator editing the row sees it inside one wave and long
        // enough that the rail is never on the frame path.
        private const float KnobRefreshSeconds = 6f;
        private float _knobsRefreshedAt = float.NegativeInfinity;
        private int _suppressSecondsCached = RemoteTunables.TownRegenSuppressSecondsAfterHitDefault;
        private int _duringWavePctCached   = RemoteTunables.TownRegenPctDuringWaveDefault;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(SafeZoneRecovery)).AddComponent<SafeZoneRecovery>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // The hero may already be present in the scene that booted us (e.g. starting in
            // MainCastle_Hall) -- recover immediately so a fresh boot into a safe zone tops off.
            if (HubScenes.IsHub(SceneManager.GetActiveScene().name))
                Recover(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (HubScenes.IsHub(scene.name))
                Recover(scene.name);
        }

        /// <summary>
        /// TOWN-FOOTPRINT tick regen: while the hero stands inside the town/castle safe ring
        /// of a hub scene, feed a small HP top-up each frame up to full. This covers the
        /// battle->town RETURN and the in-hub combat cases the on-load RestoreToFull misses
        /// (fight in town / FTUE 1-HP floor -> no scene reload -> nothing re-fires the restore).
        /// Gated to the safe ring ONLY, so the field keeps its ff.noautoheal difficulty.
        /// </summary>
        private void Update()
        {
            var health = HeroHealth.Instance;
            if (health == null || !health.IsAlive) return;
            if (health.Hp >= health.MaxHp) return;   // already full — nothing to regen

            if (!HubScenes.IsHub(SceneManager.GetActiveScene().name)) { _inTownRing = false; return; }

            // Town footprint test = HudContextEvaluator's radial model: hub scene AND hero within
            // TownRadius of the Heart-at-origin (canon §7). Hero not resolved yet -> treat the hub
            // as safe (matches the HUD's "default to town before the hero spawns").
            bool inRing;
            if (_hero == null || !_hero) _hero = Object.FindAnyObjectByType<HeroLocomotion>();
            if (_hero == null)
            {
                inRing = true;
            }
            else
            {
                Vector3 p = _hero.transform.position;
                float distSqr = p.x * p.x + p.z * p.z;   // horizontal distance to the Heart at origin
                float edge = _inTownRing ? TownRadius + TownRadiusHyst : TownRadius;  // hysteresis at the edge
                inRing = distSqr <= edge * edge;
            }
            _inTownRing = inRing;
            if (!inRing) return;

            // ── WO-1773 THE COMBAT GATE ───────────────────────────────────────────────────
            // Everything above this point is unchanged: the hub test and the ring test still decide
            // WHERE the regen may run. This decides WHETHER it runs right now.
            //
            // Both inputs are best-effort and each one FAILS OPEN (towards today's behaviour) when it
            // cannot be resolved, which is deliberate: an un-resolvable WaveManager or HeroHealth
            // must never turn into "the hero silently stops healing in town", a defect far worse than
            // the one being fixed and one with no on-screen symptom at all.
            var wm = WaveManager.Instance;
            bool waveActive = wm != null && wm.isActiveAndEnabled && wm.Phase == WavePhase.Active;
            float sinceHit = health.SecondsSinceDamageTaken;
            bool ftue = TutorialFlow.HostilesSuppressedForTutorial;

            if (Time.time - _knobsRefreshedAt >= KnobRefreshSeconds)
            {
                _knobsRefreshedAt = Time.time;
                _suppressSecondsCached = RemoteTunables.Int(RemoteTunables.KeyTownRegenSuppressSecondsAfterHit);
                _duringWavePctCached   = RemoteTunables.Int(RemoteTunables.KeyTownRegenPctDuringWave);
            }

            var gate = TownRegenTunables.ResolveFrom(
                TownRegenFractionPerSecond, waveActive, sinceHit, ftue,
                _suppressSecondsCached, _duringWavePctCached);

            if (gate.RateFraction <= 0f)
            {
                // THE PROVING LINE FOR THE SUPPRESSED HALF. WO-1773 could not tell whether the hero
                // was never being hit or was being hit and out-healed, because nothing logged either.
                // Throttled ~1/sec: this is a per-frame path (memory logcat-ring-destroys-evidence).
                FlowTrace.Throttle("SafeZone", "town-regen-gated", 1f,
                    $"town regen GATED ({gate.Reason}): hp={health.Hp:F0}/{health.MaxHp:F0} " +
                    $"waveActive={waveActive} secondsSinceHit={sinceHit:F1} " +
                    $"window={gate.SuppressSeconds}s inWavePct={gate.DuringWavePct}. " +
                    "No HP restored this frame (WO-1773 out-of-combat rule).");
                return;
            }

            float amount = health.MaxHp * gate.RateFraction * Time.deltaTime;
            if (amount <= 0f) return;
            float before = health.Hp;
            health.RegenTick(amount);
            FlowTrace.Throttle("SafeZone", "town-regen", 1f,
                $"town regen +{(health.Hp - before):F1} -> {health.Hp:F0}/{health.MaxHp:F0} " +
                $"(ungated, {gate.Reason}; waveActive={waveActive} secondsSinceHit={sinceHit:F1} " +
                $"window={gate.SuppressSeconds}s inWavePct={gate.DuringWavePct}) " +
                "(inside town/castle footprint; field still no-auto-heal).");
        }

        /// <summary>Full HP + MP restore. Null-safe; logs each leg; never throws into the load.</summary>
        private void Recover(string sceneName)
        {
            Guard.Try("SafeZone", "full HP+MP recovery", () =>
            {
                HeroHealth.Instance?.RestoreToFull();

                var abilities = Object.FindAnyObjectByType<HeroAbilities>();
                abilities?.RestoreManaToFull();

                FlowTrace.Step("SafeZone",
                    $"SAFE-ZONE full recovery ({sceneName}): hero HP+MP restored to full.");
            });
        }
    }
}
