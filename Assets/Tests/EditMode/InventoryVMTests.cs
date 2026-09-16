// =============================================================================
// InventoryVMTests (EditMode) — WO-434 Phase B permission gate for InventoryVM.
// -----------------------------------------------------------------------------
// Locks the inventory STATE + LOGIC that Phase C will bind a View to, exercised with
// FAKE IInventoryStore / IEquipTarget so the VM runs with NO scene, NO singleton
// (ARCHITECTURE_PRINCIPLES.md §2 / §2c; mirrors ShopVMTests).
//
// Asserts:
//   • owned-list projection is non-empty + matches the fake store (data gap closed),
//   • tab filtering swaps Slots + resets the selection,
//   • Select fills the Selected detail,
//   • Use routes through the EFFECT seam (WO-844): applied -> item consumed by the seam
//     + "Used X."; refused -> item KEPT + the seam's truthful reason; no seam -> item
//     kept ("Nothing happens."); gear can never be used. Drop calls the store (remove),
//   • Equip routes a weapon to the equip target + raises Changed,
//   • Dispose unsubscribes (no callback after dispose).
//
// Uses synthetic WeaponDef/ArmorDef + a fully in-memory store, so the asserts hold
// regardless of whether the real gear JSON is shipped in this env.
// =============================================================================

