# =============================================================================
# build-windows.ps1 - headless Windows x64 player build for Defenders of the Realm.
#
# Does the whole "refresh DLLs then build the exe" flow in one shot:
#   1. Auto-detects the installed Unity 6 editor (prefers the project's pinned
#      version, else the newest 6000.* install) so an editor re-install / version
#      bump does not require editing this script.
#   2. Deletes Builds/Windows first - incremental player builds skip re-emitting
#      the exe stub, leaving a stale exe against fresh scenes -> native crash.
#   3. Launches Unity in batchmode. Unity recompiles every script assembly on
#      launch (that IS the DLL refresh), then runs DesktopBuild.BuildWindows.
#
# Output: Builds/Windows/DefendersOfTheRealm.exe   (log: Builds/build.log)
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
# Release: powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 -Release
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads BOM-less files
# as ANSI, so non-ASCII chars (em-dashes, smart quotes) corrupt and break parse.
# =============================================================================

param([switch]$Release)
$ErrorActionPreference = 'Stop'
$proj       = $PSScriptRoot
$hubEditors = 'C:\Program Files\Unity\Hub\Editor'
$pinned     = '6000.4.8f1'   # version recorded in ProjectSettings/ProjectVersion.txt

# --- 1) Locate Unity.exe ------------------------------------------------------
$candidates = Get-ChildItem $hubEditors -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'Editor\Unity.exe') }
if (-not $candidates) {
    Write-Error "No Unity editor found under '$hubEditors'. Install one via Unity Hub first (include Windows Build Support)."
    exit 1
}

$chosen = $candidates | Where-Object { $_.Name -eq $pinned } | Select-Object -First 1
if (-not $chosen) {
    $six = $candidates | Where-Object { $_.Name -like '6000.*' } | Sort-Object Name -Descending
    if ($six) {
        $chosen = $six | Select-Object -First 1
        Write-Host "[build] Pinned $pinned not installed; using newest Unity 6 editor $($chosen.Name)."
    } else {
        $chosen = $candidates | Sort-Object Name -Descending | Select-Object -First 1
        Write-Warning "No Unity 6 (6000.*) editor found; falling back to $($chosen.Name). Project targets 6000.x - verify compatibility before trusting the build."
    }
}
# WO-1178: an UNSET $LASTEXITCODE is $null, and $null -ne 0 is TRUE - so the guard
# below used to fire on SUCCESS (and 'exit $null' exits 0). Seed it, and test for
# null explicitly, so a future edit to the assert script cannot reopen that hole.
$LASTEXITCODE = 0
& (Join-Path $proj 'tools\assert-unity-editor-pin.ps1') -ProjectRoot $proj -ExpectedVersion $pinned -SelectedVersion $chosen.Name
if ($null -eq $LASTEXITCODE) { exit 9 } elseif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$unity = Join-Path $chosen.FullName 'Editor\Unity.exe'
Write-Host "[build] Editor: $($chosen.Name)  ->  $unity"

# --- 2) Clean stale build output ---------------------------------------------
# A running player holds open its Data\Plugins DLLs (e.g. lib_burst_generated.dll),
# which blocks the wipe below. Stop the game player first so no manual "close the
# game" step is needed. This only targets the built game EXE by name - never the
# Unity editor or the licensing client (see memory: unity-license-kill-caution).
$player = Get-Process -Name 'DefendersOfTheRealm' -ErrorAction SilentlyContinue
if ($player) {
    Write-Host "[build] Stopping running player (DefendersOfTheRealm) so the output can be wiped."
    $player | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2   # let the OS release the file handles
}

$outDir = Join-Path $proj 'Builds\Windows'
if (Test-Path $outDir) {
    Write-Host "[build] Removing stale output: $outDir"
    Remove-Item $outDir -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $proj 'Builds') -Force | Out-Null

# --- 3) Run batchmode build ---------------------------------------------------
$log = Join-Path $proj 'Builds\build.log'
if (Test-Path $log) { Remove-Item $log -Force }

$buildMethod = if ($Release) { 'DeNelle.Editor.DesktopBuild.BuildWindowsRelease' }
               else { 'DeNelle.Editor.DesktopBuild.BuildWindows' }
$buildLabel = if ($Release) { 'RELEASE (no dev tools, no watermark)' }
              else { 'DEVELOPMENT (dev tools + watermark)' }
$unityArgs = @(
    '-batchmode', '-quit',
    '-projectPath', $proj,
    '-buildTarget', 'Win64',
    '-executeMethod', $buildMethod,
    '-logFile', $log
)
if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
    Write-Error "A 'Unity' editor process is already running - close it before batchmode (project lock)."
    exit 3
}
Write-Host "[build] Launching Unity batchmode: $buildLabel (fork-aware wait; first run after a reimport can take several minutes)."
Write-Host "[build] Log: $log"
& $unity @unityArgs | Out-Null
$wrapperExit = $LASTEXITCODE

# Unity.exe forks/relaunches, so the wrapper exit code is unreliable (see memory:
# unity-batchmode-relaunch-quirk). Poll until no 'Unity' process remains.
$deadline = (Get-Date).AddMinutes(30)
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

$license = $false
if (Test-Path $log) {
    # Unity 6 routinely fails the first license handshake (legacy protocol
    # version mismatch on the first LicenseClient pipe) and auto-recovers
    # on a fresh channel. Only flag a REAL license stopper when the error
    # appears with NO subsequent "Successfully updated license" recovery line.
    $errorHit  = [bool](Select-String -Path $log -Pattern 'HandshakeResponse reported an error|No valid Unity Editor license|ResponseCode: 505|Unsupported protocol version' -Quiet -ErrorAction SilentlyContinue)
    $recovered = [bool](Select-String -Path $log -Pattern 'Successfully updated license|Successfully connected to LicensingClient' -Quiet -ErrorAction SilentlyContinue)
    $license   = $errorHit -and -not $recovered
}

# --- 4) Report ----------------------------------------------------------------
$exe = Join-Path $outDir 'DefendersOfTheRealm.exe'
if (Test-Path $exe) {
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "[build] SUCCESS -> $exe ($mb MB)"
    exit 0
}

if ($license) { Write-Host "[build] *** LICENSE ERROR - needs interactive Hub refresh or reboot; do NOT kill processes. ***" }
Write-Host "[build] FAILED - no exe produced (wrapperExit=$wrapperExit, license=$license). Last 50 log lines:"
if (Test-Path $log) { Get-Content $log -Tail 50 }
if ($license) { exit 7 } else { exit 1 }
