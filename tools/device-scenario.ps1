# tools/device-scenario.ps1 - WO-1775 device scenario + capture harness.
#
# Cheapest-first: this is the ONLY sanctioned recipe for a scripted device scenario +
# scrcpy/logcat capture (CLAUDE.md sec.16's don't-re-inline lesson). One wrapper, called
# by hand or by a future WO-1766 exploratory driver.
#
# Modes:
#   -Observe (the DEFAULT, and the only mode with no -Serial confirmation gate beyond the
#             overlay check): capture only. Sends NO intent, NO am force-stop.
#   -Scenario "k=v;k=v": also launches the app with the WO-1775 scenario intent extras
#             BEFORE recording. Requires -Target emulator|user:<N> unless
#             -ConfirmSeekerScenario is passed explicitly (WO-1775 sec.3 policy: the owner's
#             Seeker is OBSERVE ONLY; a scripted intent on her device needs her OK for that
#             specific run).
#
# Ordering is load-bearing (WO-1775 sec.2): ring size FIRST, then a -d drain, and ONLY THEN
# -c clear. Clearing before the drain destroys evidence that may be the only copy of
# something the owner just saw.
#
# ASCII-only (PS 5.1 parses this file BOM-less, same convention as overnight-apk-build.ps1).

param(
    [Parameter(Mandatory = $true)][string]$Serial,
    [string]$Scenario = '',
    [int]$Seconds = 20,
    [switch]$Observe,
    [ValidateSet('seeker', 'emulator', 'user')][string]$Target = 'emulator',
    [int]$UserId = -1,
    [switch]$ConfirmSeekerScenario
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot\..
$RepoRoot = (Get-Location).Path

# -Serial is REQUIRED and never baked in (acceptance #4) - the param() block above is the
# only place a serial may appear; do not add a default or a literal anywhere below.
if ([string]::IsNullOrWhiteSpace($Serial)) { throw "device-scenario.ps1: -Serial is required." }

$hasScenario = -not [string]::IsNullOrWhiteSpace($Scenario)
# Observe is the DEFAULT (acceptance #6): no -Scenario => no intent, no force-stop, ever -
# regardless of what -Observe itself is set to.
if (-not $hasScenario) { $Observe = $true }

if ($hasScenario -and $Target -eq 'seeker' -and -not $ConfirmSeekerScenario) {
    Write-Host "[device-scenario] REFUSED: -Target seeker with -Scenario needs -ConfirmSeekerScenario."
    Write-Host "  WO-1775 sec.3: the owner's Seeker is OBSERVE ONLY. A scripted intent state change is"
    Write-Host "  not sent to her device unless she has said so for THIS specific run."
    exit 3
}

# -- adb on PATH for the session (never assume it) --------------------------------------
function Resolve-Adb {
    $existing = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($existing) { return $existing.Source }
    $hubRoot = 'C:\Program Files\Unity\Hub\Editor'
    if (Test-Path $hubRoot) {
        $candidate = Get-ChildItem $hubRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($candidate) {
            $env:PATH = "$(Split-Path $candidate);$env:PATH"
            return $candidate
        }
    }
    throw "device-scenario.ps1: adb.exe not found on PATH and no Unity Hub AndroidPlayer SDK located."
}
$adb = Resolve-Adb
Write-Host "[device-scenario] adb: $adb"

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    & $adb -s $Serial @Args
}

# -- confirm the device is actually online (never assume a bare 'adb devices' count) -----
$deviceLine = & $adb devices | Select-String -SimpleMatch $Serial
if (-not $deviceLine -or $deviceLine -notmatch '\bdevice\b') {
    throw "device-scenario.ps1: serial '$Serial' is not an ONLINE device per 'adb devices'."
}

# -- resolve the package id from source, never a copied literal (CLAUDE.md sec.0/sec.5) --
$androidBuildSrc = Join-Path $RepoRoot 'Assets\Editor\AndroidBuild.cs'
$packageId = $null
if (Test-Path $androidBuildSrc) {
    $m = Select-String -Path $androidBuildSrc -Pattern 'PackageId\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($m) { $packageId = $m.Matches[0].Groups[1].Value }
}
if (-not $packageId) { throw "device-scenario.ps1: could not read PackageId from $androidBuildSrc." }
Write-Host "[device-scenario] package: $packageId"

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$scenarioTag = if ($hasScenario) { ($Scenario -replace '[^A-Za-z0-9]+', '-').Trim('-') } else { 'observe' }
if ($scenarioTag.Length -gt 60) { $scenarioTag = $scenarioTag.Substring(0, 60) }
$outDir = Join-Path $RepoRoot "logs\device\pull-$stamp-$scenarioTag"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Write-Host "[device-scenario] output: $outDir"

