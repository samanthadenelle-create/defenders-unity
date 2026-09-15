# WO-1741 RESULT — both owner rulings implemented; edit-only, NOT gated

**Status:** DONE
*(Implementation complete and self-verified. **The Unity gate and the AAB run belong to the lead** — see
"What remains UNPROVEN" below.)*
**Date:** 2026-09-15
**Lane:** edit-only. No Unity, no build, no bake, no git — per the brief.

---

## Files changed (5)

| File | Change |
|---|---|
| `Assets/Editor/Regression/GooglePlayPackagingGate.cs` | Two UniTask path entries + the owner-ruling reason prose appended to `FalsePositiveAllowlist`; new `AuthoringOnlyTokens = { "wallet" }`; new `public static bool ContainsForbiddenAuthoringToken(string)` — the single seam the catalog sweep now consumes. |
| `Assets/Editor/GooglePlayContentExclusion.cs` | `Assets/_Modules/Web3/Resources` + `Assets/Resources/Data/Economy/offline-storage.json` added to `PlayExcludedAssetPaths` (with dated `<remarks>` dead-code proofs); `ad-placements.json` row widened to its real mirror pair; `structures-catalog.json` pair added; `ContainsForbiddenAuthoringToken` now delegates to the gate (kills the `" skr"` leading-space defect and the fourth vocabulary); **both** copies of the stale "no StreamingAssets twin" comment corrected. |
| `WorkOrders/WORK_ORDER_1741_play_aab_unitask_allowlist_and_jupiter_quarantine.md` | This WO — both rulings quoted verbatim, Status DONE. |
| `CLI_LANES_WO_NUMBERS.md` | Minted 1741, bumped banner 1741 -> **1742** in the SAME edit. |
| `WorkOrders/WORK_ORDER_1740_play_aab_forbidden_surface_four_leaks.md` | Status READY -> **DONE**; the now-stale `Blocked: YES — needs an owner ruling` line replaced with which questions were ruled (Q1(ii), Q3), which were **not** (Q2, Q4), and that §4's `DeNelle.Core.asmdef` item was rejected as unsafe. No body content rewritten (CLAUDE.md §15: a dated RCA is frozen). |

**No `.json` was edited** — the sweep operates at build time, so these leaks close with list entries, not
content edits. That keeps the lane entirely clear of the binary-edit-only / mirror-sync hazard.
**No scene, no asmdef, no build script touched.**

## Quality gate

```
python tools/gate_brace.py Assets/Editor/GooglePlayContentExclusion.cs Assets/Editor/Regression/GooglePlayPackagingGate.cs
  -> GATE_BRACE_SUMMARY bad=0 of 2   (exit 0)
```

| File | raw `{` | raw `}` | NUL bytes |
|---|---|---|---|
| `GooglePlayContentExclusion.cs` | 144 | 144 | 0 |
| `GooglePlayPackagingGate.cs` | 43 | 43 | 0 |

Raw counts and the gate's own comment/string-aware scanner agree on both files.

## Self-verification performed (no Unity)

1. **PS1 parser survives.** `tools/android/assert-google-play-aab-clean.ps1` parses the gate's arrays out
   of the `.cs` at run time and **fails closed**. Its logic was simulated against the edited file:
   `ForbiddenTokens` 31, `ShortTokensRequiringTextContext` 4, `FalsePositiveAllowlist` 26,
   `MinPrintableRunForShortTokens` 12. The verbatim-string entry parses to
   `com.solana.unity_sdk\runtime\plugins\unitask` — **single** backslashes, identical to the compiled
   value. (A non-verbatim `"...\\..."` literal would have parsed to doubled backslashes and silently
   diverged; that is why it is `@"..."`.)
2. **The standalone PS1 scanner SUPPRESSES with the new entries too, not just parses them.** Parsing and
   matching are different halves, and compiled-vs-script divergence is the exact WO-1364 failure. Read at
   source: `Test-AllowlistedOccurrence` uses the identical
   `windowStart = Hit - allow.Length` / `windowEnd = Hit + Token.Length + allow.Length` shape as the
   compiled `IsAllowlistedOccurrence` — **no hardcoded window**, so a 44-character phrase is handled the
   same as the existing ≤22-character ones. Its chunk carry-over is
   `keep = max(maxTokenLength - 1, 4*MinRun + 128)` = **176** characters, comfortably wider than the
   phrase. No divergence introduced.
