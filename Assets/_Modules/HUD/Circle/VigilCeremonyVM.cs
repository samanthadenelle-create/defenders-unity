// =============================================================================
// VigilCeremonyVM — state + commands for the Ceremony of Vigil plate (WO-1874).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ THE VIEW IS A SKIN. Every string the plate shows is a key or a VM-published
// value. No UnityEngine types (CircleScreenVM.cs:14-15 rule) so EditMode tests
// drive this with a fake ISource + ILedger.
//
// ⛔ NO STAT COPY. EffectText is declared ALWAYS EMPTY. CeremonyDto has no effect
// field. Perk copy is title + description only.
//
// v1: no Lonely Remnant ceremony (not in a Circle => no auto-play, no plate).
// =============================================================================

using System;
using DeNelle.Core.Circle;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.HUD
{
    /// <summary>
    /// Fetch the current ballot, dress the Heart, and decide whether the epoch plate
    /// auto-plays or is a Ballots replay.
    /// </summary>
    public sealed class VigilCeremonyVM : IPanelViewModel, IDisposable
    {
        private const string Sys = "VigilCeremony";

        public const string KeyHeld = "circle.ceremony.held";
        public const string KeyChosen = "circle.ceremony.chosen";
        public const string KeyContinue = "circle.ceremony.continue";
        public const string KeyReplay = "circle.ceremony.replay";
        public const string KeyNext = "circle.ceremony.next";
        public const string KeyWordEmber = "circle.ceremony.word.ember";
        public const string KeyWordFlame = "circle.ceremony.word.flame";
        public const string KeyWordBeacon = "circle.ceremony.word.beacon";
        public const string KeyWordPyre = "circle.ceremony.word.pyre";
        public const string KeyWordDawn = "circle.ceremony.word.dawn";
        public const string KeyLineEmber = "circle.ceremony.line.ember";
        public const string KeyLineFlame = "circle.ceremony.line.flame";
        public const string KeyLineBeacon = "circle.ceremony.line.beacon";
        public const string KeyLinePyre = "circle.ceremony.line.pyre";
        public const string KeyLineDawn = "circle.ceremony.line.dawn";

        /// <summary>Narrow rail: only FetchBallot is required (CircleSource already has it).</summary>
        public interface ISource
        {
            void FetchBallot(Action<string, long> done);
        }

        private sealed class SourceAdapter : ISource
        {
            private readonly CircleScreenVM.ISource _inner;
            public SourceAdapter(CircleScreenVM.ISource inner) { _inner = inner; }
            public void FetchBallot(Action<string, long> done)
            {
                if (_inner == null) { done(null, 0); return; }
                _inner.FetchBallot(done);
            }
        }

        private readonly ISource _source;
        private readonly IVigilCeremonyLedger _ledger;
        private readonly Action _onClose;
        private bool _disposed;
        private bool _inCircle;
        private bool _fetched;
        private bool _cannedLock;

        /// <summary>The ONE thing UiMvvmConformanceRegression.cs:74-78 looks for.</summary>
        public static VigilCeremonyVM CreateDefault(Action onClose)
            => new VigilCeremonyVM(new SourceAdapter(new CircleSource()), VigilCeremonyLedger.Shared, onClose);

        public VigilCeremonyVM(CircleScreenVM.ISource source, IVigilCeremonyLedger ledger, Action onClose)
            : this(new SourceAdapter(source), ledger, onClose)
        {
        }

        public VigilCeremonyVM(ISource source, IVigilCeremonyLedger ledger, Action onClose)
        {
            _source = source;
            _ledger = ledger;
            _onClose = onClose;
            ContinueKey = KeyContinue;
            ReplayKey = KeyReplay;
            HeldKey = KeyHeld;
            ChosenKey = KeyChosen;
            NextKey = KeyNext;
            EffectText = string.Empty;
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────
        public string Title => string.IsNullOrEmpty(WordTitleText)
            ? new LocalizedText("circle.title").Resolve()
            : WordTitleText;
        public event Action Changed;
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
        }

        private void Raise()
        {
            if (_disposed) return;
            Changed?.Invoke();
        }

        // ── published plate ───────────────────────────────────────────────────
        public bool InCircle => _inCircle;
        public bool Fetched => _fetched;
        public bool IsReplay { get; private set; }
        public bool ShouldAutoPlay { get; private set; }
        public bool HasPayload { get; private set; }

        public int EpochIndex { get; private set; }
        public int DressingTier { get; private set; }
        public string Word { get; private set; }
        public string WordKey { get; private set; }
        public string WordTitleText { get; private set; }
        public string LineKey { get; private set; }
        public string LineText { get; private set; }
        public string CircleName { get; private set; }
        public string HeldKey { get; private set; }
        public string HeldText { get; private set; }
        public string ChosenKey { get; private set; }
        public string ChosenText { get; private set; }
        public string PerkTitleText { get; private set; }
        public string PerkDescriptionText { get; private set; }
        public string NextKey { get; private set; }
        public string NextText { get; private set; }
        public string ContinueKey { get; private set; }
        public string ReplayKey { get; private set; }
        public string NextEndsAt { get; private set; }

        /// <summary>
        /// RULING 3 pin. ALWAYS EMPTY. CeremonyDto has no effect field; this property
        /// exists so the absence is deliberate, same as BallotOptionRowVM.EffectText.
        /// </summary>
        public string EffectText { get; private set; }

        public IVigilCeremonyLedger Ledger => _ledger;

        /// <summary>Fetch the ballot, dress immediately, compute auto-play.</summary>
        public void Fetch(Action done)
        {
            if (_source == null)
            {
                _inCircle = false;
                ShouldAutoPlay = false;
                _fetched = true;
                Raise();
                done?.Invoke();
                return;
            }

            FlowTrace.Step(Sys, "fetch ballot for ceremony consider");
            _source.FetchBallot((body, status) =>
            {
                ApplyBallot(body, status);
                done?.Invoke();
            });
        }

        /// <summary>Replay path: play even if this epoch was already seen.</summary>
        public void MarkReplay()
        {
            IsReplay = true;
            ShouldAutoPlay = false;
            Raise();
        }

        /// <summary>Ask the Heart to play roots then trunk then canopy.</summary>
        public void BeginPresentation()
        {
            if (_ledger == null) return;
            _ledger.RequestSequence();
        }

        /// <summary>
        /// CONTINUE. Auto-play CONTINUE bumps last-seen. Replay CONTINUE does not.
        /// </summary>
        public void Continue()
        {
            if (!IsReplay) RememberIfNeeded();
            Close();
        }

        /// <summary>
        /// Skip jumps to dressing-on and closes. Auto-play Skip bumps last-seen.
        /// Replay Skip does not.
        /// </summary>
        public void Skip()
        {
            if (!IsReplay) RememberIfNeeded();
            Close();
        }

        public void RememberIfNeeded()
        {
            if (IsReplay) return;
            if (EpochIndex <= 0) return;
            _ledger?.RememberEpoch(EpochIndex);
        }

        /// <summary>
        /// QA canned plate: apply a remembered payload without fetching a live ballot.
        /// EpochIndex 0 is not a live epoch; ShouldAutoPlay stays false so Consider
        /// cannot loop this as a settled epoch.
        /// </summary>
        public void ApplyCannedPayload(VigilCeremonyPayload payload)
        {
            if (payload == null || !payload.HasContent)
            {
                FlowTrace.Warn(Sys, "canned ceremony payload empty — no plate.");
                return;
            }
            _cannedLock = true;
            _fetched = true;
            _inCircle = true;
            IsReplay = false;
            ApplyPayload(payload, 0);
            ShouldAutoPlay = false;
            Raise();
        }

        private void ApplyBallot(string body, long status)
        {
            if (_cannedLock)
            {
                FlowTrace.Step(Sys, "canned ceremony payload locked — ignoring live ballot (not a live epoch lie).");
                _fetched = true;
                Raise();
                return;
            }
            _fetched = true;
            ShouldAutoPlay = false;
            HasPayload = false;
            _inCircle = false;
            EffectText = string.Empty;

            if (status == 0)
            {
                FlowTrace.Warn(Sys, "ceremony fetch transport failed — no plate.");
                Raise();
                return;
            }

            var refusal = CircleWire.RefusalCode(body);
            if (refusal == "CLAN_NOT_IN_CLAN")
            {
                FlowTrace.Step(Sys, "not in a Circle — v1 Lonely Remnant ceremony is none.");
                Raise();
                return;
            }
            if (status != 200 || refusal != null)
            {
                FlowTrace.Warn(Sys, "ceremony fetch refused status=" + status + " code=" + (refusal ?? ""));
                Raise();
                return;
            }

            var parsed = CircleWire.Parse<CircleWire.BallotResponse>(body);
            if (parsed == null || !parsed.ok)
            {
                FlowTrace.Warn(Sys, "ceremony ballot body did not parse.");
                Raise();
                return;
            }

            _inCircle = true;
            var payload = BuildPayload(parsed, _ledger != null ? _ledger.LastPayload : null);
            ApplyPayload(payload, parsed.vigil_weight);
            Raise();
        }

        private void ApplyPayload(VigilCeremonyPayload payload, double vigilWeight)
        {
            if (payload == null || !payload.HasContent)
            {
                int weightTier = VigilCeremonyWords.HighestUnlockedTier(vigilWeight);
                if (weightTier > 0) _ledger?.SetDressing(weightTier);
                return;
            }

            HasPayload = true;
            EpochIndex = payload.EpochIndex;
            DressingTier = payload.Tier > 0
                ? payload.Tier
                : VigilCeremonyWords.HighestUnlockedTier(vigilWeight);
            if (DressingTier <= 0 && !string.IsNullOrEmpty(payload.Word))
                DressingTier = VigilCeremonyWords.TierForWord(payload.Word);

            Word = string.IsNullOrEmpty(payload.Word)
                ? VigilCeremonyWords.WordForTier(DressingTier)
                : payload.Word;
            WordKey = VigilCeremonyWords.WordKeyForTier(DressingTier);
            WordTitleText = string.IsNullOrEmpty(WordKey)
                ? Word
                : new LocalizedText(WordKey).Resolve();
            LineKey = VigilCeremonyWords.LineKeyForTier(DressingTier);
            LineText = string.IsNullOrEmpty(payload.Line)
                ? (string.IsNullOrEmpty(LineKey) ? VigilCeremonyWords.LineForTier(DressingTier)
                    : new LocalizedText(LineKey).Resolve())
                : payload.Line;
            CircleName = payload.CircleName ?? string.Empty;
            PerkTitleText = payload.PerkTitle ?? string.Empty;
            PerkDescriptionText = payload.PerkDescription ?? string.Empty;
            NextEndsAt = payload.NextEndsAt;

            HeldText = LocalText.Format(KeyHeld, CircleName);
            ChosenText = LocalText.Format(KeyChosen, CircleName, PerkTitleText);
            NextText = LocalText.Format(KeyNext, NextEndsAt ?? string.Empty);

            if (payload.Passed && payload.HasContent)
                _ledger?.RememberPayload(payload);

            if (DressingTier > 0)
                _ledger?.SetDressing(DressingTier);

            bool unseen = _ledger == null || !_ledger.HasSeen(EpochIndex);
            ShouldAutoPlay = _inCircle && payload.Passed && EpochIndex > 0 && unseen && !IsReplay;

            int epoch = EpochIndex;
            int tier = DressingTier;
            bool autoPlay = ShouldAutoPlay;
            FlowTrace.Step(Sys, "ceremony applied epoch=" + epoch + " tier=" + tier +
                                " autoPlay=" + autoPlay);
        }

        /// <summary>
        /// Prefer the wire ceremony object; otherwise derive from vigil_weight + result
        /// + winning option + perks. JsonUtility default-constructs a missing ceremony,
        /// so emptiness is an empty word / epoch_index 0.
        /// </summary>
        public static VigilCeremonyPayload BuildPayload(
            CircleWire.BallotResponse parsed, VigilCeremonyPayload previous)
        {
            if (parsed == null) return previous;

            var wire = parsed.ceremony;
            bool wireHasWord = wire != null && !string.IsNullOrEmpty(wire.word);
            bool wireHasEpoch = wire != null && wire.epoch_index > 0;

            string winningTitle = null;
            string winningDesc = null;
            if (parsed.result != null && !string.IsNullOrEmpty(parsed.result.winning_option)
                && parsed.ballot != null && parsed.ballot.options != null)
            {
                foreach (var o in parsed.ballot.options)
                {
                    if (o == null) continue;
                    if (!string.Equals(o.option_id, parsed.result.winning_option, StringComparison.Ordinal))
                        continue;
                    winningTitle = o.title;
                    winningDesc = o.description;
                    break;
                }
            }

            string perkTitle = null;
            string perkDesc = null;
            if (parsed.perks != null)
            {
                foreach (var p in parsed.perks)
                {
                    if (p == null) continue;
                    if (!string.IsNullOrEmpty(p.title)) perkTitle = p.title;
                    if (!string.IsNullOrEmpty(p.description)) perkDesc = p.description;
                    if (perkTitle != null) break;
                }
            }

            int epoch = wireHasEpoch
                ? wire.epoch_index
                : (parsed.epoch != null ? parsed.epoch.index : 0);
            int tier = 0;
            string word = null;
            string line = null;
            if (wireHasWord)
            {
                word = wire.word;
                tier = VigilCeremonyWords.TierForWord(word);
                line = wire.line;
            }
            if (tier <= 0)
                tier = VigilCeremonyWords.HighestUnlockedTier(parsed.vigil_weight);
            if (string.IsNullOrEmpty(word))
                word = VigilCeremonyWords.WordForTier(tier);
            if (string.IsNullOrEmpty(line))
                line = VigilCeremonyWords.LineForTier(tier);

            bool passed = wire != null && (wire.passed || wireHasWord)
                ? wire.passed
                : (parsed.result != null && parsed.result.passed);

            string circleName = wire != null ? wire.circle_name : null;
            if (string.IsNullOrEmpty(circleName) && previous != null)
                circleName = previous.CircleName;

            string nextEnds = wire != null ? wire.next_ends_at : null;
            if (string.IsNullOrEmpty(nextEnds) && parsed.epoch != null)
                nextEnds = parsed.epoch.ends_at;

            var payload = new VigilCeremonyPayload
            {
                EpochIndex = epoch,
                Tier = tier,
                Word = word,
                Line = line,
                CircleName = circleName ?? string.Empty,
                PerkTitle = FirstNonEmpty(wire != null ? wire.perk_title : null, winningTitle, perkTitle,
                    previous != null ? previous.PerkTitle : null),
                PerkDescription = FirstNonEmpty(wire != null ? wire.perk_description : null, winningDesc, perkDesc,
                    previous != null ? previous.PerkDescription : null),
                Passed = passed,
                NextEndsAt = nextEnds ?? (previous != null ? previous.NextEndsAt : null)
            };

            if (!payload.HasContent) return previous;
            if (!payload.Passed && previous != null && previous.Passed) return previous;
            return payload;
        }

        private static string FirstNonEmpty(params string[] parts)
        {
            if (parts == null) return string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i])) return parts[i];
            }
            return string.Empty;
        }
    }
}
