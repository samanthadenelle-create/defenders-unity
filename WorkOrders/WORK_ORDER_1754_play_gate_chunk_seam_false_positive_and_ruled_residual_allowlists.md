# WORK ORDER 1754 — The Play compliance gate's 64 KiB chunk seam truncates its own allowlist window, so `cryptography` reads as `crypto`; plus the owner-ruled residuals that need allowlist entries

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, from the WO-1740 RCA of chain run 14:14:58 (that RCA is appended to `WorkOrders/WORK_ORDER_1740_play_aab_forbidden_surface_four_leaks.md` as `## RCA 2026-09-15` — read it first; it carries the byte evidence for everything below).
**Silo:** `Assets/Editor/Regression/GooglePlayPackagingGate.cs` (`ScanStream`, the allowlist matcher, `:464`) and its PowerShell mirror `assert-google-play-aab-clean.ps1`. ⛔ Do NOT rename any assembly, namespace or identifier in this ticket — §3 below is the owner's ruling, not this lane's work.

## What is PROVEN (RCA, by bytes)
1. **The Unity build SUCCEEDED.** Zero `error CS`; `Build Finished, Result: Success`; a valid 455,022,573-byte artifact. `exit=8` is `run-unity-method.ps1`'s marker-absence code (`:29`, `:256`) — the gate returned false, `AndroidBuild.cs:203` called `EditorApplication.Exit(1)`, so `[AndroidBuild] SUCCEEDED` never printed. **Marker absence and the rejection are ONE event.** A seat reading `AAB_BUILD_MARKER_ABSENT` as a compile failure will chase a build that was never broken.
2. **`token:crypto` is a FALSE POSITIVE, and the mechanism is a gate defect.** The real string is `system.security.cryptography.hmacsha256` — a core .NET name present in every IL2CPP build. Of 119 occurrences, **zero** are live under the gate's own rule (89 already suppressed by the existing `"cryptograph"` allowlist, 30 fail the word boundary); UTF-16 yields zero at both byte parities. The hit exists only because `ScanStream` chunks at 64 KiB and the allowlist window is truncated at the seam: `crypto` @ 1,900,536, the phrase ends at 1,900,547, chunk 29 ends at **1,900,544**.
   ⚠ This is NOT a new discovery — WO-1741 §8 recorded it as a deliberately deferred residual. What is new is the measurement, plus a correction: it fires on `cryptograph`, not on a UniTask path as 1741 predicted.
3. **WO-1741 worked.** None of WO-1740 §2's original offenders (Jupiter panel, `canon-strings` `storeBalance*`, `ad-placements`, `offline-storage`, `structures-catalog`) appear in this run, and its UniTask allowlist suppressed **72 of 76** `solana` hits. WO-1741 §7 is literally titled *"THE NEXT AAB WILL STILL SAY PLAY_ARTIFACT_DIRTY"* and named five of today's six offenders in advance. The gate is doing its job; the remaining work is the seam plus the ruled residuals.

## §1 — THE WORK (this lane)
**Fix the chunk seam. It is item 1 and it comes before every leak**, because until it is fixed no token's live-hit count can be trusted. The RCA is explicit that a tail-only bound leaves it flaky; **all three vectors are required**:
 (a) the truncated tail — a match window that runs past the chunk end;
 (b) a head re-scan of the retained 264 bytes carried into the next chunk;
 (c) `:464`'s `after == text.Length` false boundary, which treats a chunk end as a word end.
Add a regression that feeds a synthetic stream with `cryptography` straddling a 64 KiB boundary at each of the three positions and FAILS on any implementation that misses one.

**Then re-measure every token's live-hit count with the fixed scanner and write the new numbers into this WO.** Do not carry the pre-fix counts forward.

## §2 — ALLOWLIST CANDIDATES (this lane MAY implement, each with a written reason, WO-1741's precedent)
- `cryptograph` — already allowlisted; the seam fix alone should retire the `crypto` hit. Verify, do not widen.
- `SolanaDappStore` / `SolanaWallet` — **already owner-ruled ACCEPTED residuals** (WO-1377, quoted in `PlayMetadataIdentifierRegression.cs:41-51`: they *"may not be RENAMED or REORDERED regardless"*). A gate that rejects an artifact for an identifier the owner has ruled must stay is a gate contradicting canon. Allowlist with that citation.
- `dotr-arena-skr-balance` — `ArenaWalletService.cs:48-50` states it may not be renamed. Same treatment.
- `437e0227…` (Unity's `PerformanceTestRunInfo` build receipt; its one `solana` is inside `"com.solana.unity_sdk@1.2.9"`) — same class as `dependencies.pb`, already skipped at `:388-390`, but its filename is a per-build content hash so it needs a pattern, not a literal.
⚠ **Item 7 cannot be written blind:** these identifiers are NUL-packed name-table entries, so a dotted phrase can never match, and a NUL-bounded one may not survive the PS1 mirror's `'"([^"]*)"'` array parser (`assert-google-play-aab-clean.ps1:56`). Prove the mirror parses whatever shape you choose, or say it cannot.

## §3 — GENUINE LEAKS AND OWNER RULINGS (NOT this lane)
- **`web3` is entirely the namespace `DeNelle.Core.Web3`**, and `BackendRequestSigner` is REQUIRED by the Play identity/save path (`:117`, `:205-212`), so it cannot be excluded. **The only fix is the rename, and that is the owner's open ruling** (WO-1741 §6). Until she rules, no AAB can pass. Report it; do not do it.
- **`skr`, 13 live** — genuine authored literals, 12 located: `DefenseReportBuilder.cs:439,514,586`, `TowerPlacementRotateMenu.cs:629` (which RENDERS, though `BuildModeController.cs:129-131` says the tool is no longer called from placement), and others. **One at offset 239,196 is untraced — recorded unproven.** Removing authored literals is product work; mint it separately once the seam fix gives true counts.
- **New finding: `CatalogFallbackData.g.cs` is a THIRD copy of the catalog.** The sweep neutralised `structures-catalog.json` (log `:9164`) but this git-tracked generated file embeds the original `_authoringNote` as a C# literal, and its generator is a `[MenuItem]` only — hand-run, never invoked by the exclusion path. That is the §2/§5/§16 duplicated-state failure in a new place.

## Acceptance
1. The straddle regression fails on the pre-fix scanner and passes after.
2. A fresh AAB chain run's `PLAY_ARTIFACT_DIRTY` list no longer carries `crypto`, and the remaining offenders are exactly those §3 names.
3. Re-measured live-hit counts written into this WO.
4. Lane flips this Status line and writes the `.RESULT.md`.
