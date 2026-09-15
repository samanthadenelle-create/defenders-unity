# =============================================================================
# google-play-aab-build.ps1 - THE one sanctioned way a Google Play AAB is produced.
#
# WO-1365. Modelled on overnight-apk-build.ps1 deliberately: the AAB lane had NONE
# of the gate discipline the APK lane has. Before this file existed:
#   1. NO WRAPPER  - no .ps1 anywhere invoked DeNelle.Editor.AndroidBuild.BuildGooglePlayAab.
#      The 2026-09-01 AAB came from a hand-assembled Unity.exe command line with no
#      -ExpectMarker, so its evidence was PASS-UNASSERTED shaped at best. CLAUDE.md s16's
#      lesson exactly: a raw invocation bypasses every gate the scripts hold.
#   2. NEVER PUSHED R2 - AndroidBuild.cs shells out to nothing, and every caller of
#      tools/r2-ship.ps1 was an APK/Windows lane. But the AAB resolves the SAME remote
#      catalog as the APK (AddressableAssetSettings Remote.LoadPath .../[BuildTarget],
#      BuildTarget = activeBuildTarget => .../Android/), and every build stamps a new
#      version and therefore requests a NEW content-hashed catalog. Without a push, an
#      installed AAB 404s its art and the player sees capsule enemies WITH NO ERROR.
#      That is s16 occurrence FIVE, and it was waiting to happen.
#   3. NOTHING ASSERTED SIZE - AndroidBuild.cs:201 PRINTS the size and does not gate.
#      That is how 31 MB appeared in two days with every marker green, taking the
#      artifact from 482,843,623 bytes (RC, 08-30) to a bundletool-measured
#      510,443,276 - 510,523,099 - OVER Play's 500 MB base-module ceiling.
#
# ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less files as ANSI, so
# smart quotes and em dashes corrupt the parse.
#
# Repo root is machine-dependent (C:\eoa on one box, D:\eoa on another - CLAUDE.md s0),
# so it is resolved from this script's own location, never hardcoded. The Unity editor
# root is resolved the same way run-unity-method.ps1 resolves it - by SEARCHING the Hub,
# never by hardcoding 6000.4.8f1, which would break on the next editor upgrade.
#
# -----------------------------------------------------------------------------
# USAGE
#   powershell -ExecutionPolicy Bypass -File .\google-play-aab-build.ps1
#       Full chain: signing preflight -> build -> R2 push+verify (BLOCKS) -> size guard.
#
#   powershell -ExecutionPolicy Bypass -File .\google-play-aab-build.ps1 -MeasureOnly
#       No build, no push. Measures the AAB already on disk with bundletool and emits
#       the size marker. This is how you prove the guard RED against a fat artifact.
#
#   ... -SizeCeilingBytes 524288000     Raise/lower the ceiling (default 500,000,000).
#   ... -Defines 'FOO;BAR'              Player scripting defines, forwarded verbatim.
#
# MARKERS (judge by MARKER on a fresh log, NEVER the exit code - the runners in this
# repo exit 0 on refusals and FAILs; memory: gates-report-success-without-proving-it):
#   AAB_SIGNING_OK / AAB_SIGNING_FAIL     release keystore proven before and after the build
#   [AndroidBuild] SUCCEEDED              asserted via run-unity-method -ExpectMarker
#   AAB_REJECTED                          WO-1739. The Play COMPLIANCE gate did not pass:
#                                         the SUCCEEDED marker is absent from a fresh log,
#                                         or the log carries PLAY_ARTIFACT_REJECTED /
#                                         PLAY_ARTIFACT_DIRTY / PLAY_SOURCE_ISOLATION_FAIL /
#                                         PLAY_NEUTRAL_*. The run STOPS here: no AAB_OK, no
#                                         AAB_SIGNING_OK, no R2, no size marker, and the
#                                         artifact is MOVED to Builds\Android\rejected\ so
#                                         nothing uploadable is left at the build path.
#                                         Marker ABSENCE is a FAILURE, never an unknown.
#   AAB_BUILD_UNPROVEN                    why the verdict above was reached (stale log /
#                                         no marker / no log).
#   AAB_QUARANTINED                       where the rejected artifact was moved.
#   AAB_STALE                             newest AAB predates this run => the build made none
#   R2_PARITY_OK / R2_PARITY_FAILED       from tools\r2-ship.ps1, mirrored into the status file
#   AAB_SIZE_OK <bytes> (<margin> under <ceiling>)
#   AAB_SIZE_FAIL <bytes> (<over> OVER <ceiling>)
#   AAB_SIZE_UNMEASURED                   bundletool/java could not be located => FAIL CLOSED
#   AAB_DONE
#
# Exit codes: 0 ok. 1 build produced no fresh AAB. 3 R2 parity failed.
#             5 signing would be/was DEBUG. 6 size guard failed or could not measure.
#             7 the Play COMPLIANCE gate did not pass (WO-1739) - the artifact exists, is
#               release-signed and is under the ceiling, and is STILL not shippable.
# =============================================================================
param(
    [string]$Defines = '',
    # Play's base-module compressed-download ceiling. 500 MB, decimal reading.
    # NOTE: the MB-vs-MiB ambiguity is REAL and worth ~14 MB (500,000,000 vs 524,288,000)
    # and Google documents neither. WO-1365 ruled: engineer to the STRICT reading. Do
    # not raise this default to the generous one to make a build pass.
    [long]$SizeCeilingBytes = 500000000,
    # Measure the artifact already on disk. No Unity, no push.
    [switch]$MeasureOnly,
    # Skip the bundletool measurement (it costs ~2-4 min and a ~1 GB temp file).
    # Deliberately NOT the default: an unmeasured AAB is exactly the artifact this
    # ticket exists to stop.
    [switch]$SkipSizeGuard
)

