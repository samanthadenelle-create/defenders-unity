// =============================================================================
// WalletBridgeStub — placeholder swap-transaction signer (WO-43).
// -----------------------------------------------------------------------------
// SCAFFOLD ONLY. This logs the serialised Jupiter /swap transaction and fires
// the success callback with a FAKE signature so the swap UI flow is testable in
// the editor and dev builds WITHOUT a live wallet. It signs NOTHING and submits
// NOTHING on-chain.
//
// Replace in a follow-up WO with the real Solana signer. The project already
// ships DeNelle.Wallet.SolanaWalletProvider (behind the SOLANA_SDK define) which
// signs SystemProgram/TokenProgram transfers; a Jupiter-swap path needs to
// deserialise the base64 swapTransaction from the /swap response and pass it to
// the connected wallet's SignAndSendTransaction. That signing cannot be verified
// here, so it is deliberately left as a stub.
//
// SECURITY: holds no key and signs nothing. The real signer must rely on the
// player's own wallet (Phantom deep-link / Seeker Seed Vault) to sign — the game
// must never hold a private key (spec Part 10).
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Web3
{
    /// <summary>
    /// Stub wallet bridge — logs the serialised transaction and fires the success
    /// callback with a fake signature in editor / dev builds, and hard-fails in a
    /// release build. Replace with the real Solana swap signer.
    /// </summary>
    public static class WalletBridgeStub
    {
        public static void SignAndSendTransaction(
            string serialisedSwapJson,
            Action<string> onSuccess,
            Action<string> onError = null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int len = serialisedSwapJson != null ? serialisedSwapJson.Length : 0;
            FlowTrace.Warn("WalletBridge", $"STUB sign+send (editor/dev): NO real signing, firing fake signature. Payload={len} chars (flag_14)");
            Debug.Log("[WalletBridgeStub] Would sign + send transaction. " +
                      "Replace this stub with the real wallet swap signer.");
            Debug.Log($"[WalletBridgeStub] Payload length: {len} chars");
            // Simulate a successful signing with a fake signature.
            string fakeSig = "STUB_SIG_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (onSuccess != null) onSuccess(fakeSig);
#else
            FlowTrace.Fail("WalletBridge", "HARD FAIL: WalletBridgeStub reached in a RELEASE build — no real swap signer wired; swap cannot complete (flag_14)");
            Debug.LogError("[WalletBridgeStub] WalletBridgeStub reached in a release " +
                           "build. Wire the real wallet swap signer before shipping.");
            if (onError != null) onError("Wallet bridge not configured.");
#endif
        }
    }
}
