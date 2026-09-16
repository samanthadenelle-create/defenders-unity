// =============================================================================
// LiveWalletSource — the live producer of the shared WalletVM DTO (MVVM Silo C).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Strict-MVVM migration (UI_MVVM_MIGRATION_PLAN.md §2 note): a small live-wallet
// SOURCE that owns the wallet subscriptions (EconomyService.OnChanged for the
// in-session Wood/Iron pools + GameState.ResourcesChanged for the GameState-backed
// Food/Crystals/Coins) and produces the EXISTING Core.UI.Mvvm.WalletVM DTO plus a
// Changed event. This is deliberately NOT a second WalletVM — it reuses the one in
// Core.UI.Mvvm (a readonly-struct set of currency chips). BuildWalletRow binds this
// source's DTO instead of reading EconomyService.Instance / GameStateService.
//
// CreateDefault() resolves EconomyService.Instance itself (the ONLY resolution
// site); the injectable ctor lets §2c tests drive it over a fake IEconomy.
// =============================================================================

using System;
using DeNelle.Core.State;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village
{
    /// <summary>
    /// Owns the live wallet subscription and republishes the current balances as the shared
    /// <see cref="WalletVM"/> DTO (Wood/Iron/Food/Crystals/Gold chips, in that order), raising
    /// <see cref="Changed"/> on every mutation. View-agnostic — the View renders the DTO.
    /// </summary>
    public sealed class LiveWalletSource : IDisposable
    {
        /// <summary>Legacy IconRole marker on WalletVM entries. BuildWalletRow now renders kit
        /// CurrencyChips (icon-first); this field remains for DTO shape / tests.</summary>
        public const string IconRoleLetter = "letter";

        private readonly IEconomy _economy;
        private readonly Action<ResourceSnapshot> _ecoHandler;
        private readonly UnityEngine.Events.UnityAction _stateHandler;
        private bool _subscribed;
        private bool _disposed;

        /// <summary>The current wallet snapshot as chips (Wood/Iron/Food/Crystals/Gold). Never null.</summary>
        public WalletVM Wallet { get; private set; }

        /// <summary>Raised after every wallet mutation (a rebuilt <see cref="Wallet"/> is ready).</summary>
        public event Action Changed;

        /// <summary>Resolves EconomyService.Instance + GameStateService itself and subscribes both
        /// live wallet feeds. The View calls THIS and never names the services.</summary>
        public static LiveWalletSource CreateDefault()
        {
            var src = new LiveWalletSource(EconomyService.Instance);
            src.Subscribe();
            return src;
        }

        public LiveWalletSource(IEconomy economy)
        {
            _economy = economy;
            _ecoHandler = _ => Refresh();
            _stateHandler = Refresh;
            Refresh();   // seed Wallet before any subscription fires
        }

        /// <summary>Hook the live feeds (idempotent). CreateDefault calls it; tests may drive
        /// <see cref="Refresh"/> directly instead.</summary>
        public void Subscribe()
        {
            if (_subscribed || _disposed) return;
            _subscribed = true;
            var gs = GameStateService.Instance;
            if (gs != null) gs.ResourcesChanged.AddListener(_stateHandler);
            if (_economy != null) _economy.OnChanged += _ecoHandler;
        }

        /// <summary>Re-read the live wallet, rebuild the DTO, and raise <see cref="Changed"/>.</summary>
        public void Refresh()
        {
            int wood     = _economy != null ? _economy.Wood : 0;
            int iron     = _economy != null ? _economy.Iron : 0;
            // ⛔ STONE, NOT FOOD (WO-1163; leak caught by the owner on device 2026-08-25).
            // `EconomyService.Food` is the FROZEN INTERNAL SAVE SLOT that Stone now occupies -
            // WO-1163 deliberately reused it rather than migrating, so the field name is
            // persistence vocabulary and must NOT be renamed here. Everything the PLAYER reads
            // is Stone. The town strip converted; THIS surface did not, and shipped "F 130" to a
            // live build. Same "one fact written twice" class as the WO number block and the
            // assembly table - the conversion was applied per-surface instead of at one seam.
            int stone    = _economy != null ? _economy.Stone : 0;
            int crystals = _economy != null ? _economy.Crystals : 0;
            int gold     = _economy != null ? _economy.Coins : 0;

            Wallet = new WalletVM(new[]
            {
                new WalletVM.Entry("wood",     IconRoleLetter, "W", wood),
                new WalletVM.Entry("iron",     IconRoleLetter, "I", iron),
                new WalletVM.Entry("stone",    IconRoleLetter, "S", stone),
                new WalletVM.Entry("crystals", IconRoleLetter, "C", crystals),
                new WalletVM.Entry("gold",     IconRoleLetter, "G", gold),
            });
            if (!_disposed) Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_subscribed)
            {
                var gs = GameStateService.Instance;
                if (gs != null && _stateHandler != null) gs.ResourcesChanged.RemoveListener(_stateHandler);
                if (_economy != null && _ecoHandler != null) _economy.OnChanged -= _ecoHandler;
            }
            Changed = null;
        }
    }
}
