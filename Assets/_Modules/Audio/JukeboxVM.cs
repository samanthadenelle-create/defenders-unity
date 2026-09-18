// =============================================================================
// JukeboxVM — the PURE ViewModel behind MusicSelectionPanel (strict-MVVM Silo E).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Audio   Namespace: DeNelle.Audio
//
// The ambient-track SELECTION MODEL that used to live in the MusicSelectionPanel
// VIEW now lives here:
//   * implements DeNelle.Core.UI.Mvvm.IPanelViewModel (Title / Changed / Close / Dispose).
//   * NO UnityEngine UI types — unit-testable without a scene.
//   * projects AudioService.AmbientChoicesFor(context) into UI-free Track rows,
//     computing isSelected = (track == chosen) with the "chosen == None -> context
//     default" fallback HERE (not in the View).
//   * SetAmbientChoice(track) is a command; the View only routes taps + renders.
// BattleLock stays respected by the View's PanelManager.NotifyOpened gate (open
// is rejected in battle) + its input gate — the VM only models the selection.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Audio
{
    /// <summary>
    /// Pure ViewModel for the ambient-music jukebox. Lists the curated tracks for the
    /// player's current context with the selected one flagged, and persists a new pick
    /// via a command. Raises <see cref="Changed"/> after a pick.
    /// </summary>
    public sealed class JukeboxVM : IPanelViewModel, IDisposable
    {
        // ── Seam over AudioService (fake in tests; singleton live). ──
        public interface ISource
        {
            bool Ready { get; }                                          // AudioService.Instance != null
            AudioService.AmbientContext CurrentContext { get; }         // AudioService.CurrentAmbientContext
            MusicTrack GetAmbientChoice(AudioService.AmbientContext c);  // AudioService.GetAmbientChoice(c)
            void SetAmbientChoice(AudioService.AmbientContext c, MusicTrack t); // AudioService.SetAmbientChoice(c,t)
            IReadOnlyList<MusicChoice> ChoicesFor(AudioService.AmbientContext c);  // AudioService.AmbientChoicesFor(c)
            MusicTrack DefaultTrackFor(AudioService.AmbientContext c);   // AudioService.DefaultTrackFor(c)
        }

        /// <summary>One projected jukebox row (track + display name + selected flag). UI-free.</summary>
        public readonly struct TrackRow
        {
            public readonly MusicTrack Track;
            public readonly string DisplayName;
            public readonly bool IsSelected;
            public TrackRow(MusicTrack track, string displayName, bool isSelected)
            { Track = track; DisplayName = displayName; IsSelected = isSelected; }
        }

        private readonly ISource _source;
        private readonly Action _onClose;
        private bool _disposed;

        private readonly List<TrackRow> _tracks = new List<TrackRow>();

        public static JukeboxVM CreateDefault(Action onClose)
            => new JukeboxVM(new ServiceSource(), onClose);

        public JukeboxVM(ISource source, Action onClose)
        {
            _source = source;
            _onClose = onClose;
            Rebuild();
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────
        public event Action Changed;
        // WO-1857: reuses the shared common.jukebox row - COMMON_KEYS_REGISTRY names this exact
        // call site alongside MusicSelectionPanel's own modal title (same feature name, same job).
        public string Title => new LocalizedText("common.jukebox").Resolve();
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>True when the audio service is up (View shows "Audio not ready." otherwise).</summary>
        public bool AudioReady { get; private set; }

        /// <summary>Curated track rows for the current context, selected one flagged. Never null.</summary>
        public IReadOnlyList<TrackRow> Tracks => _tracks;

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Persist + preview the picked track (service plays it live in-context), then refresh.</summary>
        public void SetAmbientChoice(MusicTrack track)
        {
            if (_source == null || !_source.Ready) return;
            _source.SetAmbientChoice(_source.CurrentContext, track);
            Rebuild();   // refresh the selected flag
        }

        // ── Projection (moved verbatim from the View) ───────────────────────────

        /// <summary>Rebuild the track rows for the CURRENT ambient context + selection.
        /// Public so the View can refresh on open (context may have changed).</summary>
        public void Rebuild()
        {
            _tracks.Clear();
            AudioReady = _source != null && _source.Ready;
            if (!AudioReady) { Raise(); return; }

            AudioService.AmbientContext context = _source.CurrentContext;
            MusicTrack chosen = _source.GetAmbientChoice(context);
            // None => the player hasn't picked; the context default is effectively selected.
            if (chosen == MusicTrack.None)
                chosen = _source.DefaultTrackFor(context);

            var choices = _source.ChoicesFor(context);
            if (choices != null)
            {
                foreach (var choice in choices)
                {
                    if (choice == null) continue;
                    _tracks.Add(new TrackRow(choice.Track, choice.DisplayName, choice.Track == chosen));
                }
            }
            Raise();
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }

        // ── Real seam: wraps AudioService (SOLE live resolution site). ──
        private sealed class ServiceSource : ISource
        {
            public bool Ready => AudioService.Instance != null;

            public AudioService.AmbientContext CurrentContext
                => AudioService.Instance != null
                    ? AudioService.Instance.CurrentAmbientContext
                    : AudioService.AmbientContext.Village;

            public MusicTrack GetAmbientChoice(AudioService.AmbientContext c)
                => AudioService.Instance != null ? AudioService.Instance.GetAmbientChoice(c) : MusicTrack.None;

            public void SetAmbientChoice(AudioService.AmbientContext c, MusicTrack t)
                => AudioService.Instance?.SetAmbientChoice(c, t);

            public IReadOnlyList<MusicChoice> ChoicesFor(AudioService.AmbientContext c)
                => AudioService.AmbientChoicesFor(c);

            public MusicTrack DefaultTrackFor(AudioService.AmbientContext c)
                => AudioService.DefaultTrackFor(c);
        }
    }
}