function Fail-Exit {
    param([string]$Message, [int]$Code = 1)
    Write-Host "[device-scenario] FAIL: $Message"
    "DEVICE_SCENARIO_FAIL $Message" | Out-File -Encoding ascii (Join-Path $outDir 'INDEX.md') -Append
    exit $Code
}

# =========================================================================================
# STEP 1 - SAFETY GATE: overlay check (memory device-lanes-overlay-apps-and-start-new).
# Runs REGARDLESS of -Observe/-Scenario, on EVERY target - an overlay can eat input on a
# scripted lane too, and on the owner's Seeker it is the whole reason this gate exists.
# =========================================================================================
$overlayHit = Invoke-Adb shell dumpsys window windows |
    Select-String -Pattern 'SYSTEM_ALERT', 'TYPE_APPLICATION_OVERLAY'
if ($overlayHit) {
    Write-Host "[device-scenario] STOP: overlay window(s) present:"
    $overlayHit | ForEach-Object { Write-Host "  $_" }
    Fail-Exit "overlay window present - see console for the offending window(s)" 2
}
Write-Host "[device-scenario] overlay check clear."

# =========================================================================================
# STEP 2 - ring size, THEN drain (-d), and ONLY THEN clear (-c). Order is load-bearing.
# =========================================================================================
$ringInfo = Invoke-Adb logcat -g
Write-Host "[device-scenario] logcat ring: $ringInfo"
$preLog = Join-Path $outDir 'pre.log'
Invoke-Adb logcat -d 2>&1 | Out-File -Encoding utf8 $preLog
Write-Host "[device-scenario] pre.log drained ($((Get-Item $preLog).Length) bytes)."
Invoke-Adb logcat -c
Write-Host "[device-scenario] logcat cleared (AFTER the drain above, never before)."

# =========================================================================================
# STEP 3 - scenario intent (only when -Scenario is set; -Observe sends nothing).
# =========================================================================================
if ($hasScenario) {
    # Resolve the launcher activity from the DEVICE, never from a doc (WO-1775 sec.1.5).
    $resolve = Invoke-Adb shell cmd package resolve-activity --brief $packageId
    $activityLine = ($resolve -split "`n" | Select-Object -Last 1).Trim()
    if (-not $activityLine -or $activityLine -notmatch '/') {
        Fail-Exit "could not resolve the launcher activity via 'cmd package resolve-activity --brief $packageId' (got: $resolve)"
    }
    Write-Host "[device-scenario] resolved launcher activity: $activityLine"

    # Force-stop FIRST so onNewIntent is never in play (WO-1775 sec.1.5).
    if ($Target -ne 'seeker') {
        Invoke-Adb shell am force-stop $packageId
        Write-Host "[device-scenario] force-stop OK."
    } else {
        Write-Host "[device-scenario] SKIPPED force-stop - never on the owner's device (sec.3)."
    }

    # ONE typed extra per key - never a packed "k=v;k=v" string handed to the device's own
    # shell (WO-1775 sec.1.5: a packed extra splits on the device's sh at ';'). The split
    # below happens HERE, in PowerShell, before anything reaches adb.
    $amArgs = @('shell', 'am', 'start', '-n', "$packageId/$activityLine")
    foreach ($pair in ($Scenario -split ';')) {
        if ([string]::IsNullOrWhiteSpace($pair)) { continue }
        $kv = $pair -split '=', 2
        if ($kv.Count -ne 2) { Fail-Exit "malformed scenario token '$pair' (expected key=value)" }
        $key = 'dotr.' + $kv[0].Trim()
        $value = $kv[1].Trim()
        $intVal = 0
        if ([int]::TryParse($value, [ref]$intVal)) {
            $amArgs += @('--ei', $key, $value)
        } else {
            $amArgs += @('--es', $key, $value)
        }
    }
    if ($Target -eq 'user' -and $UserId -ge 0) { $amArgs = @('shell', 'am', 'start', '--user', "$UserId") + $amArgs[3..($amArgs.Count - 1)] }

    Write-Host "[device-scenario] am start: $($amArgs -join ' ')"
    Invoke-Adb @amArgs
    "Scenario: $Scenario" | Out-File -Encoding ascii (Join-Path $outDir 'INDEX.md') -Append
    "Resolved activity: $activityLine" | Out-File -Encoding ascii (Join-Path $outDir 'INDEX.md') -Append
} else {
    Write-Host "[device-scenario] -Observe: no intent sent, no force-stop."
    "Mode: observe (no intent, no force-stop)" | Out-File -Encoding ascii (Join-Path $outDir 'INDEX.md') -Append
}

