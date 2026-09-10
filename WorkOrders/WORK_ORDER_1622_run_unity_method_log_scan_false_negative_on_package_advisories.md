# WO-1622 - run-unity-method.ps1 returns VERDICT=FAIL reason=LOG_SCAN on a run that PASSED, because the Solana package's WebGL advisories match its `error CS` grep

**Status:** FIXED 2026-09-10 - lead re-judged with the patched runner: Builds/wave1-compile4 now PASS (errorCS=21 underAssets=0 elsewhere=21, set aside with a NOTE), Builds/wave1-compile2 still FAIL (underAssets=14). The WebGL skip is surfaced as the count + NOTE, not a marker field (lead accepted option 2). (was: IMPLEMENTED - awaiting lead review, lane RUNNER)
**RESULT:** `WorkOrders/WORK_ORDER_1622_run_unity_method_log_scan_false_negative_on_package_advisories.RESULT.md`
**Minted:** 2026-09-10 (CLI, main-line banner; bumped 1621 -> 1625 in the SAME edit)
**Silo / Lane:** Tooling / gate runner (`run-unity-method.ps1`) - **isolated, no .cs, no Unity**
**Severity:** P1 process, and the blocking consumer is MEASURED, not assumed:
**`tools/regression/checkin_gate.ps1:296-305`** runs `DeNelle.Editor.CompileGate.Run` through this
runner, captures `$rc = $LASTEXITCODE` (`:299`) and computes
`$compileOk = ($rc -eq 0) -and $marker` (`:303`) - **so a run whose marker it separately confirms
FOUND is still recorded `'Compile gate' 'FAIL'`**, and stages 3-5 of the check-in gate are gated on
it. A gate that cries wolf is a gate that gets worked around, which is the failure CLAUDE.md sec.16
spends a whole section on.
**Type:** EXISTING system. The runner works; its verdict predicate went stale under it.
**Owner words:** none - this is a lane finding, surfaced by the wave1 compile runs.
**Canon note:** CLAUDE.md sec.8 / memory `gates-report-success-without-proving-it` -
**judge by the MARKER on a fresh log, never the exit code.** That rule is what let the wave1 runs be
believed at all. This ticket makes the exit code agree with the marker instead of contradicting it.

---

## 1. The defect, measured (every line and log opened 2026-09-10)

### 1a. Two runs said FAIL while every proof of success was in the same log

`Builds/wave1-compile3.runner.txt` (3390 bytes, mtime 2026-09-09 23:30) and
`Builds/wave1-compile4.runner.txt` (3390 bytes, mtime 2026-09-09 23:44), lines 51-52, verbatim:

```
[run] evidence OK: marker 'COMPILE_GATE_OK' FOUND in a fresh log. marker='COMPILE_GATE_OK' log=D:\EoA\Builds\wave1-compile3 mtime=2026-09-09T23:30:17 (59.7s after run start) sizeBytes=1236434 runStart=2026-09-09T23:29:18
[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors present) marker='COMPILE_GATE_OK' log=D:\EoA\Builds\wave1-compile3 mtime=2026-09-09T23:30:17 (59.7s after run start) sizeBytes=1236434 runStart=2026-09-09T23:29:18
```

**The runner found its own expected marker on a fresh log, then failed the run anyway.** Its own
summary line says why:

```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=True
```

`succeeded=True` AND `compileErrors=True`.

### 1b. Every `error CS` line in that log is a package advisory - all 21 of them

Scanned `D:\EoA\Builds\wave1-compile3` this session with PowerShell `Select-String` (the log is
UTF-16 - memory `unity-logs-are-utf16-read-with-powershell`; do not grep it):

