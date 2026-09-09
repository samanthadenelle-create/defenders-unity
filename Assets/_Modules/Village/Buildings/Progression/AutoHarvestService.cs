// =============================================================================
// AutoHarvestService — passive auto-collect capstone (Lumbermill "Ancient Sawmill").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Buildings.Progression
//
// The owner-named WC3 "auto-gather" lever. Collectors accrue pending resources but
// normally need a manual CollectAll tap (ResourceCollectorService). When the player
// owns a perk that sets GameModifiers.AutoCollect (compiled into ModifierService.
// Active), this DDOL ticker auto-invokes CollectAll on a fixed interval so resources
// bank passively — no tap required. NO-OP (and the timer resets) whenever the flag is
// off, so it costs nothing until the capstone is researched. Village -> Core is legal,
// so it reads the Core ModifierService directly.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Village.Monetization;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>Self-bootstrapping DDOL ticker that auto-taps CollectAll while the
    /// <c>autoCollect</c> perk flag is active. Idle (zero cost) when the flag is off.</summary>
    public sealed class AutoHarvestService : MonoBehaviour
    {
        /// <summary>Seconds between passive auto-collect sweeps (CoC-style slow drip).</summary>
        private const float TickInterval = 20f;
        private const string HostName = "AutoHarvestHost";

        private float _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (GameObject.Find(HostName) != null) return;
            var host = new GameObject(HostName);
            Object.DontDestroyOnLoad(host);
            host.AddComponent<AutoHarvestService>();
            FlowTrace.Once("Perk", "auto-harvest-bootstrap", "AutoHarvestService DDOL created");
        }

        private void Update()
        {
            var mods = ModifierService.Active;              // static, never null (Compute() returns fresh)
            bool perk = mods != null && mods.AutoCollect;
            // WO-1246: a harvest-auto-collect pack token opens a 24h CollectAll window.
            // The perk remains the permanent capstone; the token is the paid-for timed twin.
            if (!ConvenienceRedeemer.HarvestAutoCollectShouldTick(perk))
            { _timer = 0f; return; }

            _timer += Time.deltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            // Passive collection must never open the player-owned Harvest Result modal.
            // The first actionable warning belongs to a manual Collect; background ticks
            // continue banking/retaining silently after that.
            int banked = ResourceCollectorService.CollectAll(showResult: false);
            FlowTrace.Step("Perk", "auto-harvest tick collected " + banked);
        }
    }
}
