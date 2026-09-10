# =============================================================================
# run-unity-method.ps1 - invoke a single Unity Editor static method in batchmode
# and wait for the REAL editor to finish.
#
# Unity.exe forks/relaunches on launch (see memory: unity-batchmode-relaunch-
# quirk), so the process this script starts can return early with a blank exit
# code while the actual editor keeps working. We therefore ignore the wrapper
# exit code and poll until no 'Unity' process remains, then judge success from
# the log (compile errors under Assets/ - see WO-1622 below - exceptions /
# "Aborting batchmode").
#
# WO-1622 (2026-09-10): the compile-error half of that scan is SCOPED TO Assets/.
# It used to be a bare, path-blind 'error CS\d+' grep over the whole log. Since
# 2026-09-07 the compile gate deliberately PRINTS the error lines it has already
# ruled advisory - the Solana package's WebGL CS1069 module-reference gap, 21 of
# them - so every clean compile carried 'error CS' text and this runner failed it
# while its own evidence line said the expected marker was FOUND on a fresh log.
# Measured 2026-09-10 across Builds/: 19 of the 21 logs matching 'error CS' carry
# ZERO errors under Assets/. Only a compiler error whose OWN source path is under
# Assets/ is this tree's verdict; that criterion is authored ONCE, here, and must
# not be re-implemented in any calling chain (CLAUDE.md s.16).
#
# ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less files as ANSI,
# so smart-quotes / em-dashes corrupt and break the parse.
#
# WO-984 (2026-08-14): log TEXT alone cannot prove a run happened - a run that
# never started logs no errors, and a STALE log from a previous run reads exactly
# like a fresh pass. Pass -ExpectMarker <MARKER> to make the caller declare what
# success looks like; the wrapper then fails closed (exit 8, with a NAMED reason)
# when the log is missing / stale / truncated / marker-less. The error-text scan
# below is KEPT - it catches real failures that still emit a marker.
# -ExpectMarker is OPTIONAL for backward compatibility: omitted => today's
# behaviour exactly, plus one NOTICE line so the omission is visible, not silent.
#
# Usage: powershell -ExecutionPolicy Bypass -File .\run-unity-method.ps1 `
#            -Method DeNelle.Editor.TripoAssetPostprocessor.ForceReextractAll `
#            -LogName tripo-extract.log `
#            -ExpectMarker TRIPO_EXTRACT_OK
# =============================================================================
param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$LogName,
    [int]$TimeoutMin = 30,
    # WO-984: the marker this run must print to be believed (e.g. COMPILE_GATE_OK,
    # UI_CAPTURE_OK, CHECKIN_SUITE_OK). Optional - see header.
    [string]$ExpectMarker = '',
    # Smallest log a real batchmode run can plausibly produce. Under this we call
    # the run truncated/aborted rather than passed. Only consulted with -ExpectMarker.
    [int]$MinLogBytes = 1024,
    # VERIFICATION MODE (no Unity launch): judge an ALREADY-EXISTING log as if the
    # run had started at the given time. Value is any parseable date/time string.
    # Exercises the SAME evidence gate as a real run - this is how the stale-log and
    # missing-log rows of WO-984 are demonstrated without staging a crashed editor.
    [string]$JudgeExistingLog = '',
    # Force the ACTIVE BUILD TARGET for this run (e.g. Win64, Android, WebGL).
    # WHY THIS EXISTS (2026-08-05): an APK build leaves the project's active target on
    # Android. A later DESKTOP build then dies with "Native extension for Android target
    # not found" and an SBP/Addressables failure - and because the wrapper judges by log
    # text rather than a marker, it reads as a generic failure rather than a target
    # mismatch. Pass -BuildTarget Win64 after any Android build. Omitted => whatever the
    # project's active target happens to be, i.e. today's behaviour, unchanged.
    [string]$BuildTarget = '',
    # Scripting define symbols forwarded to the PLAYER compilation (semicolon- or
    # comma-separated). WHY THIS EXISTS (2026-08-22): a custom -executeMethod build owns
    # its own BuildPlayerOptions, so Unity's command-line defines do NOT automatically
    # reach the player -- AndroidBuild.CommandLineScriptingDefines() reads this argument
    # back off the command line and forwards it explicitly. Without this passthrough the
    # owner-test symbols (STORE_RAIL_LOCAL_TEST / MONETIZATION_LOCAL_TEST) are
    # unreachable from the sanctioned ship chain, and the only way to get them into an
    # APK would be a raw run-unity-method call that BYPASSES the s16 R2 push+verify --
    # exactly the hole that shipped capsule enemies on 2026-08-20. Omitted => empty
    # array => today's behaviour, monetization OFF, unchanged.
    [string]$ExtraScriptingDefines = ''
)

