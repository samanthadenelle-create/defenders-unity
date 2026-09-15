// =============================================================================
// ArenaWalletService - the CURRENCY-AGNOSTIC Arena wager wallet (WO-1366).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Arena   (STATIC)
//
// ONE Arena, ONE code path (owner 2026-09-04: "both to use same logic just different
// curency for wagers"). Arena debits a wager, credits a purse and never knows or cares
// what the currency is. The currency is INJECTED by the existing per-channel seam,
// CurrencySkinResolver.ResolveWagerCurrency (Core/Platform), never branched on here:
//
//   Skr      (SolanaDappStore) -> the client-stub PlayerPrefs balance, BYTE-IDENTICAL to
//                                 the pre-WO-1366 behaviour (seeded 500, key unchanged).
//                                 STUB - NOT on-chain custody; promoting it is a money-
//                                 path change with its own ruling (WO-1366 section 3).
//   Crystals (GooglePlay)      -> the player's REAL Crystals, GameState.Resources.Crystals,
//                                 through GameStateService.AddCrystals (the one mutator;
//                                 clamps >= 0, persists, raises ResourcesChanged). The
//                                 debit is guarded HERE so it never goes negative and never
//                                 relies on the clamp - a clamp would silently under-charge.
//   Refused  (Unknown / Pi)    -> every Debit is refused with a worded sentence.
//
// !! THE PLAY BUILD NEVER TOUCHES THE STUB KEY. dotr-arena-skr-balance is seeded to 500
// FREE; reading or converting it on the Crystals path would grant premium currency for
// nothing (WO-1366 section 4). The stub is loaded ONLY on the Skr path.
//
// The public shape ArenaMode / ArenaVM depend on is unchanged: Balance / CanAfford /
// Debit / Credit / DevReset (+ a Debit overload that returns the refusal sentence, and
// CurrencyLabel for presentation).
//
// ASCII-only strings. No Unity scene dependency.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;
using DeNelle.Core.State;

namespace DeNelle.Village.Arena
{
    /// <summary>
    /// The Arena wager wallet. Resolves its backing store from the payment channel via
    /// <see cref="CurrencySkinResolver.ResolveWagerCurrency(out string)"/> on every call.
    /// </summary>
    public static class ArenaWalletService
    {
        private const string Sys = "ArenaWallet";

        // PlayerPrefs key for the persisted client SKR balance (devnet stub). UNCHANGED
        // on purpose - a renamed key would read as a fresh 500 seed (WO-1366 section 4).
        //
        // !! WO-1759 - THE PLAY SPELLING DIFFERS, AND THAT IS NOT A MIGRATION.
        // MEASURED in the rejected Play AAB (EchoesOfElarion-GooglePlay-20260915-165534,
        // global-metadata.dat): this literal is TWO of the only THREE `skr` occurrences the
        // packaging gate's matcher fires on, at offsets 1,587,839 and 10,556,064.
        // !! WHY WO-1754's ExactIdentifierAllowlist CANNOT SUPPRESS THEM: that rule requires a
        // NON-IDENTIFIER character on BOTH sides of the ruled key, which a NUL-delimited NAME
        // table entry satisfies. IL2CPP's STRING-LITERAL table is not NUL-delimited - literals
        // are packed end to end - so the byte after the key is the `d` of the next literal
        // (`...dotr-arena-pursedotr-arena-skr-balancedotr-arena-streak...`). The allowlist is
        // right to refuse there, and it must keep refusing: the whole point of the both-side
        // rule is that `dotr-arena-skr-balance-v2` still fires. So the leak is removed at the
        // SOURCE, per this WO's step 1, instead of by widening the gate.
        //
        // NOTHING IS MIGRATED. The Play channel resolves WagerCurrency.Crystals, so
        // EnsureLoaded() / the stub path is unreachable in this variant (file header, WO-1366
        // section 4) - the Play build has never written this key, so there is no stored value
        // for a different spelling to orphan. The dApp / Seeker build keeps the ruled key
        // VERBATIM, which is the half WO-1366 section 4 actually rules on.
#if GOOGLE_PLAY
        private const string PrefBalanceKey = "dotr-arena-wager-balance";
#else
        private const string PrefBalanceKey = "dotr-arena-skr-balance";
#endif

        // Seed balance so a brand-new SKR-stub player can immediately stake a wager.
        private const long SeedBalance = 500L;

