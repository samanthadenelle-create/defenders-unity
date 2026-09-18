// =============================================================================
// QuestTrackerVM — the PURE ViewModel behind QuestTrackerHud (strict-MVVM Silo E).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// The tracked-quest RESOLUTION that used to live in the QuestTrackerHud VIEW now
// lives here (WO-454 type-aware fallback: prefer a main/story quest, else the
// first active). The View becomes a dumb icon that reads vm.* only — it no longer
// touches QuestService or QuestCatalog.
//   * implements IPanelViewModel (Title / Changed / Close / Dispose).
//   * NO UnityEngine types — unit-testable without a scene.
//   * exposes HasTrackedQuest (icon visibility), ResolvedTrackedId, ObjectiveText,
//     and UpdateSnapshot (the "tracked|objective" string the View diff-checks to
//     light its update dot). SetTracked(id) is a command.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.UI;          // WO-1857: LocalizedText — the panel title is player copy
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.HUD
{
    /// <summary>
    /// Pure ViewModel for the minimized quest-tracker HUD icon. Resolves the tracked
    /// quest (player pin, else the WO-454 type-aware fallback) and projects the icon's
    /// visibility + update-cue snapshot. Raises <see cref="Changed"/> on any quest change.
    /// </summary>
    public sealed class QuestTrackerVM : IPanelViewModel, IDisposable
    {
        // ── Seam: everything the VM reads about quests (fake in tests; singleton live). ──
        public interface ISource
        {
            event Action Changed;                       // QuestService.QuestChanged
            IReadOnlyList<string> ActiveQuestIds();     // QuestService.ActiveQuestIds()
            string TrackedId { get; }                   // QuestService.TrackedId
            bool IsActive(string id);                   // QuestService.IsActive(id)
            void SetTracked(string id);                 // QuestService.SetTracked(id)
            string ObjectiveTextFor(string id);         // QuestService.GetStage(id)?.ObjectiveText ?? ""
            string QuestTypeOf(string id);              // QuestCatalog.FindQuest(id)?.Type
        }

        private readonly ISource _source;
        private readonly Action _onClose;
        private readonly Action _changedHandler;
        private bool _disposed;

        public static QuestTrackerVM CreateDefault(Action onClose)
            => new QuestTrackerVM(new ServiceSource(), onClose);

        public QuestTrackerVM(ISource source, Action onClose)
        {
            _source = source;
            _onClose = onClose;
            if (_source != null)
            {
                _changedHandler = Rebuild;
                _source.Changed += _changedHandler;
            }
            Rebuild();
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────
        public event Action Changed;
        public string Title => new LocalizedText("hud.quest_tracker.title").Resolve();
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_source != null && _changedHandler != null) _source.Changed -= _changedHandler;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>True when a trackable quest resolved (the icon is shown; otherwise hidden).</summary>
        public bool HasTrackedQuest { get; private set; }

        /// <summary>The resolved tracked quest id (player pin or WO-454 fallback), or null.</summary>
        public string ResolvedTrackedId { get; private set; }

        /// <summary>The tracked quest's current objective line (empty when none).</summary>
        public string ObjectiveText { get; private set; }

        /// <summary>"trackedId|objective" — the View diff-checks this to light its update dot.</summary>
        public string UpdateSnapshot { get; private set; }

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Pin a quest as the tracked HUD quest (empty clears it). Delegates to the service.</summary>
        public void SetTracked(string id)
        {
            _source?.SetTracked(id);
            // The service raises Changed -> Rebuild; call Rebuild directly too for the fake seam case.
            Rebuild();
        }

        // ── Resolution (moved verbatim from the View) ───────────────────────────

        private void Rebuild()
        {
            HasTrackedQuest = false;
            ResolvedTrackedId = null;
            ObjectiveText = "";
            UpdateSnapshot = "";

            if (_source == null) { Raise(); return; }

            var ids = _source.ActiveQuestIds();
            if (ids == null || ids.Count == 0) { Raise(); return; }

            // Player-tracked quest; fall back to an active quest until one is chosen.
            string tracked = _source.TrackedId;
            if (string.IsNullOrEmpty(tracked) || !_source.IsActive(tracked))
            {
                // WO-454 type-aware fallback — prefer a main/story quest over the rest,
                // otherwise the first active. Empty Type normalizes to "story", so a
                // catalog with no type data keeps the old "first active" behavior.
                tracked = null;
                string firstActive = null;
                foreach (var id in ids)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (firstActive == null) firstActive = id;
                    string raw = _source.QuestTypeOf(id);
                    string ty = !string.IsNullOrEmpty(raw) ? raw.Trim().ToLowerInvariant() : "story";
                    if (ty == "main" || ty == "story") { tracked = id; break; }
                }
                if (tracked == null) tracked = firstActive;
            }
            if (tracked == null) { Raise(); return; }

            HasTrackedQuest = true;
            ResolvedTrackedId = tracked;
            string objective = _source.ObjectiveTextFor(tracked) ?? "";
            ObjectiveText = objective;
            UpdateSnapshot = tracked + "|" + objective;
            Raise();
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }

        // ── Real seam: wraps QuestService + QuestCatalog (SOLE live resolution site). ──
        private sealed class ServiceSource : ISource
        {
            public event Action Changed
            {
                add    { if (DeNelle.Core.Quests.QuestService.Instance != null) DeNelle.Core.Quests.QuestService.Instance.QuestChanged += value; }
                remove { if (DeNelle.Core.Quests.QuestService.Instance != null) DeNelle.Core.Quests.QuestService.Instance.QuestChanged -= value; }
            }

            public IReadOnlyList<string> ActiveQuestIds()
                => DeNelle.Core.Quests.QuestService.Instance?.ActiveQuestIds() ?? System.Array.Empty<string>();

            public string TrackedId => DeNelle.Core.Quests.QuestService.Instance?.TrackedId;

            public bool IsActive(string id)
                => DeNelle.Core.Quests.QuestService.Instance != null
                   && DeNelle.Core.Quests.QuestService.Instance.IsActive(id);

            public void SetTracked(string id) => DeNelle.Core.Quests.QuestService.Instance?.SetTracked(id);

            public string ObjectiveTextFor(string id)
            {
                var stage = DeNelle.Core.Quests.QuestService.Instance?.GetStage(id);
                return stage != null && !string.IsNullOrEmpty(stage.ObjectiveText) ? stage.ObjectiveText : "";
            }

            public string QuestTypeOf(string id)
            {
                var d = DeNelle.Core.Quests.QuestCatalog.FindQuest(id);
                return d != null ? d.Type : null;
            }
        }
    }
}
