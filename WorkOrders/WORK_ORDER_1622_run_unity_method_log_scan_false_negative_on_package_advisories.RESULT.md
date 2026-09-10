# WO-1622 RESULT - run-unity-method.ps1 log scan is now scoped to `Assets/`

**Status:** IMPLEMENTED - awaiting lead review (lane RUNNER 2026-09-10)
**Lane:** Tooling / gate runner. PowerShell only. **No `.cs` touched. No Unity launched. No commit.**
**Files changed:** `run-unity-method.ps1` (repo root) - the ONLY source file changed.
**Files NOT changed:** `tools/regression/checkin_gate.ps1` (needs no edit - see sec.4),
`Assets/Editor/CompileGate.cs` (read-only for this lane), every calling chain.

---

## 1. What changed

`run-unity-method.ps1`, the log-judgement block plus the three verdict lines.

**Post-edit line numbers, read back at source 2026-09-10** (`Select-String` over the edited file -
not computed):
```
182 # --- judge success from the log ---
183 $succeeded = $false; $compileErr = $false; $license = $false
190     $succeeded  = [bool](Select-String ... 'Exiting batchmode successfully|terminate with return code 0' ...)   <- PIN, untouched
200     $license     = [bool](Select-String ... handshake/505 ...)                                                  <- PIN, untouched
202 $errCsOther = $errCsTotal - $errCsAssets
203 $errScan    = "errorCS=$errCsTotal underAssets=$errCsAssets elsewhere=$errCsOther"
206 if ($errCsAssets -gt 0) { Write-Host "[run] first Assets/ compile error: $firstAssetsErr" }
277 Write-Verdict "[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors under Assets/) $evidence $errScan"
278 if ($errCsAssets -gt 0) { Write-Host "[run] LOG_SCAN first Assets/ compile error: $firstAssetsErr" }
```
(The HEAD line numbers quoted elsewhere in this RESULT - `:171-177`, `:174`, `:84`, `:207`, `:235` -
are the WO's own, i.e. pre-edit.)

**Before (HEAD, `:174`):**
```powershell
$compileErr = [bool](Select-String -Path $log -Pattern 'error CS\d+' -Quiet -ErrorAction SilentlyContinue)
```

**After** - the scan is bound to the error's OWN source-path token, so only a compiler error whose
file lives under `Assets/` is the tree's verdict:
```powershell
$errCsLines  = @(Select-String -Path $log -Pattern 'error CS\d+' -ErrorAction SilentlyContinue)
$errCsTotal  = $errCsLines.Count
$assetsHits  = @($errCsLines | Where-Object { $_.Line -match 'Assets[\\/][^:\r\n]*\(\d+,\d+\):\s*error CS\d+' })
$errCsAssets = $assetsHits.Count
$compileErr  = ($errCsAssets -gt 0)
```

This is **WO sec.3 option 2** (path-scope the runner's own grep), not option 1. Reasons, stated for
the lead:

- Option 1 requires this runner to carry the compile gate's WebGL sub-marker string literals. That is
  a **new marker string in a file that does not carry it**, which the lane brief forbids, and it
  couples a *generic* runner (it also drives AndroidBuild, capture, autopilot, playmode) to one
  method's private sub-markers.
- Option 2 is generic and covers every caller.
- **The `Packages/` + `Library/PackageCache/` criterion from `CompileGate.SplitPackageErrors`
  (`Assets/Editor/CompileGate.cs:772-785`, read at source 2026-09-10) was deliberately NOT copied.**
  The runner asks the inverse, narrower question - "is this error under `Assets/`?" - so there is no
  second copy of the gate's criterion to drift (WO sec.3).

**Honesty of the verdict line (WO sec.3's "do not silently turn a skip into a green").** Because the
advisory/skip marker literals are off-limits here, the same intent is served **generically, by
count**: every `VERDICT=` line now carries `errorCS=<total> underAssets=<n> elsewhere=<m>`, and a
run with `elsewhere > 0` prints an explicit `NOTE:` line saying those lines were set aside and are
still in the log. A reader of a PASS line can never conclude "there were no error CS lines".
**Open question for the lead:** the WebGL skip is surfaced as a COUNT, not as the `webgl=skipped`
field WO sec.3 option 1 names. If the lead wants the literal marker field instead, say so and it is a
two-line change.

### Pins honoured (WO sec.5)
- `:173` clean-exit test - **untouched** (now `:191`).
- LICENSE_ERROR branch + `exit 7` + the 505 comment - **untouched**.
- `$ExpectMarker` handling and `VERDICT=PASS-UNASSERTED` - **untouched** as a token; the `$errScan`
  suffix is appended after the existing text, so `Write-Verdict`'s own
  `VERDICT=PASS(?!-UNASSERTED)` regex (`:84` at HEAD) still classifies correctly.
- `Write-Verdict` output shape - prefix `[run] VERDICT=` and the `reason=` field kept. **No new
  reason value was introduced**; `reason=LOG_SCAN` is retained, with its parenthetical corrected from
  "compile errors present" to "compile errors under Assets/".

### The `[run] wrapperExit=... compileErrors=` summary line has NO external parser
Its tail gained ` ($errScan)`, so the lane grepped for anyone binding to it. Searched 67 `.ps1` files
(repo root + `tools/**` + `.claude/skills/**`, `.claude/worktrees/` excluded) for
`compileErrors=|wrapperExit=` 2026-09-10. **Four hits, all of them scripts EMITTING their own
summary line, none parsing this one:** `build-webgl.ps1:163`, `build-windows.ps1:132`,
`run-tests.ps1:94`, `tools/run-unity-playmode.ps1:104`. Safe to append.

### Boundary of what was measured
The `Assets[\\/]...\(\d+,\d+\):\s*error CS\d+` binding would also red on a hypothetical
`Packages/com.x/Assets/Foo.cs(1,1): error CSnnnn`. **No such line exists in any of the 192 logs
scanned**, so this is the edge of the measurement, not an observed defect. If one ever appears the
narrowing is a one-token change (anchor on `(^|[\s"'])Assets[\\/]`).

### PowerShell 5.1 compatibility
No ternary, no `??`, no `&&`. Verified by parsing the file with the **5.1** parser:
```
PS> [System.Management.Automation.Language.Parser]::ParseFile('D:\EoA\run-unity-method.ps1', [ref]$null, [ref]$err)
PARSE CLEAN (5.1.26100.9444)
nonAscii=0        # the file's ASCII-only rule (header :11-12) is preserved
```

### Known narrowing - stated plainly, not papered over
A **pathless** `error CS` line (e.g. `CS0006` metadata-file-not-found, which names an assembly rather
than a source position) no longer reds on its own. The backstop is `:191`'s clean-exit test: Unity
aborts batchmode rather than printing `Exiting batchmode successfully` when the tree itself fails to
compile. This is a real, deliberate reduction in coverage and the lead should know it.

---

## 2. Proof - RED first, then GREEN

### 2a. There IS a harness, and it is already in the file - `-JudgeExistingLog`

**WO sec.6's premise ("no PowerShell test harness exists") is true of `tools/regression/`, but the
runner ships its own.** `run-unity-method.ps1:38-42` + `:104-112` (HEAD line numbers) define
**JUDGE-ONLY MODE: no Unity is launched**, and the script then falls through to the exact same
evidence gate and verdict code path as a real run. **No function extraction and no structural change
were needed**, so the WO's hand-back condition did not trigger. Every run below is judge-only; **the
lane launched no Unity process** (a WebGL build was running on this machine throughout).

Fixtures were written to **`Builds/`, which is gitignored** (`git check-ignore -v` -> `.gitignore:8:/[Bb]uilds/`),
so nothing new enters the tree. `tools/regression/fixtures/` was NOT used because `.gitignore:361:*.log`
also swallows `.log` there and the lane's scope is PowerShell-only. Fixture paths:
`Builds/wo1622-fixtureA.txt`, `Builds/wo1622-fixtureB.txt`, `Builds/wo1622-fixtureC.txt`.

**Encoding finding, contradicting WO sec.6.** WO sec.6 requires the fixture to preserve UTF-16
"because the log is UTF-16". **Measured 2026-09-10 - it is not.** First 16 bytes of
`Builds/wave1-compile3`:
```
5B 4C 69 63 65 6E 73 69 6E 67 3A 3A 4D 6F 64 75      ("[Licensing::Modu")
nulsInFirst2000=0
```
BOM-less single-byte text. A UTF-16 fixture would have been the thing the runner never sees - the
exact failure mode WO sec.6 warns about, inverted. The fixtures are BOM-less UTF-8, matching the real
log byte-for-byte in encoding.

### 2b. Fixture construction (exact command shape)

```powershell
$src = Get-Content 'Builds\wave1-compile3'                     # 9846 lines
$keep = @('[WO-1622 FIXTURE A] ...')
foreach ($l in $src) {
  if ($l -match 'error CS\d+' -or $l -match 'COMPILE_GATE_OK' -or
      $l -match 'Exiting batchmode successfully' -or $l -match 'COMPILE_GATE_WEBGL') { $keep += $l }
}
# padded with error-free filler to clear -MinLogBytes (default 1024)
[System.IO.File]::WriteAllLines(..., $keep, (New-Object System.Text.UTF8Encoding($false)))
# B = A + 'Assets\_Modules\Core\Whatever.cs(1,1): error CS0000: seeded by WO-1622 fixture B'
# C = A minus every 'error CS' line
```
Resulting shapes (measured):
```
A : bytes=9115 errorCSlines=21 markerPresent=True cleanExit=True
B : bytes=9197 errorCSlines=22 markerPresent=True cleanExit=True
C : bytes=2053 errorCSlines=0  markerPresent=True cleanExit=True
```

### 2c. Command used for every probe

```powershell
powershell -ExecutionPolicy Bypass -File .\run-unity-method.ps1 `
  -Method '<probe-name>' -LogName '<log>' -ExpectMarker 'COMPILE_GATE_OK' `
  -JudgeExistingLog '<runStart>'
```
`-Method` is a fixture-only name so `Write-Verdict`'s ops-channel post (`:81-88`) cannot be mistaken
for a real gate run. `runStart` for the two real logs was taken from their own runner transcripts
(`Builds/wave1-compile3.runner.txt:51`, `Builds/wave1-compile4.runner.txt:51`); fixtures use
`2000-01-01T00:00:00` so the staleness check (`:207` HEAD) passes on any checkout mtime.

### 2d. RED - at HEAD, before the edit (verbatim)

Real log, `Builds/wave1-compile3`, `-JudgeExistingLog '2026-09-09T23:29:18'`:
```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=True
[run] evidence OK: marker 'COMPILE_GATE_OK' FOUND in a fresh log. marker='COMPILE_GATE_OK' log=D:\EoA\Builds\wave1-compile3 mtime=2026-09-09T23:30:17 (59.7s after run start) sizeBytes=1236434 runStart=2026-09-09T23:29:18
[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors present) marker='COMPILE_GATE_OK' log=D:\EoA\Builds\wave1-compile3 mtime=2026-09-09T23:30:17 (59.7s after run start) sizeBytes=1236434 runStart=2026-09-09T23:29:18
EXITCODE=1
```
Real log, `Builds/wave1-compile4`, `-JudgeExistingLog '2026-09-09T23:43:25'`:
```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=True
[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors present) marker='COMPILE_GATE_OK' log=D:\EoA\Builds\wave1-compile4 mtime=2026-09-09T23:44:28 (63.0s after run start) sizeBytes=1333485 runStart=2026-09-09T23:43:25
EXITCODE=1
```
Fixtures at HEAD:
```
FIXTURE A -> VERDICT=FAIL reason=LOG_SCAN ... sizeBytes=9115   EXITCODE=1   <-- RED (expected PASS)
FIXTURE B -> VERDICT=FAIL reason=LOG_SCAN ... sizeBytes=9197   EXITCODE=1   (correct)
FIXTURE C -> VERDICT=PASS marker='COMPILE_GATE_OK' FOUND ...   EXITCODE=0   (correct)
```

### 2e. GREEN - after the edit (verbatim)

`Builds/wave1-compile3`:
```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=False (errorCS=21 underAssets=0 elsewhere=21)
[run] NOTE: 21 'error CS' line(s) outside Assets/ were SET ASIDE - they are not this tree's verdict (WO-1622). They are still in the log; read them there.
[run] VERDICT=PASS marker='COMPILE_GATE_OK' FOUND log=D:\EoA\Builds\wave1-compile3 mtime=2026-09-09T23:30:17 sizeBytes=1236434 errorCS=21 underAssets=0 elsewhere=21
EXITCODE=0
```
`Builds/wave1-compile4`:
```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=False (errorCS=21 underAssets=0 elsewhere=21)
[run] NOTE: 21 'error CS' line(s) outside Assets/ were SET ASIDE - they are not this tree's verdict (WO-1622). They are still in the log; read them there.
[run] VERDICT=PASS marker='COMPILE_GATE_OK' FOUND log=D:\EoA\Builds\wave1-compile4 mtime=2026-09-09T23:44:28 sizeBytes=1333485 errorCS=21 underAssets=0 elsewhere=21
EXITCODE=0
```
Fixture A (the RED case, now green):
```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=False (errorCS=21 underAssets=0 elsewhere=21)
[run] NOTE: 21 'error CS' line(s) outside Assets/ were SET ASIDE - they are not this tree's verdict (WO-1622). They are still in the log; read them there.
[run] VERDICT=PASS marker='COMPILE_GATE_OK' FOUND log=D:\EoA\Builds\wo1622-fixtureA.txt mtime=2026-09-10T00:54:11 sizeBytes=9115 errorCS=21 underAssets=0 elsewhere=21
EXITCODE=0
```
Fixture B (the seeded `Assets/` error - **must still fail**):
```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=True (errorCS=22 underAssets=1 elsewhere=21)
[run] first Assets/ compile error: Assets\_Modules\Core\Whatever.cs(1,1): error CS0000: seeded by WO-1622 fixture B
[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors under Assets/) marker='COMPILE_GATE_OK' log=D:\EoA\Builds\wo1622-fixtureB.txt ... sizeBytes=9197 runStart=2000-01-01T00:00:00 errorCS=22 underAssets=1 elsewhere=21
[run] LOG_SCAN first Assets/ compile error: Assets\_Modules\Core\Whatever.cs(1,1): error CS0000: seeded by WO-1622 fixture B
EXITCODE=1
```
Fixture C (no `error CS` at all - marker did not become the only test):
```
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=False (errorCS=0 underAssets=0 elsewhere=0)
[run] VERDICT=PASS marker='COMPILE_GATE_OK' FOUND log=D:\EoA\Builds\wo1622-fixtureC.txt mtime=2026-09-10T00:54:11 sizeBytes=2053 errorCS=0 underAssets=0 elsewhere=0
EXITCODE=0
```

### 2f. Negative controls on TWO REAL logs that carry REAL `Assets/` errors

Not synthetic - these are historical failing runs still on disk. Both must stay red, and do:
```
Builds/wave1-compile2 :
[run] wrapperExit=0 timedOut=False succeeded=True license=False compileErrors=True (errorCS=35 underAssets=14 elsewhere=21)
[run] first Assets/ compile error: Assets\_Modules\Core\Addressables\OfflineContentService.cs(980,30): error CS0103: The name 'Caching' does not exist in the current context
[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors under Assets/) ... errorCS=35 underAssets=14 elsewhere=21
EXITCODE=1

Builds/localization-font-build.log :
[run] wrapperExit=0 timedOut=False succeeded=False license=False compileErrors=True (errorCS=3 underAssets=3 elsewhere=0)
[run] first Assets/ compile error: Assets\Editor\Regression\GlyphCoverageRegression.cs(84,52): error CS1503: Argument 1: cannot convert from 'uint' to 'char'
[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors under Assets/) ... errorCS=3 underAssets=3 elsewhere=0
EXITCODE=1
```
`wave1-compile2` is the strongest single case: **21 package advisories AND 14 real `Assets/` errors in
one log**, and the new predicate correctly reds it while greening its advisory-only successors
compile3/compile4.

---

## 3. Blast radius - MEASURED, not assumed (WO sec.4)

### 3a. Which runs actually trip the old predicate

Rather than reason about which methods emit advisories, the lane scanned **all 192 files in `Builds/`**
larger than 1 KiB for `error CS` lines (2026-09-10). **21 files match.** Of those:

| bucket | files | meaning under the fix |
|---|---|---|
| `error CS` present, **0 under `Assets/`** (all 21 lines under `Packages/`) | **19** | were FALSE FAILs at HEAD; now PASS |
| real `Assets/` errors only (`localization-font-build.log`, 3) | **1** | FAIL at HEAD, **FAIL after** - unchanged |
| both (`wave1-compile2`: 21 package + 14 Assets) | **1** | FAIL at HEAD, **FAIL after** - unchanged |

The 19 false-FAIL logs: `compile-1605-authority-final.log`, `compile-1605-store-pi-runtime.log`,
`compile-gate-current.log`, `compile-gate-defense-report.log`, `compile-gate-dragon-mage.log`,
`compile-gate-garrison-post.log`, `compile-gate-garrison-win.log`, `compile-gate-raid-ui.log`,
`compile-gate-raidbase.log`, `compile-gate-recurring-dragon-spells.log`, `compile-gate-troopvfx.log`,
`compile-grok-merge-verify.log`, `compile-localization.log`, `compile-reconcile-1605.log`,
`current-wave-compile.log`, `honest-feedback-compile.log`, `localization-promo-compile`,
`wave1-compile3`, `wave1-compile4`. **Every one is a compile-gate run.** No APK/AAB/capture/fleet log
in `Builds/` carries an `error CS` line at all, which is consistent with WO sec.4's note that
`Builds/apk-build.log` carries zero - so the ship chains' exit-code checks are, by measurement, not
currently affected.

### 3b. The eleven callers

All eleven exist; all consume `$LASTEXITCODE` somewhere. Invocation / `$LASTEXITCODE`-reference counts,
grepped 2026-09-10:

| caller | run-unity-method invocations | `$LASTEXITCODE` refs | measured effect of the fix |
|---|---|---|---|
| `tools/regression/checkin_gate.ps1` | 10 | 8 | **THE PROVEN CONSUMER.** `:296-305`. Stage 2 flips FAIL -> PASS on an advisory-only compile; stages 3-5 unblock. |
| `morning-ship-chain.ps1` | 2 | 4 | No change measured - its `:111` guard covers `AndroidBuild.BuildSeekerApk`, and no APK log in `Builds/` has an `error CS` line. |
| `overnight-apk-build.ps1` | 2 | 2 | Same - APK path, no advisory lines measured. |
| `distribute-android.ps1` | 1 | 3 | Same - APK/distribute path. |
| `google-play-aab-build.ps1` | 4 | 2 | Same - AAB path. |
| `install-apk-to-seeker.ps1` | 1 | 5 | Same - install path. |
| `run-autopilot-fleet.ps1` | 2 | 1 | Same - no fleet log in `Builds/` matches `error CS`. |
| `run-tests.ps1` | 1 | 4 | Same. |
| `tools/command-centre.ps1` | 4 | 1 | Same. |
| `tools/localization/run-localization-overnight.ps1` | 1 | 4 | **Would have been affected**: `Builds/localization-font-build.log` carries 3 `Assets/` errors - those STILL red. `Builds/compile-localization.log` is advisory-only and now greens correctly. |
| `tools/run-unity-playmode.ps1` | 3 | 3 | Same - no playmode log matches. |

**The direction of every change is FAIL -> PASS on a run that already proved its marker.** No caller
gains a new failure.

---

## 4. `checkin_gate.ps1` needs no edit - and WO sec.7 forbids one anyway

`tools/regression/checkin_gate.ps1:299-305` (read at source 2026-09-10):
```powershell
$rc = $LASTEXITCODE
$marker = [bool](Select-String -Path $clog -Pattern 'COMPILE_GATE_OK' -Quiet)
$compileOk = ($rc -eq 0) -and $marker
```
That predicate is **correct as written**; it was starved by the runner's wrong exit code. With the
runner fixed it evaluates `$true` on an advisory-only compile. **No copy of the new criterion was
inlined into it, or into any chain** (CLAUDE.md sec.16, WO sec.7).

---

## 5. Two corrections to the WO itself (findings, not actions)

1. **WO sec.5 says `checkin_gate.ps1:85` parses the `PASS-UNASSERTED` literal. It does not.**
   `checkin_gate.ps1:85` is `return (Join-Path $chosen.FullName 'Editor\Unity.exe')`, the tail of the
   editor-pin helper. Searched the tracked tree 2026-09-10: **the only occurrences of
   `PASS-UNASSERTED` are inside `run-unity-method.ps1` itself** (`:84`, `:235` at HEAD) and inside
   stale copies under `.claude/worktrees/`, plus a comment in `google-play-aab-build.ps1:8`. **No
   consumer parses it.** The literal was preserved anyway, exactly as pinned - this note only
   corrects the stated reason.
2. **WO sec.6's "the log is UTF-16, preserve the encoding" is disproven** - see sec.2a. The log is
   BOM-less single-byte text. The memory `unity-logs-are-utf16-read-with-powershell` does hold for the
   R2 tooling logs; it does not hold for these batchmode editor logs.

Both are recorded rather than silently worked around (CLAUDE.md sec.11B B).

---

## 5b. Two housekeeping notes for the committer

- **Ops-channel noise.** `Write-Verdict` posts every verdict outward. The 11 judge-only probes above
  posted lines titled `GATE PASS  -  WO1622.*` / `GATE FAIL  -  WO1622.*` to the private dev channel.
  **They are fixture probes, not gate runs - do not triage them.**
- **`git diff` prints `warning: ... LF will be replaced by CRLF`.** Pre-existing, not introduced:
  `core.autocrlf=true` while the working-copy file is LF-only (measured: `crlf=0 lfTotal=279`), and
  the diff is hunk-scoped (`37 insertions, 7 deletions`), not a whole-file EOL rewrite. Git will
  normalise on `git add`; the commit is clean either way. Flagged so the committer is not surprised.

## 6. What the lane did NOT do

- Did not run Unity, gate, or commit. A WebGL build was live on this machine and no `Unity.exe` was
  launched or killed.
- Did not touch any `.cs`. `Assets/Editor/CompileGate.cs` was **read only** (`:526-546`, `:766-785`).
- Did not attempt the Solana `CS1069` WebGL module-reference gap (WO-1575 follow-up).
- Did not touch `tools/r2-ship.ps1`, `.githooks/pre-push`, or any chain script.
- Did not add a checked-in fixture folder or a driver script: the runner's own `-JudgeExistingLog`
  mode is the harness, and the lane's scope was PowerShell-only on the two named files. **If the lead
  wants the three fixtures and a 15-line assert-driver checked in under `tools/regression/`, that is
  one small follow-up** - the fixtures currently live in gitignored `Builds/` and the exact build
  commands are in sec.2b.