Set-Location $PSScriptRoot
$root      = $PSScriptRoot
$status    = Join-Path $root 'Builds\aab-status.txt'
$aabPath   = Join-Path $root 'Builds\Android\EchoesOfElarion-GooglePlay.aab'
$buildLog  = Join-Path $root 'Builds\aab-build.log'
$parityLog = Join-Path $root 'Builds\r2-parity.log'
$startedAt = Get-Date

New-Item -ItemType Directory -Force -Path (Join-Path $root 'Builds') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $root 'Builds\Android') | Out-Null

function Say {
    param([string]$Line)
    Write-Host "[aab] $Line"
    $Line | Out-File -Encoding ascii -Append $status
}

if (-not $MeasureOnly) {
    "AAB_START $(Get-Date -Format o)" | Out-File -Encoding ascii $status
} else {
    "AAB_MEASURE_ONLY $(Get-Date -Format o)" | Out-File -Encoding ascii $status
}

# -----------------------------------------------------------------------------
# 1. SIGNING PREFLIGHT - refuse BEFORE burning 15 minutes on a debug-signed AAB.
#
# ApplyReleaseSigning (AndroidBuild.cs:367-404) reads a gitignored keystore.properties
# at the repo root. If it is ABSENT or INCOMPLETE it sets useCustomKeystore = false,
# logs a WARNING, and the build proceeds happily to a DEBUG-SIGNED artifact - which
# Play rejects at upload, 15 minutes and a human's attention later. A warning is not
# a gate. This is, and it checks the same four keys the C# requires.
# -----------------------------------------------------------------------------
function Test-ReleaseSigningReady {
    $propsPath = Join-Path $root 'keystore.properties'
    if (-not (Test-Path $propsPath)) {
        Say "AAB_SIGNING_FAIL keystore.properties not found at $propsPath - the build would fall back to DEBUG signing, which Play rejects."
        return $false
    }
    $kv = @{}
    foreach ($raw in (Get-Content $propsPath)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0) { continue }
        if ($line.StartsWith('#')) { continue }
        $eq = $line.IndexOf('=')
        if ($eq -le 0) { continue }
        $kv[$line.Substring(0, $eq).Trim()] = $line.Substring($eq + 1).Trim()
    }
    foreach ($key in @('keystore.path', 'keystore.alias', 'keystore.storepass', 'keystore.keypass')) {
        if (-not $kv.ContainsKey($key)) {
            Say "AAB_SIGNING_FAIL keystore.properties is missing '$key' - DEBUG signing fallback."
            return $false
        }
        if ([string]::IsNullOrWhiteSpace($kv[$key])) {
            Say "AAB_SIGNING_FAIL keystore.properties '$key' is empty - DEBUG signing fallback."
            return $false
        }
    }
    if (-not (Test-Path $kv['keystore.path'])) {
        Say "AAB_SIGNING_FAIL keystore file '$($kv['keystore.path'])' does not exist - DEBUG signing fallback."
        return $false
    }
    Say "AAB_SIGNING_PREFLIGHT_OK keystore='$(Split-Path -Leaf $kv['keystore.path'])' alias='$($kv['keystore.alias'])'"
    return $true
}

