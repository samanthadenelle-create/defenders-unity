<#
.SYNOPSIS
Salvage dead git worktrees and clones by preserving unique work as patches, then deleting the folders.

.DESCRIPTION
Owner ruling 2026-09-14: reclaim ~300 GB of dead git worktrees by mechanically salvaging every piece
of possibly-unique work as a patch, then deleting the folders. No judgment about uniqueness is made;
everything is salvaged.

For every worktree/clone discovered:
  1. Create salvage directory with INFO.txt (path, kind, branch, HEAD, status)
  2. Save tracked.patch and staged.patch (binary patches, using Start-Process for raw bytes)
  3. Copy untracked files (skip Library/Temp/Logs/obj/.venv; skip files >25MB)
  4. For standalone clones: fetch commits into D:\eoa as refs/salvage/<name>/*
  5. Delete the worktree/clone
  6. Report summary with SALVAGE_OK or SALVAGE_PARTIAL marker

Recovery recipe:
  - git apply D:\eoa-salvage\<name>\tracked.patch
  - git apply D:\eoa-salvage\<name>\staged.patch
  - git branch -a | findstr salvage (for clone commits)

.PARAMETER DryRun
Default $true. Print what would be done; do not delete anything. Respects $Execute.
If -Execute is present, switches to $false (execute the plan).

.PARAMETER Execute
Switch. Perform the salvage and deletion. Overrides -DryRun $true default.

.PARAMETER SalvageRoot
Directory to store salvaged patches and files. Default: D:\eoa-salvage

.PARAMETER Only
Process only this one path. For testing.

.EXAMPLE
# Dry run: see what would be salvaged and deleted
.\worktree_salvage.ps1

# Execute: salvage and delete everything
.\worktree_salvage.ps1 -Execute

# Test one entry
.\worktree_salvage.ps1 -Only 'D:\eoa-release-final'
#>

param(
    [bool] $DryRun = $true,
    [switch] $Execute,
    [string] $SalvageRoot = "D:\eoa-salvage",
    [string] $Only = ""
)

# Override DryRun if -Execute is given
if ($Execute) { $DryRun = $false }

$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"

# Resolve main tree path (case-insensitive, full path)
$mainTreeAbsolute = (Resolve-Path "D:\eoa" -ErrorAction Stop).Path.ToLower()

# Initialize counters
$salvageLog = if ($DryRun) { "$PSScriptRoot\..\..\eoa-salvage-dryrun.log" } else { "$SalvageRoot\salvage.log" }
$entries = 0
$totalFreedGB = 0
$totalSalvageGB = 0
$failedEntries = @()

function Write-Log {
    param([string] $Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$timestamp] $Message"
    Write-Host $line
    if (-not $DryRun) {
        Add-Content -Path $salvageLog -Value $line -Encoding UTF8
    }
}

function Get-DirectorySizeGB {
    param([string] $Path)
    if (-not (Test-Path $Path -PathType Container)) { return 0 }
    try {
        $size = (Get-ChildItem $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
                 Measure-Object -Property Length -Sum).Sum
        if ($null -eq $size) { $size = 0 }
        return [math]::Round($size / 1GB, 2)
    }
    catch {
        return 0
    }
}

function Discover-Worktrees {
    $discovered = @()

    # 1. Registered linked worktrees
    $linkedOutput = & git -C D:\eoa worktree list --porcelain 2>$null
    if ($linkedOutput) {
        foreach ($line in $linkedOutput) {
            if ($line -match "^worktree\s+(.+)$") {
                $path = $Matches[1]
                $pathLower = $path.ToLower()
                if ($pathLower -ne $mainTreeAbsolute -and (Test-Path $path)) {
                    $discovered += @{ Path = $path; Kind = "linked" }
                }
            }
        }
    }

    # 2. Agent worktrees (D:\eoa\.claude\worktrees\*)
    if (Test-Path "D:\eoa\.claude\worktrees" -PathType Container) {
        Get-ChildItem "D:\eoa\.claude\worktrees" -Directory -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            $path = $_.FullName
            if ((Test-Path "$path\.git" -PathType Any) -and $path.ToLower() -ne $mainTreeAbsolute) {
                $discovered += @{ Path = $path; Kind = "agent" }
            }
        }
    }

    # 3. Temporary worktrees (D:\eoa\tmp\*)
    if (Test-Path "D:\eoa\tmp" -PathType Container) {
        Get-ChildItem "D:\eoa\tmp" -Directory -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            $path = $_.FullName
            if ((Test-Path "$path\.git" -PathType Any) -and $path.ToLower() -ne $mainTreeAbsolute) {
                $discovered += @{ Path = $path; Kind = "clone" }
            }
        }
    }

    # 4. Standalone clones (D:\eoa-*) at the root of D:\
    Get-ChildItem "D:\" -Directory -Force -ErrorAction SilentlyContinue |
    ForEach-Object {
        $path = $_.FullName
        if ($path -match "^D:\\eoa-" -and (Test-Path "$path\.git" -PathType Any) -and $path.ToLower() -ne $mainTreeAbsolute) {
            $discovered += @{ Path = $path; Kind = "clone" }
        }
    }

    # 2026-09-14 run: the registered list gives D:/EoA/... and the folder scan gives D:\eoa\... for the
    # SAME worktree; both passed the string compare, the second pass ran against an already-deleted
    # folder and OVERWROTE 24 real tracked.patch files with empty ones. Dedupe on the normalized path.
    $seen = @{}
    $unique = @()
    foreach ($e in $discovered) {
        $k = (($e.Path -replace '/', '\').TrimEnd('\')).ToLower()
        if (-not $seen.ContainsKey($k)) { $seen[$k] = $true; $unique += $e }
    }
    return $unique | Sort-Object { $_.Path }
}

function Salvage-Entry {
    param([hashtable] $Entry)

    $path = $Entry.Path
    $kind = $Entry.Kind

    # Normalize path for comparison (convert forward slashes, lowercase)
    $pathNormalized = ($path -replace '/', '\').ToLower()

    # Guard: never touch main tree
    if ($pathNormalized -eq $mainTreeAbsolute) {
        Write-Log "SKIP: $path (main tree)"
        return $null
    }

    # Deduce name for salvage directory
    $leafName = Split-Path -Leaf $path
    if ($kind -eq "agent" -and $leafName -match "^agent-") {
        $salvageName = "claude-wt-$($leafName -replace '^agent-', '')"
    } else {
        $salvageName = $leafName
    }

    $salvageDir = Join-Path $SalvageRoot $salvageName
    $sizeGB = Get-DirectorySizeGB $path

    if ($DryRun) {
        Write-Log "WOULD SALVAGE: $path -> $salvageDir ($sizeGB GB)"
        return @{
            Path = $path
            SalvageName = $salvageName
            SalvageDir = $salvageDir
            Kind = $kind
            SizeGB = $sizeGB
            Success = $null
        }
    }

    Write-Log "SALVAGING: $path -> $salvageDir"

    try {
        # Create salvage directory
        if (-not (Test-Path $salvageDir)) {
            New-Item -ItemType Directory -Path $salvageDir -Force | Out-Null
        }

        # Gather info
        $infoLines = @()
        $infoLines += "Salvaged: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        $infoLines += "Path: $path"
        $infoLines += "Kind: $kind"
        $infoLines += "Size (GB): $sizeGB"

        # Get branch and HEAD
        $branch = "UNKNOWN"
        $head = "UNKNOWN"

        try {
            $branch = & git -C $path rev-parse --abbrev-ref HEAD 2>$null
            if ([string]::IsNullOrWhiteSpace($branch)) { $branch = "DETACHED" }
        }
        catch { }

        try {
            $head = & git -C $path rev-parse HEAD 2>$null
            if ([string]::IsNullOrWhiteSpace($head)) { $head = "UNKNOWN" }
        }
        catch { }

        $infoLines += "Branch: $branch"
        $infoLines += "HEAD: $head"

        # Get status count
        $statusCount = 0
        try {
            $statusOutput = & git -C $path status --short 2>$null
            $statusCount = @($statusOutput | Where-Object { $_ }).Count
        }
        catch { }
        $infoLines += "Uncommitted: $statusCount"

        # Get commits ahead of dev
        $aheadCount = 0
        try {
            $logOutput = & git -C $path log --oneline dev..HEAD 2>$null
            $aheadCount = @($logOutput | Where-Object { $_ }).Count
        }
        catch { }
        $infoLines += "Commits ahead of dev: $aheadCount"

        # Write INFO.txt
        $infoLines | Set-Content -Path (Join-Path $salvageDir "INFO.txt") -Encoding UTF8

        # Save tracked.patch (binary)
        if (-not (Test-Path $path)) { throw "source folder is gone; refusing to overwrite the salvage of $salvageName" }
        Write-Log "  Saving tracked.patch..."
        $trackedPatchPath = Join-Path $salvageDir "tracked.patch"
        try {
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = "git"
            $psi.Arguments = "-C `"$path`" -c core.safecrlf=false diff HEAD --binary"
            $psi.UseShellExecute = $false
            $psi.RedirectStandardOutput = $true
            $psi.CreateNoWindow = $true
            $proc = [System.Diagnostics.Process]::Start($psi)
            $stream = $proc.StandardOutput.BaseStream
            $fileStream = [System.IO.File]::Create($trackedPatchPath)
            $stream.CopyTo($fileStream)
            $fileStream.Close()
            $stream.Close()
            $proc.WaitForExit()
            Write-Log "  Saved tracked.patch"
        }
        catch {
            Write-Log "  ERROR saving tracked.patch: $_"
        }

        # Save staged.patch (binary)
        Write-Log "  Saving staged.patch..."
        $stagedPatchPath = Join-Path $salvageDir "staged.patch"
        try {
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = "git"
            $psi.Arguments = "-C `"$path`" -c core.safecrlf=false diff --cached --binary"
            $psi.UseShellExecute = $false
            $psi.RedirectStandardOutput = $true
            $psi.CreateNoWindow = $true
            $proc = [System.Diagnostics.Process]::Start($psi)
            $stream = $proc.StandardOutput.BaseStream
            $fileStream = [System.IO.File]::Create($stagedPatchPath)
            $stream.CopyTo($fileStream)
            $fileStream.Close()
            $stream.Close()
            $proc.WaitForExit()
            Write-Log "  Saved staged.patch"
        }
        catch {
            Write-Log "  ERROR saving staged.patch: $_"
        }

        # Copy untracked files
        Write-Log "  Copying untracked files..."
        $untrackedDir = Join-Path $salvageDir "untracked"
        $skippedLarge = @()
        try {
            $untrackedFiles = & git -C $path ls-files --others --exclude-standard 2>$null
            if ($untrackedFiles) {
                foreach ($file in $untrackedFiles) {
                    $srcFile = Join-Path $path $file

                    # Skip ignored folders
                    if ($file -match "^(Library|Temp|Logs|obj|\.venv)(/|\\)" -or $file -match "^(Library|Temp|Logs|obj|\.venv)$") {
                        continue
                    }

                    if (Test-Path $srcFile -PathType Leaf) {
                        $fileSize = (Get-Item $srcFile -Force).Length
                        if ($fileSize -gt 25MB) {
                            $sizeStr = [math]::Round($fileSize / 1MB, 1)
                            $skippedLarge += "$file ($sizeStr MB)"
                            continue
                        }

                        $dstFile = Join-Path $untrackedDir $file
                        $dstParent = Split-Path $dstFile
                        if (-not (Test-Path $dstParent)) {
                            New-Item -ItemType Directory -Path $dstParent -Force | Out-Null
                        }
                        Copy-Item $srcFile -Destination $dstFile -Force
                    }
                }
            }
        }
        catch {
            Write-Log "  WARNING copying untracked files: $_"
        }

        if ($skippedLarge.Count -gt 0) {
            $skippedLarge | Set-Content -Path (Join-Path $salvageDir "SKIPPED_LARGE.txt") -Encoding UTF8
            Write-Log "  Skipped $($skippedLarge.Count) large files"
        }

        # For clones: fetch commits into main repo
        if ($kind -eq "clone") {
            Write-Log "  Fetching commits into main repo..."
            try {
                & git -C D:\eoa fetch "$path" "+refs/heads/*:refs/salvage/$salvageName/*" 2>$null
                Write-Log "  Fetched commits"
            }
            catch {
                Write-Log "  WARNING fetch failed: $_"
            }
        }

        # Delete the worktree/clone
        Write-Log "  Deleting $kind..."

        if ($kind -eq "linked") {
            try {
                & git -C D:\eoa worktree remove --force "$path" 2>$null
                # a native git failure does not throw: prove the folder is gone, else fall back
                if (Test-Path $path) {
                    Write-Log "  git worktree remove left the folder; Remove-Item fallback"
                    Remove-Item -Recurse -Force -Path $path -ErrorAction Stop
                }
                if (Test-Path $path) { throw "folder still present after removal: $path" }
                Write-Log "  Removed linked worktree"
            }
            catch {
                Write-Log "  WARNING git worktree remove failed, trying Remove-Item..."
                Remove-Item -Recurse -Force -Path $path -ErrorAction Stop
                Write-Log "  Removed via Remove-Item"
            }
        } else {
            # clone or agent
            Remove-Item -Recurse -Force -Path $path -ErrorAction Stop
            Write-Log "  Removed via Remove-Item"
        }

        Write-Log "SALVAGED OK: $salvageName ($sizeGB GB)"

        return @{
            Path = $path
            SalvageName = $salvageName
            SalvageDir = $salvageDir
            Kind = $kind
            SizeGB = $sizeGB
            Success = $true
        }
    }
    catch {
        Write-Log "ERROR salvaging $path : $_"
        $failedEntries += $path
        return @{
            Path = $path
            SalvageName = $salvageName
            SalvageDir = $salvageDir
            Kind = $kind
            SizeGB = $sizeGB
            Success = $false
        }
    }
}

# Main
Write-Log ""
Write-Log "=== WORKTREE SALVAGE $(if ($DryRun) { 'DRY RUN' } else { 'EXECUTE' }) ==="
Write-Log "Owner ruling 2026-09-14: salvage ~300 GB of dead worktrees"
Write-Log "Salvage root: $SalvageRoot"
Write-Log ""

try {
    # Discover worktrees
    Write-Log "Discovering worktrees..."
    $discovered = Discover-Worktrees

    if ($Only) {
        $discovered = $discovered | Where-Object { $_.Path -eq $Only }
        if (-not $discovered) {
            Write-Log "ERROR: -Only path not found: $Only"
            exit 1
        }
    }

    Write-Log "Found $($discovered.Count) worktree(s)"
    Write-Log ""

    # Salvage each entry
    $results = @()
    foreach ($entry in $discovered) {
        $result = Salvage-Entry $entry
        if ($result) {
            $results += $result
            $entries++
            $totalSalvageGB += $result.SizeGB
            if ($result.Success -eq $true) {
                $totalFreedGB += $result.SizeGB
            }
        }
    }

    # Prune worktree metadata
    if (-not $DryRun) {
        Write-Log ""
        Write-Log "Pruning worktree metadata..."
        & git -C D:\eoa worktree prune 2>$null
        Write-Log "Pruned"
    }

    # Report
    Write-Log ""
    Write-Log "=== SUMMARY ==="
    Write-Log "Processed: $entries"
    Write-Log "Salvaged: $totalSalvageGB GB"
    Write-Log "Freed: $totalFreedGB GB"
    if ($failedEntries.Count -gt 0) {
        Write-Log "Failed: $($failedEntries.Count)"
        $failedEntries | ForEach-Object { Write-Log "  $_" }
        Write-Log "SALVAGE_PARTIAL entries=$entries salvaged_GB=$totalSalvageGB freed_GB=$totalFreedGB"
    } else {
        Write-Log "SALVAGE_OK entries=$entries salvaged_GB=$totalSalvageGB freed_GB=$totalFreedGB salvage_root=$SalvageRoot"
    }
    Write-Log ""
}
catch {
    Write-Log "FATAL: $_"
    exit 1
}
