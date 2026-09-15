// =============================================================================
// IWalletSigner — cross-assembly message-signing seam (WO-121, backend auth).
// -----------------------------------------------------------------------------
// The counterpart to the WO-120 backend save-auth gate. The backend's
// /api/game/save and /api/game/load now require a wallet-signed nonce in the
// X-Wallet / X-Nonce / X-Signature headers (see api/_lib/wallet-auth.js). The
// signing has to happen client-side with the connected wallet's ed25519 key.
//
// ── WHY A CORE INTERFACE ─────────────────────────────────────────────────────
// GameStateService (the HTTP save/load path) lives in DeNelle.Core. The wallet
// stack (WalletService / IWalletProvider) lives in DeNelle.Wallet, which already
// REFERENCES DeNelle.Core — so Core can NOT reference Wallet (circular asmdef,
// CS0234). The seam therefore is a Core-defined interface the Wallet module
// implements and registers in CoreServices, exactly like IJupiterService /
// IVillageHud / IAudioService. GameStateService resolves it via
// CoreServices.WalletSigner and never references DeNelle.Wallet.
//
// ── CAPABILITY-GATED (offline play must not break) ───────────────────────────
// Only StubWalletProvider ships today and it canNOT sign (no real ed25519 key).
// So CanSign is the capability check: when it is false, GameStateService SKIPS
// the auth headers entirely and the existing unauthed save path keeps working.
// CanSign flips true only when a real signer (SolanaWalletProvider behind the
// SOLANA_SDK define, via MWA / Seed Vault) is connected.
//
// ── MESSAGE FORMAT (must match the backend byte-for-byte) ────────────────────
//   dotr-save:v1:<wallet>:<nonce>:<sha256-hex-of-raw-body>     (save / POST)
//   dotr-save:v1:<wallet>:<nonce>:load                          (load / GET)
// The caller (GameStateService) builds that exact UTF-8 string and passes it to
// SignMessageBase58; the signer returns the base58 ed25519 signature. The
// signer NEVER builds the message itself — keeping the canonical format in one
// place (GameStateService) guarantees client + server agree.
// =============================================================================

using System.Threading.Tasks;

namespace DeNelle.Core.Backend
{
    /// <summary>
    /// A wallet that can ed25519-sign an arbitrary UTF-8 message and expose its
    /// base58 public address. Implemented by <c>WalletService</c> in
    /// DeNelle.Wallet and registered via <see cref="DeNelle.Core.CoreServices.RegisterWalletSigner"/>.
    /// Consumers (GameStateService) resolve it through
    /// <see cref="DeNelle.Core.CoreServices.WalletSigner"/> and MUST null-check.
    /// </summary>
    public interface IWalletSigner
    {
        /// <summary>
        /// True when this signer holds a real key and can produce a valid
        /// ed25519 signature (a connected SolanaWalletProvider). False for the
        /// devnet stub, which has no key — callers SKIP auth headers when false
        /// so offline / unauthed play is preserved.
        /// </summary>
        bool CanSign { get; }

        /// <summary>
        /// The connected wallet's base58 address (the on-chain identity =
        /// playerId), or null/empty when no wallet is connected. This MUST equal
        /// the X-Wallet header and the save body's playerId.
        /// </summary>
        string WalletAddress { get; }

        /// <summary>
        /// ed25519-signs the EXACT UTF-8 bytes of <paramref name="utf8Message"/>
        /// with the connected wallet key and returns the base58 signature.
        /// Returns null on failure or when the wallet cannot sign (caller then
        /// skips the auth headers rather than sending a bad signature). The
        /// caller owns the canonical message format — this only signs.
        /// </summary>
        Task<string> SignMessageBase58(string utf8Message);
    }
}