# -----------------------------------------------------------------------------
# 2. THE SIZE GUARD.
#
# bundletool and a JDK are ALREADY on this machine - they ship with Unity's Android
# module - so this costs nothing to install:
#   <editor>/Editor/Data/PlaybackEngines/AndroidPlayer/Tools/bundletool-all-*.jar
#   <editor>/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe
# java is NOT on PATH. Both are invoked by full path, and the editor is FOUND, not
# hardcoded.
#
# build-apks writes a ~1.0-1.5 GB .apks intermediate. It is deleted in a finally
# block: three stale ones at 1.49 GB each were already sitting in Builds/Android
# when this ticket was written. A size guard that fills the disk is its own defect.
#
# --modules=base is the number Play enforces, and it is also the whole app here:
# BundleConfig.pb declares modules {base} only. MIN and MAX differ by ~80 KB because
# the payload lives in assets/, which is never split by density or language. MAX is
# treated as binding - Google's wording is "any of the possible downloads".
# -----------------------------------------------------------------------------
function Resolve-AndroidPlayerTools {
    $hubEditors = 'C:\Program Files\Unity\Hub\Editor'
    $pinned     = '6000.4.8f1'
    if (-not (Test-Path $hubEditors)) { return $null }
    $dirs = Get-ChildItem $hubEditors -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'Editor\Data\PlaybackEngines\AndroidPlayer\Tools') }
    if (-not $dirs) { return $null }
    $chosen = $dirs | Where-Object { $_.Name -eq $pinned } | Select-Object -First 1
    if (-not $chosen) { $chosen = $dirs | Where-Object { $_.Name -like '6000.*' } | Sort-Object Name -Descending | Select-Object -First 1 }
    if (-not $chosen) { $chosen = $dirs | Sort-Object Name -Descending | Select-Object -First 1 }
    $player = Join-Path $chosen.FullName 'Editor\Data\PlaybackEngines\AndroidPlayer'
    $jar = Get-ChildItem (Join-Path $player 'Tools') -Filter 'bundletool-all-*.jar' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    $java = Join-Path $player 'OpenJDK\bin\java.exe'
    if (-not $jar) { return $null }
    if (-not (Test-Path $java)) { return $null }
    return [pscustomobject]@{ Editor = $chosen.Name; Jar = $jar.FullName; Java = $java }
}

