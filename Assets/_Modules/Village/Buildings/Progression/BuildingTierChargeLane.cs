// =============================================================================
// BuildingTierChargeLane — the ONE place that answers "which resource does a
// building-tier upgrade actually take out of the player's wallet?"
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Buildings.Progression
// WO-2005 (BUILD inventory reconciliation) · owner ruling 22, 2026-09-06
//
// ⛔ THE AUTHORED COST KEY IS NOT THE CHARGED RESOURCE. building-tiers.json authors
// `costWood`, `costGold` and `costCrystal`. The spend uses NONE of those lanes: it
// takes the AMOUNT from BuildingTierDef.PrimaryMaterialCost = Max(CostWood,
// CostCrystal) (BuildingTierCatalog.cs:68) and picks the RESOURCE purely from the
// TIER NUMBER. Measured at source 2026-09-06:
//
//     tier 1  -> HarvestResource.Wood
//     tier 2  -> HarvestResource.Stone    // the persisted slot the player sees as STONE
//     tier 3+ -> HarvestResource.Iron
//
// So EVERY ladder's tier 2 is a STONE cost whatever its JSON says, and the Cathedral
// of Magic ladder — authored 1280/2560/5440/11200 CRYSTALS — is charged Wood, Stone,
// Iron, Iron. The screens have always been honest (they show the charged lane); the
// JSON is what lies. Owner ruling 22: **the CHARGE is right and the AUTHORING is
// wrong** — crystals are the scarce currency and re-pointing this ladder at them
// would price the Cathedral out of reach. Do NOT "fix" the code to charge crystals.
//
// ⚠ HarvestResource.Stone IS THE STONE WALLET. The enum member is a frozen persisted
// name (ResourceBuildingProgression.cs:45, GameState.Resources.Stone); WO-1416 retired
// FOOD as a resource and the Quarry now pays STONE into that same slot. Renaming the
// member would orphan every save. Read ResourceBuildingProgression.LabelFor for the
// player-facing word — never hardcode "Food".
//
// ⛔ WHY THIS FILE EXISTS AT ALL: the three-branch rule was COPY-PASTED at four sites
//     BuildingUpgradeService.TierCost        (the affordability check)
//     BuildingUpgradeService.TryUpgrade      (the job basket that a cancel refunds)
//     BuildingUpgradeVM.ComposeNextCity      (the upgrade panel's cost lines)
//     ManageScreenVM                         (the Manage card's cost line)
// and a fifth copy was about to be added by WO-2005's inventory model. Four copies of
// one rule is the exact duplicated-state shape behind CLAUDE.md §2 (the stale WO block),
// §5 (the retired assembly table) and §16 (the drifted R2 push). Three of the four now
// call this file; ManageScreenVM is owned by another lane this session and is handed
// back to be re-pointed here. **Never re-inline the branch.**
// =============================================================================

using DeNelle.Core.State;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>
    /// The tier-index -&gt; charged-resource rule for building TIER ladders
    /// (building-tiers.json). See the file header for why the authored cost key is
    /// not the charged resource.
    /// </summary>
    public static class BuildingTierChargeLane
    {
        /// <summary>
        /// The resource a tier upgrade is CHARGED in. T1 Wood, T2 Food (the Stone
        /// wallet slot), T3+ Iron. Tiers below 1 are clamped to the tier-1 lane, which
        /// is what the original four-way copy did by falling through its else-chain.
        /// </summary>
        public static HarvestResource For(int tier)
        {
            if (tier <= 1) return HarvestResource.Wood;
            if (tier == 2) return HarvestResource.Stone;
            return HarvestResource.Iron;
        }

        /// <summary>
        /// The charged lane for a tier def; <see cref="HarvestResource.Wood"/> for a
        /// null def (matching the old else-chain's fall-through) so a caller never has
        /// to null-branch before asking.
        /// </summary>
        public static HarvestResource For(BuildingTierDef def) => For(def != null ? def.Tier : 1);

        /// <summary>
        /// The player-facing word for the charged lane — routed through
        /// <see cref="ResourceBuildingProgression.LabelFor"/> so "Food" is never printed
        /// for the Stone slot. Diagnostics and reconciliation read this; the cost lines
        /// bind their own icon rows.
        /// </summary>
        public static string LabelFor(int tier) => ResourceBuildingProgression.LabelFor(For(tier));

        /// <summary>
        /// The full charged basket of a tier: the primary material in its lane, plus the
        /// Gold term. Amounts are unchanged from the authored data — only the RESOURCE
        /// is derived here.
        /// </summary>
        public static void Split(BuildingTierDef def, out HarvestResource lane, out int primary, out int gold)
        {
            lane = For(def);
            primary = def != null ? def.PrimaryMaterialCost : 0;
            gold = def != null ? def.CostGold : 0;
        }
    }
}
