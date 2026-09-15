# WORK ORDER 1759 — The LAST AAB offender: the SKR staking/showcase surface is compiled into the Play build, and the 3-char `skr` token also matches inside ordinary .NET identifiers

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, from AAB chain run **16:55:34** — the first run after WO-1754 + WO-1755. That run took the offender list from SIX to **ONE**: `crypto`, `web3`, `solana` and the build receipt are all gone.
**Silo:** the SKR surface (`Assets/_Modules/Core/UI/SkrShowcasePanel.cs` and the SKR staking/catalog code the metadata names) + `Assets/Editor/Regression/GooglePlayPackagingGate.cs`'s short-token rule. ⛔ NOT the chunk-seam scanner logic (WO-1754, landed and working), NOT the exclusion script's existing entries.

## The only remaining offender, verbatim
```
[GooglePlayPackagingGate] PLAY_ARTIFACT_DIRTY:
- content:base/assets/bin/Data/Managed/Metadata/global-metadata.dat token:skr
```

## Measured by the lead from the quarantined artifact
`Builds/Android/rejected/EchoesOfElarion-GooglePlay-20260915-165534.REJECTED.aab`, `global-metadata.dat`, 19,874,336 bytes, **43** raw `skr` occurrences, read as NUL-delimited name-table entries.

**Class A — GENUINE. The gate is right; this is a real crypto surface in a Play artifact.**

| offset | entry |
|---|---|
| 2,199,283 / 2,200,496 | `costSkr`, `_costSkr` |
| 2,349,568 / 2,350,222 | `_headerSkr`, `skrDelta` |
| 2,581,348 / 2,582,639 | `get_SkrPreview`, `SkrPreview` |
| 2,616,199 | `SkrShowcasePanel` |
| 2,647,775 … 2,649,172 | `get_SkrDefault`, `<SkrDefault>k__BackingField`, `SkrDefault`, `get_IsSkr`, `IsSkr`, `Skr` |
| 2,650,836 … 2,651,894 | `stakedSkr`, `activeStakeSkr`, `DemoMockStakeSkr`, `get_RewardBearingStakeSkr`, `SkrBaseUnits`, `RewardBearingStakeSkr` |
| 2,697,485 / 2,697,693 | `NativeSkrPolishBonus`, `NativeSkrPolishBonusBootstrap` |
| 10,752,841 / 10,752,926 / 10,780,482 | `DeNelle.Core.Catalog\|NativeSkrPolishBonus…`, `DeNelle.Core.UI\|SkrShowcasePanel` |
| 10,801,195 | the SOURCE PATH `\Assets\_Modules\Core\UI\SkrShowcasePanel.cs` — the same class the WO-1755 folder move fixed |

SKR is **Solana Mobile's governance token and is not ours** (memory `skr-is-solana-mobile-governance-token`; we only ever read a balance). A Play build carrying a staking showcase, a stake balance and a reward-bearing-stake accessor is exactly the surface this gate exists to stop.

**Class B — FALSE POSITIVES. `skr` is three characters and matches INSIDE ordinary .NET identifiers.**
`VoidTa`**skR**`esult` · `AnyTa`**skR**`equiresNotifyDebuggerOfWaitCompletion` · `Ta`**skR**`eceive` · `ValueTa`**skR**`eceive` · `_userTokenTa`**skR**`esultProperty` · `colorMa`**skR**`tHandle` · `UpdateMa`**skR**`egions` · `skroa` · plus a binary run at 238,770.

This is the **same defect class as `crypto` inside `cryptography`** that WO-1754 just fixed — a short token with no identifier-boundary requirement. `ShortTokensRequiringTextContext` does not help here: these ARE printable identifier runs.

## The work, in this order — and the order is the point
1. **Remove the genuine surface from the Play variant FIRST.** Compile the SKR showcase/staking code out under `!GOOGLE_PLAY`, the way `DeNelle.Web3.asmdef` already does, or move it behind an existing constrained assembly. ⛔ The dApp Store / Solana build MUST keep its SKR surface intact — this is variant-scoped, never a deletion (memory `android-seeker-distribution-and-wallet-strategy`).
2. **Only then** give `skr` an identifier-boundary rule so `TaskResult` and `MaskRegion` stop firing. ⚠ Doing this FIRST would hide the genuine leak behind a gate change — the one thing this gate must never do. A boundary rule is **not** an allowlist entry: `stakedSkr` and `SkrShowcasePanel` must still fire if step 1 is ever reverted, so prove on those exact strings that they do.
3. Extend `PlayGateChunkSeamRegression` (or a sibling) with both halves: `VoidTaskResult` must NOT fire; `stakedSkr`, `SkrShowcasePanel`, `costSkr` MUST fire.

## Acceptance
1. A fresh AAB chain run reaches **`AAB_OK` → `AAB_SIGNING_OK` → `R2_PARITY_OK` → `AAB_SIZE_OK` → `AAB_DONE`** with an empty `PLAY_ARTIFACT_DIRTY`.
2. The Solana/dApp Store variant still compiles the SKR surface — proven, not assumed.
3. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs (the lead runs both).
4. Lane flips this Status line and writes the `.RESULT.md`.
