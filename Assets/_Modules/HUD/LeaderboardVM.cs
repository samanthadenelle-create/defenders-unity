// =============================================================================
// LeaderboardVM — the PURE ViewModel behind LeaderboardPanel (strict-MVVM Silo E).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ALL leaderboard state + the async fetch that used to live in the Leaderboard-
// Panel VIEW now lives here:
//   * implements IPanelViewModel (Title / Changed / Close / Dispose).
//   * NO UnityEngine UI types — unit-testable without a scene.
//   * OWNS the async FetchTopAsync: SelectMetric / Refresh drive the fetch, the
//     result is projected into UI-free Row structs and Changed fires.
//   * projects GetLocalProfile + IsLocalStub/SourceLabel into ready-to-render
//     strings so the View reads vm.* only and never names LeaderboardService.
// =============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Services;
using DeNelle.Core.Social;
using DeNelle.Core.UI;          // WO-1857: LocalizedText / LocalText — title + footer copy
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.HUD
{
    /// <summary>
    /// Pure ViewModel for the leaderboard modal. Holds the active metric, the local
    /// profile lines, the fetched ranked rows (projected), and the honest source
    /// footer. Raises <see cref="Changed"/> after a fetch completes / the source swaps.
    /// </summary>
    public sealed class LeaderboardVM : IPanelViewModel, IDisposable
    {
        // ── Seam over LeaderboardService (fake in tests; singleton live). ──
        public interface ISource
        {
            event Action Changed;                                          // LeaderboardService.Changed
            PlayerProfile GetLocalProfile();                               // LeaderboardService.GetLocalProfile()
            void FetchTopAsync(LeaderboardMetric metric, int limit,
                               Action<IReadOnlyList<LeaderboardEntry>> onResult);  // FetchTopAsync
            bool IsLocalStub { get; }                                      // LeaderboardService.IsLocalStub
            string SourceLabel { get; }                                    // LeaderboardService.SourceLabel
        }

        /// <summary>Read-only public-town directory seam, separate from score authority.</summary>
        public interface IShowcaseSource
        {
            void FetchTopTen(Action<IReadOnlyList<TopTownVisitEntry>> onResult);
        }

        /// <summary>One projected leaderboard row (all strings ready to render, no LeaderboardEntry leak).</summary>
        public readonly struct Row
        {
            public readonly string Rank;
            public readonly string Name;
            public readonly string Score;
            public readonly bool IsLocal;
            public readonly int Index;     // zebra striping
            public readonly string ShowcaseId;
            public bool CanVisit => TownShowcaseIds.IsShowcaseId(ShowcaseId);
            public Row(string rank, string name, string score, bool isLocal, int index, string showcaseId = null)
            { Rank = rank; Name = name; Score = score; IsLocal = isLocal; Index = index; ShowcaseId = showcaseId; }
        }

        private const int FetchLimit = 20;

        private readonly ISource _source;
        private readonly IShowcaseSource _showcaseSource;
        private readonly Action _onClose;
        private readonly Action _changedHandler;
        private bool _disposed;

        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<int, TopTownVisitEntry> _visitsByRank =
            new Dictionary<int, TopTownVisitEntry>();
        private int _refreshGeneration;

        public static LeaderboardVM CreateDefault(Action onClose)
            => new LeaderboardVM(new ServiceSource(), onClose, new ShowcaseSource());

        public LeaderboardVM(ISource source, Action onClose, IShowcaseSource showcaseSource = null)
        {
            _source = source;
            _showcaseSource = showcaseSource;
            _onClose = onClose;
            if (_source != null)
            {
                _changedHandler = OnSourceChanged;
                _source.Changed += _changedHandler;
            }
            Refresh();
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────
        public event Action Changed;
        public string Title => new LocalizedText("common.leaderboard").Resolve();
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_source != null && _changedHandler != null) _source.Changed -= _changedHandler;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>The active ranked metric (Best Wave / Crystals / Arena Wins).</summary>
        public LeaderboardMetric Metric { get; private set; } = LeaderboardMetric.BestWave;

        /// <summary>Profile header line ("You - Ranger   #CODE").</summary>
        public string ProfileHeroLine { get; private set; } = "";

        /// <summary>Profile stats line ("Best Wave 12    Crystals 340    Magic 8    Arena 3-1").</summary>
        public string ProfileStatsLine { get; private set; } = "";

        /// <summary>Honest source footer (names the offline stub when applicable).</summary>
        public string FooterText { get; private set; } = "";

        /// <summary>Projected ranked rows (a single "No entries yet." row when empty). Never null.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>Compatible, explicitly-published Top-10 towns for next/previous navigation.</summary>
        public IReadOnlyList<TopTownVisitEntry> VisitEntries { get; private set; } =
            Array.Empty<TopTownVisitEntry>();

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Switch the ranked metric + re-fetch. No-op if already active.</summary>
        public void SelectMetric(LeaderboardMetric metric)
        {
            Metric = metric;
            Refresh();
        }

        /// <summary>Re-pull profile + footer + the ranked rows for the active metric.</summary>
        public void Refresh()
        {
            int generation = ++_refreshGeneration;
            if (_source == null) { _rows.Clear(); Raise(); return; }

            RebuildProfile(_source.GetLocalProfile());

            // WO-1857: ONE complete sentence per branch (key-naming.md), never the old
            // "Source: " + label + "." concatenation - the label's position moves per locale.
            // Each key is its own visible literal argument - a key hidden inside a ternary passed
            // AS the key argument is invisible to a regex key extractor.
            FooterText = _source.IsLocalStub
                ? LocalText.Format("hud.leaderboard.source_footer_local", _source.SourceLabel)
                : LocalText.Format("hud.leaderboard.source_footer", _source.SourceLabel);

            // Clear the previous board's visit join BEFORE either async source can complete.
            // In particular, a synchronous score stub must never briefly inherit Best-Wave
            // showcase ids after the user switches to Crystals/Arena.
            _visitsByRank.Clear();
            VisitEntries = Array.Empty<TopTownVisitEntry>();

            // Owns the async fetch: the stub completes synchronously, a live source later.
            _source.FetchTopAsync(Metric, FetchLimit, rows =>
            {
                if (generation != _refreshGeneration) return;
                RebuildRows(rows);
            });
            // Public town visits are deliberately attached only to the all-time Best Wave Top 10.
            // Offline placeholder rivals and non-wave boards must never look publishable.
            if (Metric == LeaderboardMetric.BestWave && !_source.IsLocalStub && _showcaseSource != null)
            {
                _showcaseSource.FetchTopTen(entries =>
                {
                    if (generation != _refreshGeneration || Metric != LeaderboardMetric.BestWave) return;
                    var safe = new List<TopTownVisitEntry>();
                    if (entries != null)
                    {
                        for (int i = 0; i < entries.Count && safe.Count < 10; i++)
                        {
                            var entry = entries[i];
                            if (entry == null || entry.Rank < 1 || entry.Rank > 10) continue;
                            safe.Add(entry);
                            if (entry.CanVisit) _visitsByRank[entry.Rank] = entry;
                        }
                    }
                    VisitEntries = safe;
                    ApplyVisitAffordances();
                    Raise();
                });
            }
        }

        // ── Projection (moved verbatim from the View) ───────────────────────────

        private void OnSourceChanged() => Refresh();

        private void RebuildProfile(PlayerProfile p)
        {
            if (p == null) { ProfileHeroLine = ""; ProfileStatsLine = ""; return; }

            var heroLine = string.IsNullOrEmpty(p.HeroClass) || p.HeroClass == "None"
                ? p.DisplayName
                : p.DisplayName + " - " + p.HeroClass;
            string code = string.IsNullOrEmpty(p.InviteCode) ? "" : "   #" + p.InviteCode;
            ProfileHeroLine = heroLine + code;
            ProfileStatsLine = "Best Wave " + p.BestWave + "    Crystals " + p.Crystals
                             + "    Magic " + p.Magic + "    Arena " + p.ArenaWins + "-" + p.ArenaLosses;
        }

        private void RebuildRows(IReadOnlyList<LeaderboardEntry> rows)
        {
            _rows.Clear();
            if (rows == null || rows.Count == 0)
            {
                _rows.Add(new Row("-", "No entries yet.", "", false, 0));
                Raise();
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i];
                string showcaseId = _visitsByRank.TryGetValue(e.Rank, out var visit) ? visit.ShowcaseId : null;
                _rows.Add(new Row(e.Rank.ToString(), e.Name ?? "?", e.Score.ToString(), e.IsLocalPlayer, i, showcaseId));
            }
            Raise();
        }

        private void ApplyVisitAffordances()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (!int.TryParse(row.Rank, out int rank)) continue;
                string showcaseId = _visitsByRank.TryGetValue(rank, out var visit) ? visit.ShowcaseId : null;
                _rows[i] = new Row(row.Rank, row.Name, row.Score, row.IsLocal, row.Index, showcaseId);
            }
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }

        // ── Real seam: wraps LeaderboardService (SOLE live resolution site). ──
        private sealed class ServiceSource : ISource
        {
            public event Action Changed
            {
                add    { if (LeaderboardService.Instance != null) LeaderboardService.Instance.Changed += value; }
                remove { if (LeaderboardService.Instance != null) LeaderboardService.Instance.Changed -= value; }
            }

            public PlayerProfile GetLocalProfile() => LeaderboardService.Instance?.GetLocalProfile();

            public void FetchTopAsync(LeaderboardMetric metric, int limit,
                                      Action<IReadOnlyList<LeaderboardEntry>> onResult)
            {
                if (LeaderboardService.Instance != null)
                    LeaderboardService.Instance.FetchTopAsync(metric, limit, onResult);
                else
                    onResult?.Invoke(System.Array.Empty<LeaderboardEntry>());
            }

            public bool IsLocalStub => LeaderboardService.Instance == null || LeaderboardService.Instance.IsLocalStub;
            public string SourceLabel => LeaderboardService.Instance != null ? LeaderboardService.Instance.SourceLabel : "Local (offline)";
        }

        private sealed class ShowcaseSource : IShowcaseSource
        {
            private readonly TownShowcaseClient _client = new TownShowcaseClient();
            public void FetchTopTen(Action<IReadOnlyList<TopTownVisitEntry>> onResult) => Fetch(onResult).Forget();

            private async Cysharp.Threading.Tasks.UniTaskVoid Fetch(Action<IReadOnlyList<TopTownVisitEntry>> onResult)
            {
                var rows = await _client.FetchTopTenAsync();
                onResult?.Invoke(rows ?? Array.Empty<TopTownVisitEntry>());
            }
        }
    }
}
