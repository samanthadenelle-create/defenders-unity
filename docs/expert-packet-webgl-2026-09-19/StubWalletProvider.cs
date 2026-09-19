// =============================================================================
// StubWalletProvider — the devnet-mock IWalletProvider (Week 7, no SDK)
// -----------------------------------------------------------------------------
// The Solana Unity SDK is NOT installed in the v2 foundation — its install is
// deferred to Week 7 (unity-decisions.md, 2026-05-18). So WalletService needs a
// provider that compiles and RUNS today, with no SDK dependency. This is it.
//
// What it fakes:
//   * Connect()     — generates a SELF-EVIDENTLY FAKE, 44-char, devnet-shaped
//                     address after a short simulated handshake delay.
//
// SECURITY (audit 2026-08-02) — the stub address must never be mistakable for a
// real wallet. It used to be a plain 44-char base58 string minted from a CONSTANT
// seed (new Random(0xDEFEED)), so (a) every device produced the SAME first address
// and (b) GameStateService's old denylist test ("does not start with guest-local-")
// read it as a real cloud identity. If SOLANA_SDK were ever missing from an Android
// build, every tester would have shared ONE player_data row. Two structural fixes:
//   1. The address carries the "stub-wallet-" marker, whose '-' is NOT in the base58
//      alphabet — so it fails GameStateService.IsCloudIdentityShaped by construction,
//      not by policy. Length stays 44 (12 marker + 32 base58) so nothing that
//      measures a devnet-shaped address changes.
//   2. The RNG is seeded per instance, so two devices no longer mint the same
//      "wallet" for logs, analytics and leaderboards.
// Cloud-save keying additionally requires provider attestation
// (WalletService.IsRealSigningWallet), which this provider can never satisfy.
//   * GetBalance()  — returns fixed devnet mock balances (generous, so the store
//                     and all five packs are exercisable end-to-end).
//   * SendPayment() — simulates Solana finality (~1s), debits the mock balance,
//                     and returns a fabricated tx signature on success.
//
// When the Solana Unity SDK lands, a SolanaWalletProvider : IWalletProvider
// replaces this — WalletService and every caller are untouched, the interface
// is the seam. This file can then be kept for EditMode tests and offline dev.
// =============================================================================

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DeNelle.Wallet
{
    /// <summary>
    /// Devnet mock <see cref="IWalletProvider"/>. Fake balances, simulated
    /// transaction confirmation — lets the wallet + pack-store module compile
    /// and run before the Solana Unity SDK is installed.
    /// </summary>
    public sealed class StubWalletProvider : IWalletProvider
    {
        // Simulated network timings — kept short so dev/test flows are snappy.
        private const int ConnectDelayMs = 350;
        private const int ConfirmDelayMs = 1100; // mirrors Solana's ~1s finality

        // base58 alphabet — Bitcoin/Solana convention (no 0, O, I, l).
        private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        /// <summary>
        /// The marker every stub address carries. The hyphen is NOT a base58 character,
        /// which is what makes a stub address structurally unable to pass the cloud
        /// identity allowlist (GameStateService.IsCloudIdentityShaped). 12 chars, so a
        /// 32-char base58 tail still yields the familiar 44-char total length.
        /// Do NOT "clean this up" into pure base58 — the ugliness IS the safety.
        /// </summary>
        public const string FakeAddressMarker = "stub-wallet-";

        // Devnet mock balances — generous on purpose so all five packs (up to
        // Founder's Vow at 0.45 SOL / 49.99 USDC / 600 SKR) are purchasable.
        private static readonly WalletBalance StartingBalance = new WalletBalance
        {
            Sol = 5d,
            Usdc = 250d,
            Skr = 2000d,
        };

        private WalletBalance _balance = StartingBalance;
        private WalletAccount _account;
        private bool _connected;
        // Per-INSTANCE seed (was the constant 0xDEFEED): a fixed seed made every device
        // mint an identical first "wallet", which is indistinguishable from a shared
        // identity in logs, analytics and any list keyed by address.
        private readonly System.Random _rng = new System.Random(Guid.NewGuid().GetHashCode());

        /// <inheritdoc/>
        public string ProviderName => "Devnet Stub Wallet";

        /// <inheritdoc/>
        public bool IsConnected => _connected;

        /// <inheritdoc/>
        public WalletAccount Account => _account;

        // =====================================================================
        //  Connect / Disconnect
        // =====================================================================

        /// <inheritdoc/>
        public async UniTask<WalletAccount> Connect(WalletNetwork network)
        {
            // Simulate the MWA / deep-link handshake round-trip.
            await UniTask.Delay(ConnectDelayMs);

            _account = new WalletAccount
            {
                Address = GenerateDevnetAddress(),
                WalletName = ProviderName,
            };
            _connected = true;
            _balance = StartingBalance;

            Debug.Log($"[StubWalletProvider] Connected ({network}) — {_account.ShortAddress}.");
            return _account;
        }

        /// <inheritdoc/>
        public UniTask Disconnect()
        {
            _connected = false;
            _account = default;
            Debug.Log("[StubWalletProvider] Disconnected.");
            return UniTask.CompletedTask;
        }

        // =====================================================================
        //  GetBalance
        // =====================================================================

        /// <inheritdoc/>
        public async UniTask<WalletBalance> GetBalance(WalletNetwork network)
        {
            if (!_connected)
            {
                Debug.LogWarning("[StubWalletProvider] GetBalance with no wallet connected.");
                return default;
            }

            // A small simulated RPC round-trip.
            await UniTask.Delay(120);
            return _balance;
        }

        // =====================================================================
        //  SendPayment — simulated devnet transfer + confirmation
        // =====================================================================

        /// <inheritdoc/>
        public async UniTask<PaymentResult> SendPayment(
            string packSku, CurrencyKind currency, double amount, WalletNetwork network)
        {
            if (!_connected)
                return PaymentResult.Failure(packSku, currency, "No wallet connected.");

            var available = _balance.For(currency);
            if (available < amount)
                return PaymentResult.Failure(
                    packSku, currency,
                    $"Insufficient {currency} — need {amount}, have {available}.");

            // Simulate building + signing + submitting, then Solana finality.
            await UniTask.Delay(ConfirmDelayMs);

            // Debit the mock balance so repeat purchases behave realistically.
            switch (currency)
            {
                case CurrencyKind.Sol: _balance.Sol -= amount; break;
                case CurrencyKind.Usdc: _balance.Usdc -= amount; break;
                case CurrencyKind.Skr: _balance.Skr -= amount; break;
            }

            var signature = GenerateTxSignature();
            Debug.Log($"[StubWalletProvider] Devnet tx confirmed — {packSku} paid {amount} {currency}, sig {signature}.");
            return PaymentResult.Success(packSku, currency, amount, signature);
        }

        // =====================================================================
        //  Message signing (WO-121) — the stub holds no key, so it CANNOT sign
        // =====================================================================

        /// <inheritdoc/>
        // The devnet stub has no real ed25519 key, so it can never produce a
        // signature the backend would accept. Returning false here is what makes
        // GameStateService SKIP the auth headers and keep the unauthed save path
        // working until a real SolanaWalletProvider is connected.
        public bool CanSignMessages => false;

        /// <inheritdoc/>
        public UniTask<string> SignMessageBase58(string utf8Message)
        {
            Debug.LogWarning("[StubWalletProvider] SignMessageBase58 called — the devnet stub cannot sign. " +
                             "Returning null (auth headers will be skipped).");
            return UniTask.FromResult<string>(null);
        }

        // =====================================================================
        //  Mock-value generation
        // =====================================================================

        /// <summary>
        /// A devnet-shaped 44-char address that is SELF-EVIDENTLY FAKE: the
        /// "stub-wallet-" marker (non-base58 '-') plus 32 base58 chars. Not a real key,
        /// and by construction not a value that can key a cloud save.
        /// </summary>
        private string GenerateDevnetAddress()
        {
            return FakeAddressMarker + RandomBase58(44 - FakeAddressMarker.Length);
        }

        /// <summary>A fabricated base58 transaction signature (Solana signatures are ~88 chars).</summary>
        private string GenerateTxSignature()
        {
            return RandomBase58(88);
        }

        private string RandomBase58(int length)
        {
            var sb = new StringBuilder(length);
            for (var i = 0; i < length; i++)
                sb.Append(Base58Alphabet[_rng.Next(Base58Alphabet.Length)]);
            return sb.ToString();
        }
    }
}
