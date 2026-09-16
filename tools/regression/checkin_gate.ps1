# =============================================================================
# checkin_gate.ps1 - FULL check-in gate (CLI / Windows + Unity). WO-329.
# -----------------------------------------------------------------------------
# One entry point both for the CLI team's pre-commit verify. Runs, in order:
#
#   1. STATIC GATE    - tools/regression/static_gate.py (no Unity).
#   1d.NODE TESTS     - node --test test/*.test.js, TAP reporter, judged by the summary
#                       line '# fail 0' on a FRESH log. No Unity, ~2 s, and stage 2 is
#                       gated on it so a red backend fails BEFORE any batchmode.
#   2. COMPILE GATE   - DeNelle.Editor.CompileGate.Run via run-unity-method.ps1
#                       (expects the COMPILE_GATE_OK marker in the log).
#   3. DATA REGRESSION- DeNelle.Editor.DataRegression.RunAll via run-unity-method.ps1.
#                       *** THIS IS "THE" REGRESSION GATE *** (~90 registered oracle
#                       suites + ~26 inline catalog checks). Expects the shaped marker
#                       REGRESSION_OK <n>/<n> suites.
#   4. CHECK-IN BATTERY-DeNelle.Editor.RegressionSuite.RunAll via run-unity-method.ps1
#                       (22 cases: scene-open, NavMesh castle gate, source lints).
#                       Expects CHECKIN_SUITE_OK. Stages 3, 4 AND 5 must all be green.
#   5. SESSION GUARDS - DeNelle.Editor.SessionRegression.RunAll via run-unity-method.ps1.
#                       Expects the shaped marker SESSION_GUARDS_OK <p>/<n> checks on a
#                       FRESH log (mtime at/after this stage started).
#   6. EDITMODE TESTS - Unity -runTests -testPlatform EditMode.
#   7. PLAYMODE TESTS - Unity -runTests -testPlatform PlayMode.
#   8. BUILD (opt)    - build-windows.ps1, only when -Build is passed.
#
# ADDED 2026-09-06 (tooling lane; WO number to be assigned by the lead): stage 1d is new.
# The node suites under test/ - the whole api/ backend surface - were run by NO gate in
# this repo; every stage here drove Unity.
# See the block at stage 1d for why it is judged by the TAP line and not the exit code.
#
# ADDED 2026-09-06 (WO-1493): stage 5 is new. SESSION_GUARDS_OK is one of the three
# DISTINCT gate markers CLAUDE.md section 8 established, and it appeared in ZERO logs -
# no chain, no gate, nothing had ever invoked SessionRegression.RunAll. A marker no
# runner emits is not a gate. Same 2026-08-02 shape as the note below, one entry point
# further along.
#
# FIXED 2026-08-02: stage 3 used to run the stage-4 battery and judge it by the bare
# literal REGRESSION_OK - which all three regression classes emitted - so the full
# DataRegression oracle set had NEVER run in the automated check-in path.
#
# Prints a single summary table and returns ONE exit code: 0 only when every
# stage that ran passed. Stages 1-2 are hard prerequisites (a failure there
# short-circuits the rest, since tests cannot run against code that does not
# compile). Both test platforms always run so you see the full picture.
#
# Reuses run-unity-method.ps1 (CompileGate) and build-windows.ps1 (build) rather
# than forking them. The -runTests invocation has no existing wrapper, so this
# script launches the editor itself (same fork-aware poll + log discovery the
# other scripts use) and judges pass/fail from the NUnit results XML.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\tools\regression\checkin_gate.ps1
#   powershell -ExecutionPolicy Bypass -File .\tools\regression\checkin_gate.ps1 -Build
#   powershell -ExecutionPolicy Bypass -File .\tools\regression\checkin_gate.ps1 -SkipPlayMode
#
# ASCII-only on purpose (Windows PowerShell 5.1 reads BOM-less files as ANSI).
# =============================================================================
param(
    [switch]$Build,
    [switch]$SkipPlayMode,
    [int]$TimeoutMin = 40
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$proj      = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$results   = @()   # summary rows: [pscustomobject]{ Stage; Status; Detail }

function Add-Result($stage, $status, $detail) {
    $script:results += [pscustomobject]@{ Stage = $stage; Status = $status; Detail = $detail }
}

# --- locate Unity editor (mirrors run-unity-method.ps1) ----------------------
function Get-UnityExe {
    $hubEditors = 'C:\Program Files\Unity\Hub\Editor'
    $pinned     = '6000.4.8f1'
    $cands = Get-ChildItem $hubEditors -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'Editor\Unity.exe') }
    if (-not $cands) { throw "No Unity editor under '$hubEditors'." }
    $chosen = $cands | Where-Object { $_.Name -eq $pinned } | Select-Object -First 1
    if (-not $chosen) { $chosen = $cands | Where-Object { $_.Name -like '6000.*' } | Sort-Object Name -Descending | Select-Object -First 1 }
    if (-not $chosen) { $chosen = $cands | Sort-Object Name -Descending | Select-Object -First 1 }
    & (Join-Path $proj 'tools\assert-unity-editor-pin.ps1') -ProjectRoot $proj -ExpectedVersion $pinned -SelectedVersion $chosen.Name
    if ($LASTEXITCODE -ne 0) { throw "Unity editor pin assertion failed with exit code $LASTEXITCODE." }
    return (Join-Path $chosen.FullName 'Editor\Unity.exe')
}

