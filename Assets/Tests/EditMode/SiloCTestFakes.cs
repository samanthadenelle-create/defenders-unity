// SCAFFOLD — CLI must build-verify under Unity Test Framework
// =============================================================================
// SiloCTestFakes — shared fakes for the MVVM Silo C (Build/Tower) VM tests.
// -----------------------------------------------------------------------------
// A scene-free IEconomy + ITowerUpgradeTarget so the Silo C ViewModels
// (StructureCardVM / BuildPaletteVM / LiveWalletSource / TowerUpgradeVM /
// PlacedTowerListVM / BuildMenuVM) are exercised over deterministic fakes,
// mirroring EconomyServiceTests. EditMode never calls Awake, so no scene wiring.
// =============================================================================

using System;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.UI;

namespace DeNelle.Tests.EditMode
{
    /// <summary>A pure in-memory <see cref="IEconomy"/> for VM tests.</summary>
    internal sealed class FakeEconomy : IEconomy
    {
        public int Coins { get; set; }
        public int Wood { get; set; }
        public int Iron { get; set; }
        public int Stone { get; set; }
        public int Crystals { get; set; }

        public bool CanAfford(ResourceCost cost)
            => Wood >= cost.Wood && Stone >= cost.Stone && Iron >= cost.Iron
               && Crystals >= cost.Crystals && Coins >= cost.Coins;

        public bool TrySpend(ResourceCost cost)
        {
            if (!CanAfford(cost)) return false;
            Wood -= cost.Wood; Stone -= cost.Stone; Iron -= cost.Iron;
            Crystals -= cost.Crystals; Coins -= cost.Coins;
            Fire();
            return true;
        }

        public ResourceCost Grant(ResourceCost amount)
        {
            Wood += amount.Wood; Stone += amount.Stone; Iron += amount.Iron;
            Crystals += amount.Crystals; Coins += amount.Coins;
            Fire();
            // Uncapped fake: every requested unit lands, so the applied basket IS the request.
            return amount;
        }

        public event Action<ResourceSnapshot> OnChanged;

        /// <summary>Raise OnChanged with the current totals (tests use this to prove live rebinds).</summary>
        public void Fire() => OnChanged?.Invoke(new ResourceSnapshot(Wood, Stone, Iron, Crystals));
    }

    /// <summary>A fake placed tower for TowerUpgradeVM tests (no scene Tower needed).</summary>
    internal sealed class FakeTowerUpgradeTarget : ITowerUpgradeTarget
    {
        public bool HasData { get; set; } = true;
        public int CurrentLevel { get; set; } = 1;
        public int NextUpgradeCost { get; set; } = 50;
        public int UpgradeCalls { get; private set; }
        public Tower.UpgradeResult Result = Tower.UpgradeResult.Success;

        public Tower.UpgradeResult TryUpgrade()
        {
            UpgradeCalls++;
            if (Result == Tower.UpgradeResult.Success) CurrentLevel++;
            return Result;
        }
    }
}
