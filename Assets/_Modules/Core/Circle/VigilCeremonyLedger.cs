// =============================================================================
// VigilCeremonyLedger — per-install last-seen epoch + Heart dressing tier (WO-1874).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Circle
//
// Last-seen epoch is PlayerPrefs, NEVER the save schema (no bump). Dressing is
// a third Heart axis: Village reads this ledger; HUD never talks to Village.
// IVigilCeremonyLedger is the EditMode seam so tests do not need PlayerPrefs.
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Core.Circle
{
    /// <summary>
    /// The last ceremony the plate can replay, persisted per install so a later
    /// Ballots visit still has "Watch the last vigil" after a new ballot opens.
    /// No effect field — narrative only.
    /// </summary>
    [Serializable]
    public sealed class VigilCeremonyPayload
    {
        public int EpochIndex;
        public int Tier;
        public string Word;
        public string Line;
        public string CircleName;
        public string PerkTitle;
        public string PerkDescription;
        public bool Passed;
        public string NextEndsAt;

        public bool HasContent =>
            !string.IsNullOrEmpty(Word) || EpochIndex > 0 || Tier > 0;
    }

    /// <summary>Test seam for <see cref="VigilCeremonyLedger"/>. No Unity types on the VM side.</summary>
    public interface IVigilCeremonyLedger
    {
        event Action DressingChanged;
        event Action SequenceRequested;
        event Action SequenceCompleted;

        int CurrentDressingTier { get; }
        int LastSeenEpoch { get; }
        VigilCeremonyPayload LastPayload { get; }

        bool HasSeen(int epochIndex);
        void RememberEpoch(int epochIndex);
        void SetDressing(int tier);
        void RememberPayload(VigilCeremonyPayload payload);
        void RequestSequence();
        void NotifySequenceCompleted();
    }

    /// <summary>
    /// Live PlayerPrefs ledger. Village dressing and the HUD plate both bind this
    /// singleton; EditMode tests substitute <see cref="IVigilCeremonyLedger"/>.
    /// </summary>
    public sealed class VigilCeremonyLedger : IVigilCeremonyLedger
    {
        public const string LastEpochKey = "vigil.ceremony.lastEpoch";
        public const string DressingTierKey = "vigil.ceremony.dressingTier";
        public const string PayloadKey = "vigil.ceremony.lastPayload";

        private const string Sys = "VigilCeremony";

        private static VigilCeremonyLedger _shared;
        public static VigilCeremonyLedger Shared
        {
            get
            {
                if (_shared == null) _shared = new VigilCeremonyLedger();
                return _shared;
            }
        }

        /// <summary>EditMode / a later test host may replace the live singleton.</summary>
        public static void ReplaceShared(VigilCeremonyLedger next) => _shared = next;

        public event Action DressingChanged;
        public event Action SequenceRequested;
        public event Action SequenceCompleted;

        private int _dressingTier;
        private int _lastSeenEpoch;
        private VigilCeremonyPayload _payload;
        private bool _loaded;

        public VigilCeremonyLedger()
        {
            Load();
        }

        public int CurrentDressingTier
        {
            get { Load(); return _dressingTier; }
        }

        public int LastSeenEpoch
        {
            get { Load(); return _lastSeenEpoch; }
        }

        public VigilCeremonyPayload LastPayload
        {
            get { Load(); return _payload; }
        }

        public bool HasSeen(int epochIndex)
        {
            if (epochIndex <= 0) return false;
            Load();
            return _lastSeenEpoch >= epochIndex;
        }

        public void RememberEpoch(int epochIndex)
        {
            if (epochIndex <= 0) return;
            Load();
            if (epochIndex <= _lastSeenEpoch) return;
            _lastSeenEpoch = epochIndex;
            try
            {
                PlayerPrefs.SetInt(LastEpochKey, _lastSeenEpoch);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "RememberEpoch PlayerPrefs threw " + ex.GetType().Name);
            }
            int remembered = _lastSeenEpoch;
            FlowTrace.Step(Sys, "remembered epoch " + remembered);
        }

        public void SetDressing(int tier)
        {
            int clamped = VigilCeremonyWords.ClampTier(tier);
            Load();
            if (clamped == _dressingTier) return;
            _dressingTier = clamped;
            try
            {
                PlayerPrefs.SetInt(DressingTierKey, _dressingTier);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "SetDressing PlayerPrefs threw " + ex.GetType().Name);
            }
            int now = _dressingTier;
            FlowTrace.Step(Sys, "dressing tier now " + now);
            DressingChanged?.Invoke();
        }

        public void RememberPayload(VigilCeremonyPayload payload)
        {
            if (payload == null || !payload.HasContent) return;
            Load();
            _payload = payload;
            try
            {
                PlayerPrefs.SetString(PayloadKey, JsonUtility.ToJson(payload));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "RememberPayload PlayerPrefs threw " + ex.GetType().Name);
            }
        }

        public void RequestSequence()
        {
            Load();
            int tier = _dressingTier;
            FlowTrace.Step(Sys, "sequence requested at dressing tier " + tier);
            SequenceRequested?.Invoke();
        }

        public void NotifySequenceCompleted()
        {
            FlowTrace.Step(Sys, "sequence completed");
            SequenceCompleted?.Invoke();
        }

        private void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                _lastSeenEpoch = PlayerPrefs.GetInt(LastEpochKey, 0);
                _dressingTier = VigilCeremonyWords.ClampTier(PlayerPrefs.GetInt(DressingTierKey, 0));
                string raw = PlayerPrefs.GetString(PayloadKey, string.Empty);
                if (!string.IsNullOrEmpty(raw))
                    _payload = JsonUtility.FromJson<VigilCeremonyPayload>(raw);
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "ledger load threw " + ex.GetType().Name + " — fail-closed at epoch 0 / tier 0.");
                _lastSeenEpoch = 0;
                _dressingTier = 0;
                _payload = null;
            }
        }
    }
}