# --- run a Unity test platform and judge from the NUnit XML ------------------
function Invoke-UnityTests($platform) {
    if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
        throw "A 'Unity' editor process is already running - close it before batchmode (project lock)."
    }
    $unity   = Get-UnityExe
    $logDir  = Join-Path $proj 'Builds'
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $resultsXml = Join-Path $logDir ("tests-$platform.xml")
    $log        = Join-Path $logDir ("tests-$platform.log")
    if (Test-Path $resultsXml) { Remove-Item $resultsXml -Force }
    if (Test-Path $log)        { Remove-Item $log -Force }

    $args = @(
        '-batchmode',
        '-projectPath', $proj,
        '-runTests',
        '-testPlatform', $platform,
        '-testResults', $resultsXml,
        '-logFile', $log
    )
    Write-Host "[gate] $platform tests: launching Unity -runTests"
    Write-Host "[gate]   results=$resultsXml"
    & $unity @args | Out-Null

    # Fork/relaunch quirk: ignore the wrapper exit code, poll until the editor exits.
    $deadline = (Get-Date).AddMinutes($TimeoutMin)
    Start-Sleep -Seconds 6
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) {
            Start-Sleep -Seconds 4
            if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) { break }
        }
        Start-Sleep -Seconds 10
    }
    $lock = Join-Path $proj 'Temp\UnityLockfile'
    if (Test-Path $lock) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }

    if (-not (Test-Path $resultsXml)) {
        # NOTE: ${platform} MUST be brace-delimited here. "$platform:" is a HARD PowerShell
        # 5.1 PARSER error (drive-qualified variable reference) - it made this entire script
        # unparseable, i.e. the check-in gate could not run at all. Found 2026-08-02.
        Write-Host "[gate] ${platform}: no results XML produced. Last 40 log lines:"
        if (Test-Path $log) { Get-Content $log -Tail 40 }
        return [pscustomobject]@{ Ok = $false; Detail = 'no results XML (compile/license error?)' }
    }

    [xml]$xml = Get-Content $resultsXml
    $run = $xml.'test-run'
    $total  = [int]$run.total
    $passed = [int]$run.passed
    $failed = [int]$run.failed
    $ok = ($run.result -eq 'Passed') -and ($failed -eq 0)
    $detail = "$passed/$total passed, $failed failed"
    if (-not $ok) {
        Write-Host "[gate] $platform FAILED tests:"
        $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
            Write-Host ("    x " + $_.fullname)
        }
    }
    return [pscustomobject]@{ Ok = $ok; Detail = $detail }
}

Write-Host '================================================================'
Write-Host ' DEFENDERS OF THE REALM - FULL CHECK-IN GATE (CLI / Unity)'
Write-Host "  project: $proj"
Write-Host "  build:   $($Build.IsPresent)   skipPlayMode: $($SkipPlayMode.IsPresent)"
Write-Host '================================================================'

