# =============================================================================
# build-webgl.ps1 - headless WebGL (browser) build for Defenders of the Realm.
#
# Mirrors build-windows.ps1 (WO-09 section 2.1). Differences: WebGL target,
# Builds/WebGL output, the DeNelle.Editor.WebGLBuild.BuildWebGL entry point, and
# a 60-minute deadline (first IL2CPP compile is slow). Success = index.html.
#
# PREREQUISITE: the "WebGL Build Support" editor module must be installed
#   (Unity Hub -> Installs -> 6000.4.8f1 -> Add Modules -> WebGL Build Support,
#    or:  "Unity Hub" -- --headless install-modules --version 6000.4.8f1 -m webgl)
# Without it the build fails immediately - the WebGL target is unavailable.
#
# Output: Builds/WebGL/index.html (+ Build/, StreamingAssets/)   (log: Builds/webgl-build.log)
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-webgl.ps1
#
# NOTE: keep this file ASCII-only (Windows PowerShell 5.1 reads BOM-less files as
# ANSI; non-ASCII chars corrupt and break parse).
# =============================================================================

param(
    # WO-126: itch.io can't serve Brotli (.br) payloads (no Content-Encoding: br
    # header). Pass -NoBrotli to build uncompressed files itch.io can host.
    [switch]$NoBrotli,

    # Pass -DevBuild to produce a Development player (BuildOptions.Development ->
    # defines DEVELOPMENT_BUILD -> compiles in the AutoPilot bot + DevTools QA panel).
    # This is the LOCALHOST bot-testing build: serve it (serve-webgl.ps1) and open
    # "http://localhost:PORT/?autopilot=1&seed=1001" to spawn an in-browser chaos bot.
    # Larger (Development disables code/data compression) but localhost has no size cap.
    # NEVER deploy a -DevBuild to prod (it would expose ?autopilot=1 to real users).
    [switch]$DevBuild
)

$ErrorActionPreference = 'Stop'
$proj       = $PSScriptRoot
$hubEditors = 'C:\Program Files\Unity\Hub\Editor'
$pinned     = '6000.4.8f1'   # version recorded in ProjectSettings/ProjectVersion.txt

# --- 1) Locate Unity.exe ------------------------------------------------------
$candidates = Get-ChildItem $hubEditors -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'Editor\Unity.exe') }
if (-not $candidates) {
    Write-Error "No Unity editor found under '$hubEditors'. Install one via Unity Hub first (include WebGL Build Support)."
    exit 1
}

$chosen = $candidates | Where-Object { $_.Name -eq $pinned } | Select-Object -First 1
if (-not $chosen) {
    $six = $candidates | Where-Object { $_.Name -like '6000.*' } | Sort-Object Name -Descending
    if ($six) {
        $chosen = $six | Select-Object -First 1
        Write-Host "[webgl] Pinned $pinned not installed; using newest Unity 6 editor $($chosen.Name)."
    } else {
        $chosen = $candidates | Sort-Object Name -Descending | Select-Object -First 1
        Write-Warning "No Unity 6 (6000.*) editor found; falling back to $($chosen.Name)."
    }
}
# WO-1178: an UNSET $LASTEXITCODE is $null, and $null -ne 0 is TRUE - so the guard
# below used to fire on SUCCESS (and 'exit $null' exits 0). Seed it, and test for
# null explicitly, so a future edit to the assert script cannot reopen that hole.
$LASTEXITCODE = 0
& (Join-Path $proj 'tools\assert-unity-editor-pin.ps1') -ProjectRoot $proj -ExpectedVersion $pinned -SelectedVersion $chosen.Name
if ($null -eq $LASTEXITCODE) { exit 9 } elseif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$unity = Join-Path $chosen.FullName 'Editor\Unity.exe'
Write-Host "[webgl] Editor: $($chosen.Name)  ->  $unity"

# Warn early if the WebGL module is missing (the build will fail without it).
$wglModule = Join-Path $chosen.FullName 'Editor\Data\PlaybackEngines\WebGLSupport'
if (-not (Test-Path $wglModule)) {
    Write-Warning "[webgl] 'WebGL Build Support' module NOT found at $wglModule - the build will fail. Install it via Unity Hub first."
}

# --- 2) Clean stale build output ---------------------------------------------
$outDir = Join-Path $proj 'Builds\WebGL'
if (Test-Path $outDir) {
    Write-Host "[webgl] Removing stale output: $outDir"
    Remove-Item $outDir -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $proj 'Builds') -Force | Out-Null

# --- 3) Run batchmode build ---------------------------------------------------
$log = Join-Path $proj 'Builds\webgl-build.log'
if (Test-Path $log) { Remove-Item $log -Force }