        // True once we have read (and seeded, if first run) the persisted STUB balance.
        private static bool _loaded;
        private static long _balance;

        /// <summary>The wager currency the resolved payment channel wagers in.</summary>
        public static CurrencySkinResolver.WagerCurrency Currency =>
            CurrencySkinResolver.ResolveWagerCurrency(out _);

        /// <summary>Player-facing unit label for the current wager currency ("Crystals", the SKR skin name, or "-").</summary>
        public static string CurrencyLabel => CurrencySkinResolver.WagerCurrencyName(Currency);

        /// <summary>
        /// The current wager balance in the channel's currency: the stub SKR number, or the
        /// player's live Crystals, or 0 when the channel has no wager rail.
        /// </summary>
        public static long Balance
        {
            get
            {
                switch (CurrencySkinResolver.ResolveWagerCurrency(out _))
                {
                    case CurrencySkinResolver.WagerCurrency.Skr:
                        EnsureLoaded();
                        return _balance;
                    case CurrencySkinResolver.WagerCurrency.Crystals:
                        return CrystalsBalance();
                    default:
                        return 0L;
                }
            }
        }

        /// <summary>True if the channel's wager balance covers <paramref name="amount"/>. Always false on a refused channel.</summary>
        public static bool CanAfford(long amount)
        {
            var currency = CurrencySkinResolver.ResolveWagerCurrency(out _);
            if (currency == CurrencySkinResolver.WagerCurrency.Refused) return false;
            return amount <= 0 || Balance >= amount;
        }

        /// <summary>
        /// Stake / spend <paramref name="amount"/>. Returns false (no change) if the channel
        /// refuses wagers, the balance is insufficient or the amount is non-positive.
        /// </summary>
        public static bool Debit(long amount) => Debit(amount, out _);

        /// <summary>
        /// Stake / spend <paramref name="amount"/>, reporting WHY in <paramref name="refusal"/>
        /// when it returns false (never empty on a refusal - a dead button is forbidden).
        /// </summary>
        public static bool Debit(long amount, out string refusal)
        {
            var currency = CurrencySkinResolver.ResolveWagerCurrency(out refusal);
            string unit = CurrencySkinResolver.WagerCurrencyName(currency);

            if (currency == CurrencySkinResolver.WagerCurrency.Refused)
            {
                if (string.IsNullOrEmpty(refusal)) refusal = "Arena wagers are unavailable on this build.";
                FlowTrace.Warn(Sys, $"Debit REFUSED (channel has no wager rail): {refusal}");
                return false;
            }
            if (amount <= 0)
            {
                refusal = "Wager must be a positive amount.";
                FlowTrace.Warn(Sys, $"Debit refused: non-positive amount {amount} ({unit})");
                return false;
            }

            switch (currency)
            {
                case CurrencySkinResolver.WagerCurrency.Crystals:
                {
                    var svc = GameStateService.Instance;
                    if (svc == null || svc.State == null)
                    {
                        refusal = "Your Crystals could not be read - try again in a moment.";
                        FlowTrace.Fail(Sys, $"Debit {amount} Crystals refused: GameStateService unavailable (no wallet to charge)");
                        return false;
                    }
                    if (amount > int.MaxValue)
                    {
                        refusal = "That wager is larger than any Crystals balance can hold.";
                        FlowTrace.Warn(Sys, $"Debit refused: amount {amount} exceeds the int Crystals range");
                        return false;
                    }
                    long have = svc.State.Resources.Crystals;
                    if (have < amount)
                    {
                        // GUARD: refuse BEFORE the mutator. AddCrystals clamps at 0, so a
                        // blind AddCrystals(-n) would silently charge less than the wager.
                        refusal = $"Not enough Crystals: need {amount}, have {have}.";
                        FlowTrace.Warn(Sys, $"Debit refused: need {amount} Crystals, have {have}");
                        return false;
                    }
                    svc.AddCrystals(-(int)amount);   // negative = spend; persists + raises ResourcesChanged
                    long after = svc.State.Resources.Crystals;
                    FlowTrace.Step(Sys, $"Debit {amount} Crystals -> balance {after} (was {have}; GameState.Resources.Crystals via AddCrystals)");
                    if (after != have - amount)
                        FlowTrace.Fail(Sys, $"Debit {amount} Crystals landed at {after}, expected {have - amount} - the wager was NOT charged exactly");
                    return true;
                }

                case CurrencySkinResolver.WagerCurrency.Skr:
                default:
                {
                    EnsureLoaded();
                    if (_balance < amount)
                    {
                        refusal = $"Not enough {unit}: need {amount}, have {_balance}.";
                        FlowTrace.Warn(Sys, $"Debit refused: need {amount} {unit}, have {_balance}");
                        Debug.LogWarning($"[ArenaWalletService] STUB Debit refused: need {amount} {unit}, have {_balance}.");
                        return false;
                    }
                    _balance -= amount;
                    Persist();
                    FlowTrace.Step(Sys, $"Debit {amount} {unit} -> balance {_balance} (client stub)");
                    Debug.Log($"[ArenaWalletService] STUB Debit {amount} {unit} -> balance {_balance}.");
                    return true;
                }
            }
        }