3. **Allowlist scope proven, 9/9.** Simulated `MatchesTokenInPayload` + `IsAllowlistedOccurrence` against
   the shipped entries: suppresses both separator forms of the UniTask subpath; still FIRES on
   `SolanaWalletAdapterWebGL`, `Web3AuthSDK`, `codebase/InGameWallet.cs`, the `com.solana.unity_sdk@1.2.9`
   receipt, and on `Solana.Unity.` even inside an allowlisted UniTask path.
4. **Sweep vocabulary widened with zero new build failures.** Walked every string value of all five swept
   catalogs under the new union vocabulary: every token-bearing value is `MAPPED` or `_`-prefixed. The
   *old* vocabulary was walked too, independently reproducing the WO-1740 RCA — exactly
   `storeBalanceBoundIdentity` and `storeBalanceUnavailable` were missed.
5. **Mirror equality.** `cmp` on both new pairs: `structures-catalog.json` 113776 bytes identical;
   `ad-placements.json` 12853 bytes identical.
6. **Dead-code proofs** for both new quarantine entries (§3 / §5 of the WO).

## ⛔ What remains UNPROVEN — the only real proof is the next AAB, and it is the lead's to run

- **Nothing here was compiled.** `COMPILE_GATE_OK` has not been earned. The new
  `GooglePlayPackagingGate.ContainsForbiddenAuthoringToken` is called across an assembly boundary
  (`DeNelle.Editor` -> `DeNelle.EditorRegression`); that reference direction was read at source in both
  `.asmdef` files and the member is `public`, **but only the compiler settles it.**
- **No regression suite was run.** No marker on any log.
- **THE NEXT AAB WILL STILL REPORT `PLAY_ARTIFACT_DIRTY`** — see WO §7. The first-party identifier
  residuals (`DeNelle.Core.Web3`, `SolanaWallet`/`SolanaDappStore`, `dotr-arena-skr-balance`, the
  `CRYSTALS/SKR` literal, the package-provenance blob) are untouched by these rulings and need their own.
  **Do not read a red gate after this change as a failure of this change.**
- **Whether Google's automated review would have flagged any of this is unproven and unprovable from
  here.** The gate is a deliberately conservative self-imposed proxy for Play policy, not a copy of it.
- **The quarantine's Seeker-safety was not exercised.** The restore path is structural
  (`ApplyForDefines` -> `RestoreAll`) and unchanged, but no Android build ran to demonstrate
  `Assets/_Modules/Web3/Resources` coming back. Worth one `git status` check after the first Play build.

## Owner rulings requested (two)

1. **Rename `DeNelle.Core.Web3` / `Assets/_Modules/Core/Web3/` to a neutral name.** WO §6: a define
   constraint on `DeNelle.Core.asmdef` would delete the whole core assembly from the Play player, and the
   Jupiter members there are *already* `#if !GOOGLE_PLAY` and two-side-pinned; `IWalletSigner` /
   `BackendRequestSigner` must keep compiling on Play (save-auth). The residual is the **name**, and only
   a rename removes it.
2. **`Packages/com.solana.unity_sdk/Resources/` — three force-included textures, one of them Solana
   ecosystem branding (`magicblock-logo.png`), shipping into the Play player from a `!GOOGLE_PLAY`
   package.** WO §3a: the identical "asmdef excludes the code, `Resources` ships anyway" class, one
   directory root outside `Assets/`. It does **not** trip the gate today, and quarantining it would need a
   `Packages/` -> `Assets/` move this file has never exercised.

## ⚠ Two follow-ups exist only as PROSE in this WO — they need board rows

Both are recorded in WO-1741 §8: the **rename ruling** above, and the **generalising
`Resources`-under-constrained-asmdef regression** (WO-1740 §4.1, not written here because
`PlayExcludedAssetPaths` is `internal` to `DeNelle.Editor` and the regression assembly cannot see it).
This repo has already paid for exactly this: the banner records that WO-1738 was split out of WO-1737 §7
*"rather than left as prose ... eight days were lost."* The brief authorised **one** WO, so no second
number was minted — but these two will be lost the same way unless the lead mints rows for them.