$unityArgs = @(
    '-batchmode', '-quit',
    '-projectPath', $proj,
    '-buildTarget', 'WebGL',
    '-executeMethod', 'DeNelle.Editor.WebGLBuild.BuildWebGL',
    '-logFile', $log
)
if ($NoBrotli) {
    $unityArgs += '-noBrotli'
    Write-Host "[webgl] -NoBrotli: building UNCOMPRESSED payloads (itch.io-compatible, larger first load)."
}
if ($DevBuild) {
    $unityArgs += '-devBuild'
    Write-Host "[webgl] -DevBuild: Development player (AutoPilot bot + DevTools QA panel compiled in) for LOCALHOST bot testing. Do NOT deploy to prod."
}
if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
    Write-Error "A 'Unity' editor process is already running - close it before batchmode (project lock)."
    exit 3
}
Write-Host "[webgl] Launching Unity batchmode. First IL2CPP WebGL compile can take 30-60 min."
Write-Host "[webgl] Log: $log"
& $unity @unityArgs | Out-Null
$wrapperExit = $LASTEXITCODE

# Unity forks/relaunches, so poll until no 'Unity' process remains (60 min deadline).
$deadline = (Get-Date).AddMinutes(60)
Start-Sleep -Seconds 6
while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) {
        Start-Sleep -Seconds 4
        if (-not (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)) { break }
    }
    Start-Sleep -Seconds 15
}
$lock = Join-Path $proj 'Temp\UnityLockfile'
if (Test-Path $lock) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }

$license = $false
if (Test-Path $log) {
    $errorHit  = [bool](Select-String -Path $log -Pattern 'HandshakeResponse reported an error|No valid Unity Editor license|ResponseCode: 505|Unsupported protocol version' -Quiet -ErrorAction SilentlyContinue)
    $recovered = [bool](Select-String -Path $log -Pattern 'Successfully updated license|Successfully connected to LicensingClient' -Quiet -ErrorAction SilentlyContinue)
    $license   = $errorHit -and -not $recovered
}

# --- 4) Report ----------------------------------------------------------------
$index = Join-Path $outDir 'index.html'
if (Test-Path $index) {
    $mb = [math]::Round(((Get-ChildItem $outDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Write-Host "[webgl] SUCCESS -> $index (build dir ~$mb MB on disk)"

    # --- Restore extra static pages the Unity build itself doesn't produce ------
    # This project's Vercel static root IS this output dir (vercel.json
    # outputDirectory), so any hand-authored page (a video showcase, the
    # clan-chat.html Cherry embed host) has to live HERE to be servable at all -
    # and a fresh build wipes $outDir first, silently dropping them every time.
    # WebExtras/ is the durable source; site/clan-chat.html stays the canonical
    # source for that one file. Never treat a missing page here as fatal - a
    # missing extra should not fail the whole WebGL build.
    $webExtras = Join-Path $proj 'WebExtras'
    if (Test-Path $webExtras) {
        Copy-Item -Path (Join-Path $webExtras '*') -Destination $outDir -Recurse -Force
        Write-Host "[webgl] Restored WebExtras/ into $outDir (raids.html + media, etc.)"
    }
    $clanChatSrc = Join-Path $proj 'site\clan-chat.html'
    if (Test-Path $clanChatSrc) {
        Copy-Item -Path $clanChatSrc -Destination (Join-Path $outDir 'clan-chat.html') -Force
        Write-Host "[webgl] Restored site/clan-chat.html into $outDir"
    }

    if ($NoBrotli) {
        # itch.io packaging: no .br payloads, no Vercel header file, zip the folder.
        $br = Get-ChildItem $outDir -Recurse -File -Filter '*.br' -ErrorAction SilentlyContinue
        if ($br) {
            Write-Warning "[webgl] -NoBrotli set but $($br.Count) .br file(s) still present - compression flag did not take:"
            $br | ForEach-Object { Write-Host "    $($_.FullName)" }
        } else {
            Write-Host "[webgl] Verified: 0 .br files in output."
        }

        $vercel = Join-Path $outDir 'vercel.json'
        if (Test-Path $vercel) {
            Remove-Item $vercel -Force
            Write-Host "[webgl] Removed vercel.json (not used by itch.io static host)."
        }

        $zip = Join-Path $proj 'Builds\WebGL_noBrotli.zip'
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zip -Force
        $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
        Write-Host "[webgl] Packaged -> $zip (~$zipMb MB) - upload this to itch.io."
    } else {
        Write-Host "[webgl] (Brotli build; check .data payload against the 300 MB cap. For itch.io re-run with -NoBrotli.)"
    }
    exit 0
}

if ($license) { Write-Host "[webgl] *** LICENSE ERROR - needs interactive Hub refresh or reboot. ***" }
Write-Host "[webgl] FAILED - no index.html produced (wrapperExit=$wrapperExit, license=$license). Last 50 log lines:"
if (Test-Path $log) { Get-Content $log -Tail 50 }
if ($license) { exit 7 } else { exit 1 }
