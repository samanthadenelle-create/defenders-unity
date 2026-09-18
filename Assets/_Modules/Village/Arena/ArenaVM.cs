// =============================================================================
// ArenaVM — the pure ViewModel behind ArenaPanel (async-PvP entry + result).
// Strict-MVVM migration Silo D.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Arena
//
// ALL Arena game state + flow lives here, view-agnostic:
//   * ArenaCatalog opponent projection (name / tier / flavour / garrison / stake ->
//     purse + affordability) exposed as ItemVM cards + per-id helpers.
//   * The wager-wallet balance (in the channel's currency - WO-1366) + W/L record
//     (ArenaWalletService + ArenaProgressStore).
//   * The "Use My Castle" toggle write (ArenaMode.UsePlayerCastle).
//   * Start-raid + begin-attack/defend commands (ArenaMode.TryStartRaid + the recruit/
//     defense controllers).
//   * The OnRaidEnded push seam — the VM owns the subscription and re-raises it as
//     <see cref="RaidEnded"/> with the captured result the View paints.
// The View (ArenaPanel) binds this, re-renders on Changed, routes taps to commands,
// and never reads a service / catalog itself.
//
// PURE C#: no UnityEngine UI types; unit-testable over a fake IArenaBackend (§2c).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village.Arena
{
    /// <summary>
    /// The seam the ArenaVM resolves game state through. The live implementation
    /// (<see cref="ArenaLiveBackend"/>) wires ArenaCatalog / ArenaWalletService /
    /// ArenaProgressStore / ArenaMode; tests supply a fake.
    /// </summary>
    public interface IArenaBackend
    {
        IReadOnlyList<ArenaOpponentDef> Opponents { get; }
        long Balance { get; }
        /// <summary>
        /// The unit label of the wager currency ("Crystals" on Google Play, the SKR skin name
        /// on the dApp Store - WO-1366). Default implementation reads the one seam so an
        /// existing fake backend keeps compiling; the live backend overrides explicitly.
        /// </summary>
        string CurrencyLabel => ArenaWalletService.CurrencyLabel;
        int Wins { get; }
        int Losses { get; }
        int Streak { get; }
        bool CanAfford(long wager);
        bool UsePlayerCastle { get; set; }
        bool TryStartRaid(ArenaOpponentDef opponent);
        bool BeginAttack();
        bool BeginDefense();
        event Action<ArenaOpponentDef, ArenaResult, long> RaidEnded;
    }

    /// <summary>Pure ViewModel for the Arena entry + result panel.</summary>
    public sealed class ArenaVM : IPanelViewModel, IDisposable
    {
        /// <summary>Icon role key on each opponent card (the View maps it to art; no game state).</summary>
        public const string IconRoleOpponent = "arena-opponent";

        private readonly IArenaBackend _backend;
        private readonly Action _onClose;
        private readonly List<ItemVM> _opponents = new List<ItemVM>();
        private readonly Dictionary<string, ArenaOpponentDef> _byId =
            new Dictionary<string, ArenaOpponentDef>();
        private readonly Action<ArenaOpponentDef, ArenaResult, long> _raidEndedHandler;
        private bool _disposed;

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        public string Title => LocalText.Get("arena.title");

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_backend != null && _raidEndedHandler != null) _backend.RaidEnded -= _raidEndedHandler;
            Changed = null;
            RaidEnded = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>One card per seeded opponent (name + stake + affordability). Never null.</summary>
        public IReadOnlyList<ItemVM> Opponents => _opponents;

        public long Balance => _backend != null ? _backend.Balance : 0L;
        /// <summary>Unit label for Balance / stakes / deltas (the View never hardcodes a currency).</summary>
        public string CurrencyLabel => _backend != null ? _backend.CurrencyLabel : "-";
        public int Wins => _backend != null ? _backend.Wins : 0;
        public int Losses => _backend != null ? _backend.Losses : 0;
        public int Streak => _backend != null ? _backend.Streak : 0;

        /// <summary>W/L + streak readout line for the record well.</summary>
        public string RecordLine => Wins + "W / " + Losses + "L   (" + Streak + " streak)";

        /// <summary>The result-screen footer stats line.</summary>
        public string StatsLine => CurrencyLabel + " " + Balance + "      " + Wins + "W / " + Losses + "L      Streak " + Streak;

        /// <summary>Whether the raid fights the player's own castle (ArenaMode toggle).</summary>
        public bool UsePlayerCastle => _backend != null && _backend.UsePlayerCastle;

        /// <summary>The defender-base sub-label on the toggle well.</summary>
        public string DefenderLabel => UsePlayerCastle ? "My Castle" : "Seeded opponent";

        /// <summary>The toggle button caption (ASCII glyph on the ON state — colorblind-safe).</summary>
        public string CastleToggleLabel => UsePlayerCastle ? LocalText.Get("arena.castle_toggle.my_castle") : LocalText.Get("arena.castle_toggle.use_my_castle");

        // Per-opponent detail helpers (View renders from these — no catalog re-pull).
        public string FlavourFor(string id) { var o = OppOf(id); return o != null ? o.Flavour : ""; }
        public int TierFor(string id) { var o = OppOf(id); return o != null ? o.Tier : 0; }
        public int GuardCountFor(string id) { var o = OppOf(id); return o != null ? o.GuardCount : 0; }
        public long WagerFor(string id) { var o = OppOf(id); return o != null ? o.Wager : 0L; }
        public long WinPurseFor(string id) { var o = OppOf(id); return o != null ? o.WinPurse : 0L; }

        // ── Result-screen data (captured from the OnRaidEnded push seam) ────────

        /// <summary>Raised after a raid resolves; the View shows the result screen on this.</summary>
        public event Action RaidEnded;

        public string LastOpponentName { get; private set; } = "opponent";
        public ArenaResult LastResult { get; private set; } = ArenaResult.None;
        public long LastDelta { get; private set; }

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Flip the "Use My Castle" defender toggle. Raises Changed (label repaints).</summary>
        public void ToggleUseMyCastle()
        {
            if (_backend != null) _backend.UsePlayerCastle = !_backend.UsePlayerCastle;
            Raise();
        }

        /// <summary>Stake + start a raid against the opponent id. Returns true when the raid
        /// started (the View hides its overlay); false refreshes the header (can't afford / busy).</summary>
        public bool TryStartRaid(string id)
        {
            var opp = OppOf(id);
            if (opp == null) return false;
            bool ok = _backend != null && _backend.TryStartRaid(opp);
            if (!ok) Raise();
            return ok;
        }

        /// <summary>Open the ATTACK recruit flow. Returns false if it could not enter.</summary>
        public bool BeginAttack() => _backend != null && _backend.BeginAttack();

        /// <summary>Open the DEFENSE placement flow. Returns false if it could not enter.</summary>
        public bool BeginDefense() => _backend != null && _backend.BeginDefense();

        // ── Construction / resolution ───────────────────────────────────────────

        /// <summary>The ONLY resolution site: wires the live Arena statics/singleton.</summary>
        public static ArenaVM CreateDefault(Action onClose = null) =>
            new ArenaVM(new ArenaLiveBackend(), onClose);

        public ArenaVM(IArenaBackend backend, Action onClose)
        {
            _backend = backend;
            _onClose = onClose;

            if (_backend != null)
            {
                _raidEndedHandler = HandleRaidEnded;
                _backend.RaidEnded += _raidEndedHandler;
            }

            Rebuild();
        }

        private void HandleRaidEnded(ArenaOpponentDef opp, ArenaResult result, long skrDelta)
        {
            LastOpponentName = opp != null ? opp.DisplayName : "opponent";
            LastResult = result;
            LastDelta = skrDelta;
            Raise();
            if (!_disposed) RaidEnded?.Invoke();
        }

        private ArenaOpponentDef OppOf(string id) =>
            id != null && _byId.TryGetValue(id, out var o) ? o : null;

        private void Rebuild()
        {
            _opponents.Clear();
            _byId.Clear();
            var list = _backend != null ? _backend.Opponents : null;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null || string.IsNullOrEmpty(o.Id)) continue;
                _byId[o.Id] = o;
                string name = string.IsNullOrEmpty(o.DisplayName) ? o.Id : o.DisplayName;
                bool afford = _backend.CanAfford(o.Wager);
                _opponents.Add(new ItemVM(o.Id, name, IconRoleOpponent, o.Id, (int)o.Wager, _backend.CurrencyLabel,
                                          afford, rarity: null, equipped: false, locked: false));
            }
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }

    /// <summary>
    /// Live <see cref="IArenaBackend"/> — the sole binding to the Arena statics + the
    /// ArenaMode singleton (ArenaCatalog / ArenaWalletService / ArenaProgressStore +
    /// the recruit/defense controllers). Kept out of the View so ArenaPanel stays a
    /// dumb skin.
    /// </summary>
    public sealed class ArenaLiveBackend : IArenaBackend
    {
        public IReadOnlyList<ArenaOpponentDef> Opponents => ArenaCatalog.All;
        public long Balance => ArenaWalletService.Balance;
        public string CurrencyLabel => ArenaWalletService.CurrencyLabel;
        public int Wins => ArenaProgressStore.Current.Wins;
        public int Losses => ArenaProgressStore.Current.Losses;
        public int Streak => ArenaProgressStore.Current.Streak;
        public bool CanAfford(long wager) => ArenaWalletService.CanAfford(wager);

        public bool UsePlayerCastle
        {
            get => ArenaMode.Instance != null && ArenaMode.Instance.UsePlayerCastle;
            set { if (ArenaMode.Instance != null) ArenaMode.Instance.UsePlayerCastle = value; }
        }

        public bool TryStartRaid(ArenaOpponentDef opponent) =>
            ArenaMode.Instance != null && ArenaMode.Instance.TryStartRaid(opponent);

        public bool BeginAttack()
        {
            try
            {
                var ctl = ArenaAttackRecruitController.EnsureExists();
                // Default the raid target to the first seeded opponent (MVP) so the
                // recruit -> launch path always has a base to raid.
                var all = ArenaCatalog.All;
                if (all != null && all.Count > 0) ctl.SetOpponent(all[0]);
                ctl.Enter();
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[ArenaLiveBackend] BeginAttack failed: " + e);
                return false;
            }
        }

        public bool BeginDefense()
        {
            try
            {
                var ctl = ArenaDefenseSetupController.EnsureExists();
                ctl.Enter();
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[ArenaLiveBackend] BeginDefense failed: " + e);
                return false;
            }
        }

        public event Action<ArenaOpponentDef, ArenaResult, long> RaidEnded
        {
            add { if (ArenaMode.Instance != null) ArenaMode.Instance.OnRaidEnded += value; }
            remove { if (ArenaMode.Instance != null) ArenaMode.Instance.OnRaidEnded -= value; }
        }
    }
}
