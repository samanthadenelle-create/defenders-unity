// =============================================================================
// CastleDefensePlansPickup -- WO-1013 walk-over collection of the Castle Defense
// Plans drop (the wave-2 reward that unlocks the Arcane Spire).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Mirrors the ComposedKeyPickup walk-over grammar (Dungeons WO-1001 slice 7):
// trigger sphere + GetComponentInParent<HeroHealth> hero check + one-shot _taken
// latch. ONE MonoBehaviour, named for the file (the ComposedKeyLock.cs lesson --
// though this component is runtime-spawned by CastleDefensePlansService and never
// scene-serialized, the convention costs nothing and prevents the bake trap).
//
// MECHANICS vs PRESENTATION (WO-1013 SS2/SS3, binding): TryCollect() IS the
// mechanics -- persisted unlock flag (ProgressionUnlocks -> SeenTutorials store) +
// the funding grant sized to the LIVE catalog row cost (never hardcoded; the
// arcane basket is crystals-inclusive because the row's cost says so, WO-947).
// The WO-1012 contextual guide beat is PRESENTATION and hangs off the
// PlansCollected event seam -- the unlock+grant never depend on it firing.
// No banner, no modal, no FREE label (SS3): the card simply unlocks and the
// wallet simply affords it.
// =============================================================================

using System;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// Walk-over pickup for the Castle Defense Plans prop (WO-1013). Collection
    /// flips the persisted Arcane Spire unlock and grants the first Spire's funding
    /// (read from the catalog row at grant time). Once, ever.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CastleDefensePlansPickup : MonoBehaviour
    {
        /// <summary>The catalog id the plans unlock (the Arcane Spire row).</summary>
        public const string SpireCatalogId = "tower_arcane_spire";

        /// <summary>
        /// WO-1012 beat seam: raised ONCE, after the unlock+grant mechanics committed.
        /// The contextual-step pipeline (tutorial-steps.json signal adapter) subscribes
        /// here; the mechanics never depend on a subscriber existing.
        /// </summary>
        public static event Action PlansCollected;

        private bool _taken;

        private void OnTriggerEnter(Collider other)
        {
            if (_taken) return;
            if (other == null || other.GetComponentInParent<HeroHealth>() == null) return;

            if (!TryCollect(transform.position))
            {
                // Already unlocked (stale prop from a race) -- retire the prop quietly.
                if (ProgressionUnlocks.IsUnlocked(SpireCatalogId))
                {
                    _taken = true;
                    gameObject.SetActive(false);
                    Destroy(gameObject);
                }
                // Catalog row missing: leave the prop standing so a later walk-over
                // retries once the catalog is up (TryCollect traced the refusal).
                return;
            }

            _taken = true;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        /// <summary>
        /// The WO-1013 collection mechanics, callable headless (regression oracle):
        /// idempotence gate -> live catalog cost read -> persisted unlock -> funding
        /// grant -> beat seam. Returns TRUE only on the one real collection; every
        /// later call returns false with no second grant.
        /// </summary>
        public static bool TryCollect() => TryCollect(null);

        /// <summary>See <see cref="TryCollect()"/>. <paramref name="at"/> is the prop
        /// position for the funnel trace (null when driven headless).</summary>
        public static bool TryCollect(Vector3? at)
        {
            // ONE authored collection, ever (WO-1013 SS3) -- the persisted flag is the gate.
            if (ProgressionUnlocks.IsUnlocked(SpireCatalogId))
            {
                FlowTrace.Step("Progression",
                    "plans-collect ignored: tower_arcane_spire already unlocked (once-ever gate held)");
                return false;
            }

            // The funding is sized to the LIVE catalog row -- never a hardcoded basket.
            // BuildModeController.CostFor is the ONE catalog-cost resolver (multi-cost row,
            // buildCost-crystals fallback) -- the flat catalog price, deliberately WITHOUT
            // the WO-855 tower softcap: the plans fund "the first one" at its real catalog
            // cost (WO-1013 SS1.4).
            var entry = CatalogRegistry.Get(SpireCatalogId);
            var cost = BuildModeController.CostFor(entry);
            if (entry == null || entry.repo == null)
            {
                FlowTrace.Warn("Progression",
                    "plans-collect deferred: catalog row 'tower_arcane_spire' not registered -- " +
                    "no unlock, no grant; the prop stays for a retry once the catalog is up");
                return false;
            }

            if (at.HasValue)
                FlowTrace.Step("Progression", $"plans-collected @ {at.Value} (walk-over, WO-1013)");
            else
                FlowTrace.Step("Progression", "plans-collected (headless/direct TryCollect, WO-1013)");

            // Flag FIRST (once-ever beats a lost grant; the grant below is Guard-wrapped
            // and traced, so a dropped grant is a logged defect, never a silent one).
            if (!ProgressionUnlocks.Unlock(SpireCatalogId))
            {
                FlowTrace.Fail("Progression",
                    "plans-collect aborted: unlock flag write refused (no GameStateService?) -- no grant made");
                return false;
            }

            Guard.Try("Progression", "plans funding grant", () =>
            {
                var eco = EconomyService.Instance;
                if (eco == null)
                {
                    FlowTrace.Fail("Progression",
                        "plans funding DROPPED: EconomyService unavailable (unlock stands, basket lost)");
                    return;
                }
                // GrantPurchased = the promised-exact-amount lane (WO-901): the plans PROMISE
                // one Spire's worth, so the town bank cap must never shave the basket.
                eco.GrantPurchased(BuildModeController.ToEconomy(cost));
            });

            FlowTrace.Step("Progression",
                $"plans-unlocked: tower_arcane_spire visible-lock lifts on next palette Configure; " +
                $"funding granted wood={cost.wood} food={cost.stone} iron={cost.iron} crystals={cost.crystals} " +
                "(live catalog row, arcane basket -- WO-1013)");

            Guard.Try("Progression", "plans-collected beat seam", () => PlansCollected?.Invoke());
            return true;
        }
    }
}