function Invoke-SizeGuard {
    if (-not (Test-Path $aabPath)) {
        Say "AAB_SIZE_UNMEASURED no AAB at $aabPath"
        return $false
    }
    $rawBytes = (Get-Item $aabPath).Length
    Say "AAB_ON_DISK $rawBytes bytes ($([math]::Round($rawBytes/1MB,1)) MiB) $aabPath"

    $tools = Resolve-AndroidPlayerTools
    if (-not $tools) {
        Say "AAB_SIZE_UNMEASURED could not locate bundletool-all-*.jar + OpenJDK under any Unity Hub editor's AndroidPlayer. FAILING CLOSED - an unmeasured AAB is the artifact this gate exists to stop."
        return $false
    }
    Say "AAB_SIZE_TOOLS editor=$($tools.Editor) jar=$(Split-Path -Leaf $tools.Jar)"

    $apks = Join-Path $root 'Builds\Android\aab-size-measure.apks'
    if (Test-Path $apks) { Remove-Item $apks -Force -ErrorAction SilentlyContinue }
    $measured = $null
    try {
        Write-Host "[aab] bundletool build-apks (this writes a ~1 GB temp file and takes a few minutes)..."
        & $tools.Java -jar $tools.Jar build-apks --bundle=$aabPath --output=$apks --mode=default --overwrite 2>&1 |
            Tee-Object -FilePath (Join-Path $root 'Builds\aab-size-measure.log') | Out-Null
        if (-not (Test-Path $apks)) {
            Say "AAB_SIZE_UNMEASURED bundletool build-apks produced no .apks - see Builds\aab-size-measure.log"
            return $false
        }
        $out = & $tools.Java -jar $tools.Jar get-size total --apks=$apks --modules=base 2>&1
        $out | Out-File -Encoding ascii -Append (Join-Path $root 'Builds\aab-size-measure.log')
        foreach ($line in $out) {
            $t = "$line".Trim()
            if ($t -match '^(\d+),(\d+)$') {
                # MIN,MAX. MAX is binding - "any of the possible downloads".
                $measured = [long]$Matches[2]
            }
        }
    } finally {
        # ALWAYS. Three 1.49 GB strays from 08-30 are why this is a finally and not a
        # tidy-up at the end of the happy path.
        if (Test-Path $apks) {
            Remove-Item $apks -Force -ErrorAction SilentlyContinue
            Write-Host "[aab] removed the .apks intermediate."
        }
    }

    if ($null -eq $measured) {
        Say "AAB_SIZE_UNMEASURED bundletool get-size printed no MIN,MAX row - see Builds\aab-size-measure.log"
        return $false
    }

    if ($measured -gt $SizeCeilingBytes) {
        $over = $measured - $SizeCeilingBytes
        Say "AAB_SIZE_FAIL $measured ($over OVER $SizeCeilingBytes)"
        Say "  Play enforces the base-module compressed download AT UPLOAD, in the Console. This artifact cannot be uploaded."
        Say "  bundletool is 'a similar (but not identical) calculation' to the Console's - treat this as a close estimate, never the Console's verdict."
        Say "  Where the bytes are (measured 2026-09-04): 418 MiB of base/assets/bin/Data = scenes + Assets/Resources/, dominated by UI/icon textures. Addressables ships only 15.87 MiB locally, so the CDN is NOT the lever here."
        return $false
    }

    $margin = $SizeCeilingBytes - $measured
    Say "AAB_SIZE_OK $measured ($margin under $SizeCeilingBytes)"
    return $true
}

# -----------------------------------------------------------------------------
# MEASURE-ONLY: prove the guard RED (or green) against whatever is on disk.
# -----------------------------------------------------------------------------
if ($MeasureOnly) {
    $sizeOk = Invoke-SizeGuard
    Say "AAB_DONE $(Get-Date -Format o)"
    if (-not $sizeOk) { exit 6 }
    exit 0
}

# -----------------------------------------------------------------------------
# 3. FULL CHAIN
# -----------------------------------------------------------------------------
if (-not (Test-ReleaseSigningReady)) {
    Write-Host "[aab] refusing to build - see $status"
    Say "AAB_DONE $(Get-Date -Format o)"
    exit 5
}

