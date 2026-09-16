// SCAFFOLD — CLI must build-verify under Unity Test Framework
// =============================================================================
// TroopTrainingVMTests (EditMode) — WO-744 permission gate for the Barracks
// training MVVM slice. Locks the behavior MOVED out of TroopTrainingPanel (the
// View) into the pure TroopTrainingVM, so the View swap is safe only while these
// stay green (ARCHITECTURE_PRINCIPLES.md §2c). Uses a FAKE IEconomy + a real
// ArmyStorage so the VM is exercised with NO scene, NO EconomyService singleton,
// NO GameState.
//
// Asserts:
//   * roster projects every troop, sorted by UnlockBarracksTier (non-decreasing).
//   * Detail.Affordable flips with the fake wallet balance.
//   * Train with a queued trainAction reports Trained (accepted) WITHOUT growing the
//     army (WO-778 — units land when the Train job completes).
//   * Legacy null trainAction + TrainNow still mutates the army (capacity edge).
//   * Train on a locked troop does NOT mutate + does NOT spend (Locked).
//   * Train on an unaffordable troop does NOT mutate + does NOT spend (Failed).
//   * Train respects the army-cap slot edge (stops when the cap fills).
//   * CreateDefault builds a non-empty roster (null economy/army in EditMode).
//   * Dispose() unsubscribes (no callback after dispose).
// =============================================================================

