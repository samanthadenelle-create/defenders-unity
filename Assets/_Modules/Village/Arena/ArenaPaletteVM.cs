// =============================================================================
// ArenaPaletteVM — the ONE shared ViewModel behind BOTH Arena palettes
// (ArenaAttackPaletteUI + ArenaDefensePaletteUI). Strict-MVVM migration Silo D.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Arena
//
// Both palettes render the SAME ArenaDefenseCatalog defs against the SAME 50-point
// pool as tappable cards (name + point cost, greyed when unaffordable). They differ
// only in the interaction MODE:
//   * Defense — one card is ARMED (highlight); a re-tap of the armed card stays
//     legal even if the pool is spent.
//   * Attack  — each tap ADDS a troop to the squad (additive); no armed highlight.
// This VM exposes that as a <see cref="ArenaPaletteMode"/> flag; the card
// projection (affordability + armed) differs per mode, so ONE VM serves both.
//
// The controllers own the live spend (they push spent/remaining/squadCount each
// render) so the VM never re-derives the budget — it is the single source of truth.
// PURE C#: no UnityEngine UI types; unit-testable with a fake def list (§2c).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village.Arena
{
    /// <summary>Which palette interaction this VM backs (see file header).</summary>
    public enum ArenaPaletteMode { Attack, Defense }

    /// <summary>
    /// Shared ViewModel for the Arena Attack / Defense palettes. Projects
    /// <see cref="ArenaDefenseCatalog"/> defs as <see cref="ItemVM"/> cards against a
    /// pushed-in point budget, honouring the mode-specific affordability + armed rules.
    /// </summary>
    public sealed class ArenaPaletteVM : IPanelViewModel, IDisposable
    {
        /// <summary>Icon role key on each defender card (the View maps it to art; no game state).</summary>
        public const string IconRoleDefender = "arena-defender";

        private readonly IReadOnlyList<ArenaDefenseDef> _defs;
        private readonly int _pool;
        private readonly Action _onClose;
        private readonly List<ItemVM> _cards = new List<ItemVM>();
        private readonly Dictionary<string, ArenaDefenseDef> _byId =
            new Dictionary<string, ArenaDefenseDef>();
        private bool _disposed;

        public ArenaPaletteMode Mode { get; }

        /// <summary>The point pool (DefensePointPool = 50). Sum of placed costs stays &lt;= this.</summary>
        public int Pool => _pool;

        /// <summary>Points spent so far (pushed by the controller).</summary>
        public int Spent { get; private set; }

        /// <summary>Points remaining (pushed by the controller).</summary>
        public int Remaining { get; private set; }

        /// <summary>Squad size (Attack mode only; pushed by the controller).</summary>
        public int SquadCount { get; private set; }

        /// <summary>The currently-armed defender id (Defense mode only), or null.</summary>
        public string ArmedId { get; private set; }

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        public string Title => Mode == ArenaPaletteMode.Attack ? LocalText.Get("arena.palette.attack_title") : LocalText.Get("arena.palette.defense_title");

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>One card per catalog defender (name + point cost + affordability +
        /// armed flag via <see cref="ItemVM.Equipped"/>). Never null.</summary>
        public IReadOnlyList<ItemVM> Cards => _cards;

        /// <summary>The top readout line ("Squad Points: N / 50" or "Defense Points: R / 50").</summary>
        public string PointsLabel
        {
            get
            {
                if (Mode == ArenaPaletteMode.Attack)
                    return "Squad Points: " + Spent + " / " + _pool +
                           (SquadCount > 0 ? "   (" + SquadCount + " units)" : "");
                return "Defense Points: " + Remaining + " / " + _pool;
            }
        }

        /// <summary>The catalog def for a card id (View routes its OnRecruit/OnDefSelected
        /// event through this so it never re-pulls the gameplay catalog).</summary>
        public ArenaDefenseDef DefFor(string id) =>
            id != null && _byId.TryGetValue(id, out var d) ? d : null;

        // ── Construction / resolution ───────────────────────────────────────────

        /// <summary>The ONLY resolution site: pulls the catalog + pool from
        /// <see cref="ArenaDefenseCatalog"/> so neither palette View touches it.</summary>
        public static ArenaPaletteVM CreateDefault(ArenaPaletteMode mode, Action onClose = null) =>
            new ArenaPaletteVM(mode, ArenaDefenseCatalog.All, ArenaDefenseCatalog.DefensePointPool, onClose);

        public ArenaPaletteVM(ArenaPaletteMode mode, IReadOnlyList<ArenaDefenseDef> defs, int pool, Action onClose)
        {
            Mode = mode;
            _defs = defs ?? new List<ArenaDefenseDef>();
            _pool = pool;
            _onClose = onClose;
            Remaining = pool;
            foreach (var d in _defs)
                if (d != null && !string.IsNullOrEmpty(d.Id)) _byId[d.Id] = d;
            Rebuild();
        }

        // ── Budget / arm mutations (pushed by the controller) ───────────────────

        /// <summary>Push the live budget from the controller and re-project the cards.</summary>
        public void SetBudget(int spent, int remaining, int squadCount = 0)
        {
            Spent = spent < 0 ? 0 : spent;
            Remaining = remaining;
            SquadCount = squadCount < 0 ? 0 : squadCount;
            Rebuild();
            Raise();
        }

        /// <summary>Defense mode: arm a defender card (drives the highlight). A null clears it.</summary>
        public void Arm(string id)
        {
            ArmedId = id;
            Rebuild();
            Raise();
        }

        // ── Build the cards (no Unity types) ─────────────────────────────────────

        private void Rebuild()
        {
            _cards.Clear();
            foreach (var d in _defs)
            {
                if (d == null) continue;
                bool armed = Mode == ArenaPaletteMode.Defense && d.Id == ArmedId;
                // Affordable = the def's cost fits the remaining pool (a recruit/place always
                // ADDS), OR — in Defense — it is the already-armed card so a re-tap stays legal.
                bool affordable = armed || d.PointCost <= Remaining;
                string name = string.IsNullOrEmpty(d.DisplayName) ? d.Id : d.DisplayName;
                _cards.Add(new ItemVM(d.Id, name, IconRoleDefender, d.Id, d.PointCost, "pts",
                                      affordable, rarity: null, equipped: armed, locked: false));
            }
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}