# -BuildTarget Android is passed for the same reason overnight-apk-build.ps1 passes it
# (WO-1124): the Addressables content build runs BEFORE BuildPipeline.BuildPlayer and
# builds for the ACTIVE target, so content can land in the wrong platform folder.
# BuildAndroidArtifact now switches the target itself; a redundant switch is free.
#
# -ExpectMarker is THE point of item 1 of this ticket. Without it a run that never
# started, or a stale log from a previous run, reads exactly like a pass.
$buildProven = $true
try {
    & (Join-Path $root 'run-unity-method.ps1') `
        -Method DeNelle.Editor.AndroidBuild.BuildGooglePlayAab `
        -LogName aab-build.log `
        -TimeoutMin 120 `
        -BuildTarget Android `
        -ExtraScriptingDefines $Defines `
        -ExpectMarker '[AndroidBuild] SUCCEEDED'
    if ($LASTEXITCODE -ne 0) {
        Say "AAB_BUILD_MARKER_ABSENT run-unity-method exit=$LASTEXITCODE - see $buildLog"
        $buildProven = $false
    }
} catch {
    Say "AAB_THREW $($_.Exception.Message)"
    $buildProven = $false
}

# -----------------------------------------------------------------------------
# WO-1739 - THE HARD STOP. THIS BLOCK IS WHY THE FILE EXISTS AT ALL.
#
# Until 2026-09-15 the two branches above only RECORDED the failure and fell through,
# and every marker after them was emitted over a rejected artifact. MEASURED, twice:
#   Builds/aab-final-chain-runner.log (2026-09-11 18:59) and Builds/aab-status.txt
#   (2026-09-15 10:22-10:37) both read
#       AAB_BUILD_MARKER_ABSENT run-unity-method exit=8
#       AAB_OK ... AAB_SIGNING_OK ... R2_PARITY_OK ... AAB_SIZE_OK ... AAB_DONE
#   and exited 0, over an AAB that GooglePlayPackagingGate.AssertBuiltArtifact had
#   REJECTED as carrying a forbidden crypto/wallet surface. A human reading the status
#   file would have uploaded a policy-violating artifact to Google Play.
#
# THE FRESHNESS CHECK BELOW CANNOT CATCH THIS, and that is the whole trap: the Play
# compliance gate runs AFTER BuildPipeline.BuildPlayer succeeds (AndroidBuild.cs:197-204),
# so a REJECTED run still leaves a fresh, correctly release-signed, correctly sized AAB
# at $aabPath. Every existence-and-freshness test passes. The MARKER is the only signal
# there is - which is exactly CLAUDE.md s8 / memory gates-report-success-without-proving-it:
# judge by the marker on a fresh log, and treat its ABSENCE as a FAILURE, never an unknown.
#
# So the verdict is taken from the LOG, not from the exit code alone:
#   - the log must postdate this run (a stale log is not evidence of this build), AND
#   - it must carry '[AndroidBuild] SUCCEEDED', AND
#   - it must NOT carry a Play compliance rejection.
# The $LASTEXITCODE test above is KEPT as well, because an unset $LASTEXITCODE is $null
# and '$null -ne 0' is TRUE - it fails CLOSED (memory prove-the-success-path).
#
# On failure the artifact is MOVED OUT of $aabPath into Builds\Android\rejected\. An
# unusable 434 MiB signed .aab sitting at the path a human has been told to upload from
# is itself the hazard; nothing shippable-looking may survive a rejected run.
# -----------------------------------------------------------------------------
$rejectionPatterns = @(
    'PLAY_ARTIFACT_REJECTED',
    'PLAY_ARTIFACT_DIRTY',
    'PLAY_ARTIFACT_MISSING',
    'PLAY_SOURCE_ISOLATION_FAIL',
    'PLAY_NEUTRAL_UNMAPPED_TOKEN',
    'PLAY_NEUTRAL_REWRITE_FAIL'
)
$rejectionHits = @()
if (-not (Test-Path $buildLog)) {
    Say "AAB_BUILD_UNPROVEN no build log at $buildLog - this run proved nothing."
    $buildProven = $false
} else {
    if ((Get-Item $buildLog).LastWriteTime -lt $startedAt) {
        Say "AAB_BUILD_UNPROVEN $buildLog is STALE (predates this run). The proof must postdate the bytes it claims to prove."
        $buildProven = $false
    }
    if (-not (Select-String -Path $buildLog -Pattern '\[AndroidBuild\] SUCCEEDED' -Quiet)) {
        Say "AAB_BUILD_UNPROVEN the build log carries no '[AndroidBuild] SUCCEEDED' marker. Marker ABSENCE is a FAILURE, not an unknown."
        $buildProven = $false
    }
    foreach ($pattern in $rejectionPatterns) {
        $found = Select-String -Path $buildLog -Pattern $pattern -SimpleMatch
        if ($found) {
            $buildProven = $false
            $rejectionHits += ($found | Select-Object -First 25 | ForEach-Object { $_.Line.Trim() })
        }
    }
}