# --- ops-channel verdict publisher (owner ruling 2026-08-26) -----------------
# Every VERDICT line in this file goes through Write-Verdict so the PRIVATE
# development channel sees pass AND fail. Wired HERE, in the one runner every
# gate goes through, and NOT copy-pasted into morning-ship-chain /
# overnight-apk-build: section 16 records what happens when a push+verify pair
# is inlined into two chains - they drift, and one of them silently stops
# checking. One file, one seam.
#
# STOP - A FAILED POST MUST NEVER CHANGE THE GATE VERDICT. The channel is
# observability, not authority. Judge the gate by the MARKER on a fresh log,
# exactly as before; this only mirrors that judgement outward. Hence the
# try/catch that swallows everything and the Out-Null on the tool's own output.
# status-post.mjs is already a silent no-op when the webhook is absent.
function Write-Verdict {
    param([string]$Line)
    Write-Host $Line
    try {
        $poster = Join-Path $PSScriptRoot 'tools\status-post.mjs'
        if (Test-Path $poster) {
            if (Get-Command node -ErrorAction SilentlyContinue) {
                $tag = if ($Line -match 'VERDICT=PASS(?!-UNASSERTED)') { 'GATE PASS' }
                       elseif ($Line -match 'VERDICT=PASS-UNASSERTED') { 'GATE PASS (unasserted)' }
                       else { 'GATE FAIL' }
                $subject = if ($Method) { $Method } else { 'unity' }
                & node $poster --title "$tag  -  $subject" --fence --body $Line 2>&1 | Out-Null
            }
        }
    } catch { }
}


$ErrorActionPreference = 'Stop'
$proj       = $PSScriptRoot
$hubEditors = 'C:\Program Files\Unity\Hub\Editor'
$pinned     = '6000.4.8f1'

$logDir = Join-Path $proj 'Builds'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$log = Join-Path $logDir $LogName

$judgeOnly = ($JudgeExistingLog -ne '')
$wrapperExit = 0
$timedOut = $false

if ($judgeOnly) {
    try { $runStart = [datetime]::Parse($JudgeExistingLog) }
    catch { Write-Verdict "[run] VERDICT=FAIL reason=BAD_JUDGE_TIME value='$JudgeExistingLog' (not a parseable date/time)"; exit 8 }
    Write-Host "[run] JUDGE-ONLY MODE - no Unity launched. Judging existing log as if the run started at $($runStart.ToString('s'))."
    Write-Host "[run] log=$log"
} else {
    # --- locate editor --------------------------------------------------------
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
    & (Join-Path $proj 'tools\assert-unity-editor-pin.ps1') -ProjectRoot $proj -ExpectedVersion $pinned -SelectedVersion $chosen.Name
    if ($null -eq $LASTEXITCODE) { exit 9 } elseif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $unity = Join-Path $chosen.FullName 'Editor\Unity.exe'

    # --- refuse if an editor is already open (project lock) -------------------
    if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
        Write-Error "A 'Unity' editor process is already running - close it before batchmode (project lock)."
        exit 3
    }

    # Tolerate the check-then-delete race (2026-07-13 fleet aggregation: Test-Path passed,
    # the file vanished before Remove-Item ran, and the emitter aborted with exit 1).
    if (Test-Path $log) { Remove-Item $log -Force -ErrorAction SilentlyContinue }

    $unityArgs = @('-batchmode', '-quit', '-projectPath', $proj, '-executeMethod', $Method, '-logFile', $log)
    if ($BuildTarget -ne '') { $unityArgs = @('-buildTarget', $BuildTarget) + $unityArgs }
    if ($ExtraScriptingDefines -ne '') {
        $unityArgs += @('-extraScriptingDefines', $ExtraScriptingDefines)
        Write-Host "[run] extraScriptingDefines=$ExtraScriptingDefines (forwarded to the PLAYER compilation)"
    }
    Write-Host "[run] editor=$($chosen.Name)  method=$Method  buildTarget=$(if ($BuildTarget -ne '') { $BuildTarget } else { '(project active)' })"
    Write-Host "[run] log=$log"
    # WO-984: the instant the run began. Any log older than this is a leftover from a
    # PREVIOUS run and must never be read as this run's evidence.
    $runStart = Get-Date
    & $unity @unityArgs | Out-Null
    $wrapperExit = $LASTEXITCODE

    # --- wait for the real (possibly relaunched) editor to finish -------------
    $deadline = (Get-Date).AddMinutes($TimeoutMin)
    Start-Sleep -Seconds 6
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) {
            Start-Sleep -Seconds 4   # settle: catch a relaunch spawning a new PID
            if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) { break }
        }
        Start-Sleep -Seconds 10
    }
    $timedOut = (Get-Date) -ge $deadline

    # Clear the stale editor lockfile so the next batchmode launch is not blocked.
    $lock = Join-Path $proj 'Temp\UnityLockfile'
    if (Test-Path $lock) { try { Remove-Item $lock -Force -ErrorAction Stop; Write-Host "[run] removed stale Temp\UnityLockfile" } catch { Write-Host "[run] could not remove lockfile: $($_.Exception.Message)" } }
}

