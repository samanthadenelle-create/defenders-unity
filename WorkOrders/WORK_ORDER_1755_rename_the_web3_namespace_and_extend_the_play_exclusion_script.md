# WORK ORDER 1755 — Rename the `DeNelle.Core.Web3` namespace, and extend the Play build's exclusion script to cover the remaining authored leaks

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, on the owner's ruling this session: *"do the rename and extend the exclusion script"* — after she asked *"can we create a specific one only for play builds?"* and the lead recommended one rename over a Play-only fork.
**Silo:** the `DeNelle.Core.Web3` namespace (3 declarations, 27 referencing files) and `Assets/Editor/GooglePlayContentExclusion.cs`. ⛔ Do NOT touch the compliance gate's scanner itself — that is **WO-1754** (the 64 KiB chunk-seam false positive) and it runs separately.

## The owner's ruling and the reasoning behind it
She asked whether a Play-specific copy could be made. The lead recommended against and she took the recommendation:
- **A Play-only copy of the request signer means two copies that drift**, and the one that drifts is the one signing players' saves. That is the duplicated-state failure CLAUDE.md §2, §5, §8 and §16 each describe in their own words.
- **A build-time script cannot strip a namespace.** The string is baked into IL2CPP metadata by the compiler. Rewriting source at build time would compile something different from what the gate and the suites tested — shipping code nobody reviewed. So the namespace is renamed once, in the tree, for every variant.

## §1 — THE RENAME (proven scope, re-verify before starting)
Read at source 2026-09-15:
- `BackendRequestSigner` is at `Assets/_Modules/Core/Web3/BackendRequestSigner.cs`, owned by **`Assets/_Modules/Core/DeNelle.Core.asmdef`** — the core assembly that ships in every variant. **There is no `Web3` assembly to rename**; the earlier "rename the assembly" framing on the owner's open list was wrong and is retired.
- `namespace DeNelle.Core.Web3` is declared in **3** files; **27** files reference the namespace.

Work:
1. Rename the namespace to **`DeNelle.Core.Backend`** (lead's pick; it describes what the code is — a backend request signer — and carries no wallet connotation). Update the 3 declarations and every `using`.
2. **The FOLDER may stay `Assets/_Modules/Core/Web3/`** — folder names never reach the binary, and moving files churns `.meta` guids for no benefit. If you move them anyway, say why.
3. ⛔ **Do not change behaviour.** Same types, same members, same call sites. A diff that is anything other than namespace/using lines needs a sentence explaining itself.
4. Grep the WHOLE tree afterwards — including `.asmdef` `references`, `.asmref`, any `link.xml`, docs and work orders — for the old string. A namespace referenced from an asmdef or preserved in a `link.xml` that no longer exists fails silently at build time, not compile time.
5. Prove the dApp Store / Solana variant is unaffected: nothing in its path depends on that name. Cite what you checked.

## §2 — EXTEND THE EXCLUSION SCRIPT
`Assets/Editor/GooglePlayContentExclusion.cs` already has the right machinery and it WORKS — `OnPreprocessBuild` quarantines `PlayExcludedAssetPaths` into `Assets/PlayQuarantine` and applies neutral rewrites; `OnPostprocessBuild` restores everything; `ValidateNeutralMirrorEquality` proves the restore was byte-exact. It is what quarantined the Jupiter panel (WO-1741) and neutralised the catalog strings.

Extend it to the remaining **asset-side** offenders the WO-1740 RCA named (read that RCA first — it is appended to `WorkOrders/WORK_ORDER_1740_*.md` as `## RCA 2026-09-15`):
- `Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs` — **a THIRD copy of the catalog.** The sweep neutralised `structures-catalog.json` but this git-tracked generated file embeds the original `_authoringNote` as a C# literal, and its generator is a `[MenuItem]` only, never invoked by the exclusion path. ⚠ It is a `.cs` file, so it is compiled — a neutral rewrite of it changes compiled code. **Prefer fixing the generator and regenerating the file in the tree** over rewriting it at build time; if you rewrite it at build time instead, justify it and prove the restore.
- `Packages/com.solana.unity_sdk/Resources/` — reported by WO-1740/1741 and still **unruled**. Ask, do not assume.
- Any other asset path the RCA's re-measured counts name once WO-1754's seam fix lands. **If WO-1754 has not landed, say so and work from the pre-fix counts, labelled as provisional.**

⛔ **Out of scope for the exclusion script, deliberately:** the owner-ruled un-renamable identifiers (`SolanaDappStore`, `SolanaWallet` — WO-1377, quoted at `PlayMetadataIdentifierRegression.cs:41-51`; `dotr-arena-skr-balance` — `ArenaWalletService.cs:48-50`). Those get a **gate allowlist** in WO-1754, not an exclusion. Excluding an identifier the owner ruled must stay would be the script fighting canon.

## §3 — AUTHORED `skr` LITERALS IN SOURCE
13 live hits, 12 located (`DefenseReportBuilder.cs:439,514,586`; `TowerPlacementRotateMenu.cs:629`, which RENDERS though `BuildModeController.cs:129-131` says the tool is no longer called from placement; others in the RCA). **One at offset 239,196 is untraced and stays recorded as unproven.** For each: is it owner-ruled un-renamable (→ WO-1754 allowlist), dead (→ delete), or an authored string that can simply be neutral (→ change it). Do not bulk-rewrite.

## Acceptance
1. Zero occurrences of `DeNelle.Core.Web3` anywhere in the tree (code, asmdef, link.xml, docs).
2. `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n>` on fresh logs — the lead runs both.
3. A fresh AAB chain run's `PLAY_ARTIFACT_DIRTY` list no longer carries `web3`, and every remaining entry is either fixed here or has a cited WO-1754 allowlist reason.
4. `ValidateNeutralMirrorEquality` still proves an exact restore after a Play build.
5. Lane flips this Status line and writes the `.RESULT.md`.

## What NOT to touch
The compliance scanner (WO-1754), `RaidAssaultAi.cs` / `TroopController.cs` / `HeroHealth.cs` / `SmartMobileCamera.cs` / the raid generator (all landed today, awaiting commit), and any `.unity` scene.
