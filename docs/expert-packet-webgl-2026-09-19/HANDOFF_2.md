# Handoff 2 — answers to the adapter findings (2026-09-19)

## .jslib (now in this folder: `SolanaWalletAdapterWebGL.jslib`)

`ExternSignTransactionWallet` calls **`walletAdapterLib.signTransaction(walletName, base64)`**, then `serialize()` back to base64. It does **not** call `signAndSendTransaction`. **Sign only.** After sign, C# must **submit via RPC and confirm**. That work was missing from the original problem statement.

No `window.jupiter` special case. Wallets are enumerated by **name** via `getWallets()` / `connectWallet(walletName)` from `@magicblock-labs/unity-wallet-adapter@1.2.1` (jsDelivr). Jupiter Wallet appears only if that CDN adapter lists it.

## VersionedTransaction in 1.2.9

**Not on disk.** `grep VersionedTransaction` under `Packages/com.solana.unity_sdk` is empty. **Bypass C# `Transaction.Deserialize` is mandatory** for Jupiter v0 / ALT txs. Opaque base64 in → signature (or signed base64) out → RPC send.

## Cross-platform wrapper (`SolanaWalletAdapter.cs`, now in this folder)

```csharp
#if UNITY_ANDROID
    _internalWallet = new SolanaMobileWalletAdapter(...);   // Seeker MWA
#elif UNITY_WEBGL
    _internalWallet = new SolanaWalletAdapterWebGL(...);    // Wallet Standard
#elif UNITY_IOS
    _internalWallet = new PhantomDeepLink(...);
#else
    // Windows / Editor: _internalWallet stays null → NotImplementedException
#endif
```

**Seeker MWA is compile-time.** A WebGL-only wire cannot change the Android APK. Windows is unimplemented here — defer.

## VFXCatalog regen

| Event | Date | Commit |
|---|---|---|
| Pet aura members deleted | 2026-08-20 | `0b18cccc5` |
| Last **Generate VFX Catalog** before that | 2026-08-16 | `e65b549ff` |
| **Catalog regenerated** (WO-1327 fireball = steam vent) | **2026-09-06** | `eb161dc98` (`VFXCatalog.asset` +36/−48, `VfxLoopFlagRegression` alignment pin) |

Someone **did** run regen after the deletion. Latest DataRegression this session: **`REGRESSION_OK 590/590`** (`Builds/r-1885.log`), which includes that alignment case. Ordinal drift is **pinned closed**, not still shipping as Cast_MuzzleFlash→Env_SteamVent.

WebGL remap can proceed **after a live raid capture**, not blocked on a second regen unless that oracle goes red.

## Atmosphere / visibilityExempt

Agreed: `SpawnAtmosphereFx` = 9× `PP_GroundFog` + 6× `PP_LightnigStormCloud`, all `visibilityExempt: true`. Highest-value WebGL remap target. Capture a **WebGL raid with storm up** before touching shaders. Owner still owes that PNG + Network.

`ApplyRaidSky` mutates `RenderSettings.ambient*` + `Camera.main.clearFlags`, restore on `OnDestroy`. WebGL tab-close mid-raid can leak storm tint into the next hub load. Named, not fixed.

## JupiterSwapService edits (agree)

- Parse `swapTransaction` (flat string, JsonUtility OK).
- Opaque base64 → adapter **sign** (not send) → **RPC submit + confirm** (extra vs original doc).
- Replace stub **release** branch with “no wallet → install Jupiter”, do not silent no-op.
- **flag_12 still open:** Jupiter public API is **mainnet**; wallet stack default in the adapter ctor is **DevNet**. Test blocker, not a write blocker.

## Still missing from us

One screenshot + Network/console of a **WebGL raid with the storm up**.
