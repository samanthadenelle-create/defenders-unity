# =============================================================================
# raid_suites_gate.ps1 - RAID headless suites regression gate (WO-1703/1704)
# -----------------------------------------------------------------------------
# The four raid headless suites are only invoked by Builds\night-remaining-
# focused.ps1, which is gitignored (line 8 of .gitignore: /[Bb]uilds/), so their
# green run cannot be reproduced from a clone. The suites cannot be registered
# in DataRegression because of asmdef reference direction (one-way from
# DeNelle.EditorWallTools). This tracked runner closes that gap and enables the
# four raid gates to be run on demand or as part of any CI/CD chain.
#
# Invokes, in order:
#   1. DeNelle.Editor.RaidWallContinuityRegression.RunHeadless
#      -> RAID_WALL_CONTINUITY_OK, Builds/raid-wall-continuity.log
#   2. DeNelle.Editor.Regression.RaidGroundCoverageRegression.RunStandalone
#      -> RAID_GROUND_COVERAGE_OK, Builds/raid-ground-coverage.log
#   3. DeNelle.Editor.RaidGroundSavedSceneRegression.RunStandalone
#      -> RAID_GROUND_SAVED_OK, Builds/raid-ground-saved.log
#   4. DeNelle.Editor.RaidPolishSavedProof.Run
#      -> RAID_POLISH_SAVED_PROOF_OK, Builds/raid-polish-saved.log
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\tools\regression\raid_suites_gate.ps1
#   powershell -ExecutionPolicy Bypass -File .\tools\regression\raid_suites_gate.ps1 -Only 2
#   powershell -ExecutionPolicy Bypass -File .\tools\regression\raid_suites_gate.ps1 -ContinueOnFail
#
# Exit code: 0 on all gates PASS, 16 on any FAIL, 3 if Unity is running.
#
# ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less files as ANSI.
#
# WO-1703 / WO-1704 (2026-09-14)
# =============================================================================
param(
    [int]$Only = 0,
    [switch]$ContinueOnFail,
    [int]$TimeoutMin = 30
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$proj = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$buildDir = Join-Path $proj 'Builds'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

# Refuse to start if Unity is running
if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
    Write-Host '[raid-gate] ERROR: A Unity editor process is already running - close it before batchmode (project lock).'
    exit 3
}

# Define the four raid gates: [method, marker, log-name-prefix]
$gates = @(
    @('DeNelle.Editor.RaidWallContinuityRegression.RunHeadless', 'RAID_WALL_CONTINUITY_OK', 'raid-wall-continuity'),
    @('DeNelle.Editor.Regression.RaidGroundCoverageRegression.RunStandalone', 'RAID_GROUND_COVERAGE_OK', 'raid-ground-coverage'),
    @('DeNelle.Editor.RaidGroundSavedSceneRegression.RunStandalone', 'RAID_GROUND_SAVED_OK', 'raid-ground-saved'),
    @('DeNelle.Editor.RaidPolishSavedProof.Run', 'RAID_POLISH_SAVED_PROOF_OK', 'raid-polish-saved')
)

$results = @()
$passed = 0
$failed = 0

Write-Host '================================================================'
Write-Host ' RAID SUITES REGRESSION GATE'
Write-Host "  project: $proj"
Write-Host "  only:    $(if ($Only -gt 0) { $Only } else { 'all' })"
Write-Host '================================================================'

for ($i = 0; $i -lt $gates.Count; $i++) {
    $gateNum = $i + 1

    # Skip if -Only is set and this gate is not the one
    if ($Only -gt 0 -and $gateNum -ne $Only) { continue }

    $method = $gates[$i][0]
    $marker = $gates[$i][1]
    $logPrefix = $gates[$i][2]
    $logName = $logPrefix + '.log'
    $logPath = Join-Path $buildDir $logName

    # Delete old log to ensure freshness
    if (Test-Path $logPath) { Remove-Item $logPath -Force -ErrorAction SilentlyContinue }

    Write-Host "`n[raid-gate] $gateNum/4 $method"

    # Invoke the gate via run-unity-method.ps1
    $stageStart = Get-Date
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $proj 'run-unity-method.ps1') `
        -Method $method -LogName $logName -TimeoutMin $TimeoutMin `
        -ExpectMarker $marker

    $gateExitCode = $LASTEXITCODE

    # Judge the gate: look for marker in log, must be fresh
    $markerFound = $false
    $logFresh = $false
    $logExists = $false
    $lineCount = 0

    if (Test-Path $logPath) {
        $logExists = $true
        $logMtime = (Get-Item $logPath).LastWriteTime
        $logFresh = ($logMtime -ge $stageStart)

        # run-unity-method.ps1 writes its log UTF-8 (2026-09-14: a real pass read as ONE garbage line
        # under -Encoding Unicode and was judged FAIL). Raw Unity -logFile output is UTF-16. Read the
        # file BOTH ways and accept the marker from whichever decoding carries it.
        $markerFound = $false
        $lineCount = 0
        foreach ($enc in @('Default', 'Unicode', 'UTF8')) {
            $logContent = $null
            try { $logContent = Get-Content -Path $logPath -Encoding $enc -ErrorAction SilentlyContinue } catch { }
            if ($logContent) {
                $n = @($logContent).Count
                if ($n -gt $lineCount) { $lineCount = $n }
                foreach ($line in $logContent) {
                    if ($line -like "*$marker*") { $markerFound = $true; break }
                }
            }
            if ($markerFound) { break }
        }
    }

    $gateOk = $markerFound -and $logFresh

    if ($gateOk) {
        Write-Host "[raid-gate] GATE $gateNum/4 $marker PASS log=$logPath lines=$lineCount"
        $results += @{ Gate = $gateNum; Status = 'PASS'; Marker = $marker; LogPath = $logPath; LineCount = $lineCount }
        $passed++
    } else {
        $whyFail = if (-not $logExists) { 'no log produced' }
                   elseif (-not $logFresh) { "log is STALE (mtime predates stage start)" }
                   elseif (-not $markerFound) { "marker '$marker' not found" }
                   else { 'unknown' }
        Write-Host "[raid-gate] GATE $gateNum/4 $marker FAIL log=$logPath lines=$lineCount reason=$whyFail"
        $results += @{ Gate = $gateNum; Status = 'FAIL'; Marker = $marker; LogPath = $logPath; LineCount = $lineCount; Reason = $whyFail }
        $failed++

        if (-not $ContinueOnFail) {
            Write-Host "[raid-gate] stopping at first failure (use -ContinueOnFail to keep going)"
            break
        }
    }
}

# Summary
Write-Host "`n================================================================"
Write-Host ' RAID SUITES GATE SUMMARY'
Write-Host '================================================================'

$results | ForEach-Object {
    $status = if ($_.Status -eq 'PASS') { 'PASS' } else { "FAIL ($($_.Reason))" }
    Write-Host "  GATE $($_.Gate)/4: $($_.Marker) - $status"
}

Write-Host ''
if ($failed -eq 0) {
    Write-Host "RAID_SUITES_GATE_OK $($passed)/$($passed)"
    exit 0
} else {
    Write-Host "RAID_SUITES_GATE_FAIL $($passed)/$($passed + $failed)"
    exit 16
}