# --- 1) static gate ----------------------------------------------------------
$python = (Get-Command python -ErrorAction SilentlyContinue)
if (-not $python) { $python = (Get-Command python3 -ErrorAction SilentlyContinue) }
if (-not $python) {
    Add-Result 'Static gate' 'FAIL' 'no python on PATH'
} else {
    Write-Host "`n[gate] 1/8 static gate..."
    & $python.Source (Join-Path $scriptDir 'static_gate.py') --root $proj
    if ($LASTEXITCODE -eq 0) { Add-Result 'Static gate' 'PASS' 'all static checks clean' }
    else { Add-Result 'Static gate' 'FAIL' "static_gate.py exit $LASTEXITCODE" }
}

$staticOk = ($results | Where-Object { $_.Stage -eq 'Static gate' }).Status -eq 'PASS'

# --- 1b) board check (WO-937 C) ----------------------------------------------
# python tools/board_build.py --check regenerates BOARD.html and exits 1 if any real
# work order is Unlabeled (its **Status:** line carries no canonical keyword - see
# docs/BOARD.md section 5). Wired here once Unlabeled hit 0 so the status vocabulary
# is ENFORCED and cannot regress. ~1 second, no Unity. A FAIL fails the gate summary
# but does not short-circuit the code stages - it is a docs defect, not a compile one.
if ($python) {
    Write-Host "`n[gate] board check (board_build.py --check)..."
    # The board build AUTO-INGESTS the owner's newest eoa-validations-*.json drop file on an
    # ordinary run (WO-1356 follow-up). --check already implies the opt-out in board_build.py
    # itself; this env var is the second lock, so the gate can never start reading a
    # developer's ~/Downloads and writing the shared record as a side effect of a check-in.
    $env:EOA_BOARD_SUBMIT = '0'
    & $python.Source (Join-Path $proj 'tools\board_build.py') --check
    Remove-Item Env:\EOA_BOARD_SUBMIT -ErrorAction SilentlyContinue
    if ($LASTEXITCODE -eq 0) { Add-Result 'Board check' 'PASS' 'BOARD_CHECK_OK 0 unlabeled' }
    else { Add-Result 'Board check' 'FAIL' "board_build.py --check exit $LASTEXITCODE (Unlabeled WOs listed above)" }
} else {
    Add-Result 'Board check' 'FAIL' 'no python on PATH'
}

# --- 1c) owner-validation round trip (2026-09-03) ----------------------------
# Proves a board REBUILD cannot lose the owner's felt-test sign-offs. Her sign-off is
# the only thing that closes a ticket (CLAUDE.md 13), and it used to be pinned to a
# per-commit localStorage key that orphaned it hourly. The record now lives in
# proof/owner-validations.json; this stage keeps the read path from silently rotting
# back to "always empty". ~3 seconds, no Unity, and it touches neither the live record
# nor the live BOARD.html. Judged by the MARKER, not the exit code.
#
# WO-1355 widened it: the board build now also FLIPS Pass+validated FIXED tickets to
# CLOSED, i.e. a plain board build REWRITES **Status:** lines from a data file. Stages
# 5-9 of the same test pin that pass (both-signals-required, idempotent, a CLOSED or a
# non-FIXED ticket never touched, the status body preserved, abort on a corrupt record)
# against a throwaway WorkOrders/ via EOA_WO_DIR. Same one marker still judges it.
if ($python) {
    Write-Host "`n[gate] owner-validation round trip..."
    $vOut = & $python.Source (Join-Path $proj 'tools/board_validation_roundtrip_test.py') 2>&1
    $vOut | Select-Object -Last 4 | ForEach-Object { Write-Host $_ }
    if ($vOut -match 'VALIDATION_ROUNDTRIP_OK') {
        Add-Result 'Owner validations' 'PASS' 'VALIDATION_ROUNDTRIP_OK rebuild preserves sign-offs'
    } else {
        Add-Result 'Owner validations' 'FAIL' 'no VALIDATION_ROUNDTRIP_OK marker (a rebuild may be losing owner sign-offs)'
    }
} else {
    Add-Result 'Owner validations' 'FAIL' 'no python on PATH'
}