| measurement | value |
|---|---|
| lines matching `error CS\d+` | **21** |
| of those, under `Packages\com.solana.unity_sdk\` | **21** |
| of those, under `Assets\` or `Assets/` | **0** |
| distinct error codes | **CS1069, and only CS1069** |
| lines carrying CompileGate's `(package, advisory)` prefix | **12** |
| lines NOT carrying it (raw Unity compiler emissions, log lines 7432-7434, 9210, 9212, 9214, 9248-9250) | **9** |

All 21 are the same three source positions repeated:
`Packages\com.solana.unity_sdk\Runtime\Plugins\WebGLSupport\WebGLInput\WebGLInput.cs(271,13)`,
`(279,13)` and `(398,13)`, each
`error CS1069: The type name 'WebGLInput' could not be found in the namespace 'UnityEngine'. This
type has been forwarded to assembly 'UnityEngine.WebGLModule' ...`.

The same log also carries, read this session:

```
COMPILE_GATE_OK :: scripts compiled clean
Exiting batchmode successfully now!
```

### 1c. The predicate

`run-unity-method.ps1`:

- `:171` `$succeeded = $false; $compileErr = $false; $license = $false`
- `:173` `$succeeded  = [bool](Select-String -Path $log -Pattern 'Exiting batchmode successfully|terminate with return code 0' -Quiet ...)`
- **`:174` `$compileErr = [bool](Select-String -Path $log -Pattern 'error CS\d+' -Quiet ...)`**
- `:177` prints the summary quoted in sec.1a
- `:233` `if ($succeeded -and -not $compileErr) { ... exit 0 }`
- **`:248` `Write-Verdict "[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors present) $evidence"`** then `:249` `exit 1`

`:174` is a **bare, path-blind `error CS\d+` grep over the whole log**. It cannot tell an
`Assets/` error from a `Packages/` advisory, and it cannot tell a real error from CompileGate's own
echo of one.

### 1d. This is THREE DAYS OLD, not an ancient bug - and that matters

CompileGate's package-advisory split is dated in its own comment,
`Assets/Editor/CompileGate.cs:526-533` (read at source):

> *"---- package-only errors are ADVISORY, not a verdict (cg-wave10, 2026-09-07) ----  First live
> run: the only errors were Packages\com.solana.unity_sdk\...\WebGLInput.cs CS1069 ... Errors under
> Assets/ - the `#if UNITY_WEBGL` code this pass exists to catch (WebTrace.cs:325 CS1501) - still
> FAIL. ... until then the package lines are printed under their own marker so their presence stays
> visible on the log."*

So on **2026-09-07** CompileGate started deliberately PRINTING package `error CS` lines that it had
decided are not a verdict. `run-unity-method.ps1:174` has grepped for `error CS` since long before
that. **The gate got smarter and told the runner nothing, and the runner has been calling every
clean compile a failure ever since.** This is the identical duplicated-state failure CLAUDE.md
sec.2, sec.5, sec.8 and sec.16 each describe: two places deciding the same question, one of them
moving.

### 1e. THE BRIEF'S FIRST FIX OPTION IS DISPROVEN BY THE MEASUREMENT - do not implement it

The obvious fix - *"exempt the lines CompileGate tagged `(package, advisory)`"* - **does not work**,
and sec.1b is the proof: only **12 of the 21** matches carry that tag. The other **9 are the raw
Unity compiler emissions** that CompileGate is quoting. Exempting the tagged lines leaves 9 matches
for `:174`, `$compileErr` stays `$true`, and the verdict is still FAIL. Recorded here so the lane
does not spend a cycle discovering it (CLAUDE.md sec.11B).

## 2. Target - what "fixed" means

`run-unity-method.ps1`'s exit code agrees with the marker it just proved. A run that produced
`COMPILE_GATE_OK` on a fresh log and `Exiting batchmode successfully now!` exits 0. A run with even
one `error CS` under `Assets/` still exits non-zero, loudly.

## 3. Architecture ruling

