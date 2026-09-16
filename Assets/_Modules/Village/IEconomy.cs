// =============================================================================
// IEconomy — minimal economy seam (WO-431, MVVM shop slice).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// A thin, testable contract over the resource economy so a pure ViewModel (originally
// ShopVM - deleted 2026-09-06 with its doorless panel, WO-1430; PartyShopVM now)
// can read balances, check affordability, spend, grant, and observe change WITHOUT
// newing up the concrete EconomyService singleton or touching a scene. EconomyService
// implements it ADDITIVELY (no member is removed/rewritten); tests inject a fake.
//
// Lives in DeNelle.Village (NOT Core) because it speaks in ResourceCost, which is a
// DeNelle.Village type — Core cannot see it (CLAUDE.md §5 / WO-431 design decision 2).
// =============================================================================

using System;

namespace DeNelle.Village
{
    /// <summary>
    /// The slice of the economy a shop ViewModel needs: read balances, check + perform
    /// transactions, and observe change. Implemented by <see cref="EconomyService"/> (additive)
    /// and by unit-test fakes so a shop VM is testable without a scene.
    /// </summary>
    public interface IEconomy
    {
        int Coins { get; }
        int Wood { get; }
        int Iron { get; }
        int Stone { get; }
        int Crystals { get; }

        /// <summary>True when every resource pool covers <paramref name="cost"/>.</summary>
        bool CanAfford(ResourceCost cost);

        /// <summary>Atomically spends <paramref name="cost"/> if affordable; false (no-op) when short.</summary>
        bool TrySpend(ResourceCost cost);

        /// <summary>
        /// Adds resources (sell refunds, rewards). Negatives clamped to 0.
        /// ⚠ RETURNS THE **APPLIED** BASKET, not the requested one — a grant is clamped by
        /// TownBankCapacity, so requested and applied differ whenever a store is full. Callers
        /// that LOG or POP a gain must report this value: showing the pre-clamp number is how a
        /// silent loss hides (the Echo silo dump did exactly that until 2026-08-16).
        /// </summary>
        ResourceCost Grant(ResourceCost amount);

        /// <summary>Fires after any resource change with the new totals.</summary>
        event Action<ResourceSnapshot> OnChanged;
    }
}