using System;
using NUnit.Framework;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class TroopTrainingVMTests
    {
        // ── Fake economy: controllable pools + transaction bookkeeping (mirrors ShopVMTests) ──
        private sealed class FakeEconomy : IEconomy
        {
            public int Coins { get; set; }
            public int Wood { get; set; }
            public int Iron { get; set; }
            public int Stone { get; set; }
            public int Crystals { get; set; }

            public int SpendCalls;

            public event Action<ResourceSnapshot> OnChanged;

            public bool CanAfford(ResourceCost cost) =>
                Coins >= cost.Coins && Wood >= cost.Wood && Iron >= cost.Iron &&
                Stone >= cost.Stone && Crystals >= cost.Crystals;

            public bool TrySpend(ResourceCost cost)
            {
                if (!CanAfford(cost)) return false;
                Coins -= cost.Coins; Wood -= cost.Wood; Iron -= cost.Iron;
                Stone -= cost.Stone; Crystals -= cost.Crystals;
                SpendCalls++;
                OnChanged?.Invoke(new ResourceSnapshot(Wood, Stone, Iron, Crystals));
                return true;
            }

            public ResourceCost Grant(ResourceCost amount)
            {
                Coins += amount.Coins; Wood += amount.Wood; Iron += amount.Iron;
                Stone += amount.Stone; Crystals += amount.Crystals;
                OnChanged?.Invoke(new ResourceSnapshot(Wood, Stone, Iron, Crystals));
                // Uncapped fake: every requested unit lands, so the applied basket IS the request.
                return amount;
            }
        }

        private static FakeEconomy Rich() =>
            new FakeEconomy { Coins = 1000000, Wood = 1000000, Iron = 1000000, Stone = 1000000, Crystals = 1000000 };

        // The first trainable (unlocked) troop in the projection, or null if none.
        private static string FirstTrainable(TroopTrainingVM vm)
        {
            foreach (var t in vm.Troops) if (!t.Locked) return t.Id;
            return null;
        }

        // The first locked troop in the projection, or null if all are day-one.
        private static string FirstLocked(TroopTrainingVM vm)
        {
            foreach (var t in vm.Troops) if (t.Locked) return t.Id;
            return null;
        }

        [Test]
        public void roster_lists_all_troops_sorted_by_unlock_tier()
        {
            using var vm = new TroopTrainingVM(Rich(), new ArmyStorage(), null);
            Assert.That(vm.Troops.Count, Is.GreaterThan(0), "roster must project the catalog troops");

            int prev = int.MinValue;
            foreach (var t in vm.Troops)
            {
                int tier = vm.Detail(t.Id).UnlockBarracksTier;
                Assert.That(tier, Is.GreaterThanOrEqualTo(prev),
                    "roster must be sorted by UnlockBarracksTier (non-decreasing)");
                prev = tier;
            }
        }

        [Test]
        public void affordable_flips_with_fake_wallet_balance()
        {
            using (var vmRich = new TroopTrainingVM(Rich(), new ArmyStorage(), null))
            {
                string id = FirstTrainable(vmRich);
                Assert.That(id, Is.Not.Null, "test needs at least one trainable (day-one) troop");
                Assert.That(vmRich.Detail(id).Affordable, Is.True,
                    "with a huge wallet a trainable troop must read affordable");
            }

            var broke = new FakeEconomy();   // all pools 0
            using (var vmBroke = new TroopTrainingVM(broke, new ArmyStorage(), null))
            {
                // A day-one troop with a non-zero cost must read unaffordable at 0 resources.
                foreach (var t in vmBroke.Troops)
                {
                    var d = vmBroke.Detail(t.Id);
                    if (!t.Locked && d.CostString != "Free")
                        Assert.That(d.Affordable, Is.False,
                            "with 0 resources a costed trainable troop must read unaffordable");
                }
            }
        }

        [Test]
        public void train_queued_action_accepts_without_growing_army()
        {
            // WO-778: queued path — trainAction spends + returns count, army unchanged until job completes.
            var eco = Rich();
            var army = new ArmyStorage();
            Func<string, int, int> queueFake = (id, qty) =>
            {
                // Spend once per unit like BarracksService.EnqueueTraining would.
                int n = 0;
                for (int i = 0; i < qty; i++)
                {
                    var def = TroopCatalog.Find(id);
                    if (def == null) break;
                    if (!eco.TrySpend(new ResourceCost(coins: def.CostGold))) break;
                    n++;
                }
                return n;
            };
            using var vm = new TroopTrainingVM(eco, army, null, onSaved: null, trainAction: queueFake);

            string id = FirstTrainable(vm);
            Assert.That(id, Is.Not.Null, "test needs at least one trainable troop");

            int changed = 0;
            vm.Changed += () => changed++;

            int before = army.Owned.Count;
            int spends = eco.SpendCalls;
            var result = vm.Train(id, 1);

            Assert.That(result.Outcome, Is.EqualTo(TrainOutcome.Queued),
                "accepted (enqueued) train must report Queued");
            Assert.That(result.Count, Is.EqualTo(1), "one accepted unit");
            Assert.That(army.Owned.Count, Is.EqualTo(before),
                "queued train must NOT grow the army until the Train job completes");
            Assert.That(eco.SpendCalls, Is.EqualTo(spends + 1), "queued train spends at enqueue");
            Assert.That(changed, Is.GreaterThan(0), "Train must raise Changed so the View re-renders");
        }

        [Test]
        public void train_legacy_null_action_mutates_army_instantly()
        {
            // Null trainAction keeps the legacy instant TrainNow path (capacity / cheat tests).
            var eco = Rich();
            var army = new ArmyStorage();
            using var vm = new TroopTrainingVM(eco, army, null);

            string id = FirstTrainable(vm);
            Assert.That(id, Is.Not.Null, "test needs at least one trainable troop");

            int before = army.Owned.Count;
            var result = vm.Train(id, 1);

            Assert.That(result.Outcome, Is.EqualTo(TrainOutcome.Trained), "instant train reports Trained");
            Assert.That(result.Count, Is.EqualTo(1), "one train adds one troop");
            Assert.That(army.Owned.Count, Is.EqualTo(before + 1), "legacy null action must mint into the army");
        }

        [Test]
        public void train_locked_troop_does_not_mutate_or_spend()
        {
            var eco = Rich();
            var army = new ArmyStorage();
            using var vm = new TroopTrainingVM(eco, army, null);

            string id = FirstLocked(vm);
            Assume.That(id, Is.Not.Null, "no locked troop in the catalog to exercise the gate — inconclusive");

            int before = army.Owned.Count;
            int spends = eco.SpendCalls;
            var result = vm.Train(id, 1);

            Assert.That(result.Outcome, Is.EqualTo(TrainOutcome.Locked), "a locked troop must report Locked");
            Assert.That(army.Owned.Count, Is.EqualTo(before), "a locked train must not mutate the army");
            Assert.That(eco.SpendCalls, Is.EqualTo(spends), "a locked train must not spend");
        }

        [Test]
        public void train_unaffordable_fails_no_spend_no_mutation()
        {
            var broke = new FakeEconomy();   // all pools 0
            var army = new ArmyStorage();
            using var vm = new TroopTrainingVM(broke, army, null);

            // Find a trainable troop whose cost is non-zero (so 0 resources cannot afford it).
            string id = null;
            foreach (var t in vm.Troops)
                if (!t.Locked && vm.Detail(t.Id).CostString != "Free") { id = t.Id; break; }
            Assume.That(id, Is.Not.Null, "no costed trainable troop to exercise the afford gate — inconclusive");

            var result = vm.Train(id, 1);

            Assert.That(result.Outcome, Is.EqualTo(TrainOutcome.Failed), "an unaffordable train must report Failed");
            Assert.That(army.Owned.Count, Is.EqualTo(0), "an unaffordable train must not mutate the army");
            Assert.That(broke.SpendCalls, Is.EqualTo(0), "an unaffordable train must not spend");
        }

        [Test]
        public void train_respects_army_cap_slot_edge()
        {
            var eco = Rich();
            var army = new ArmyStorage();
            using var vm = new TroopTrainingVM(eco, army, null);

            string id = FirstTrainable(vm);
            Assert.That(id, Is.Not.Null, "test needs at least one trainable troop");

            int slots = vm.Detail(id).Slots;
            Assume.That(slots, Is.GreaterThan(0), "trainable troop must have a positive slot cost");
            int expected = army.MaxArmySize / slots;   // how many fit before the cap fills

            // Ask for far more than the cap allows; the VM must stop at the cap.
            var result = vm.Train(id, expected + 50);

            Assert.That(result.Count, Is.EqualTo(expected), "train must stop when the army cap fills");
            Assert.That(army.Owned.Count, Is.EqualTo(expected), "army must hold exactly the cap-limited count");
        }

        [Test]
        public void create_default_builds_a_non_empty_roster()
        {
            // EditMode has no EconomyService/GameState singleton -> null economy + null army. The roster
            // still projects (affordability computed as true against a null economy). Locks the factory.
            using var vm = TroopTrainingVM.CreateDefault(null);
            Assert.That(vm.Troops.Count, Is.GreaterThan(0), "CreateDefault must build the catalog roster");
        }

        [Test]
        public void dispose_unsubscribes_no_callback_after_dispose()
        {
            var eco = Rich();
            var vm = new TroopTrainingVM(eco, new ArmyStorage(), null);

            int changed = 0;
            vm.Changed += () => changed++;
            vm.Dispose();

            int before = changed;
            eco.Grant(new ResourceCost(wood: 50));
            Assert.That(changed, Is.EqualTo(before),
                "after Dispose the VM must not raise Changed from economy events (handler unsubscribed)");
        }
    }
}