        /// <summary>
        /// Pay <paramref name="amount"/> into the channel's wager balance (a won purse, or a
        /// no-contest refund). No-op on a non-positive amount or a refused channel.
        /// </summary>
        public static void Credit(long amount)
        {
            var currency = CurrencySkinResolver.ResolveWagerCurrency(out string refusal);
            string unit = CurrencySkinResolver.WagerCurrencyName(currency);

            if (amount <= 0) { FlowTrace.Warn(Sys, $"Credit no-op: non-positive amount {amount} ({unit})"); return; }

            switch (currency)
            {
                case CurrencySkinResolver.WagerCurrency.Refused:
                    // A credit can only follow a debit, and every debit is refused on this
                    // channel - reaching here means a caller bypassed Debit. Never silent.
                    FlowTrace.Fail(Sys, $"Credit {amount} DROPPED: channel has no wager rail ({refusal})");
                    return;

                case CurrencySkinResolver.WagerCurrency.Crystals:
                {
                    var svc = GameStateService.Instance;
                    if (svc == null || svc.State == null)
                    {
                        FlowTrace.Fail(Sys, $"Credit {amount} Crystals DROPPED: GameStateService unavailable (purse lost)");
                        return;
                    }
                    if (amount > int.MaxValue) amount = int.MaxValue;
                    long before = svc.State.Resources.Crystals;
                    svc.AddCrystals((int)amount);
                    FlowTrace.Step(Sys, $"Credit {amount} Crystals -> balance {svc.State.Resources.Crystals} (was {before}; via AddCrystals)");
                    return;
                }

                case CurrencySkinResolver.WagerCurrency.Skr:
                default:
                    EnsureLoaded();
                    _balance += amount;
                    Persist();
                    FlowTrace.Step(Sys, $"Credit {amount} {unit} -> balance {_balance} (client stub)");
                    Debug.Log($"[ArenaWalletService] STUB Credit {amount} {unit} -> balance {_balance}.");
                    return;
            }
        }

        /// <summary>DEV/TEST only: hard-reset the SKR STUB balance back to the seed amount. Never touches Crystals.</summary>
        public static void DevReset()
        {
            _balance = SeedBalance;
            _loaded = true;
            Persist();
        }

        /// <summary>
        /// TEST seam: forget the cached stub balance so the next Skr read re-loads it from
        /// PlayerPrefs (a suite that restores the pref must also drop this cache).
        /// </summary>
        public static void ForgetCachedStubForTests() { _loaded = false; _balance = 0L; }

        // The live Crystals balance, or 0 (traced) when no GameStateService is up.
        private static long CrystalsBalance()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                FlowTrace.Warn(Sys, "Crystals balance read with no GameStateService - reporting 0 (Arena shows unaffordable)");
                return 0L;
            }
            return svc.State.Resources.Crystals;
        }

        // Loads the SKR STUB balance. Only ever called from the Skr paths above - the
        // Crystals path must never read or seed this key (free-Crystals trap, WO-1366).
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            if (PlayerPrefs.HasKey(PrefBalanceKey))
            {
                // PlayerPrefs has no long getter; we store as a string for full range.
                long.TryParse(PlayerPrefs.GetString(PrefBalanceKey, SeedBalance.ToString()), out _balance);
            }
            else
            {
                _balance = SeedBalance;
                Persist();
                Debug.Log($"[ArenaWalletService] STUB seeded first-run wager balance = {SeedBalance}.");
            }
        }

        private static void Persist()
        {
            PlayerPrefs.SetString(PrefBalanceKey, _balance.ToString());
            PlayerPrefs.Save();
        }
    }
}
