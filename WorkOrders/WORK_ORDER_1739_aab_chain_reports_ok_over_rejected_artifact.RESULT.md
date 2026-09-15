# WO-1739 RESULT — the AAB chain can no longer report success over a rejected artifact

**Status:** DONE (script fix + RCA). **The content leak is NOT fixed — see WO-1740.**
**Date:** 2026-09-15
**Files changed:** `google-play-aab-build.ps1` only. **No `Assets/` file was touched.**

---

## What was changed

`google-play-aab-build.ps1`:

1. The two post-Unity failure branches (`$LASTEXITCODE -ne 0`, and the `catch` -> `AAB_THREW`) now set
   `$buildProven = $false` instead of only logging and falling through.
2. A new verdict block takes its answer **from the log, not the exit code** (`CLAUDE.md` §8; memory
   `gates-report-success-without-proving-it`). The run is proven only if the log postdates `$startedAt`,
   carries `[AndroidBuild] SUCCEEDED`, and carries **none** of `PLAY_ARTIFACT_REJECTED`,
   `PLAY_ARTIFACT_DIRTY`, `PLAY_ARTIFACT_MISSING`, `PLAY_SOURCE_ISOLATION_FAIL`,
   `PLAY_NEUTRAL_UNMAPPED_TOKEN`, `PLAY_NEUTRAL_REWRITE_FAIL`. Marker **absence is a failure, not an
   unknown**. The `$LASTEXITCODE` test is kept because an unset `$LASTEXITCODE` is `$null` and
   `$null -ne 0` is TRUE — it fails closed (memory `prove-the-success-path`).
3. On failure: `AAB_REJECTED`, the gate's `PLAY_ARTIFACT_DIRTY` bullet list copied into the status file so
   it names the offending entries and tokens, the artifact **moved** to
   `Builds\Android\rejected\EchoesOfElarion-GooglePlay-<stamp>.REJECTED.aab`, then `AAB_DONE` and
   **exit 7** — before `tools\r2-ship.ps1` and before the size guard.
4. Header updated: markers `AAB_REJECTED` / `AAB_BUILD_UNPROVEN` / `AAB_QUARANTINED`, exit code 7.

## Verification performed

- `System.Management.Automation.Language.Parser::ParseFile` -> **0 parse errors** under Windows
  PowerShell 5.1.
- **0 non-ASCII bytes** in the file (the header requires ASCII-only: PS 5.1 reads BOM-less files as ANSI).
- `tools\r2-ship.ps1` delegation unchanged — the push/verify pair is still the ONE file (§16), simply not
  reached on a rejected run.
- No `.cs` touched, so `tools/gate_brace.py` and the NUL guard do not apply to this lane.

**Not verified, and deliberately so:** the script was not executed. Doing so means a 15-minute Unity AAB
build, which this lane was instructed not to run. The rejection path is proven by construction and by
parse, not by a live run. **A live run of the new path has NOT been observed** — the first real Play AAB
attempt will be its proof.

## The finding that matters more than the fix

**This was occurrence TWO.** `Builds/aab-final-chain-runner.log` (2026-09-11 18:43-19:00) shows the
identical `run-unity-method exit=8` -> `AAB_OK` -> `AAB_SIGNING_OK` -> `AAB_SIZE_OK` -> exit 0 sequence,
four days before the run that prompted this ticket. `run-unity-method.ps1` printed
`VERDICT=FAIL reason=MARKER_ABSENT - this run is NOT PROVEN` both times. The wrapper discarded a correct
verdict, twice.

**And it was occurrences THREE and FOUR as well — this is now PROVEN, not inferred.** Both
`Builds/Android/store/EchoesOfElarion-GooglePlay-2026.09.07.359670.aab` and `...359746.aab` were opened as
zips and scanned: **each carries every forbidden surface the 09-15 run was rejected for**, including
`JupiterSwapPanel`, `Powered by Jupiter`, `SKR: unavailable in this build`, `live on the Solana dApp Store`
and `SKR on-ramp`. `JupiterSwapPanel.uxml` dates to 2026-05-26 (`5d13d5b3b`), so it predates every AAB in
the tree.

⛔ **No AAB in this repository has ever passed the artifact gate**, and no log in `Builds/` contains
`PLAY_ARTIFACT_CLEAN_OK`. The Play lane has been structurally red since the gate was hardened on
2026-09-04 (`6979fb961`), and this reporting defect hid every instance. **The two 09-07 store AABs must not
be uploaded to Google Play.**

**"Why now" is therefore: it is NOT now.** Neither candidate in the brief is the cause. Today's commits
touched none of the offending files. The new `*_IronBastion` scenes are not implicated — not one entry in
the `PLAY_ARTIFACT_DIRTY` list is a `level*`/`sharedassets*` scene payload; every hit is
`Resources`/`StreamingAssets`/IL2CPP metadata. The only thing that changed today is that someone read the
log instead of the status file.

## Sibling script

`overnight-apk-build.ps1:72-77` has the **same defect shape** — `throw` caught by its own `catch` into
`APK_THREW`, then falls through to `APK_OK`. It was **not** changed in this lane (per the brief). Its blast
radius is smaller: the Seeker APK lane has no packaging gate (that build is *supposed* to carry the
wallet), and its freshness check does catch the "no artifact produced" case. The residual hole is a fresh
APK produced by a run whose `SUCCEEDED` marker never appeared. Worth its own ticket; not this one.

## Still open

**WO-1740** — the four actual content leaks, headed by
`Assets/_Modules/Web3/Resources/JupiterSwapPanel.uxml` ("Powered by Jupiter Aggregator") shipping in the
Play AAB. **BLOCKED ON AN OWNER RULING**: four questions, chief among them whether the vendored-UniTask
`solana` directory-name tokens in `global-metadata.dat` are allowlisted, un-vendored, or accepted as a
hard block on Play shipping at all.
