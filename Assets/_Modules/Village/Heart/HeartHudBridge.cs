// =============================================================================
// HeartHudBridge — WO-20: pushes Heart HP + the crystal balance into the village
// HUD. Companion to WaveHudBridge (wave) and HeroAbilitiesHudBridge
// (mana/cooldowns). Before this, VillageHudController.SetHeartHp / SetCrystals
// had no runtime caller (only the DevPanel pushed Heart HP), so the Heart HP bar
// and crystal counter stayed frozen at their UXML defaults during normal play.
//
// Cross-asmdef: DeNelle.Village cannot reference DeNelle.HUD, so the HUD is
// discovered by component-type name and its setters invoked by reflection — the
// same seam WaveHudBridge / HeroAbilitiesHudBridge use. Attached at runtime by
// VillageController (the gates/hero/HUD are baked by the edit-time scene builder,
// which the curated-scene rule forbids re-running).
//
// DEF-54 (this pass): Heart HP is now event-driven via HeartController.OnHealthChanged.
//   The per-frame SetHeartHp() push has been removed from Update(). Crystals have
//   no change event on GameStateService so they remain on a 0.5 s throttled poll —
//   cheap and correct. Update() now only runs the crystal throttle + deferred
//   Resolve() retries; it exits immediately once fully resolved + subscribed.
//
// DEF (resource bar): also pushes the full Wood/Iron/Food/Gems wallet onto the
//   HUD resource bar via VillageHudController.SetResources(int,int,int,int). The
//   numbers are the REAL banked totals from EconomyService.Snapshot (the same
//   wallet TowerUpgrade / BuildingUpgradePanel spend from), so what shows on
//   screen is exactly what the player can spend. EconomyService is in this same
//   assembly (DeNelle.Village), so it's read directly and subscribed to its
//   OnChanged event for live updates; the throttled poll re-pushes as a safety
//   net (covers EconomyService bootstrapping after this bridge resolves).
// =============================================================================