# --- 1d) NODE BACKEND TESTS (2026-09-06, tooling lane) ----------------------------------------
# `node --test test/*.test.js` - the backend/API suites under test/. The 2026-09-06
# gate-coverage matrix found that ZERO gates ran them: every stage in this file drives
# Unity, so the entire api/ + tools/ JS surface (wallet auth, session renewal, save
# round trip, store/knob rendering) had no automated gate at all. A suite nothing runs
# is not a gate - the same finding as stage 5's SESSION_GUARDS_OK and the 2026-08-02
# marker collision, one layer out.
#
# PLACED BEFORE THE UNITY STAGES ON PURPOSE: it costs ~2 seconds and needs no editor
# lock, so a red backend fails the run before 40 minutes of batchmode. Stage 2 is
# gated on it ($staticOk -and $nodeOk), so a red here SKIPs every Unity stage.
#
# JUDGED ON A FRESH LOG, by the TAP summary line 'fail 0' - never the exit code:
#   * the reporter is PINNED to tap. Node's default flips with the version and with
#     whether stdout is a TTY; the spec reporter writes 'i fail 0' with a non-ASCII
#     glyph, which is not a stable thing to grep in a BOM-less ANSI-read script.
#   * 'fail 0' ALONE IS NOT A PASS. An empty run - a bad glob, a renamed folder -
#     prints 'fail 0' too, so '# tests <n>' must be present with n > 0.
#   * '# cancelled' must be 0. A test killed by a timeout is CANCELLED, not FAILED:
#     the marker would be present on a red tree (SUNDAY_HOUSEKEEPING.md section 3,
#     rule 2 - marker present, tree red).
#
# Start-Process, not '& node ... 2>&1': $ErrorActionPreference is 'Stop' at the top of
# this file, and in PS 5.1 redirecting a native command's stderr wraps each line in a
# NativeCommandError - one experimental-feature warning from node would terminate the
# whole gate. The glob is expanded here too; PowerShell does not expand it for an
# external exe and node's own glob support is version-dependent.
$nodeOk = $false
$node = (Get-Command node -ErrorAction SilentlyContinue)
$testFiles = @(Get-ChildItem (Join-Path $proj 'test') -Filter '*.test.js' -ErrorAction SilentlyContinue |
    ForEach-Object { $_.FullName })
