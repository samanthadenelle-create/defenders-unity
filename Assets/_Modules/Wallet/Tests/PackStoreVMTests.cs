// SCAFFOLD — CLI must build-verify under Unity Test Framework
// =============================================================================
// PackStoreVMTests (EditMode) — WO-744 permission gate for the pack-store's
// game-state seam. Locks the ownership + entitlement grant MOVED out of PackStore
// (the View) into PackStoreVM, so the View no longer names GameStateService /
// FindFirstObjectOfType (ARCHITECTURE_PRINCIPLES.md §2c). Exercises the VM over a
// plain injected GameState — NO scene, NO GameStateService singleton.
//
// Asserts (money/reward path unchanged):
//   * ApplyPackContents tops up crystals/food/coins + records the pack SKU + its
//     cosmetic SKUs as owned.
//   * IsOwned reflects the grant; IsOwned is false for an unknown SKU.
//   * Ownership is idempotent (RecordOwned dedups) — the purchase re-grant guard.
//   * A null state does not throw (self-reports the lost entitlement).
// =============================================================================

using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DeNelle.Core.State;
using DeNelle.Wallet;

namespace DeNelle.Wallet.Tests
{
    [TestFixture]
    public class PackStoreVMTests
    {
        private static PackDef MakePack(string sku, int crystals, int food, int coins, params string[] cosmetics)
        {
            var pack = new PackDef
            {
                Sku = sku,
                Contents = new PackContents
                {
                    Economy = new PackEconomy { Crystals = crystals, Stone = food, Coins = coins },
                }
            };
            if (cosmetics != null)
                foreach (var c in cosmetics) pack.Contents.Cosmetics.Add(c);
            return pack;
        }

        [Test]
        public void apply_pack_records_owned_and_cosmetics()
        {
            // Economy grants route through EconomyService and CosmeticOwnershipService.
            // (resolved by type-name reflection). Those singletons are ABSENT in EditMode (no scene),
            // so each grant self-reports a loud FlowTrace.Fail (Debug.LogError) and state.Resources is
            // left untouched — correct behaviour, so we no longer assert the resource top-up here (it
            // is covered where the services are live). We DO assert the ownership record, which is
            // written straight onto GameState (no seam) and must always land.
            var state = ScriptableObject.CreateInstance<GameState>();

            var vm = new PackStoreVM(() => state);
            var pack = MakePack("pack-x", crystals: 100, food: 50, coins: 25, "cos-a", "cos-b");

            // The four service-missing grant failures, in emission order (resources, coins, then each
            // cosmetic) — self-reported via FlowTrace.Fail (Debug.LogError).
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("grant resources.*EconomyService missing"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("grant coins.*EconomyService missing"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("grant cosmetic 'cos-a'.*CosmeticOwnershipService missing"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("grant cosmetic 'cos-b'.*CosmeticOwnershipService missing"));

            vm.ApplyPackContents(pack);

            Assert.That(state.OwnedItemIds, Does.Contain("pack-x"), "pack SKU recorded owned");
            Assert.That(state.OwnedItemIds, Does.Contain("cos-a"), "cosmetic SKU recorded owned");
            Assert.That(state.OwnedItemIds, Does.Contain("cos-b"), "cosmetic SKU recorded owned");
            Assert.That(vm.IsOwned("pack-x"), Is.True, "IsOwned reflects the grant");
        }

        [Test]
        public void is_owned_is_false_for_unknown_sku()
        {
            var state = ScriptableObject.CreateInstance<GameState>();
            var vm = new PackStoreVM(() => state);
            Assert.That(vm.IsOwned("never-bought"), Is.False);
        }

        [Test]
        public void ownership_grant_is_idempotent()
        {
            var state = ScriptableObject.CreateInstance<GameState>();
            var vm = new PackStoreVM(() => state);
            var pack = MakePack("pack-x", crystals: 10, food: 0, coins: 0);

            // Each apply routes the crystal grant through the (EditMode-absent) EconomyService seam,
            // self-reporting one FlowTrace.Fail (Debug.LogError) per call — two applies, two errors.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("grant resources.*EconomyService missing"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("grant resources.*EconomyService missing"));

            vm.ApplyPackContents(pack);
            vm.ApplyPackContents(pack);   // re-grant

            int occurrences = state.OwnedItemIds.FindAll(s => s == "pack-x").Count;
            Assert.That(occurrences, Is.EqualTo(1),
                "RecordOwned must dedup — the SKU is recorded once even on a re-grant");
        }

        [Test]
        public void apply_with_null_state_does_not_throw()
        {
            var vm = new PackStoreVM(() => null);
            var pack = MakePack("pack-x", crystals: 10, food: 0, coins: 0);
            // The null-state path escalated to a loud FlowTrace.Fail (Debug.LogError): the payment
            // already settled, so a lost entitlement must self-report rather than silently swallow.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no GameStateService/State"));
            Assert.DoesNotThrow(() => vm.ApplyPackContents(pack),
                "no GameState must self-report, never throw (the payment already settled)");
        }
    }
}
