// =============================================================================
// VigilCeremonyTests (EditMode) — WO-1874.
// -----------------------------------------------------------------------------
// No scene, no network: VigilCeremonyWords is pure; the VM takes a fake ISource
// and IVigilCeremonyLedger. PlayerPrefs is never required.
// =============================================================================

using System;
using NUnit.Framework;
using DeNelle.Core.Circle;
using DeNelle.HUD;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class VigilCeremonyTests
    {
        private sealed class FakeLedger : IVigilCeremonyLedger
        {
            public event Action DressingChanged;
            public event Action SequenceRequested;
            public event Action SequenceCompleted;

            public int CurrentDressingTier { get; private set; }
            public int LastSeenEpoch { get; private set; }
            public VigilCeremonyPayload LastPayload { get; private set; }

            public bool HasSeen(int epochIndex)
            {
                if (epochIndex <= 0) return false;
                return LastSeenEpoch >= epochIndex;
            }

            public void RememberEpoch(int epochIndex)
            {
                if (epochIndex <= 0) return;
                if (epochIndex > LastSeenEpoch) LastSeenEpoch = epochIndex;
            }

            public void SetDressing(int tier)
            {
                int clamped = VigilCeremonyWords.ClampTier(tier);
                if (clamped == CurrentDressingTier) return;
                CurrentDressingTier = clamped;
                DressingChanged?.Invoke();
            }

            public void RememberPayload(VigilCeremonyPayload payload)
            {
                if (payload == null || !payload.HasContent) return;
                LastPayload = payload;
            }

            public void RequestSequence() => SequenceRequested?.Invoke();
            public void NotifySequenceCompleted() => SequenceCompleted?.Invoke();
        }

        private sealed class FakeSource : VigilCeremonyVM.ISource
        {
            public string Body = "{\"ok\":true}";
            public long Status = 200;
            public void FetchBallot(Action<string, long> done) => done(Body, Status);
        }

        [Test]
        public void WordForTier_MapsFiveWords()
        {
            Assert.That(VigilCeremonyWords.WordForTier(1), Is.EqualTo("Ember"));
            Assert.That(VigilCeremonyWords.WordForTier(2), Is.EqualTo("Flame"));
            Assert.That(VigilCeremonyWords.WordForTier(3), Is.EqualTo("Beacon"));
            Assert.That(VigilCeremonyWords.WordForTier(4), Is.EqualTo("Pyre"));
            Assert.That(VigilCeremonyWords.WordForTier(5), Is.EqualTo("Dawn"));
            Assert.That(VigilCeremonyWords.WordForTier(0), Is.EqualTo(string.Empty));
            Assert.That(VigilCeremonyWords.WordForTier(6), Is.EqualTo(string.Empty));
        }

        [Test]
        public void HighestUnlockedTier_MatchesMeetsTier()
        {
            Assert.That(VigilCeremonyWords.HighestUnlockedTier(0), Is.EqualTo(0));
            Assert.That(VigilCeremonyWords.HighestUnlockedTier(0.0001), Is.EqualTo(1));
            Assert.That(VigilCeremonyWords.HighestUnlockedTier(1), Is.EqualTo(1));
            Assert.That(VigilCeremonyWords.HighestUnlockedTier(86400), Is.EqualTo(2));
            Assert.That(VigilCeremonyWords.HighestUnlockedTier(604800), Is.EqualTo(3));
            Assert.That(VigilCeremonyWords.HighestUnlockedTier(2592000), Is.EqualTo(4));
            Assert.That(VigilCeremonyWords.HighestUnlockedTier(7776000), Is.EqualTo(5));
        }

        [Test]
        public void DawnLine_DoesNotContainAncestorsStir()
        {
            string dawn = VigilCeremonyWords.LineForTier(5);
            Assert.That(dawn, Does.Not.Contain("ancestors stir"));
            Assert.That(dawn, Does.Not.Contain("The ancestors stir"));
            Assert.That(dawn, Is.EqualTo("The canopy shifts. The Circle has been heard."));
        }

        [Test]
        public void Ledger_HasSeenAndRememberEpoch()
        {
            var ledger = new FakeLedger();
            Assert.That(ledger.HasSeen(5), Is.False);
            ledger.RememberEpoch(4);
            Assert.That(ledger.HasSeen(4), Is.True);
            Assert.That(ledger.HasSeen(5), Is.False);
            ledger.RememberEpoch(5);
            Assert.That(ledger.HasSeen(5), Is.True);
        }

        [Test]
        public void VM_PassedUnseenEpoch_ShouldAutoPlay()
        {
            var ledger = new FakeLedger();
            ledger.RememberEpoch(4);
            var src = new FakeSource { Body = PassedCeremonyJson(5) };
            var vm = new VigilCeremonyVM(src, ledger, () => { });
            vm.Fetch(null);
            Assert.That(vm.ShouldAutoPlay, Is.True);
            Assert.That(vm.HasPayload, Is.True);
            Assert.That(vm.EffectText, Is.EqualTo(string.Empty));
        }

        [Test]
        public void VM_PassedAlreadySeen_ShouldNotAutoPlay()
        {
            var ledger = new FakeLedger();
            ledger.RememberEpoch(5);
            var src = new FakeSource { Body = PassedCeremonyJson(5) };
            var vm = new VigilCeremonyVM(src, ledger, () => { });
            vm.Fetch(null);
            Assert.That(vm.ShouldAutoPlay, Is.False);
            Assert.That(vm.HasPayload, Is.True);
        }

        [Test]
        public void VM_NotPassed_NoAutoPlay()
        {
            var ledger = new FakeLedger();
            var src = new FakeSource
            {
                Body = "{\"ok\":true,\"vigil_weight\":1,\"ceremony\":{" +
                       "\"epoch_index\":5,\"word\":\"Ember\",\"line\":\"A spark.\"," +
                       "\"circle_name\":\"Emberwatch\",\"perk_title\":\"\"," +
                       "\"perk_description\":\"\",\"passed\":false,\"next_ends_at\":\"soon\"}}"
            };
            var vm = new VigilCeremonyVM(src, ledger, () => { });
            vm.Fetch(null);
            Assert.That(vm.ShouldAutoPlay, Is.False);
        }

        [Test]
        public void VM_EffectTextNeverAppears_AndCeremonyDtoHasNoEffect()
        {
            var ledger = new FakeLedger();
            var src = new FakeSource { Body = PassedCeremonyJson(3) };
            var vm = new VigilCeremonyVM(src, ledger, () => { });
            vm.Fetch(null);
            Assert.That(vm.EffectText, Is.EqualTo(string.Empty));
            Assert.That(vm.PerkDescriptionText, Does.Not.Contain("%"));

            var dto = typeof(CircleWire).GetNestedType("CeremonyDto");
            Assert.That(dto, Is.Not.Null);
            Assert.That(dto.GetField("effect"), Is.Null);
            Assert.That(dto.GetProperty("effect"), Is.Null);
            Assert.That(dto.GetField("perk_id"), Is.Null);
        }

        [Test]
        public void VM_ReplayDoesNotRequireUnseenEpoch()
        {
            var ledger = new FakeLedger();
            ledger.RememberEpoch(5);
            var src = new FakeSource { Body = PassedCeremonyJson(5) };
            bool closed = false;
            var vm = new VigilCeremonyVM(src, ledger, () => { closed = true; });
            vm.MarkReplay();
            vm.Fetch(null);
            Assert.That(vm.ShouldAutoPlay, Is.False);
            Assert.That(vm.HasPayload, Is.True);
            Assert.That(vm.IsReplay, Is.True);
            int seen = ledger.LastSeenEpoch;
            vm.Continue();
            Assert.That(ledger.LastSeenEpoch, Is.EqualTo(seen), "replay CONTINUE must not bump last-seen");
            Assert.That(closed, Is.True);
        }

        [Test]
        public void VM_AutoPlayContinueBumpsLastSeen()
        {
            var ledger = new FakeLedger();
            ledger.RememberEpoch(4);
            var src = new FakeSource { Body = PassedCeremonyJson(5) };
            var vm = new VigilCeremonyVM(src, ledger, () => { });
            vm.Fetch(null);
            Assert.That(vm.ShouldAutoPlay, Is.True);
            vm.Continue();
            Assert.That(ledger.HasSeen(5), Is.True);
        }

        private static string PassedCeremonyJson(int epoch)
        {
            return "{\"ok\":true,\"vigil_weight\":100,\"ceremony\":{" +
                   "\"epoch_index\":" + epoch + "," +
                   "\"word\":\"Dawn\"," +
                   "\"line\":\"The canopy shifts. The Circle has been heard.\"," +
                   "\"circle_name\":\"Emberwatch\"," +
                   "\"perk_title\":\"The canopy keeps watch\"," +
                   "\"perk_description\":\"The Circle's watch is complete.\"," +
                   "\"passed\":true," +
                   "\"next_ends_at\":\"soon\"}}";
        }
    }
}