if (-not $node) {
    Add-Result 'Node backend tests' 'FAIL' 'no node on PATH (the test/*.test.js suites cannot run)'
} elseif ($testFiles.Count -eq 0) {
    Add-Result 'Node backend tests' 'FAIL' 'no test/*.test.js files found - an empty run prints "fail 0"'
} else {
    Write-Host "`n[gate] 1d/9 node backend tests ($($testFiles.Count) files)..."
    $nlog        = Join-Path $proj 'Builds\node-tests.log'
    $nerr        = Join-Path $proj 'Builds\node-tests.err.log'
    $nStageStart = Get-Date
    New-Item -ItemType Directory -Path (Join-Path $proj 'Builds') -Force | Out-Null
    if (Test-Path $nlog) { Remove-Item $nlog -Force -ErrorAction SilentlyContinue }
    if (Test-Path $nerr) { Remove-Item $nerr -Force -ErrorAction SilentlyContinue }
    $nodeArgs = @('--test', '--test-reporter=tap') + $testFiles
    $p = Start-Process -FilePath $node.Source -ArgumentList $nodeArgs -WorkingDirectory $proj `
        -RedirectStandardOutput $nlog -RedirectStandardError $nerr -Wait -PassThru -NoNewWindow
    $nFresh = $false
    $nFail = $null; $nTests = $null; $nCancelled = $null
    if (Test-Path $nlog) {
        $nFresh     = ((Get-Item $nlog).LastWriteTime -ge $nStageStart)
        $nFail      = Select-String -Path $nlog -Pattern '^#\s*fail\s+0\s*$'      | Select-Object -First 1
        $nTests     = Select-String -Path $nlog -Pattern '^#\s*tests\s+([1-9]\d*)\s*$' | Select-Object -First 1
        $nCancelled = Select-String -Path $nlog -Pattern '^#\s*cancelled\s+0\s*$' | Select-Object -First 1
    }
    $nodeOk = $nFresh -and [bool]$nFail -and [bool]$nTests -and [bool]$nCancelled
    if ($nodeOk) {
        $nCount = ([regex]'\d+').Match($nTests.Line).Value
        Add-Result 'Node backend tests' 'PASS' "fail 0 - $nCount tests across $($testFiles.Count) files (exit $($p.ExitCode))"
    } else {
        $why = if (-not (Test-Path $nlog)) { 'no log produced' }
               elseif (-not $nFresh) { "log is STALE (mtime $((Get-Item $nlog).LastWriteTime) predates stage start $nStageStart)" }
               elseif (-not $nTests) { 'no "# tests <n>" line with n > 0 - the run selected nothing' }
               elseif (-not $nFail) { 'the TAP summary is not "# fail 0"' }
               else { 'cancelled tests present (a timed-out test is cancelled, not failed)' }
        Add-Result 'Node backend tests' 'FAIL' "$why (see Builds\node-tests.log)"
        Write-Host '[gate] node backend FAIL rows:'
        if (Test-Path $nlog) {
            Select-String -Path $nlog -Pattern '^not ok |^#\s*(tests|pass|fail|cancelled)\s' |
                Select-Object -First 40 | ForEach-Object { Write-Host ('    ' + $_.Line.Trim()) }
        }
        if (Test-Path $nerr) { Get-Content $nerr -Tail 20 | ForEach-Object { Write-Host ('    err: ' + $_) } }
    }
}

# --- 2) compile gate ---------------------------------------------------------
$compileOk = $false
if ($staticOk -and $nodeOk) {
    Write-Host "`n[gate] 2/9 compile gate (CompileGate.Run)..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $proj 'run-unity-method.ps1') `
        -Method 'DeNelle.Editor.CompileGate.Run' -LogName 'compilegate.log' -TimeoutMin $TimeoutMin `
        -ExpectMarker 'COMPILE_GATE_OK'
    $rc = $LASTEXITCODE
    $clog = Join-Path $proj 'Builds\compilegate.log'
    $marker = $false
    if (Test-Path $clog) { $marker = [bool](Select-String -Path $clog -Pattern 'COMPILE_GATE_OK' -Quiet) }
    $compileOk = ($rc -eq 0) -and $marker
    if ($compileOk) { Add-Result 'Compile gate' 'PASS' 'COMPILE_GATE_OK' }
    else { Add-Result 'Compile gate' 'FAIL' "exit $rc, marker=$marker" }
} else {
    $skipWhy = if (-not $staticOk) { 'static gate failed' } else { 'node backend tests red - failing fast before 40 min of batchmode' }
    Add-Result 'Compile gate' 'SKIP' $skipWhy
}

