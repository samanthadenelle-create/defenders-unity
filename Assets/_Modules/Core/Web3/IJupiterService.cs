// =============================================================================
// IJupiterService — cross-assembly contract for the Jupiter swap panel (WO-43).
// -----------------------------------------------------------------------------
// Registered in CoreServices; callers (Village / HeroSelect / HUD) open the
// swap panel via CoreServices.Jupiter?.OpenSwapPanel(...) and NEVER reference
// the DeNelle.Web3 assembly directly. The concrete implementation
// (JupiterSwapService) lives in DeNelle.Web3.
//
// DEVNET / SECURITY NOTE: the project's wallet stack (DeNelle.Wallet) is
// devnet-only and owner-gated to mainnet (v2-unity-port-spec Part 10). The
// Jupiter aggregator REST API is mainnet. No real swap transaction is signed
// or submitted by this scaffold — see JupiterSwapService for the explicit
// TODO where real wallet signing must be wired before going live.
// =============================================================================

// -----------------------------------------------------------------------------
// WO-1377 (2026-09-09) — TYPE-LEVEL COMPILE-OUT UNDER GOOGLE_PLAY.
//
// IL2CPP's global-metadata.dat carries TYPE AND MEMBER NAMES, not just string
// literals, so a runtime `#if` INSIDE a method removes nothing: `IJupiterService`,
// `SwapQuote`, `SwapInputToken` and `USDC` all ship as identifiers in the Google
// Play AAB unless the TYPES themselves are compiled out. This whole namespace body
// is therefore wrapped, not guarded.
//
// PROVEN SAFE (Phase 1, quoted in the WO): these types have ZERO consumers in any
// assembly that ships under GOOGLE_PLAY. Their only references are
//   * CoreServices.Jupiter / RegisterJupiter / UnregisterJupiter — wrapped in the
//     same `#if !GOOGLE_PLAY` in the same change;
//   * Assets/_Modules/Web3/* (JupiterSwapService, JupiterSwapBootstrap, SwapVM) —
//     the DeNelle.Web3 assembly, already `"!GOOGLE_PLAY"`-constrained at
//     DeNelle.Web3.asmdef:17, so it is not compiled into a Play build at all;
//   * Assets/Tests/EditMode/SwapVMTests.cs — DeNelle.Tests.EditMode, which is
//     `UNITY_INCLUDE_TESTS`-constrained and never compiled into a player build.
// None of these types is persisted: no field of them exists in Core/State, no
// PlayerPrefs writer, no ToString()/Enum.Parse round-trip. Nothing is renamed and
// nothing is reordered — SwapInputToken keeps USDC = 0, SOL = 1.
//
// ⛔ DO NOT DELETE. Jupiter swap is a real dApp-lane feature; it is only ABSENT on
// Play. The oracle PlayMetadataIdentifierRegression FAILS BOTH WAYS — if these
// identifiers survive under GOOGLE_PLAY, and if they stop existing without it.
// -----------------------------------------------------------------------------

using System.Threading.Tasks;

#if !GOOGLE_PLAY
namespace DeNelle.Core.Web3
{
    /// <summary>
    /// Fetches swap quotes from Jupiter and opens the in-game swap panel.
    /// Implemented by <c>JupiterSwapService</c> in DeNelle.Web3.
    /// </summary>
    public interface IJupiterService
    {
        /// <summary>
        /// Opens the swap panel pre-filled to acquire at least
        /// <paramref name="minimumSkr"/> SKR. Pass 0 to open blank.
        /// </summary>
        void OpenSwapPanel(decimal minimumSkr = 0m);

        /// <summary>Closes the panel if it is currently visible.</summary>
        void CloseSwapPanel();

        /// <summary>
        /// Fetches a live quote without opening the panel.
        /// Returns null on network error.
        /// </summary>
        Task<SwapQuote> GetQuoteAsync(SwapInputToken input, decimal inputAmount);
    }

    /// <summary>
    /// Quote returned by the Jupiter quote endpoint. Plain class (not a C# 9
    /// record with init-only setters) so it compiles on the project's C#
    /// language level without requiring the <c>IsExternalInit</c> shim.
    /// </summary>
    public sealed class SwapQuote
    {
        /// <summary>SKR the player will receive (after fees).</summary>
        public decimal SkrOut { get; set; }
        /// <summary>Exchange rate: 1 input token = N SKR.</summary>
        public decimal Rate { get; set; }
        /// <summary>Platform fee taken by Defenders (in input-token units).</summary>
        public decimal PlatformFee { get; set; }
        /// <summary>Network + Jupiter route fees (in input-token units).</summary>
        public decimal NetworkFee { get; set; }
        /// <summary>Slippage tolerance used for this quote, in bps.</summary>
        public int SlippageBps { get; set; }
        /// <summary>Opaque quote-response JSON from Jupiter — forwarded verbatim to /swap.</summary>
        public string RoutePlan { get; set; }
    }

    /// <summary>Input token options for the swap panel. v1 exposes USDC only.</summary>
    public enum SwapInputToken
    {
        /// <summary>USDC — the only input surfaced in the v1 UI.</summary>
        USDC = 0,
        /// <summary>SOL — reserved for v2; not shown in the UI until explicitly enabled.</summary>
        SOL = 1
    }
}
#endif  // !GOOGLE_PLAY  (WO-1377 — type-level compile-out, see the header)
