// SCAFFOLD — CLI must build-verify under Unity Test Framework
// =============================================================================
// LiveWalletSourceTests (EditMode) — MVVM Silo C §2c permission gate.
// -----------------------------------------------------------------------------
// Locks the BuildWalletRow behaviour in LiveWalletSource: the WalletVM DTO chips
// (Wood/Iron/Food/Crystals/Gold, in order, with letter badges) project from the
// IEconomy pools, and Refresh republishes + raises Changed. Over a fake IEconomy
// (Subscribe is not exercised so no GameStateService is touched).
// =============================================================================

using NUnit.Framework;
using DeNelle.Village;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class LiveWalletSourceTests
    {
        [Test]
        public void wallet_dto_projects_all_five_pools_in_order()
        {
            var econ = new FakeEconomy { Wood = 5, Iron = 3, Stone = 7, Crystals = 9, Coins = 11 };
            var src = new LiveWalletSource(econ);
            var entries = src.Wallet.Entries;

            Assert.That(entries.Count, Is.EqualTo(5));
            Assert.That(entries[0].CurrencyId, Is.EqualTo("wood"));
            Assert.That(entries[0].IconName, Is.EqualTo("W"));
            Assert.That(entries[0].Amount, Is.EqualTo(5));
            Assert.That(entries[1].CurrencyId, Is.EqualTo("iron"));
            Assert.That(entries[1].Amount, Is.EqualTo(3));
            Assert.That(entries[2].CurrencyId, Is.EqualTo("stone"));
            Assert.That(entries[2].IconName, Is.EqualTo("S"));
            Assert.That(entries[2].Amount, Is.EqualTo(7));
            Assert.That(entries[3].CurrencyId, Is.EqualTo("crystals"));
            Assert.That(entries[3].Amount, Is.EqualTo(9));
            Assert.That(entries[4].CurrencyId, Is.EqualTo("gold"));
            Assert.That(entries[4].Amount, Is.EqualTo(11));
        }

        [Test]
        public void refresh_republishes_and_raises_changed()
        {
            var econ = new FakeEconomy { Wood = 1 };
            var src = new LiveWalletSource(econ);

            int changed = 0;
            src.Changed += () => changed++;

            econ.Wood = 250;
            src.Refresh();

            Assert.That(src.Wallet.Entries[0].Amount, Is.EqualTo(250));
            Assert.That(changed, Is.EqualTo(1), "Refresh raises Changed once");
        }

        [Test]
        public void dispose_stops_change_notifications()
        {
            var src = new LiveWalletSource(new FakeEconomy());
            int changed = 0;
            src.Changed += () => changed++;
            src.Dispose();
            src.Refresh();
            Assert.That(changed, Is.EqualTo(0), "no Changed after Dispose");
        }
    }
}