if (-not $buildProven) {
    if ($rejectionHits.Count -gt 0) {
        Say "AAB_REJECTED $(Get-Date -Format o) - THE PLAY COMPLIANCE GATE REJECTED THIS ARTIFACT. DO NOT UPLOAD ANYTHING FROM THIS RUN."
    } else {
        # Keep the marker honest: no compliance rejection was found in the log, so the
        # failure is the BUILD being unproven (Gradle death, timeout, stale/absent log).
        # Same stop, same exit code - a different reason, and the reader must not be sent
        # hunting for a crypto surface that was never named.
        Say "AAB_REJECTED $(Get-Date -Format o) - no compliance rejection was found in the log; the BUILD ITSELF is unproven (see the AAB_BUILD_UNPROVEN line above). DO NOT UPLOAD ANYTHING FROM THIS RUN."
    }
    if ($rejectionHits.Count -gt 0) {
        Say "  The gate named these offenders (verbatim from $buildLog):"
        foreach ($hit in $rejectionHits) { Say "    $hit" }
        # The DIRTY block is a bulleted list under its header; carry it across too so the
        # status file names the actual entries and tokens without a second log read.
        $dirty = Select-String -Path $buildLog -Pattern 'PLAY_ARTIFACT_DIRTY' -SimpleMatch -Context 0, 60
        if ($dirty) {
            foreach ($line in ($dirty | Select-Object -First 1).Context.PostContext) {
                $t = "$line".Trim()
                if ($t -notmatch '^\s*-\s') { break }
                Say "    $t"
            }
        }
    }

    # Quarantine the artifact. It is signed and correctly sized, so nothing about the
    # FILE warns a human off it - only its location can.
    if (Test-Path $aabPath) {
        $rejectDir = Join-Path $root 'Builds\Android\rejected'
        New-Item -ItemType Directory -Force -Path $rejectDir | Out-Null
        $stamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
        $rejectPath = Join-Path $rejectDir "EchoesOfElarion-GooglePlay-$stamp.REJECTED.aab"
        try {
            Move-Item -LiteralPath $aabPath -Destination $rejectPath -Force
            Say "  AAB_QUARANTINED the artifact was MOVED to $rejectPath so nothing uploadable is left at $aabPath."
        } catch {
            Say "  AAB_QUARANTINE_FAILED could not move $aabPath ($($_.Exception.Message)). DELETE IT BY HAND - it is NOT shippable."
        }
    }

    Say "  No AAB_OK, no AAB_SIGNING_OK, no size marker will be emitted for this run."
    Say "  Next: read $buildLog and fix what it names, then rebuild. If the gate named a crypto/wallet"
    Say "  surface, the GATE IS NOT THE DEFECT - see WorkOrders/WORK_ORDER_1740_play_aab_forbidden_surface_four_leaks.md."
    Say "AAB_DONE $(Get-Date -Format o)"
    exit 7
}