# --- judge success from the log ----------------------------------------------
$succeeded = $false; $compileErr = $false; $license = $false
# WO-1622 counters. Kept as COUNTS, not booleans, so the verdict line can never
# imply that the non-Assets error lines were absent - they were set aside, and the
# reader is told how many. Silently turning "some other layer's errors" into a bare
# green is the class of defect memory gates-report-success-without-proving-it names.
$errCsTotal = 0; $errCsAssets = 0; $firstAssetsErr = ''
if (Test-Path $log) {
    $succeeded  = [bool](Select-String -Path $log -Pattern 'Exiting batchmode successfully|terminate with return code 0' -Quiet -ErrorAction SilentlyContinue)
    # Bind to the error's OWN path token - '<...>Assets/<file>(line,col): error CSnnnn' -
    # so a Packages/ line that merely MENTIONS Assets in its message text is not counted,
    # and an Assets/ error is counted whether Unity emitted it raw or the gate echoed it.
    $errCsLines  = @(Select-String -Path $log -Pattern 'error CS\d+' -ErrorAction SilentlyContinue)
    $errCsTotal  = $errCsLines.Count
    $assetsHits  = @($errCsLines | Where-Object { $_.Line -match 'Assets[\\/][^:\r\n]*\(\d+,\d+\):\s*error CS\d+' })
    $errCsAssets = $assetsHits.Count
    if ($errCsAssets -gt 0) { $firstAssetsErr = $assetsHits[0].Line.Trim() }
    $compileErr  = ($errCsAssets -gt 0)
    $license     = [bool](Select-String -Path $log -Pattern 'HandshakeResponse reported an error|No valid Unity Editor license|ResponseCode: 505|Unsupported protocol version' -Quiet -ErrorAction SilentlyContinue)
}
$errCsOther = $errCsTotal - $errCsAssets
$errScan    = "errorCS=$errCsTotal underAssets=$errCsAssets elsewhere=$errCsOther"
Write-Host "[run] wrapperExit=$wrapperExit timedOut=$timedOut succeeded=$succeeded license=$license compileErrors=$compileErr ($errScan)"
if ($errCsOther -gt 0) { Write-Host "[run] NOTE: $errCsOther 'error CS' line(s) outside Assets/ were SET ASIDE - they are not this tree's verdict (WO-1622). They are still in the log; read them there." }
if ($errCsAssets -gt 0) { Write-Host "[run] first Assets/ compile error: $firstAssetsErr" }
Write-Host "[run] --- log tail (45) ---"
if (Test-Path $log) { Get-Content $log -Tail 45 }

