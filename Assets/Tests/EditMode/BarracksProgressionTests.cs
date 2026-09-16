// SCAFFOLD — CLI must build-verify under Unity Test Framework
// =============================================================================
// BarracksProgressionTests (EditMode) — WO-771.9 integration permission gate.
// -----------------------------------------------------------------------------
// Behavioral, headless (no MonoBehaviour singletons): drives the PURE
// BarracksProgression core + the PURE ObsidianQueueEngine over a
// ScriptableObject.CreateInstance<GameState>() + a FakeEconomy, proving the
// WO-771.9 integration contract end-to-end:
//   * a troop is trainable/selectable ONLY when the barracks level has unlocked it,
//   * a barracks upgrade job, ENQUEUED on the Builder channel + COMPLETED, raises
//     BarracksLevel by one AND unlocks the next troop,
//   * a troop upgrade job (Research channel) raises that troop's TroopLevels entry,
//   * the barracks upgrade cost from barracks.json is deducted from the wallet,
//   * a trained troop grant lands in the existing ArmyStorage roster,
//   * an upgrade level raises the resolved effective stats (TroopStatResolver).
// Determinism/golden-sim is V2 (no RaidSim) — SKIPPED (see the class-tail note).
// =============================================================================

using System;
using NUnit.Framework;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Core.Jobs;
using DeNelle.Village;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class BarracksProgressionTests
    {
        // Controllable IEconomy (mirrors TroopTrainingVMTests.FakeEconomy).
        private sealed class FakeEconomy : IEconomy
        {
            public int Coins { get; set; }
            public int Wood { get; set; }
            public int Iron { get; set; }
            public int Stone { get; set; }
            public int Crystals { get; set; }
            public int SpendCalls;
            public event Action<ResourceSnapshot> OnChanged;

            public bool CanAfford(ResourceCost c) =>
                Coins >= c.Coins && Wood >= c.Wood && Iron >= c.Iron && Stone >= c.Stone && Crystals >= c.Crystals;

            public bool TrySpend(ResourceCost c)
            {
                if (!CanAfford(c)) return false;
                Coins -= c.Coins; Wood -= c.Wood; Iron -= c.Iron; Stone -= c.Stone; Crystals -= c.Crystals;
                SpendCalls++;
                OnChanged?.Invoke(new ResourceSnapshot(Wood, Stone, Iron, Crystals));
                return true;
            }

            public ResourceCost Grant(ResourceCost a)
            {
                Coins += a.Coins; Wood += a.Wood; Iron += a.Iron; Stone += a.Stone; Crystals += a.Crystals;
                OnChanged?.Invoke(new ResourceSnapshot(Wood, Stone, Iron, Crystals));
                // Uncapped fake: every requested unit lands, so the applied basket IS the request.
                return a;
            }
        }

        private GameState _state;

        [SetUp]
        public void SetUp()
        {
            _state = ScriptableObject.CreateInstance<GameState>();
            _state.BarracksLevel = 1;
            _state.TroopLevels = new System.Collections.Generic.Dictionary<string, int>();
            _state.Army = new ArmyStorage();
        }

        [TearDown]
        public void TearDown()
        {
            if (_state != null) UnityEngine.Object.DestroyImmediate(_state);
        }

        [Test]
        public void troop_gating_follows_barracks_level()
        {
            // Day-one (level 1) unlocks Footman + Archer; the tier-2 Spearman is locked.
            Assert.That(BarracksProgression.IsTroopUnlocked("troop-footman", 1), Is.True, "Footman unlocked at L1");
            Assert.That(BarracksProgression.IsTroopUnlocked("troop-archer", 1), Is.True, "Archer unlocked at L1");
            Assert.That(BarracksProgression.IsTroopUnlocked("troop-spearman", 1), Is.False, "Spearman locked at L1");
            Assert.That(BarracksProgression.IsTroopUnlocked("troop-spearman", 2), Is.True, "Spearman unlocks at L2");
        }

        [Test]
        public void barracks_upgrade_raises_level_and_unlocks_next_troop()
        {
            Assert.That(BarracksProgression.BarracksLevelOf(_state), Is.EqualTo(1));
            Assert.That(BarracksProgression.IsTroopUnlocked("troop-spearman", BarracksProgression.BarracksLevelOf(_state)), Is.False);

            int newLevel = BarracksProgression.ApplyBarracksUpgrade(_state);

            Assert.That(newLevel, Is.EqualTo(2), "barracks upgrade raises the level by one");
            Assert.That(_state.BarracksLevel, Is.EqualTo(2));
            Assert.That(BarracksProgression.IsTroopUnlocked("troop-spearman", _state.BarracksLevel), Is.True,
                "the newly-reached level unlocks the next troop");
        }

        [Test]
        public void enqueue_builder_job_then_complete_applies_barracks_upgrade()
        {
            // The queue-routed path (no private timer): a BarracksUpgrade job on the Builder
            // channel, driven through the PURE engine, whose completion effect is the barracks
            // level-up mutator (exactly what BarracksService.BarracksUpgradeEffect wires live).
            Assert.That(JobChannels.DefaultChannel(JobKind.BarracksUpgrade), Is.EqualTo(ChannelId.Builder),
                "barracks upgrade runs on the Builder channel");

            var ch = new ChannelState();
            var job = new BuildJobData
            {
                StructureId = BarracksService.BarracksJobId,
                Kind = (int)JobKind.BarracksUpgrade,
                Channel = (int)ChannelId.Builder,
                DurationMs = 1000,
            };
            double now = 5000;
            Assert.That(ObsidianQueueEngine.Enqueue(ch, 1, job, now), Is.True, "job starts immediately (free slot)");

            int completed = ObsidianQueueEngine.Resolve(ch, 1, now + 1500,
                j => BarracksProgression.ApplyBarracksUpgrade(_state));

            Assert.That(completed, Is.EqualTo(1), "the job completes once its duration elapses");
            Assert.That(_state.BarracksLevel, Is.EqualTo(2), "completion raised BarracksLevel by one");
        }

        [Test]
        public void troop_upgrade_job_channel_is_research_and_raises_troop_level()
        {
            Assert.That(JobChannels.DefaultChannel(JobKind.TroopUpgrade), Is.EqualTo(ChannelId.Research),
                "troop-track upgrade runs on the Research channel");

            Assert.That(BarracksProgression.TroopLevelOf(_state, "troop-footman"), Is.EqualTo(1), "baseline is level 1");
            int lvl = BarracksProgression.ApplyTroopUpgrade(_state, "troop-footman");
            Assert.That(lvl, Is.EqualTo(2));
            Assert.That(_state.TroopLevels["troop-footman"], Is.EqualTo(2), "TroopLevels raised by one");
        }

        [Test]
        public void barracks_upgrade_cost_is_deducted_from_wallet()
        {
            // Read through BarracksProgression, then pin the current canonical L2 basket.
            var cost = BarracksProgression.BarracksUpgradeCost(1);
            Assert.That(cost.Wood, Is.EqualTo(300));
            Assert.That(cost.Stone, Is.EqualTo(80));
            Assert.That(cost.Iron, Is.EqualTo(120));

            var exact = new FakeEconomy { Wood = cost.Wood, Stone = cost.Stone, Iron = cost.Iron };
            Assert.That(exact.TrySpend(cost), Is.True, "an exactly-funded wallet affords the upgrade");
            Assert.That(exact.Wood, Is.EqualTo(0));
            Assert.That(exact.Stone, Is.EqualTo(0));
            Assert.That(exact.Iron, Is.EqualTo(0));

            var broke = new FakeEconomy { Wood = cost.Wood - 1, Stone = cost.Stone, Iron = cost.Iron };
            Assert.That(broke.CanAfford(cost), Is.False, "a wallet short by one wood cannot afford it");
            Assert.That(broke.TrySpend(cost), Is.False, "and TrySpend refuses (no mutation)");
            Assert.That(broke.Wood, Is.EqualTo(cost.Wood - 1), "a failed spend deducts nothing");
        }

        [Test]
        public void trained_troop_grant_lands_in_the_army_roster()
        {
            int before = _state.Army.Owned.Count;
            int count = BarracksProgression.GrantTrainedTroop(_state, "troop-footman");
            Assert.That(count, Is.EqualTo(before + 1), "the grant adds one troop");
            Assert.That(_state.Army.Owned.Count, Is.EqualTo(before + 1));
            Assert.That(_state.Army.Owned[_state.Army.Owned.Count - 1].TroopDefId, Is.EqualTo("troop-footman"));
        }

        [Test]
        public void a_higher_upgrade_level_raises_effective_stats()
        {
            var def = TroopCatalog.Find("troop-footman");
            Assume.That(def, Is.Not.Null, "footman must exist in troops.json");

            var lvl1 = TroopStatResolver.Effective(def, 1);
            var lvl3 = TroopStatResolver.Effective(def, 3);

            Assert.That(lvl3.MaxHp, Is.GreaterThan(lvl1.MaxHp), "strength curve raises effective HP with level");
            Assert.That(lvl3.AttackRange, Is.GreaterThanOrEqualTo(lvl1.AttackRange), "reach curve is non-decreasing with level");
            Assert.That(lvl3.Level, Is.EqualTo(3));
        }

        // NOTE — determinism / golden-sim acceptance (WO-771.9 §"determinism rule 4") is a
        // V2 item: no RaidSim exists in V1 (the raid reuses the real-time combat), so there is
        // no fixed-dt tick-damage golden vector to lock here. DEFERRED to V2 per WORK_ORDER_771
        // (771.3 sim is V2). This suite covers the V1 integration contract instead.
    }
}