using System.Reflection;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    public sealed class HeartHudBridge : MonoBehaviour
    {
        // HeartController.SetHp clamps to 0-100, so the Heart HP scale max is 100.
        private const float HeartMaxHp = 100f;

        private Object _hud;                 // VillageHudController (held as Object — no DeNelle.HUD ref)
        private MethodInfo _setHeartHp;      // SetHeartHp(float current, float max)
        private MethodInfo _setCrystals;     // SetCrystals(int amount)
        private MethodInfo _setGold;         // SetGold(int amount) — town Gold/Coins badge (optional)
        private MethodInfo _setResources;    // SetResources(int wood, int iron, int food, int gems)
        // §12 log-on-change cache: PushResources runs every frame; trace only when the wallet moves.
        private int _lastLoggedWood = int.MinValue, _lastLoggedIron = int.MinValue,
                    _lastLoggedFood = int.MinValue, _lastLoggedCrystals = int.MinValue;
        private HeartController _heart;
        private readonly object[] _hpArgs = new object[2];
        private readonly object[] _crystalArgs = new object[1];
        private readonly object[] _goldArgs = new object[1];
        private readonly object[] _resourceArgs = new object[4];

        // DEF (resource bar): live wallet feed. EconomyService is in THIS assembly,
        // so it's read directly + subscribed to its OnChanged event (no reflection
        // for the read; only the HUD push crosses the asmdef seam).
        private EconomyService _subscribedEconomy;

        // The bridge is attached after the scene (incl. the HUD) loads, so it
        // resolves on the first frame in practice. The cap is a pure safety net:
        // if the HUD/Heart are genuinely absent, stop the per-frame scene scans
        // rather than running FindObjectsByType forever (~10s at 60fps).
        private const int MaxResolveAttempts = 600;
        private int _resolveAttempts;
        private bool _gaveUp;

        // DEF-54: event subscription tracking — must unsub on OnDisable.
        private HeartController _subscribedHeart;

        // Crystal poll throttle — GameStateService has no change event.
        private const float CrystalPollInterval = 0.5f;
        private float _crystalPollTimer;

        private void OnEnable()
        {
            Resolve();
            TrySubscribeHeart();
            TrySubscribeEconomy();
            // Push the initial HP, crystals and wallet immediately so the HUD is
            // correct on enable without waiting for the first event or poll interval.
            PushHp(_heart != null ? _heart.Hp : 0f);
            PushCrystals();
            PushGold();
            PushResources();
        }

        private void OnDisable()
        {
            if (_subscribedHeart != null)
            {
                _subscribedHeart.OnHealthChanged -= OnHeartHpChanged;
                _subscribedHeart = null;
            }
            if (_subscribedEconomy != null)
            {
                _subscribedEconomy.OnChanged -= OnEconomyChanged;
                _subscribedEconomy = null;
            }
        }

        private void Resolve()
        {
            if (_gaveUp) return;
            if (_hud != null && _heart != null) return; // fully resolved — nothing to scan
            if (++_resolveAttempts > MaxResolveAttempts)
            {
                _gaveUp = true;
                Debug.LogWarning("[HeartHudBridge] VillageHudController / HeartController not found — Heart-HP / crystal push disabled.");
                return;
            }

            if (_hud == null)
            {
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                {
                    if (mb != null && mb.GetType().Name == "VillageHudController") { _hud = mb; break; }
                }
            }
            // Audit P3 (economy-store): check ALL three reflected methods, not just _setHeartHp —
            // the asymmetric guard would never retry SetCrystals/SetResources if only one resolved.
            if (_hud != null && (_setHeartHp == null || _setCrystals == null || _setResources == null))
            {
                var t = _hud.GetType();
                _setHeartHp = t.GetMethod("SetHeartHp", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(float), typeof(float) }, null);
                _setCrystals = t.GetMethod("SetCrystals", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int) }, null);
                // Gold/Coins badge — OPTIONAL: resolved alongside the others but NOT
                // part of the re-resolve guard, so a null _setGold never blocks them.
                _setGold = t.GetMethod("SetGold", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int) }, null);
                _setResources = t.GetMethod("SetResources", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int) }, null);
            }
            if (_heart == null) _heart = FindAnyObjectByType<HeartController>();
        }

        /// <summary>
        /// Subscribes to <see cref="EconomyService.OnChanged"/> once the service
        /// exists, so the resource bar updates the moment wood/iron/food/crystals
        /// change (harvest, wave reward, upgrade spend). Idempotent.
        /// </summary>
        private void TrySubscribeEconomy()
        {
            if (_subscribedEconomy != null) return;       // already subscribed
            var econ = EconomyService.Instance;
            if (econ == null) return;                     // service not bootstrapped yet
            _subscribedEconomy = econ;
            _subscribedEconomy.OnChanged += OnEconomyChanged;
        }

        private void OnEconomyChanged(ResourceSnapshot snapshot) => PushResources(snapshot);

        /// <summary>
        /// Subscribes to <see cref="HeartController.OnHealthChanged"/> once the heart
        /// is resolved. Idempotent — safe to call multiple times.
        /// </summary>
        private void TrySubscribeHeart()
        {
            if (_subscribedHeart != null) return;     // already subscribed
            if (_heart == null) return;               // not yet resolved

            _subscribedHeart = _heart;
            _heart.OnHealthChanged += OnHeartHpChanged;
        }

        private void OnHeartHpChanged(float hp) => PushHp(hp);

        private void PushHp(float hp)
        {
            if (_setHeartHp == null || _hud == null) return;
            _hpArgs[0] = hp;
            _hpArgs[1] = HeartMaxHp;
            _setHeartHp.Invoke(_hud, _hpArgs);
        }

        private void PushCrystals()
        {
            if (_setCrystals == null || _hud == null) return;
            _crystalArgs[0] = CurrentCrystals();
            _setCrystals.Invoke(_hud, _crystalArgs);
        }

        /// <summary>Pushes the current Gold/Coins wallet to the HUD's town Gold badge. Null-safe / optional.</summary>
        private void PushGold()
        {
            if (_setGold == null || _hud == null) return;
            _goldArgs[0] = CurrentCoins();
            _setGold.Invoke(_hud, _goldArgs);
        }

        /// <summary>Pushes the current EconomyService wallet to the HUD resource bar.</summary>
        private void PushResources()
        {
            var econ = EconomyService.Instance;
            if (econ == null) return;
            PushResources(econ.Snapshot);
        }

        /// <summary>
        /// Pushes a specific wallet snapshot to the HUD's SetResources(int,int,int,int).
        /// Order matches the HUD signature: wood, iron, food, gems(crystals).
        /// </summary>
        private void PushResources(ResourceSnapshot snapshot)
        {
            if (_setResources == null || _hud == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Eco",
                    $"HeartHudBridge.PushResources SKIPPED — setResources={(_setResources != null)} hud={(_hud != null)} (HUD will NOT reflect the change)");
                return;
            }
            _resourceArgs[0] = snapshot.Wood;
            _resourceArgs[1] = snapshot.Iron;
            _resourceArgs[2] = snapshot.Stone;
            _resourceArgs[3] = snapshot.Crystals;
            _setResources.Invoke(_hud, _resourceArgs);
            // §12: this runs every frame — log ONLY when the wallet changes, else the trace floods the
            // capture (it drowned the seam-cross lines in the owner's F8 grab) and allocs a string/frame.
            // The HUD push itself stays every-frame (unchanged behavior); only the trace is gated.
            if (snapshot.Wood != _lastLoggedWood || snapshot.Iron != _lastLoggedIron ||
                snapshot.Stone != _lastLoggedFood || snapshot.Crystals != _lastLoggedCrystals)
            {
                _lastLoggedWood = snapshot.Wood; _lastLoggedIron = snapshot.Iron;
                _lastLoggedFood = snapshot.Stone; _lastLoggedCrystals = snapshot.Crystals;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Eco",
                    $"HeartHudBridge pushed HUD W{snapshot.Wood} I{snapshot.Iron} F{snapshot.Stone} C{snapshot.Crystals}");
            }
        }

        private void Update()
        {
            // WO-424: the RESOURCE bar / crystal feed must NOT be gated on a HeartController.
            // The castle hub and the overworld have no Heart of Elarion (it's a Village structure),
            // so requiring _heart here meant this bridge never subscribed to EconomyService nor
            // pushed the wallet in those scenes — the bar read 0/0/0/0 and harvested resources
            // (which DO fire EconomyService.OnChanged) were dropped on the floor. Gate only on
            // the HUD; the Heart is optional and its HP push is independently guarded on _heart.
            if (_hud == null)
            {
                Resolve();
                if (_hud == null) return;
            }

            // Keep trying to find a Heart cheaply (bounded by MaxResolveAttempts inside Resolve),
            // but never block the wallet feed on it.
            if (_heart == null) Resolve();

            // Subscriptions are idempotent; TrySubscribeHeart no-ops while _heart is null.
            TrySubscribeHeart();
            TrySubscribeEconomy();

            // Crystal poll — GameStateService has no change event; throttle to 0.5s. The wallet
            // is event-driven (EconomyService.OnChanged) but re-pushed here too as a safety net
            // for the bootstrap-order race (and to cover the first frame the HUD resolves).
            _crystalPollTimer -= Time.deltaTime;
            if (_crystalPollTimer <= 0f)
            {
                _crystalPollTimer = CrystalPollInterval;
                if (_heart != null) PushHp(_heart.Hp);
                PushCrystals();
                PushGold();
                PushResources();
            }
        }

        private static int CurrentCrystals()
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            return state != null ? state.Resources.Crystals : 0;
        }

        private static int CurrentCoins()
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            return state != null ? state.Resources.Coins : 0;
        }
    }
}
