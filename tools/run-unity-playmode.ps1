# =============================================================================
# run-unity-playmode.ps1 - invoke a Unity Editor static method in batchmode that
# ENTERS PLAY MODE and exits the editor itself.
#
# WHY THIS EXISTS AND run-unity-method.ps1 CANNOT BE USED
# ---------------------------------------------------------------------------
# run-unity-method.ps1 passes -quit. Unity quits the moment the executeMethod
# RETURNS, and a play-mode harness returns immediately (EnterPlaymode is a
# request, not a call) - so the editor would quit before Play ever ticked and the
# run would produce ZERO output while looking like a clean pass. That is exactly
# the failure UICaptureLaunch documents for its legacy RunCapture() path.
#
# So: NO -quit. The harness is responsible for calling EditorApplication.Exit
# when it is done, and this wrapper enforces a hard timeout in case it does not.
#
# Everything else follows run-unity-method.ps1: markers are the verdict, never
# the exit code (memory: gates-report-success-without-proving-it), and the log
# must be FRESH (WO-984) or the run is not proven.
#
# ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less files as ANSI.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\tools\run-unity-playmode.ps1 `
#       -Method DeNelle.Editor.KnightGearProofCapture.Run `
#       -LogName knight-gear-proof.log `
#       -ExpectMarker KNIGHT_GEAR_PROOF_OK
# =============================================================================
param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$LogName,
    [int]$TimeoutMin = 25,
    [string]$ExpectMarker = '',
    [int]$MinLogBytes = 1024
)

$ErrorActionPreference = 'Stop'
$proj       = Split-Path -Parent $PSScriptRoot
$hubEditors = 'C:\Program Files\Unity\Hub\Editor'
$pinned     = '6000.4.8f1'

$logDir = Join-Path $proj 'Builds'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$log = Join-Path $logDir $LogName

$candidates = Get-ChildItem $hubEditors -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'Editor\Unity.exe') }
if (-not $candidates) { Write-Error "No Unity editor under '$hubEditors'."; exit 2 }
$chosen = $candidates | Where-Object { $_.Name -eq $pinned } | Select-Object -First 1
if (-not $chosen) { $chosen = $candidates | Where-Object { $_.Name -like '6000.*' } | Sort-Object Name -Descending | Select-Object -First 1 }
if (-not $chosen) { $chosen = $candidates | Sort-Object Name -Descending | Select-Object -First 1 }
# WO-1178: an UNSET $LASTEXITCODE is $null, and $null -ne 0 is TRUE - so the guard
# below used to fire on SUCCESS (and 'exit $null' exits 0). Seed it, and test for
# null explicitly, so a future edit to the assert script cannot reopen that hole.
$LASTEXITCODE = 0
& (Join-Path $PSScriptRoot 'assert-unity-editor-pin.ps1') -ProjectRoot $proj -ExpectedVersion $pinned -SelectedVersion $chosen.Name
if ($null -eq $LASTEXITCODE) { exit 9 } elseif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$unity = Join-Path $chosen.FullName 'Editor\Unity.exe'

if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
    Write-Error "A 'Unity' editor process is already running - close it before batchmode (project lock)."
    exit 3
}
if (Test-Path $log) { Remove-Item $log -Force -ErrorAction SilentlyContinue }

# NO -quit. NO -nographics (we need a real device to render real pixels).
$unityArgs = @('-batchmode', '-projectPath', $proj, '-executeMethod', $Method, '-logFile', $log)
Write-Host "[playmode] editor=$($chosen.Name) method=$Method"
Write-Host "[playmode] log=$log"
$runStart = Get-Date
$p = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru

$deadline = (Get-Date).AddMinutes($TimeoutMin)
$timedOut = $false
while ($true) {
    Start-Sleep -Seconds 8
    if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) {
        Start-Sleep -Seconds 4   # settle: Unity forks/relaunches on launch
        if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) { break }
    }
    if ((Get-Date) -ge $deadline) { $timedOut = $true; break }
}
if ($timedOut) {
    Write-Host "[playmode] TIMEOUT after $TimeoutMin min - killing the editor. The harness never called EditorApplication.Exit."
    Get-Process -Name 'Unity' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 5
}

$lock = Join-Path $proj 'Temp\UnityLockfile'
if (Test-Path $lock) { try { Remove-Item $lock -Force -ErrorAction Stop; Write-Host "[playmode] removed stale Temp\UnityLockfile" } catch { } }

$compileErr = $false; $license = $false; $markerHit = $false
$logExists = Test-Path $log
$logSize = -1; $logMtimeS = '(none)'
if ($logExists) {
    $fi = Get-Item $log
    $logSize = $fi.Length
    $logMtimeS = $fi.LastWriteTime.ToString('s')
    $compileErr = [bool](Select-String -Path $log -Pattern 'error CS\d+' -Quiet -ErrorAction SilentlyContinue)
    $license    = [bool](Select-String -Path $log -Pattern 'HandshakeResponse reported an error|No valid Unity Editor license' -Quiet -ErrorAction SilentlyContinue)
    if ($ExpectMarker -ne '') {
        $markerPattern = '(?<![A-Za-z0-9_])' + [regex]::Escape($ExpectMarker) + '(?![A-Za-z0-9_])'
        $markerHit = [bool](Select-String -Path $log -Pattern $markerPattern -Quiet -ErrorAction SilentlyContinue)
    }
}
Write-Host "[playmode] timedOut=$timedOut license=$license compileErrors=$compileErr sizeBytes=$logSize mtime=$logMtimeS"
Write-Host "[playmode] --- log tail (40) ---"
if ($logExists) { Get-Content $log -Tail 40 }

if ($ExpectMarker -eq '') {
    Write-Host "[playmode] NOTICE: no -ExpectMarker supplied - nothing proves this log came from this run."
    exit 0
}
$reason = ''
if ($timedOut) { $reason = 'HARNESS_TIMEOUT' }
elseif ($compileErr) { $reason = 'COMPILE_ERRORS' }
elseif (-not $logExists) { $reason = 'LOG_MISSING' }
elseif ((Get-Item $log).LastWriteTime -lt $runStart.AddSeconds(-2)) { $reason = 'LOG_STALE_FROM_EARLIER_RUN' }
elseif ($logSize -lt $MinLogBytes) { $reason = "LOG_TRUNCATED (under MinLogBytes=$MinLogBytes)" }
elseif (-not $markerHit) { $reason = 'MARKER_ABSENT' }
if ($reason -ne '') {
    Write-Host "[playmode] VERDICT=FAIL reason=$reason marker='$ExpectMarker' log=$log"
    exit 8
}
Write-Host "[playmode] VERDICT=PASS marker='$ExpectMarker' FOUND log=$log"
exit 0
