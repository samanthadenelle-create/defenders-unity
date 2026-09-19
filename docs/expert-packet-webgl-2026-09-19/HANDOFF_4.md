# Handoff 4 — Connect Wallet path and build config (2026-09-19)

## Two different stubs (the screenshot is not WalletBridgeStub)

`Wallet stub…bg3h` is **`StubWalletProvider`**, not `WalletBridgeStub`.

`WalletAccount.ShortAddress` is `first4…last4`. Stub addresses are `stub-wallet-` + 32 base58 chars (`StubWalletProvider.FakeAddressMarker`). UI shows **Devnet Stub Wallet** / `stub…xxxx`. That matches the title-screen capture.

**`WalletBridgeStub` is only the Jupiter `/swap` signer.** Connect Wallet never calls it.

## Button → stub wiring (files now in this folder)

1. Title **Connect Wallet** lives in Core (`PiSignInController`) and raises `CurrencySkinResolver.WalletConnectRequested`.
2. `WalletSkinBootstrap.OnConnectRequested` → `WalletService.Connect()`.
3. `WalletService()` auto-selects provider:

```csharp
if (SolanaWalletProvider.IsSupportedOnThisPlatform)
    _provider = new SolanaWalletProvider();  // MWA
else
    _provider = new StubWalletProvider();    // WebGL, Windows, Editor
```

4. `SolanaWalletProvider.IsSupportedOnThisPlatform` is **`#if SOLANA_SDK && UNITY_ANDROID && !UNITY_EDITOR`**. WebGL is **false by construction**. Connect on the live URL **must** use `StubWalletProvider`. That is WO-766, not a mis-deploy.

Magicblock `getWallets()` was never called because **Connect never reaches `SolanaWalletAdapter`**.

## DEVELOPMENT_BUILD on the live URL

**Ship path is not a Development Build.**

- `build-webgl.ps1`: `-DevBuild` is opt-in. Default does **not** pass `-devBuild`. Comment: “Do NOT deploy to prod.”
- `WebGLBuild.cs:73-74, 124`: `BuildOptions.Development` **only** if `-devBuild` is on the command line. Default `BuildOptions.None`.

This session’s WebGL was `powershell -File .\build-webgl.ps1` **without** `-DevBuild`. `DEVELOPMENT_BUILD` should **not** be set in `376181`.

The fake connect on prod is **`StubWalletProvider`**, which has **no** `#if DEVELOPMENT_BUILD`. It runs in release.

`WalletBridgeStub`’s editor/dev fake-sig path is a **different** file, used only after Jupiter `/swap`. Title Connect does not hit it. Do not treat the live URL as proof that `DEVELOPMENT_BUILD` is on.

## Where the `#if UNITY_WEBGL` repoint goes

**`WalletService()` constructor** (auto-select): on `UNITY_WEBGL`, do **not** take `StubWalletProvider`. Take a WebGL provider that wraps `SolanaWalletAdapter` / Magicblock.

Do **not** change `SolanaWalletProvider.IsSupportedOnThisPlatform` (that is Seeker MWA). Do **not** change the Android branch.

Then the Jupiter enumeration test becomes meaningful: Connect → adapter `getWallets()` → does Jupiter appear?

## Packet adds

| File | Why |
|---|---|
| `PiSignInController.cs` | Title/login Connect face (Core) |
| `WalletSkinBootstrap.cs` | SKR skin → `WalletService.Connect` |
| `WalletService.cs` | Auto-select stub vs MWA |
| `StubWalletProvider.cs` | Fake `stub-wallet-` address |
| `SolanaWalletProvider.cs` | `IsSupportedOnThisPlatform` Android-only |
| `WebGLBuild.cs` + `build-webgl.ps1` | Default `BuildOptions.None`; `-DevBuild` opt-in |
