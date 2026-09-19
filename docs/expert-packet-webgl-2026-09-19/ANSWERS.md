# Answers to the expert — 2026-09-19

Owner-approved recommended cut is at the bottom. Files in this folder are copies of HEAD at packet time.

## Wallet

### Swap UI: blocking or optional?

**Optional. Night Market convenience.** Spending is never required. WebGL v1 does **not** need an in-game swap plate.

**Recommended v1:** Connect Jupiter Wallet (extension) → Night Market still quotes SKR → if the player needs SKR, **deep-link / open Jupiter web swap** (or extension swap). Do **not** port `JupiterSwapPanelController` UXML→uGUI in v1.

**v2:** code-built uGUI swap plate that calls the same bridge.

### Title “Connect Wallet” — repurpose or separate?

**Do not change the Seeker path.** The title button is `PiSignInController` / `WalletSkinBootstrap` → `WalletService.Connect()` (MWA on Android). Shared code.

**Recommended:** keep that button’s **copy**. On `UNITY_WEBGL` only, the connect handler calls the Wallet Adapter / Jupiter extension instead of MWA. Same face, platform-split implementation. Do not add a second Connect button.

### `com.solana.unity_sdk` version

**Pinned: 1.2.9** (`Packages/com.solana.unity_sdk/package.json`). Magicblock / garbles-labs.

It is **not MWA-only**. The package already contains:

- `SolanaWalletAdapterWebGL.cs` — `RuntimePlatform.WebGLPlayer` only
- `SolanaWalletAdapterWebGL.jslib` — loads `@magicblock-labs/unity-wallet-adapter@1.2.1` from jsDelivr, `connectWallet` / `signTransaction` by wallet **name**
- Android MWA is a **separate** tree: `Runtime/codebase/SolanaMobileStack/`

Our game **does not currently use** `SolanaWalletAdapterWebGL` for Jupiter swaps. `JupiterSwapService` still calls `WalletBridgeStub`. Evaluate this adapter **before** a greenfield `window.jupiter` jslib. If the CDN adapter lists Jupiter Wallet as a named wallet, prefer wiring it. If it does not, a small jslib that prefers `window.jupiter` then Phantom/Solflare is the fallback the expert described.

**Windows:** this jslib is `window`-only. Standalone Windows is a **separate** job (defer). Do not treat WebGL + Windows as one estimate.

### Degradation

Agreed. No wallet → game fully playable. Connect button → Jupiter Wallet install page when none detected.

### Bridge job (A)

Agreed. Hand Jupiter `/swap` **base64 straight to the extension**. Do not deserialize/re-sign in C# (`JupiterSwapService.cs:303-307`). Replace `WalletBridgeStub.SignAndSendTransaction` in **release WebGL** only; leave Seeker MWA alone.

---

## VFX

### How VFXManager is keyed

**Both:**

- **Enum:** `VFXManager.Play(VFXType, position)` (`VFXType.cs`, catalog prefab pool).
- **String:** `VFXManager.PlayKey("PP_GroundFog", …)` — Hovl catalog lookup (`VFXManager.Hovl.cs:382`). Raid atmosphere uses **string keys only**.

Raid caller: `RaidGarrisonSpawner.SpawnAtmosphereFx` — `PlayKey("PP_GroundFog", … visibilityExempt: true)` and `PlayKey("PP_LightnigStormCloud", …)`.

Remap is a **lookup table** in `PlayKeyInternal` under `#if UNITY_WEBGL`, not prefab surgery.

### Split (agree)

Do **not** ship remap without a repro capture.

1. Confirm repro + screenshot + Network/console on https://defenders-webgl.vercel.app/ (hard-refresh). **We do not have this PNG yet.**
2. Depth texture on **WebGL URP renderer only**, default **off**, on for desktop (not Pi).
3. Asset remap Hovl distortion/soft-particle keys → `Assets/Resources/VFX/Projectiles/`.
4. **Do not** touch `m_StripUnusedVariants`.

---

## Splash

- **No layered source in the repo.** Only flattened `Assets/Resources/Title/Title_L.jpg` and `Title_H.jpg` (copied here).
- **Shared** Seeker / Windows / WebGL. TitleController: “the title text is baked into this art.”
- Fake Continue/Start New/Play Intro are **in the JPG**. Gold uGUI row is the real buttons. Crop/repaint the **shared** JPGs, not WebGL-only.

---

## Blink / R2

- Live URL after 2026-09-19 17:32 UTC: productVersion **`2026.09.19.376181`**.
- Catalog `.bin` HTTP 200, CORS `*`.
- **`catalog.hash` has NO `Cache-Control` header** (Cloudflare default). Heuristic cache is possible. Worth `Cache-Control: no-cache` on the hash object, or a query-bust. Expert was right.

---

## Packet files

| File | Why |
|---|---|
| `JupiterSwapService.cs` | Quote + stub handoff |
| `WalletBridgeStub.cs` | Fake sig in editor; hard-fail release |
| `VFXManager.cs` | Enum `Play(VFXType)` |
| `VFXManager.Hovl.cs` | String `PlayKey` |
| `VFXType.cs` | Enum |
| `RaidGarrisonSpawner.cs` | Atmosphere caller (`PP_GroundFog` / `PP_LightnigStormCloud`) |
| `SolanaWalletAdapterWebGL.cs` + `.jslib` | Existing WebGL adapter (evaluate before greenfield) |
| `com.solana.unity_sdk.package.json` | **1.2.9** |
| `Title_L.jpg` / `Title_H.jpg` | Flattened splash only |

**Missing from packet (does not exist yet):** VFX failure PNG + browser Network/console on the live URL.

---

## Recommended first WebGL cut (owner)

1. **Wallet bridge A** — Jupiter extension via existing Magicblock adapter if it lists Jupiter; else small jslib. Stub gone on WebGL release. Connect reuses title button under `#if UNITY_WEBGL`. No in-game swap plate. Missing extension → install link. Game always playable.
2. **Owner captures VFX break** on live URL (screenshot + Network). Then remap + optional desktop depth.
3. **Splash repaint** of shared Title_L/H (no PSD).
4. **Windows signer deferred.**
5. **Blink:** verify after hard-refresh; add no-cache on `catalog.hash` if Blink persists.
