# WO-1832 RESULT — 2026-09-17, edit-only lane

**Outcome:** IMPLEMENTED. Both offenders of the 2026-09-17 `PLAY_ARTIFACT_DIRTY` rejection are fixed
at their source, and the class of each is closed by a guard that reads the gate's own vocabulary
instead of restating it. **No Unity run, no gate, no build, no commit, no `git` invocation.**

## Files changed (4 `.cs`, 0 JSON, 0 asmdef)

| File | Change |
|---|---|
| `Assets/Editor/GooglePlayContentExclusion.cs` | pricing rails now removed **by name rule** (`:413-415`) off `ContainsForbiddenAuthoringToken`, replacing three hardcoded keys; new build-time post-condition `AssertNoResidualForbiddenKey` (`:489`), called at `:429`, throwing `PLAY_NEUTRAL_RESIDUAL_KEY`; stale doc comment at `:520-527` corrected |
| `Assets/_Modules/Commerce/PackCatalog.cs` | `CurrencyDisclaimer` fallback split per artifact — `#if GOOGLE_PLAY` `:435`, neutral `:436`, authored Seeker sentence `:438` |
| `Assets/Editor/Regression/GooglePlayPackagingRegression.cs` | 5 new source pins + `CheckSweptCatalogKeys` / `WalkKeys` — the headless catalog-key oracle over all five swept catalogs, both mirrors |
| `Assets/Editor/Regression/PlayMetadataIdentifierRegression.cs` | two-sided `Pin` on `"Priced in SKR - token value moves."` (+ `PackCatalogRel`), with the written reason it is a `Pin` and not a `LiteralPin` |

## Quality gate

`python tools/gate_brace.py <4 files>` → `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0.
Raw brace counts 150/150, 59/59, 60/60, 33/33. **NUL bytes: 0 in all four.**

## Evidence (all read/measured this session)

- `Builds/aab-build.log:35029-35031` — the two offenders; `:~35017` — `Build Finished, Result: Success.`
- `Builds/Android/rejected/EchoesOfElarion-GooglePlay-20260917-115237.REJECTED.aab` (456,128,789 B),
  opened as a zip, scanned with the gate's matcher ported verbatim and its four arrays parsed from
  `GooglePlayPackagingGate.cs`:
  - `base/assets/Data/Canonical/packs.json` (40,526 B): **27 live `skr` hits, all `"skrFlat"` keys**,
    offsets 4206 … 40094. No other token live.
  - `base/assets/bin/Data/Managed/Metadata/global-metadata.dat` (19,991,740 B): **exactly 1 live `skr`
    hit**, offset **1,114,669** = `PackCatalog.cs:416`'s sentence. No `solana` / `web3` / `crypto`
    live anywhere — every WO-1741 §7 residual is closed.
- Post-condition safety: ported readable matcher over all five swept catalogs × both mirrors →
  **0 residual keys with the new rule, 27 without it** (negative control bites).
- Catalog oracle coverage: **84 token-bearing keys per mirror, 84 covered** (81 `pricing` rails,
  3 `_schemaNotes` rows whose values also carry tokens).
- `DeNelle.Commerce.asmdef` `"defineConstraints": []` (ships to Play); `DeNelle.Wallet.asmdef`
  `"!GOOGLE_PLAY"` — so all three `CurrencyDisclaimer` consumers (`PackStore.cs:1559`,
  `StoreLegalFooter.cs:104,126`) are absent from a Play player.
- `[JsonProperty("skrFlat")]` / `SkrFlat` **do not and cannot fire** in a binary entry (trailing
  boundary `F`; 3-char run) — so no serialization key moved on either variant.

## Not proven from here

A **clean AAB**. That needs Unity + bundletool (several minutes) and was deliberately not attempted.
Judge it by `R2_PARITY_OK` / the gate marker on a **fresh** log, never an exit code.

## Open items handed back

1. **One owner ruling, non-blocking** — the Play-side disclaimer wording (WO §8). Current value is an
   unobservable placeholder; the Play shelf shows no disclaimer today either way.
2. **Follow-up ticket seed** — the generalised Play-shipping literal sweep, with its two measured
   blockers written down (WO §6). Do not attempt it as a rename of the existing case 4.
