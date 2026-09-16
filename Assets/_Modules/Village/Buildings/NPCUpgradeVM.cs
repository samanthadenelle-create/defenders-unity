// =============================================================================
// NPCUpgradeVM — the tiny economy seam for NPCUpgradeStation (WO-744 MVVM).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// NPCUpgradeStation is a WORLD-SPACE interactable (a diegetic WorldSpace-canvas
// prompt at a district building), NOT a modal screen panel — so it is not a full
// IPanelViewModel/IPanelView conversion. This small VM exists only to OWN the one
// game-state read the View used to name directly (EconomyService.Instance): the
// spend + first-harvest-bonus grant. The world-prompt build/visual-upgrade
// behaviour stays in the View unchanged; it now routes the transaction through
// here so the presentation layer stops naming the economy singleton.
//
// PURE C# over the IEconomy seam (no UnityEngine UI types) — unit-testable with a
// fake economy (ARCHITECTURE_PRINCIPLES §2c). CreateDefault is the sole resolution
// site for EconomyService.Instance (mirrors PartyShopVM/BuildingUpgradeVM).
// =============================================================================

namespace DeNelle.Village
{
    /// <summary>
    /// The NPC upgrade station's economy transaction seam: try-spend the upgrade cost and grant the
    /// symbolic first-harvest bonus, behind the injectable <see cref="IEconomy"/> so the world-space
    /// View never names <c>EconomyService.Instance</c>.
    /// </summary>
    public sealed class NPCUpgradeVM
    {
        private readonly IEconomy _economy;

        /// <summary>Sole resolution site — resolves the live economy so the View never touches the singleton.</summary>
        public static NPCUpgradeVM CreateDefault() => new NPCUpgradeVM(EconomyService.Instance);

        public NPCUpgradeVM(IEconomy economy)
        {
            _economy = economy;
        }

        /// <summary>
        /// Atomically spends the upgrade <paramref name="cost"/> if affordable. Returns false (no
        /// spend) when the economy is missing OR the player is short — the exact old guard
        /// (<c>econ == null || !econ.TrySpend(cost)</c>).
        /// </summary>
        public bool TryPurchaseUpgrade(ResourceCost cost)
        {
            return _economy != null && _economy.TrySpend(cost);
        }

        /// <summary>
        /// Grants the symbolic "first harvest boost" the station awards on a successful upgrade
        /// (+5 Wood, +5 Food). Null-safe. Mirrors the old <c>econ.Grant(wood: 5, food: 5)</c>.
        /// </summary>
        public void GrantFirstHarvestBonus()
        {
            _economy?.Grant(new ResourceCost(wood: 5, stone: 5));
        }
    }
}