# --- WO-984 evidence gate: prove this run produced this log ------------------
# Absence of an error is NOT evidence of success. Four ways a "clean" log lies:
# it is not there, it is a leftover from an earlier run, it is a truncated stub,
# or it never printed the marker the caller demanded.
$logExists  = Test-Path $log
$logSize    = -1
$logMtimeS  = '(none)'
$logAgeS    = '(n/a)'
$markerHit  = $false
if ($logExists) {
    $fi        = Get-Item $log
    $logSize   = $fi.Length
    $logMtimeS = $fi.LastWriteTime.ToString('s')
    $logAgeS   = ('{0:N1}s after run start' -f ($fi.LastWriteTime - $runStart).TotalSeconds)
    if ($ExpectMarker -ne '') {
        $markerHit = [bool](Select-String -Path $log -Pattern $ExpectMarker -SimpleMatch -Quiet -ErrorAction SilentlyContinue)
    }
}
$evidence = "marker='$ExpectMarker' log=$log mtime=$logMtimeS ($logAgeS) sizeBytes=$logSize runStart=$($runStart.ToString('s'))"

if ($ExpectMarker -eq '') {
    Write-Host "[run] NOTICE: no -ExpectMarker was supplied - success is being judged by LOG TEXT ONLY. Nothing proves this log came from this run (WO-984). Pass -ExpectMarker <MARKER> to assert it."
} else {
    $reason = ''
    if (-not $logExists) {
        $reason = 'LOG_MISSING'
    } elseif ((Get-Item $log).LastWriteTime -lt $runStart.AddSeconds(-2)) {
        $reason = 'LOG_STALE_FROM_EARLIER_RUN'
    } elseif ($logSize -lt $MinLogBytes) {
        $reason = "LOG_TRUNCATED (under MinLogBytes=$MinLogBytes)"
    } elseif (-not $markerHit) {
        $reason = 'MARKER_ABSENT'
    }
    if ($reason -ne '') {
        if ($license) {
            # Keep the license-specific signal (exit 7) - it tells the caller to refresh
            # the Hub, not to go hunting for a code defect.
            Write-Verdict "[run] VERDICT=FAIL reason=LICENSE_ERROR (and $reason) - this run is NOT PROVEN. $evidence"
            Write-Host "[run] *** LICENSE ERROR (run did not complete). FIRST: RETRY ONCE - this is often transient. ***"
            Write-Host "[run] *** STILL FAILING? CLOSE UNITY HUB (owner remedy, 2026-08-25: 'i close the hub ... seems to help'). ***"
            Write-Host "[run] *** The Hub is NOT needed for batchmode. Reboot only if closing the Hub does not clear it, and do NOT kill processes. ***"
            exit 7
        }
        Write-Verdict "[run] VERDICT=FAIL reason=$reason - this run is NOT PROVEN. $evidence"
        exit 8
    }
    Write-Host "[run] evidence OK: marker '$ExpectMarker' FOUND in a fresh log. $evidence"
}

# A transient 505 license-handshake line can appear even on a fully successful
# batchmode run, so the explicit success marker is authoritative. Only treat the
# license error as fatal when the run did NOT reach a clean exit.
if ($succeeded -and -not $compileErr) {
    if ($ExpectMarker -eq '') {
        Write-Verdict "[run] VERDICT=PASS-UNASSERTED (log text only, NO marker was checked) log=$log mtime=$logMtimeS sizeBytes=$logSize $errScan"
    } else {
        Write-Verdict "[run] VERDICT=PASS marker='$ExpectMarker' FOUND log=$log mtime=$logMtimeS sizeBytes=$logSize $errScan"
    }
    exit 0
}
if ($license) {
    Write-Verdict "[run] VERDICT=FAIL reason=LICENSE_ERROR (run did not complete) $evidence"
    Write-Host "[run] *** LICENSE ERROR (run did not complete). FIRST: RETRY ONCE - this is often transient. ***"
            Write-Host "[run] *** STILL FAILING? CLOSE UNITY HUB (owner remedy, 2026-08-25: 'i close the hub ... seems to help'). ***"
            Write-Host "[run] *** The Hub is NOT needed for batchmode. Reboot only if closing the Hub does not clear it, and do NOT kill processes. ***"
    exit 7
}
Write-Verdict "[run] VERDICT=FAIL reason=LOG_SCAN (no clean-exit line, or compile errors under Assets/) $evidence $errScan"
if ($errCsAssets -gt 0) { Write-Host "[run] LOG_SCAN first Assets/ compile error: $firstAssetsErr" }
exit 1
