# Handoff 3 — confirmation (2026-09-19)

`SolanaWalletAdapter.cs` is in this folder (3066 bytes). The `#if UNITY_ANDROID` / `#elif UNITY_WEBGL` / `#elif UNITY_IOS` / `#else` split is the full file, not an excerpt.

Owner agrees with the revised first cut:

1. **First action:** load https://defenders-webgl.vercel.app/ with Jupiter Wallet installed. If Magicblock `getWallets()` lists Jupiter, wire `JupiterSwapService` → adapter (opaque base64, sign-only, then RPC submit+confirm). If not, then the small `window.jupiter` jslib. Do not write that jslib until the enum test fails.
2. **VFX:** capture first (owner). Then remap. Depth WebGL-only, default off. Do not touch `m_StripUnusedVariants`.
3. **ApplyRaidSky:** boot-time ambient reset in WebGL v1.
4. **Splash / Blink:** as written.
5. **Windows:** still deferred.

flag_12 remains a **verify** blocker (mainnet Jupiter vs adapter DevNet default), not a write blocker.