using System;
using System.Collections.Generic;
using NUnit.Framework;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class InventoryVMTests
    {
        // ── In-memory store seeded with synthetic defs (no gear JSON dependency) ──
        private sealed class FakeStore : IInventoryStore
        {
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
            public readonly Dictionary<string, WeaponDef> Weapons = new Dictionary<string, WeaponDef>();
            public readonly Dictionary<string, ArmorDef> Armors = new Dictionary<string, ArmorDef>();
            public int RemoveCalls;

            public event Action Changed;
            public void RaiseChanged() => Changed?.Invoke();

            public IReadOnlyDictionary<string, int> OwnedCounts => Counts;
            public int OwnedQuantity(string id) => Counts.TryGetValue(id, out var v) ? v : 0;
            public WeaponDef FindWeapon(string id) => Weapons.TryGetValue(id, out var w) ? w : null;
            public ArmorDef FindArmor(string id) => Armors.TryGetValue(id, out var a) ? a : null;
            public AccessoryDef FindAccessory(string id) => null;   // WO-543: not exercised here
            public IReadOnlyList<AccessoryDef> AccessoriesForSlot(string slot, int level) => Array.Empty<AccessoryDef>();

            public IReadOnlyList<(WeaponDef def, int qty)> OwnedWeapons()
            {
                var l = new List<(WeaponDef, int)>();
                foreach (var kv in Counts) if (kv.Value > 0 && Weapons.TryGetValue(kv.Key, out var w)) l.Add((w, kv.Value));
                return l;
            }
            public IReadOnlyList<(ArmorDef def, int qty)> OwnedArmor()
            {
                var l = new List<(ArmorDef, int)>();
                foreach (var kv in Counts) if (kv.Value > 0 && Armors.TryGetValue(kv.Key, out var a)) l.Add((a, kv.Value));
                return l;
            }
            public IReadOnlyList<(string id, int qty)> OwnedConsumables()
            {
                var l = new List<(string, int)>();
                foreach (var kv in Counts)
                    if (kv.Value > 0 && !Weapons.ContainsKey(kv.Key) && !Armors.ContainsKey(kv.Key)) l.Add((kv.Key, kv.Value));
                return l;
            }
            public bool WeaponFitsClass(WeaponDef w, string job) =>
                string.IsNullOrEmpty(w?.job) || w.job == "any" || string.Equals(w.job, job, StringComparison.OrdinalIgnoreCase);
            public bool ArmorFitsClass(ArmorDef a, string job) => true;

            public bool TryRemove(string id, int n)
            {
                if (!Counts.TryGetValue(id, out var have) || have < n) return false;
                RemoveCalls++;
                int left = have - n;
                if (left <= 0) Counts.Remove(id); else Counts[id] = left;
                RaiseChanged();
                return true;
            }
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
            public float WeaponMult => EquippedWeapon != null ? EquippedWeapon.damageMult : 1f;
            public float ArmorDefense => EquippedArmor != null ? EquippedArmor.defense : 0f;
            public float CurrentHealth { get; set; }
            public float MaxHealth { get; set; }
            public float CurrentMana { get; set; }
            public float MaxMana { get; set; }
            public int EquipWeaponCalls;
            public int EquipOffHandCalls;
            public event Action EquipChanged;
            public void EquipWeaponById(string id) { EquipWeaponCalls++; EquippedWeapon = new WeaponDef { id = id, name = id, damageMult = 2f }; EquipChanged?.Invoke(); }
            public void EquipArmorById(string id) { EquippedArmor = new ArmorDef { id = id, name = id, defense = 0.3f }; EquipChanged?.Invoke(); }
            public void UnequipWeapon() { EquippedWeapon = null; EquipChanged?.Invoke(); }
            public void UnequipArmor() { EquippedArmor = null; EquipChanged?.Invoke(); }
            public void EquipOffHandById(string id) { EquipOffHandCalls++; EquippedOffHand = new WeaponDef { id = id, name = id, category = "shield" }; EquipChanged?.Invoke(); }
            public void UnequipOffHand() { EquippedOffHand = null; EquipChanged?.Invoke(); }
            public void EquipAccessoryById(string id) { EquippedRing = new AccessoryDef { id = id, name = id, slot = "ring" }; EquipChanged?.Invoke(); }
            public void UnequipAccessory(string slot) { if (slot == "amulet") EquippedAmulet = null; else EquippedRing = null; EquipChanged?.Invoke(); }
        }

        // Minimal IEconomy so the footer-wallet projection (InventoryVM.Coins/Crystals) is testable
        // over a fake — no EconomyService / GameState singleton.
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

        private static FakeStore SeedStore()
        {
            var s = new FakeStore();
            s.Weapons["sword"] = new WeaponDef { id = "sword", name = "Iron Sword", job = "knight", damageMult = 1.2f, rarity = "common" };
            s.Weapons["axe"]   = new WeaponDef { id = "axe",   name = "War Axe",    job = "any",    damageMult = 1.4f, rarity = "rare" };
            s.Armors["mail"]   = new ArmorDef  { id = "mail",  name = "Chainmail",  job = "any",    defense = 0.2f,    rarity = "common" };
            s.Counts["sword"] = 1;
            s.Counts["axe"]   = 2;
            s.Counts["mail"]  = 1;
            s.Counts["heal-potion"] = 3;   // consumable (not in Weapons/Armors)
            return s;
        }

        [Test]
        public void owned_weapons_projection_non_empty_and_matches_store()
        {
            var store = SeedStore();
            using var vm = new InventoryVM(store);

            Assert.That(vm.ActiveTab, Is.EqualTo(InventoryTabKind.Weapons));
            Assert.That(vm.Slots.Count, Is.EqualTo(store.OwnedWeapons().Count));
            Assert.That(vm.Slots.Count, Is.EqualTo(2), "two owned weapons must project to two slots");

            var ids = new HashSet<string>();
            foreach (var s in vm.Slots) ids.Add(s.Id);
            Assert.That(ids.Contains("sword"), Is.True);
            Assert.That(ids.Contains("axe"), Is.True);
        }

        [Test]
        public void tab_counts_reflect_owned_categories()
        {
            var store = SeedStore();
            using var vm = new InventoryVM(store);
            Assert.That(vm.Tabs.Count, Is.EqualTo(5));
            Assert.That(vm.Tabs[(int)InventoryTabKind.Weapons].Count, Is.EqualTo(3),
                "tab badge counts owned copies (sword x1 + axe x2), while Slots count definitions");
            Assert.That(vm.Tabs[(int)InventoryTabKind.OffHand].Count, Is.Zero);
            Assert.That(vm.Tabs[(int)InventoryTabKind.Armor].Count, Is.EqualTo(1));
            Assert.That(vm.Tabs[(int)InventoryTabKind.Consumables].Count, Is.EqualTo(1));
        }

        [Test]
        public void select_tab_swaps_slots_and_resets_selection()
        {
            var store = SeedStore();
            using var vm = new InventoryVM(store);

            vm.Select(0);
            Assert.That(vm.SelectedId, Is.Not.Null);

            vm.SelectTab((int)InventoryTabKind.Consumables);
            Assert.That(vm.ActiveTab, Is.EqualTo(InventoryTabKind.Consumables));
            Assert.That(vm.SelectedId, Is.Null, "switching tabs must reset selection");
            Assert.That(vm.Slots.Count, Is.EqualTo(1), "one owned consumable");
            Assert.That(vm.Slots[0].Id, Is.EqualTo("heal-potion"));
        }

        [Test]
        public void select_fills_selected_detail()
        {
            var store = SeedStore();
            using var vm = new InventoryVM(store);

            Assert.That(vm.Selected, Is.Null);
            vm.Select(0);
            Assert.That(vm.Selected, Is.Not.Null);
            var d = vm.Selected.Value;
            Assert.That(string.IsNullOrEmpty(d.Name), Is.False);
            Assert.That(d.CanEquip, Is.True, "a weapon detail must be equippable");
            Assert.That(d.CanUse, Is.False, "a weapon is not a consumable");
            Assert.That(vm.SelectedSlotIndex, Is.EqualTo(0));
        }

        // ── WO-844: Use routes through the EFFECT seam, never a bare store decrement ──

        /// <summary>A fake effect seam honouring the InventoryUseResult contract: on apply
        /// it consumes the item itself (the way ConsumableUseService.TryUse does).</summary>
        private static Func<string, InventoryUseResult> ApplyingSeam(FakeStore store, List<string> calls = null) =>
            id =>
            {
                calls?.Add(id);
                return store.TryRemove(id, 1)
                    ? InventoryUseResult.Ok()
                    : InventoryUseResult.Refused("Nothing to use.");
            };

        [Test]
        public void use_with_effect_applied_consumes_item_and_reports_used()
        {
            var store = SeedStore();
            var seamCalls = new List<string>();
            using var vm = new InventoryVM(store, useEffect: ApplyingSeam(store, seamCalls));
            vm.SelectTab((int)InventoryTabKind.Consumables);
            vm.Select(0);

            int changed = 0;
            vm.Changed += () => changed++;

            vm.Use();

            Assert.That(seamCalls, Is.EqualTo(new List<string> { "heal-potion" }),
                "Use must route the selected id through the effect seam exactly once");
            Assert.That(store.OwnedQuantity("heal-potion"), Is.EqualTo(2),
                "the seam consumed exactly ONE (the VM must not double-decrement)");
            Assert.That(vm.Status, Does.StartWith("Used "), "an applied effect reports Used X.");
            Assert.That(changed, Is.GreaterThan(0), "Use must raise Changed");
        }

        [Test]
        public void use_with_effect_refused_keeps_item_and_reports_reason()
        {
            var store = SeedStore();
            using var vm = new InventoryVM(store,
                useEffect: _ => InventoryUseResult.Refused("Already at full health."));
            vm.SelectTab((int)InventoryTabKind.Consumables);
            vm.Select(0);

            vm.Use();

            Assert.That(store.OwnedQuantity("heal-potion"), Is.EqualTo(3),
                "a refused effect must NOT consume the item");
            Assert.That(store.RemoveCalls, Is.EqualTo(0), "the store must never see a remove on refusal");
            Assert.That(vm.Status, Is.EqualTo("Already at full health."),
                "the seam's truthful reason surfaces as the status");
            Assert.That(vm.SelectedId, Is.EqualTo("heal-potion"), "the kept item stays selected");
        }

        [Test]
        public void use_without_effect_seam_keeps_item_and_never_claims_used()
        {
            // No seam bound = no effect path. The OLD bug was consuming anyway with zero
            // effect ("Used X." lie) - lock the honest refusal.
            var store = SeedStore();
            using var vm = new InventoryVM(store);
            vm.SelectTab((int)InventoryTabKind.Consumables);
            vm.Select(0);

            vm.Use();

            Assert.That(store.OwnedQuantity("heal-potion"), Is.EqualTo(3),
                "no effect seam -> the item must be KEPT (never consume-for-nothing)");
            Assert.That(store.RemoveCalls, Is.EqualTo(0));
            Assert.That(vm.Status, Is.EqualTo("Nothing happens."));
        }

        [Test]
        public void use_on_gear_refuses_and_never_calls_seam_or_store()
        {
            var store = SeedStore();
            var seamCalls = new List<string>();
            using var vm = new InventoryVM(store, useEffect: ApplyingSeam(store, seamCalls));
            vm.Select(0);   // a weapon (Weapons tab is active by default)

            vm.Use();

            Assert.That(vm.Status, Is.EqualTo("That item cannot be used."));
            Assert.That(seamCalls, Is.Empty, "gear must never reach the effect seam");
            Assert.That(store.RemoveCalls, Is.EqualTo(0), "gear must never be consumed by Use");
        }

        [Test]
        public void drop_calls_store_and_raises_changed()
        {
            var store = SeedStore();
            using var vm = new InventoryVM(store);
            vm.Select(0);   // a weapon

            int changed = 0;
            vm.Changed += () => changed++;
            int before = store.RemoveCalls;

            vm.Drop();

            Assert.That(store.RemoveCalls, Is.EqualTo(before + 1), "Drop must remove one through the store");
            Assert.That(changed, Is.GreaterThan(0), "Drop must raise Changed");
        }

        [Test]
        public void equip_routes_weapon_to_equip_target_and_raises_changed()
        {
            var store = SeedStore();
            var equip = new FakeEquip();
            using var vm = new InventoryVM(store, equip);

            // Select the owned weapon "sword".
            int idx = -1;
            for (int i = 0; i < vm.Slots.Count; i++) if (vm.Slots[i].Id == "sword") { idx = i; break; }
            Assert.That(idx, Is.GreaterThanOrEqualTo(0));
            vm.Select(idx);

            int changed = 0;
            vm.Changed += () => changed++;

            vm.Equip();

            Assert.That(equip.EquipWeaponCalls, Is.EqualTo(1), "Equip must route the weapon to the equip target");
            Assert.That(equip.EquippedWeapon?.id, Is.EqualTo("sword"));
            Assert.That(changed, Is.GreaterThan(0), "Equip must raise Changed");
        }

        [Test]
        public void off_hand_is_separate_marks_worn_and_routes_directly()
        {
            var store = SeedStore();
            store.Weapons["heater"] = new WeaponDef { id = "heater", name = "Heater", job = "knight", category = "shield", defense = 0.12f };
            store.Counts["heater"] = 1;
            var equip = new FakeEquip { EquippedOffHand = store.Weapons["heater"] };
            using var vm = new InventoryVM(store, equip);

            for (int i = 0; i < vm.Slots.Count; i++)
                Assert.That(vm.Slots[i].Id, Is.Not.EqualTo("heater"), "Weapons must exclude off-hand rows");
            vm.SelectTab((int)InventoryTabKind.OffHand);
            Assert.That(vm.Slots.Count, Is.EqualTo(1));
            Assert.That(vm.Slots[0].Id, Is.EqualTo("heater"));
            Assert.That(vm.Slots[0].Equipped, Is.True);

            vm.Select(0);
            vm.Equip();
            Assert.That(equip.EquipOffHandCalls, Is.EqualTo(1));
            Assert.That(equip.EquipWeaponCalls, Is.Zero);
        }

        [Test]
        public void store_changed_event_rebuilds_and_raises()
        {
            var store = SeedStore();
            using var vm = new InventoryVM(store);

            int changed = 0;
            vm.Changed += () => changed++;

            // External grant of a new weapon -> the VM must re-render off the store event.
            store.Weapons["spear"] = new WeaponDef { id = "spear", name = "Spear", job = "any", damageMult = 1.3f };
            store.Counts["spear"] = 1;
            store.RaiseChanged();

            Assert.That(changed, Is.GreaterThan(0), "store Changed must propagate to the VM");
            Assert.That(vm.Slots.Count, Is.EqualTo(3), "the new owned weapon must appear");
        }

        [Test]
        public void wallet_projects_from_injected_economy()
        {
            // WO icon-leak/DI: the footer wallet now reads InventoryVM.Coins/Crystals (sourced from
            // the injected IEconomy) instead of GameStateService in the View. Lock the projection.
            var store = SeedStore();
            var eco = new FakeEconomy { Coins = 1234, Crystals = 56 };
            using var vm = new InventoryVM(store, null, null, eco);
            Assert.That(vm.Coins, Is.EqualTo(1234), "footer gold projects from the injected economy");
            Assert.That(vm.Crystals, Is.EqualTo(56), "footer crystals project from the injected economy");
        }

        [Test]
        public void create_default_returns_store_and_zero_wallet_without_economy()
        {
            // CreateDefault resolves VillageInventory.Instance + EconomyService.Instance itself (both
            // null in EditMode), so the View drops those singleton reads. Store is returned to dispose;
            // a null economy degrades to a 0/0 wallet (the same fallback the old footer showed).
            using var vm = InventoryVM.CreateDefault(null, () => { }, out var store);
            Assert.That(store, Is.Not.Null, "CreateDefault must return the built store for the View to dispose");
            Assert.That(vm.Coins, Is.EqualTo(0), "null economy -> 0 gold");
            Assert.That(vm.Crystals, Is.EqualTo(0), "null economy -> 0 crystals");
            Assert.That(vm.Tabs.Count, Is.EqualTo(5), "the five item piles still project from the (empty) store");
            store.Dispose();
        }

        [Test]
        public void dispose_unsubscribes_no_callback_after_dispose()
        {
            var store = SeedStore();
            var vm = new InventoryVM(store);

            int changed = 0;
            vm.Changed += () => changed++;
            vm.Dispose();

            int before = changed;
            store.RaiseChanged();
            Assert.That(changed, Is.EqualTo(before), "after Dispose the VM must not raise Changed from store events");
        }
    }
}