# --- 3) DATA REGRESSION - THE regression gate --------------------------------
# Runs DeNelle.Editor.DataRegression.RunAll (~90 registered oracle suites + ~26
# inline catalog checks) and judges from its SELF-DESCRIBING marker
#   REGRESSION_OK <n>/<n> suites
# This is what CLAUDE.md, START_HERE.md and every RESULT file mean by REGRESSION_OK.
#
# HISTORY (2026-08-02, why this is spelled out): this stage used to invoke
# DeNelle.Editor.RegressionSuite.RunAll - the 22-case LEGACY battery - and judge it
# by the bare literal 'REGRESSION_OK', which all THREE regression classes emitted.
# So roughly 64 oracle suites, including every one written that week, had NEVER run
# in the automated check-in path, and the small suite's pass read as the full set's.
# The marker is now shaped (count on the same line) so it cannot be confused, and
# BOTH suites run because they cover different ground.
$dataRegressionOk = $false
if ($compileOk) {
    Write-Host "`n[gate] 3/9 DATA regression - THE gate (DataRegression.RunAll)..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $proj 'run-unity-method.ps1') `
        -Method 'DeNelle.Editor.DataRegression.RunAll' -LogName 'data-regression.log' -TimeoutMin $TimeoutMin `
        -ExpectMarker 'REGRESSION_OK'
    $dlog = Join-Path $proj 'Builds\data-regression.log'
    $dmarker = $null
    if (Test-Path $dlog) {
        # Shaped grep on purpose: 'REGRESSION_OK <n>/<n> suites'. A bare REGRESSION_OK,
        # or another suite's marker that merely CONTAINS the token, cannot satisfy it.
        $dmarker = Select-String -Path $dlog -Pattern 'REGRESSION_OK \d+/\d+ suites' | Select-Object -First 1
    }
    $dataRegressionOk = [bool]$dmarker
    if ($dataRegressionOk) { Add-Result 'Data regression (THE gate)' 'PASS' ($dmarker.Line.Trim()) }
    else {
        Add-Result 'Data regression (THE gate)' 'FAIL' 'no "REGRESSION_OK <n>/<n> suites" marker (see Builds\data-regression.log)'
        if (Test-Path $dlog) {
            Write-Host '[gate] data-regression FAIL rows:'
            Select-String -Path $dlog -Pattern 'REGRESSION_FAIL|^\s+- ' | Select-Object -First 40 |
                ForEach-Object { Write-Host ('    ' + $_.Line.Trim()) }
        }
    }
} else {
    Add-Result 'Data regression (THE gate)' 'SKIP' 'compile gate not green'
}

# --- 4) legacy check-in battery (scene-open / NavMesh / source lints) --------
# DeNelle.Editor.RegressionSuite.RunAll -> CHECKIN_SUITE_OK <p>/<n> cases.
# NOT redundant with stage 3: only this one opens Village2, runs the behavioural
# NavMesh castle-gate query, and lints for per-frame fork-bombs / Yarn 'command:'.
$checkinSuiteOk = $false
if ($compileOk) {
    Write-Host "`n[gate] 4/9 legacy check-in battery (RegressionSuite.RunAll)..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $proj 'run-unity-method.ps1') `
        -Method 'DeNelle.Editor.RegressionSuite.RunAll' -LogName 'regression.log' -TimeoutMin $TimeoutMin `
        -ExpectMarker 'CHECKIN_SUITE_OK'
    $rlog = Join-Path $proj 'Builds\regression.log'
    $rmarker = $null
    if (Test-Path $rlog) {
        $rmarker = Select-String -Path $rlog -Pattern 'CHECKIN_SUITE_OK' | Select-Object -First 1
    }
    $checkinSuiteOk = [bool]$rmarker
    if ($checkinSuiteOk) { Add-Result 'Check-in battery' 'PASS' ($rmarker.Line.Trim()) }
    else {
        Add-Result 'Check-in battery' 'FAIL' 'no CHECKIN_SUITE_OK marker (see Builds\regression.log)'
        if (Test-Path $rlog) {
            Write-Host '[gate] check-in battery FAIL rows:'
            Select-String -Path $rlog -Pattern '\[FAIL\]' | ForEach-Object { Write-Host ('    ' + $_.Line.Trim()) }
        }
    }
} else {
    Add-Result 'Check-in battery' 'SKIP' 'compile gate not green'
}