# STOP - FRESHNESS, NOT EXISTENCE. overnight-apk-build.ps1 carries this lesson in its
# own comments because on 2026-08-19 a FAILED build left an older artifact on disk and
# the chain reported its size as success, so a stale build reached the owner's device.
if (-not (Test-Path $aabPath)) {
    Say "AAB_FAILED_NO_AAB $(Get-Date -Format o) - no artifact at $aabPath; see $buildLog"
    Say "AAB_DONE $(Get-Date -Format o)"
    exit 1
}
$aabItem = Get-Item $aabPath
if ($aabItem.LastWriteTime -lt $startedAt) {
    Say "AAB_STALE $(Get-Date -Format o) $aabPath is dated $($aabItem.LastWriteTime.ToString('o')) - OLDER than this run. The build produced NO aab; see $buildLog. DO NOT UPLOAD IT."
    Say "AAB_DONE $(Get-Date -Format o)"
    exit 1
}
Say "AAB_OK $(Get-Date -Format o) path=$aabPath size=$($aabItem.Length)"

# Signing, PROVEN from the build's own log rather than from the preflight's intent.
# The preflight proves the inputs exist; this proves ApplyReleaseSigning actually took
# the release branch (AndroidBuild.cs:398-403) and not the DEBUG fallback.
if (Select-String -Path $buildLog -Pattern '\[AndroidBuild\] RELEASE signing:' -Quiet) {
    $sig = (Select-String -Path $buildLog -Pattern '\[AndroidBuild\] RELEASE signing:' | Select-Object -First 1).Line.Trim()
    Say "AAB_SIGNING_OK $sig"
} else {
    Say "AAB_SIGNING_FAIL the build log carries no '[AndroidBuild] RELEASE signing:' line - this AAB is DEBUG SIGNED and Play will reject it. See $buildLog"
    Say "AAB_DONE $(Get-Date -Format o)"
    exit 5
}

# --- Content: push, then PROVE it is hosted ----------------------------------
# Delegated to tools/r2-ship.ps1, the ONE file (owner ruling 2026-08-20). Do NOT
# re-inline the push or the verify here: s16 records that the copy-pasted pair had
# already drifted between overnight-apk-build and morning-ship-chain, one of them
# silently doing only half the job.
#
# The AAB needs this as much as the APK does: it resolves the same .../Android/
# remote catalog, and every build stamps a new version, so it asks for a NEW
# content-hashed catalog that no previous push can cover.
try {
    & powershell -NoProfile -File (Join-Path $root 'tools\r2-ship.ps1')
} catch {
    Say "R2_SHIP_THREW $($_.Exception.Message)"
}

$parityFresh = $false
if (Test-Path $parityLog) {
    if ((Get-Item $parityLog).LastWriteTime -ge $startedAt) { $parityFresh = $true }
}
if ($parityFresh -and (Select-String -Path $parityLog -Pattern 'R2_PARITY_OK' -Quiet)) {
    $line = (Select-String -Path $parityLog -Pattern 'R2_PARITY_OK' | Select-Object -First 1).Line.Trim()
    Say "R2_PARITY_OK $(Get-Date -Format o) $line"
} else {
    if (-not $parityFresh) {
        Say "R2_PARITY_FAILED $(Get-Date -Format o) - $parityLog is missing or STALE (predates this run). The proof must postdate the bytes it claims to prove."
    } else {
        Say "R2_PARITY_FAILED $(Get-Date -Format o) - this AAB references bundles the bucket does not hold."
    }
    Say "  DO NOT UPLOAD OR DISTRIBUTE THIS BUILD. Players would see capsule enemies and placeholder"
    Say "  buildings, with no error on screen. Fix: powershell -File tools\r2-ship.ps1"
    Say "  See $parityLog"
    Say "AAB_DONE $(Get-Date -Format o)"
    exit 3
}

# --- Size, last, because it is the gate that decides whether it can be uploaded ---
if ($SkipSizeGuard) {
    Say "AAB_SIZE_SKIPPED -SkipSizeGuard was passed. This build is UNMEASURED and must not be uploaded on this run's evidence."
    Say "AAB_DONE $(Get-Date -Format o)"
    exit 0
}
$sizeOk = Invoke-SizeGuard
Say "AAB_DONE $(Get-Date -Format o)"
if (-not $sizeOk) { exit 6 }
exit 0
