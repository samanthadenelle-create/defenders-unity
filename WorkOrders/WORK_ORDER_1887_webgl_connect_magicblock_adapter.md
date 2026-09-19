# WO-1887 — WebGL Connect Wallet uses Magicblock adapter, not StubWalletProvider

**Status:** IMPLEMENTED — gated `COMPILE_GATE_OK` (`Builds/cg-1887fog.log`). Connect on WebGL uses Magicblock adapter. Push + WebGL redeploy this session.

**Owner:** Connect Wallet on https://defenders-webgl.vercel.app/ showed `stub-wallet-…` (StubWalletProvider). Expert: Magicblock adapter already exists; Connect never called it.

## Ruling
- `#if UNITY_WEBGL && !UNITY_EDITOR && SOLANA_SDK`: `WalletService()` selects `WebGLWalletProvider` wrapping `SolanaWalletAdapter` (Wallet Standard). Prefabs: `Resources/SolanaUnitySDK/WalletAdapterUI` + `WalletAdapterButton`.
- Do **not** change `SolanaWalletProvider.IsSupportedOnThisPlatform` (Seeker MWA).
- Editor/Windows still stub. No in-game Jupiter swap plate in this ticket. Sign-then-RPC is follow-up.
- Cluster: `RpcCluster.MainNet` (matches `WalletService.DefaultNetwork`).
- Missing wallets: `Application.OpenURL` Jupiter install (`https://jup.ag`).
- Connect timeout 90s on WebGL (picker).

## Files
- `WebGLWalletProvider.cs` (new)
- `WalletService.cs` ctor auto-select