# --- 5) SESSION GUARDS (WO-1493) ---------------------------------------------
# DeNelle.Editor.SessionRegression.RunAll -> SESSION_GUARDS_OK <p>/<n> checks.
# NOT redundant with stages 3 and 4: this suite asserts the session invariants
# (vendor contract, starter weapons, enemy/structure prefab resolution, the real
# save round trip, non-empty vendor stock) and is the ONLY emitter of this marker.
#
# JUDGED ON A FRESH LOG. The marker is greped in its SHAPED form, and the log's
# LastWriteTime must be at/after the moment this stage started - a stale log from a
# previous run reads exactly like a pass (SUNDAY_HOUSEKEEPING.md section 3, rule 3).
$sessionGuardsOk = $false
if ($compileOk) {
    Write-Host "`n[gate] 5/9 SESSION guards (SessionRegression.RunAll)..."
    $slog        = Join-Path $proj 'Builds\session-regression.log'
    $sStageStart = Get-Date
    if (Test-Path $slog) { Remove-Item $slog -Force -ErrorAction SilentlyContinue }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $proj 'run-unity-method.ps1') `
        -Method 'DeNelle.Editor.SessionRegression.RunAll' -LogName 'session-regression.log' -TimeoutMin $TimeoutMin `
        -ExpectMarker 'SESSION_GUARDS_OK'
    $smarker = $null
    $sFresh  = $false
    if (Test-Path $slog) {
        $sFresh = ((Get-Item $slog).LastWriteTime -ge $sStageStart)
        # Shaped grep on purpose: 'SESSION_GUARDS_OK <p>/<n> checks'. A bare token, or the
        # hardcoded 6/6 LABEL this stage was written to retire, cannot satisfy the gate on
        # its own - the count has to be there and the log has to be this run's.
        $smarker = Select-String -Path $slog -Pattern 'SESSION_GUARDS_OK \d+/\d+ checks' | Select-Object -First 1
    }
    $sessionGuardsOk = ([bool]$smarker) -and $sFresh
    if ($sessionGuardsOk) { Add-Result 'Session guards' 'PASS' ($smarker.Line.Trim()) }
    else {
        $why = if (-not (Test-Path $slog)) { 'no log produced' }
               elseif (-not $sFresh) { "log is STALE (mtime $((Get-Item $slog).LastWriteTime) predates stage start $sStageStart)" }
               else { 'no "SESSION_GUARDS_OK <p>/<n> checks" marker' }
        Add-Result 'Session guards' 'FAIL' "$why (see Builds\session-regression.log)"
        if (Test-Path $slog) {
            Write-Host '[gate] session-guards FAIL rows:'
            Select-String -Path $slog -Pattern 'SESSION_GUARDS_FAIL|^\s+- ' | Select-Object -First 40 |
                ForEach-Object { Write-Host ('    ' + $_.Line.Trim()) }
        }
    }
} else {
    Add-Result 'Session guards' 'SKIP' 'compile gate not green'
}

# ALL THREE markers are required. The gate must never pass while the ~90-suite set,
# the legacy battery, or the session guards are unrun.
$regressionOk = $dataRegressionOk -and $checkinSuiteOk -and $sessionGuardsOk

# --- 6/7) Unity tests --------------------------------------------------------
if ($compileOk) {
    Write-Host "`n[gate] 6/9 EditMode tests..."
    $em = Invoke-UnityTests 'EditMode'
    Add-Result 'EditMode tests' ($(if ($em.Ok) { 'PASS' } else { 'FAIL' })) $em.Detail

    if ($SkipPlayMode) {
        Add-Result 'PlayMode tests' 'SKIP' '-SkipPlayMode'
    } else {
        Write-Host "`n[gate] 7/9 PlayMode tests..."
        $pm = Invoke-UnityTests 'PlayMode'
        Add-Result 'PlayMode tests' ($(if ($pm.Ok) { 'PASS' } else { 'FAIL' })) $pm.Detail
    }
} else {
    Add-Result 'EditMode tests' 'SKIP' 'compile gate not green'
    Add-Result 'PlayMode tests' 'SKIP' 'compile gate not green'
}

# --- 6) optional build -------------------------------------------------------
$testsGreen = -not ($results | Where-Object { $_.Stage -like '*tests' -and $_.Status -eq 'FAIL' })
if ($Build) {
    if ($compileOk -and $regressionOk -and $testsGreen) {
        Write-Host "`n[gate] 8/9 build-windows.ps1..."
        & powershell -ExecutionPolicy Bypass -File (Join-Path $proj 'build-windows.ps1')
        if ($LASTEXITCODE -eq 0) { Add-Result 'Windows build' 'PASS' 'exe produced' }
        else { Add-Result 'Windows build' 'FAIL' "build-windows.ps1 exit $LASTEXITCODE" }
    } else {
        Add-Result 'Windows build' 'SKIP' 'earlier stage not green'
    }
}

# --- summary -----------------------------------------------------------------
Write-Host "`n================================================================"
Write-Host ' CHECK-IN GATE SUMMARY'
Write-Host '================================================================'
$results | Format-Table -AutoSize Stage, Status, Detail | Out-String | Write-Host

$anyFail = [bool]($results | Where-Object { $_.Status -eq 'FAIL' })
if ($anyFail) {
    Write-Host 'RESULT: FAIL - do NOT merge. Fix the FAIL rows above.'
    exit 1
}
Write-Host 'RESULT: PASS - safe to commit/merge.'
exit 0