**One owner per concern.** "Is this error line a verdict?" already has an owner and it is
`CompileGate`, at `Assets/Editor/CompileGate.cs:772-785` (`SplitPackageErrors`, read at source
2026-09-10), whose criterion is: normalise `\` to `/`, then a line is a package line if it
`StartsWith("Packages/")` or `StartsWith("Library/PackageCache/")` or contains `/Packages/` or
`/PackageCache/`; everything else is `mine`.

**The runner must not grow a second copy of that criterion.** The sanctioned shape, in order of
preference:

1. **Read CompileGate's verdict.** The gate already emits `COMPILE_GATE_WEBGL_ADVISORY` (a warning
   marker, `CompileGate.cs:543-546`) for the advisory bucket and a distinct FAIL marker for the real
   one. When the expected marker was FOUND on a fresh log AND the advisory marker is present, the
   gate has already ruled. Prefer this - it is zero duplicated state.
   **Confirmed present in `Builds/wave1-compile3` this session, verbatim - and read the SECOND
   sentence, it matters:**
   ```
   COMPILE_GATE_WEBGL_ADVISORY :: 12 error line(s) in Packages/ ignored by the WebGL pass (module reference gap, WO-1575)
   COMPILE_GATE_WEBGL_SKIPPED reason=package-reference-gap :: the WebGL pass stopped on 12 Packages/ error line(s) before producing an assembly (22.7s); the tree's own #if UNITY_WEBGL code was NOT judged this run - see COMPILE_GATE_WEBGL_ADVISORY above.
   ```
   WARNING: So the gate's ruling on the WebGL pass is **SKIPPED, not clean.** That is still a legitimate
   basis for `exit 0` here - the ACTIVE-TARGET compile did pass, which is what `COMPILE_GATE_OK`
   asserts - **but the runner must not print a bare PASS that implies the WebGL pass was judged.**
   Surface the skip in the verdict line (a `webgl=skipped` field, or a distinct reason value).
   Silently turning a skip into a green is the exact class of defect memory
   `gates-report-success-without-proving-it` names.
2. **Path-scope the runner's own grep** so it only reds on `Assets/` (or `Assets\`) paths - a
   narrowing of `:174`, not a whitelist of strings.

**Never widen the exemption to `Assets/`.** An `error CS` under `Assets/` is the one thing this
predicate exists to catch, and CompileGate's own comment (`:531-532`) names the live example -
`WebTrace.cs:325 CS1501`.

**Whatever criterion lands, it is authored ONCE in `run-unity-method.ps1`** and not copy-pasted into
`morning-ship-chain.ps1`, `overnight-apk-build.ps1`, `checkin_gate.ps1` or any other chain. CLAUDE.md
sec.16 is the whole story of what happens when a gate predicate gets inlined into two chains.

## 4. Blast radius - who consumes this exit code

Grepped 2026-09-10 for `run-unity-method` across `*.ps1` (excluding `./tmp`): **`distribute-android.ps1`,
`google-play-aab-build.ps1`, `install-apk-to-seeker.ps1`, `morning-ship-chain.ps1`,
`overnight-apk-build.ps1`, `run-autopilot-fleet.ps1`, `run-tests.ps1`, `tools/command-centre.ps1`,
`tools/localization/run-localization-overnight.ps1`, `tools/regression/checkin_gate.ps1`,
`tools/run-unity-playmode.ps1`.**

**The one caller PROVEN to be hit is `tools/regression/checkin_gate.ps1:296-305`** (read at source
2026-09-10), which invokes `DeNelle.Editor.CompileGate.Run` via this runner with
`-ExpectMarker 'COMPILE_GATE_OK'`, then:

```
$rc = $LASTEXITCODE
...
$marker = [bool](Select-String -Path $clog -Pattern 'COMPILE_GATE_OK' -Quiet)
$compileOk = ($rc -eq 0) -and $marker
if ($compileOk) { Add-Result 'Compile gate' 'PASS' 'COMPILE_GATE_OK' }
else { Add-Result 'Compile gate' 'FAIL' "exit $rc, marker=$marker" }
```

`checkin_gate.ps1:10-21` documents that stages 2 through 5 all route through this runner.

> WARNING: **`morning-ship-chain.ps1` IS NOT PROVEN TO BE AFFECTED, AND AN EARLIER DRAFT OF THIS TICKET
> SAID IT WAS.** Its `$LASTEXITCODE -ne 0` block at `:111` guards a **`DeNelle.Editor.AndroidBuild.BuildSeekerApk`**
> call (`:108-110`), not `CompileGate.Run` - and `Builds/apk-build.log`, scanned this session,
> carries **ZERO** `error CS` lines. The 21 advisories are emitted by CompileGate's own **WebGL
> pass**, which an APK build does not run. So the chain does check this exit code, but which of its
> runs actually carry advisory lines is **for the lane to MEASURE, not to assume** (CLAUDE.md
> sec.11B). Same for the other nine callers found by `grep -rln "run-unity-method" --include=*.ps1 .`
> (excluding `./tmp`): `distribute-android.ps1`, `google-play-aab-build.ps1`,
> `install-apk-to-seeker.ps1`, `overnight-apk-build.ps1`, `run-autopilot-fleet.ps1`, `run-tests.ps1`,
> `tools/command-centre.ps1`, `tools/localization/run-localization-overnight.ps1`,
> `tools/run-unity-playmode.ps1`. **List each caller's measured behaviour in the RESULT** - a fix
> that flips exit 1 to exit 0 changes control flow wherever it actually fires.

## 5. Pins - what must not move

- **`:173`'s clean-exit test.** `succeeded` is the other half of the predicate and it is correct.
- **The LICENSE_ERROR branch (`:241`) and its `exit 7` (`:246`),** plus the comment at `:230-232`
  recording that a transient 505 handshake line appears on fully successful runs. Load-bearing and
  unrelated.
- **`$ExpectMarker` handling and the `PASS-UNASSERTED` verdict (`:235`).** A run with no expected
  marker must still be distinguishable from a run whose marker was checked. `checkin_gate.ps1:85`
  parses that literal.
- **`Write-Verdict`'s output shape.** The `[run] VERDICT=` line is parsed by other scripts and by
  every seat's eyes. Keep the prefix and the `reason=` field; add a new reason value if needed.
- **CompileGate itself.** This is a **.ps1-only lane**. Do not edit `Assets/Editor/CompileGate.cs`,
  do not "fix" the Solana CS1069 advisories, and do not touch the WO-1575 module-reference follow-up
  the gate's comment names.

## 6. RED-first suite spec

> **THERE IS NO POWERSHELL TEST HARNESS IN THIS REPO, AND THAT IS A FINDING, NOT A GAP TO PAPER
> OVER.** Listed 2026-09-10: `tools/regression/` holds `MANUAL_QA_CHECKLIST.md`, `README.md`,
> `__pycache__/`, `checkin_gate.ps1`, `static_gate.py`, `static_gate.sh` - **no test runner, no
> Pester, no `tools/tests/`**. A C# regression under `Assets/Editor/Regression/` **cannot execute a
> `.ps1`**, so the existing oracle family is the wrong instrument here.

Spec the RED-first proof as a **fixture pair driven by the runner's own verdict function**, checked
in beside the script:

- **Fixture A - the real world.** A trimmed copy of `Builds/wave1-compile3` (keep the
  `COMPILE_GATE_OK` line, the `Exiting batchmode successfully now!` line, and all 21 `error CS`
  lines verbatim, **preserving the UTF-16 encoding** - a UTF-8 rewrite makes the fixture test
  something the runner never sees). Expected verdict: **PASS**.
  **RED at HEAD:** today this fixture yields `VERDICT=FAIL reason=LOG_SCAN`.
- **Fixture B - the thing the predicate exists to catch.** Fixture A plus ONE seeded line,
  `Assets\_Modules\Core\Whatever.cs(1,1): error CS0000: seeded by WO-1622 fixture B`. Expected
  verdict: **FAIL**.
  **This fixture is not optional.** Memory `prove-the-success-path-not-just-the-refusal`: a
  failure-only acceptance once shipped a guard that aborted every good run while exiting 0. Prove
  BOTH directions.
- **Fixture C - the advisory-free case.** A log with no `error CS` at all still passes, so the fix
  did not accidentally make the marker the only test.

Running the pair requires the verdict logic to be reachable without launching Unity. If that means
extracting the scan into a function the script dot-sources, **say so and hand it back** - it is a
structural change to a gate script and is lead-ruled, not lane-taken. **If no harness lands, the
RESULT states plainly that no automated harness exists and records the three fixture runs as
manually executed, with their verbatim `[run] VERDICT=` lines pasted in** (CLAUDE.md sec.11B: an
unproven thing named as unproven is useful).

## 7. Not in scope

- Do **not** edit any `.cs` file. `CompileGate.cs` is READ-ONLY for this lane.
- Do **not** attempt to resolve the Solana `CS1069` WebGL module-reference gap - `CompileGate.cs:533`
  names that as the WO-1575 follow-up.
- Do **not** re-inline the fixed predicate into `morning-ship-chain.ps1`, `overnight-apk-build.ps1`
  or `checkin_gate.ps1` (CLAUDE.md sec.16).
- Do **not** touch the R2 push/verify path, `tools/r2-ship.ps1`, or `.githooks/pre-push`.
- Do **not** "fix" `checkin_gate.ps1`'s separate PowerShell 5.1 parse history (CLAUDE.md sec.8) -
  different ticket.
- Do **not** run Unity, gate, or commit from the lane. Edit-only; the lead holds the Unity lock and
  is the sole committer.
