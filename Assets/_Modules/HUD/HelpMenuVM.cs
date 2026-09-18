// =============================================================================
// HelpMenuVM - the PURE ViewModel behind the Help/Settings modal (WO-882).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// THE LAW (owner ruling, WO-882): "the VM filters out the unavailable entry so
// the View never builds a blank button." The menu ENTRY LIST is state, and state
// belongs here - not in the View. HelpMenu.cs is now layout/render only: it walks
// Entries and stamps one kit row per entry. It does NOT decide which rows exist,
// and it must never skip/guard an entry itself (skipping in the View leaves the
// unavailable entry in the model for the next consumer to trip over).
//
// AN ENTRY IS OFFERED ONLY IF ALL THREE HOLD:
//   1. AVAILABLE       - a dev-only entry is dropped outside a dev context
//                        (IsDevContext = editor or Development Build), and a
//                        gated entry is dropped until its gate opens (the 5-tap
//                        dev unlock).
//   2. RENDERABLE      - non-empty id, non-null command, and a label that is
//                        printable ASCII (a null/blank label renders as a
//                        LABEL-LESS BUTTON, and non-ASCII renders as tofu on the
//                        device; both read as "broken" - WO-882's exact symptom).
//   3. COMMANDED       - every command is a bound method group, never null, so a
//                        tap can never land on a dead row.
// A candidate that fails any of the three is NOT emitted. There is no fourth
// "the View will hide it" state.
//
// PURITY: this file references NO UnityEngine type at all (not even Application /
// PlayerPrefs) - the dev context and the persisted unlock arrive through IHost.
// That keeps the VM unit-testable with a hand-rolled fake and lets the headless
// oracle (HelpMenuEntryRegression) construct it with an injected context.
//
// SECURITY (store-hardening Path A, S1 - preserved from the View): the dev-tools
// launcher and the dev resource grant are compile-STRIPPED from release builds,
// so a public/store APK carries no code path to them at all. The IsDevContext
// filter is the SECOND line of defence (and the one the oracle can test).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.UI;          // WO-1857: LocalizedText — the title + the offered row faces
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.HUD
{
    /// <summary>
    /// Pure ViewModel for the Help/Settings modal. Owns the menu entry list, the
    /// availability rules and the hidden 5-tap dev unlock. View-agnostic: no
    /// UnityEngine types, so it is testable without a scene (ARCH sec 2/2c).
    /// </summary>
    public sealed class HelpMenuVM : IPanelViewModel, IDisposable
    {
        // -- Seam: everything the VM needs from the Unity side, so tests + the
        //    headless oracle inject a fake and the real path binds the View. -----
        public interface IHost
        {
            /// <summary>True in the editor or a Development Build (dev-only entries may appear).</summary>
            bool IsDevContext { get; }

            /// <summary>The persisted 5-tap dev unlock (PlayerPrefs on the real host).</summary>
            bool DevUnlockPersisted { get; set; }

            void ReportBug();
            void ShowControls();
            void ShowCredits();
            void ResetProgress();
            void OpenDevTools();
            void GrantResources();
            void CloseMenu();
        }

        /// <summary>One menu row the View may render. Emitted ONLY when renderable.</summary>
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly string Label;
            public readonly bool Danger;
            public readonly Action Command;

            public Entry(string id, string label, bool danger, Action command)
            {
                Id = id;
                Label = label;
                Danger = danger;
                Command = command;
            }

            /// <summary>
            /// Can the View actually render this row? A blank/whitespace label draws a
            /// LABEL-LESS button; a non-ASCII label draws tofu; a null command draws a
            /// dead tappable box. The VM never emits an entry that fails this.
            /// </summary>
            public bool IsRenderable
            {
                get
                {
                    if (string.IsNullOrEmpty(Id)) return false;
                    if (Command == null) return false;
                    return IsPrintableAscii(Label);
                }
            }
        }

        /// <summary>Taps on the card title needed to reveal the dev grant row.</summary>
        public const int DevUnlockTapCount = 5;

        /// <summary>Rolling window (seconds) the taps must land inside.</summary>
        public const float DevUnlockTapWindowSeconds = 3f;

        // -- A menu row BEFORE the availability filter runs. --------------------
        private readonly struct Candidate
        {
            public readonly string Id;
            public readonly string Label;
            public readonly bool Danger;
            public readonly bool DevOnly;
            public readonly bool Available;
            public readonly Action Command;

            public Candidate(string id, string label, bool danger, bool devOnly, bool available, Action command)
            {
                Id = id;
                Label = label;
                Danger = danger;
                DevOnly = devOnly;
                Available = available;
                Command = command;
            }
        }

        private readonly IHost _host;
        private readonly bool _devContext;
        private readonly List<Entry> _entries = new List<Entry>();

        private bool _devUnlocked;
        private int _titleTaps;
        private float _lastTitleTapTime;
        private bool _disposed;

        /// <summary>Real path: read the dev context + persisted unlock off the live host.</summary>
        public static HelpMenuVM CreateDefault(IHost host)
        {
            bool dev = host != null && host.IsDevContext;
            bool unlocked = host != null && host.DevUnlockPersisted;
            return new HelpMenuVM(host, dev, unlocked);
        }

        /// <summary>Test/oracle ctor: the dev context is injected so a RELEASE build can be
        /// simulated from the editor (where UNITY_EDITOR keeps the dev rows compiled in).</summary>
        public HelpMenuVM(IHost host, bool devContext, bool devUnlocked)
        {
            _host = host;
            _devContext = devContext;
            _devUnlocked = devUnlocked && devContext;
            Rebuild();
        }

        // -- IPanelViewModel ----------------------------------------------------
        public event Action Changed;

        public string Title { get { return new LocalizedText("settings.help.help").Resolve(); } }

        public void Close()
        {
            if (_host != null) _host.CloseMenu();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
        }

        // -- Read-only data the View renders ------------------------------------

        /// <summary>The rows the View may stamp, in order. Every element is renderable. Never null.</summary>
        public IReadOnlyList<Entry> Entries { get { return _entries; } }

        /// <summary>True when dev-only entries are permitted (editor / Development Build).</summary>
        public bool IsDevContext { get { return _devContext; } }

        /// <summary>True once the hidden 5-tap unlock has opened the dev grant row.</summary>
        public bool DevUnlocked { get { return _devUnlocked; } }

        // -- Commands -----------------------------------------------------------

        /// <summary>
        /// Hidden dev unlock (owner 2026-07-12): five taps on the card title inside a
        /// rolling window flips the persisted unlock and the grant row becomes AVAILABLE
        /// (so the VM starts offering it). No-op outside a dev context.
        /// </summary>
        public void TapTitle(float nowSeconds)
        {
            if (!_devContext || _devUnlocked) return;
            if (nowSeconds - _lastTitleTapTime > DevUnlockTapWindowSeconds) _titleTaps = 0;
            _lastTitleTapTime = nowSeconds;
            _titleTaps++;
            if (_titleTaps < DevUnlockTapCount) return;

            _devUnlocked = true;
            if (_host != null) _host.DevUnlockPersisted = true;
            Rebuild();
        }

        // -- The one place the entry list is decided ----------------------------

        private void Rebuild()
        {
            _entries.Clear();

            var candidates = new List<Candidate>(6);
            // WO-1857: the row FACES are player copy and resolve from the table; the row IDs
            // ("report_bug", ...) are identity and stay English data. The DevOnly rows below
            // keep their literals on purpose - they are compile-stripped from release, and
            // CopyHygieneRegression.CaseDevToolsReleaseGuard pins that literal by its exact quoted
            // text below (never re-quote it here - a comment quoting it ahead of the #if guard
            // trips the same needle it is meant to protect).
            candidates.Add(new Candidate("report_bug",
                new LocalizedText("hud.help_menu.report_bug").Resolve(), false, false, true, HostReportBug));
            candidates.Add(new Candidate("controls",
                new LocalizedText("hud.help_menu.controls_row").Resolve(), false, false, true, HostShowControls));
            candidates.Add(new Candidate("reset_progress",
                new LocalizedText("hud.help_menu.reset_row").Resolve(), true, false, true, HostResetProgress));
            candidates.Add(new Candidate("credits",
                new LocalizedText("hud.help_menu.credits_row").Resolve(), false, false, true, HostShowCredits));
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
            // SECURITY (LB-11 / store-hardening S1): both rows are compile-STRIPPED from
            // release, AND flagged DevOnly so the IsDevContext filter drops them even if
            // someone ever un-strips them. They sort LAST so the release list is a stable
            // prefix of the dev list (the row order a player sees never shifts).
            candidates.Add(new Candidate("dev_tools", "Dev Tools", false, true, true, HostOpenDevTools));
            candidates.Add(new Candidate("dev_grant", "Grant Resources (dev)", false, true, _devUnlocked, HostGrantResources));
#endif

            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                if (c.DevOnly && !_devContext) continue;   // unavailable: dev-only in a release build
                if (!c.Available) continue;                // unavailable: its gate has not opened

                var entry = new Entry(c.Id, c.Label, c.Danger, c.Command);
                if (!entry.IsRenderable) continue;         // the View could not draw it - never offer it

                _entries.Add(entry);
            }

            Raise();
        }

        // -- Command wrappers: method groups, so Entry.Command is NEVER null ----
        private void HostReportBug() { if (_host != null) _host.ReportBug(); }
        private void HostShowControls() { if (_host != null) _host.ShowControls(); }
        private void HostShowCredits() { if (_host != null) _host.ShowCredits(); }
        private void HostResetProgress() { if (_host != null) _host.ResetProgress(); }
        private void HostOpenDevTools() { if (_host != null) _host.OpenDevTools(); }
        private void HostGrantResources() { if (_host != null) _host.GrantResources(); }

        /// <summary>Printable-ASCII test: at least one visible glyph, every char in 0x20..0x7E.</summary>
        private static bool IsPrintableAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            bool hasVisible = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch < ' ' || ch > '~') return false;
                if (ch != ' ') hasVisible = true;
            }
            return hasVisible;
        }

        private void Raise()
        {
            if (_disposed) return;
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
