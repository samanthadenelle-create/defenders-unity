// =============================================================================
// PartyShopVMTests (EditMode) — §2c permission gate for the PartyShopVM changes made
// by the strict-MVVM icon-leak / DI-in-Open silo (UI_MVVM_MIGRATION_PLAN §1).
// -----------------------------------------------------------------------------
// Locks the projection that was MOVED out of PartyShopPanelMvvm (the View):
//   • the 3D-preview MODEL descriptor (PreviewModelFor) — it resolves the gear def's
//     prefabPath/addressable in the VM now, so the View no longer names GearCatalog;
//   • the wallet readout (Coins) from the injected IEconomy;
//   • the CreateDefault DI-in-Open factory (resolves EconomyService.Instance +
//     VillageInventory.Instance itself) — returns the store for the View to dispose.
// Exercised over fake seams — no scene, no singleton.
// =============================================================================

using System;
using System.Collections.Generic;
using NUnit.Framework;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class PartyShopVMTests
    {
        private sealed class FakeStore : IInventoryStore
        {
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
            public event Action Changed;
            public void RaiseChanged() => Changed?.Invoke();
            public IReadOnlyDictionary<string, int> OwnedCounts => Counts;
            public int OwnedQuantity(string id) => Counts.TryGetValue(id, out var v) ? v : 0;
            public WeaponDef FindWeapon(string id) => null;
            public ArmorDef FindArmor(string id) => null;
            public AccessoryDef FindAccessory(string id) => null;
            public IReadOnlyList<AccessoryDef> AccessoriesForSlot(string slot, int level) => Array.Empty<AccessoryDef>();
            public IReadOnlyList<(WeaponDef def, int qty)> OwnedWeapons() => Array.Empty<(WeaponDef, int)>();
            public IReadOnlyList<(ArmorDef def, int qty)> OwnedArmor() => Array.Empty<(ArmorDef, int)>();
            public IReadOnlyList<(string id, int qty)> OwnedConsumables() => Array.Empty<(string, int)>();
            public bool WeaponFitsClass(WeaponDef w, string job) => true;
            public bool ArmorFitsClass(ArmorDef a, string job) => true;
            public bool TryRemove(string id, int n) => false;
        }

        private sealed class FakeEquip : IEquipTarget
        {
            public string TargetName { get; set; } = "Grom";
            public string TargetClass { get; set; } = "knight";
            public int TargetLevel { get; set; } = 1;
            public WeaponDef EquippedWeapon { get; set; }
            public ArmorDef EquippedArmor { get; set; }
            public WeaponDef EquippedOffHand { get; set; }
            public AccessoryDef EquippedRing { get; set; }
            public AccessoryDef EquippedAmulet { get; set; }
            public string EquippedWeaponName => EquippedWeapon?.name;
            public string EquippedArmorName => EquippedArmor?.name;
            public float WeaponMult => 1f;
            public float ArmorDefense => 0f;
            public float CurrentHealth { get; set; }
            public float MaxHealth { get; set; }
            public float CurrentMana { get; set; }
            public float MaxMana { get; set; }
            public event Action EquipChanged;
            public void EquipWeaponById(string id) { EquipChanged?.Invoke(); }
            public void EquipArmorById(string id) { EquipChanged?.Invoke(); }
            public void UnequipWeapon() { EquipChanged?.Invoke(); }
            public void UnequipArmor() { EquipChanged?.Invoke(); }
            public void EquipOffHandById(string id) { EquipChanged?.Invoke(); }
            public void UnequipOffHand() { EquipChanged?.Invoke(); }
            public void EquipAccessoryById(string id) { EquipChanged?.Invoke(); }
            public void UnequipAccessory(string slot) { EquipChanged?.Invoke(); }
        }

        private sealed class FakeEconomy : IEconomy
        {
            public int Coins { get; set; }
            public int Wood { get; set; }
            public int Iron { get; set; }
            public int Stone { get; set; }
            public int Crystals { get; set; }
            public event Action<ResourceSnapshot> OnChanged;
            public bool CanAfford(ResourceCost cost) => Coins >= cost.Coins;
            public bool TrySpend(ResourceCost cost) { if (!CanAfford(cost)) return false; Coins -= cost.Coins; OnChanged?.Invoke(new ResourceSnapshot(Wood, Stone, Iron, Crystals)); return true; }
            // Coins-only fake: it credits ONLY amount.Coins, so the applied basket it returns must
            // carry only those coins. Returning the full request would make the fake lie about
            // wood/food/iron/crystals it never banked.
            public ResourceCost Grant(ResourceCost amount)
            {
                Coins += amount.Coins;
                OnChanged?.Invoke(new ResourceSnapshot(Wood, Stone, Iron, Crystals));
                return new ResourceCost(coins: amount.Coins);
            }
        }

        private static PartyShopVM NewVm(IEconomy eco)
        {
            var store = new FakeStore();
            var members = new List<IEquipTarget> { new FakeEquip() };
            var levels = new List<int> { 1 };
            return new PartyShopVM("", eco, store, members, levels);
        }

        [Test]
        public void wallet_projects_from_injected_economy()
        {
            var eco = new FakeEconomy { Coins = 4242 };
            using var vm = NewVm(eco);
            Assert.That(vm.Coins, Is.EqualTo(4242), "the preview wallet chip reads Coins off the injected economy");
        }

        [Test]
        public void preview_model_for_non_gear_or_missing_id_is_not_gear()
        {
            using var vm = NewVm(new FakeEconomy { Coins = 100 });
            // No detail for an unknown id -> role null -> IsGear false (View shows the 2D icon/glyph).
            Assert.That(vm.PreviewModelFor("no-such-id").IsGear, Is.False,
                "a row with no gear detail must not resolve a 3D model (View falls back to 2D)");
            Assert.That(vm.PreviewModelFor(null).IsGear, Is.False, "a null id is never gear");
            Assert.That(vm.PreviewModelFor("").IsGear, Is.False, "an empty id is never gear");
        }

        [Test]
        public void create_default_returns_store_and_zero_wallet_without_economy()
        {
            // CreateDefault resolves EconomyService.Instance + VillageInventory.Instance itself (both
            // null in EditMode), so the View drops those singleton reads. Store returned to dispose.
            var members = new List<IEquipTarget> { new FakeEquip() };
            var levels = new List<int> { 1 };
            using var vm = PartyShopVM.CreateDefault("", null, members, levels, () => { }, null, out var store);
            Assert.That(store, Is.Not.Null, "CreateDefault must return the built store for the View to dispose");
            Assert.That(vm.Coins, Is.EqualTo(0), "null economy -> 0 gold");
            store.Dispose();
        }
    }
}