# =========================================================================================
# STEP 4 - record. --video-codec=h264 is load-bearing (the default codec wrote 0 bytes
# three times on this Seeker); --no-control is mandatory for any observation lane.
# =========================================================================================
$scrcpy = (Get-Command scrcpy.exe -ErrorAction SilentlyContinue)
if (-not $scrcpy) { Fail-Exit "scrcpy.exe not found on PATH." }
$mp4Path = Join-Path $outDir 'run.mp4'
Write-Host "[device-scenario] recording $Seconds s to $mp4Path ..."
& $scrcpy.Source -s $Serial --no-control --no-playback --no-audio --video-codec=h264 `
    --record $mp4Path --time-limit $Seconds
# Belt-and-braces: never leave a scrcpy window running past the lane, pass or fail.
Get-Process scrcpy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (-not (Test-Path $mp4Path) -or (Get-Item $mp4Path).Length -eq 0) {
    Fail-Exit "run.mp4 is missing or 0 bytes - scrcpy recording failed (check --video-codec=h264 is in effect)."
}
Write-Host "[device-scenario] run.mp4: $((Get-Item $mp4Path).Length) bytes."

# =========================================================================================
# STEP 5 - drain again (post-run log).
# =========================================================================================
$runLog = Join-Path $outDir 'run.log'
Invoke-Adb logcat -d 2>&1 | Out-File -Encoding utf8 $runLog
if (-not (Test-Path $runLog) -or (Get-Item $runLog).Length -eq 0) {
    Fail-Exit "run.log is missing or empty after the post-run drain."
}
Write-Host "[device-scenario] run.log: $((Get-Item $runLog).Length) bytes."

# =========================================================================================
# STEP 6 - contact sheets, never raw frames (WO-1775 sec.2.1/sec.5 token-cost discipline).
# =========================================================================================
$ffmpeg = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
if (-not $ffmpeg) {
    $winget = 'C:\Users\Elden\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-essentials_build\bin\ffmpeg.exe'
    if (Test-Path $winget) { $ffmpeg = @{ Source = $winget } }
}
if ($ffmpeg) {
    $sheetPattern = Join-Path $outDir 'sheet-%03d.png'
    & $ffmpeg.Source -i $mp4Path -vf "fps=1,scale=320:-1,tile=6x5" -y $sheetPattern 2>&1 | Out-Null
    $sheets = Get-ChildItem $outDir -Filter 'sheet-*.png' -ErrorAction SilentlyContinue
    Write-Host "[device-scenario] contact sheets: $($sheets.Count)"
} else {
    Write-Host "[device-scenario] WARNING: ffmpeg not found - no contact sheets produced."
}

# =========================================================================================
# STEP 7 - INDEX.md + the marker. Judge the marker on a FRESH console read, never exit code.
# =========================================================================================
$index = Join-Path $outDir 'INDEX.md'
@"
# device-scenario capture - $stamp

Serial: $Serial
Target: $Target
Package: $packageId
Scenario: $(if ($hasScenario) { $Scenario } else { '(observe - none)' })
Seconds: $Seconds
pre.log: $((Get-Item $preLog).Length) bytes
run.log: $((Get-Item $runLog).Length) bytes
run.mp4: $((Get-Item $mp4Path).Length) bytes
"@ | Out-File -Encoding ascii $index -Append

Write-Host "DEVICE_SCENARIO_OK $outDir"
exit 0
