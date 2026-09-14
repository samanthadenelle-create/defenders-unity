# f8-watch-daemon.ps1 - Persistent F8 / break-log watcher (auto-rearm forever).
# Start: f8-watch-start.ps1 | Poll: f8-check-inbox.ps1 | Stop: f8-watch-stop.ps1
#
# WO-965: every capture is now APPENDED to logs/f8-inbox/QUEUE.jsonl via f8-inbox-lib.ps1.
# LATEST_CAPTURE.md + PING.json still hold the newest capture (unchanged contract) but they are
# a VIEW; the queue is the record, so a burst can no longer collapse to its newest member.

param(
    [int]$PollSeconds = 5,
    # WO-1709 test seams. Production defaults are unchanged ('' / 0 = use the real paths, run
    # forever), so the daemon behaves EXACTLY as before unless a harness overrides them. They exist
    # because the replay defect below can only be proven by running THIS script against a scratch
    # inbox + a scratch break-log; copying its functions into a throwaway proves the copy, not the
    # daemon.
    [string]$InboxOverride = '',
    [string]$BreakLogOverride = '',
    [string]$EditorLogOverride = '',
    [string]$PlayerLogOverride = '',
    [int]$MaxPasses = 0
)

$ErrorActionPreference = 'SilentlyContinue'

. (Join-Path $PSScriptRoot 'f8-inbox-lib.ps1')

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$Inbox    = Join-Path $RepoRoot 'logs\f8-inbox'
if ($InboxOverride) { $Inbox = $InboxOverride }
$PidFile  = Join-Path $Inbox 'daemon.pid'
$PingFile = Join-Path $Inbox 'PING.json'
$Latest   = Join-Path $Inbox 'LATEST_CAPTURE.md'
$StateFile = Join-Path $Inbox 'daemon-state.json'

# Desktop persistentDataPath = LocalLow\<companyName>\<productName>. productName became
# "Echoes of Elarion" on 2026-08-08 (store-listing match), which MOVES this folder. Prefer the
# new one; fall back to the legacy folder so captures made by an older player still triage.
$BreakLogDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\DeNelle\Echoes of Elarion'
$LegacyLogDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\DeNelle\Defenders of the Realm'
if ((-not (Test-Path $BreakLogDir)) -and (Test-Path $LegacyLogDir)) { $BreakLogDir = $LegacyLogDir }
$BreakLog    = Join-Path $BreakLogDir 'break-log.jsonl'
$PlayerLog   = Join-Path $BreakLogDir 'Player.log'
$EditorLog   = Join-Path $env:LOCALAPPDATA 'Unity\Editor\Editor.log'
if ($BreakLogOverride)  { $BreakLog  = $BreakLogOverride }
if ($PlayerLogOverride) { $PlayerLog = $PlayerLogOverride }
if ($EditorLogOverride) { $EditorLog = $EditorLogOverride }

New-Item -ItemType Directory -Force -Path $Inbox | Out-Null

$myPid = $PID
if (Test-Path $PidFile) {
    $old = (Get-Content $PidFile -Raw).Trim()
    if ($old -and ($old -ne "$myPid")) {
        $proc = Get-Process -Id ([int]$old) -ErrorAction SilentlyContinue
        if ($proc -and $proc.ProcessName -match 'powershell|pwsh') {
            Write-Host "[f8-daemon] Already running (pid=$old). Exit."
            exit 0
        }
    }
}
Write-F8Text $PidFile "$myPid"

# WO-1709: this was `Select-String -Path $L` over the WHOLE file, once per emitted capture.
# Measured 2026-09-14 on this machine: Editor.log had grown to 5,415,468,464 bytes (5.4 GB) and one
# such scan cost 154,657 ms - 372 ms for the 29 MB Player.log. That ~155 s is the "unexplained
# ~150 s capture cadence" WO-1709 recorded as UNPROVEN, and it is why a 1733-row replay could never
# reach the offset save at the bottom of the break-log branch. We only ever keep the LAST 60 signal
# lines, so reading the whole file was always wasted work: seek to the tail instead. ReadWrite share
# is REQUIRED (Unity holds these logs open) - same reason as the Editor/Player scan further down.
$HarvestTailBytes = 4MB
function Read-LogTail([string]$path, [int64]$maxBytes) {
    $fs = $null; $sr = $null
    try {
        $fs = [System.IO.File]::Open($path, 'Open', 'Read', [System.IO.FileShare]::ReadWrite)
        $start = [Math]::Max([int64]0, $fs.Length - $maxBytes)
        [void]$fs.Seek($start, 'Begin')
        $sr = New-Object System.IO.StreamReader($fs)
        return $sr.ReadToEnd()
    } catch {
        return ''
    } finally {
        if ($sr) { $sr.Close() }
        if ($fs) { $fs.Close() }
    }
}

function Harvest-Context {
    $blocks = @()
    foreach ($L in @($EditorLog, $PlayerLog)) {
        if (-not (Test-Path $L)) { continue }
        $text = Read-LogTail $L $HarvestTailBytes
        if ([string]::IsNullOrEmpty($text)) { continue }
        $hits = @($text -split "`r?`n" |
            Where-Object { $_ -match '\[Flow:|\[FeatureFlags\]|ff\.[a-z]+ =|\[Guard\]|EXCEPTION|NullReference' } |
            Select-Object -Last 60)
        if ($hits.Count -gt 0) {
            $blocks += ('--- {0} (last 60 signal lines within the final {1} MB) ---' -f $L, [int]($HarvestTailBytes / 1MB))
            $blocks += $hits
        }
    }
    return $blocks
}

function Alert-Owner([string]$Title, [string]$Body) {
    try { [System.Media.SystemSounds]::Exclamation.Play() } catch { }
    Write-Host ('[f8-daemon] ALERT: {0} - {1}' -f $Title, $Body)
}

function Emit-Capture([string]$kind, [string]$body, [string]$triggerLine) {
    # The seq is allocated INSIDE Publish-F8Capture (under the inbox lock) - __F8SEQ__ is the
    # placeholder it substitutes, so the header can be built before the number exists.
    $harvest = Harvest-Context
    $nl = [Environment]::NewLine

    $md = @(
        '# F8 Capture (auto-inbox seq=__F8SEQ__)'
        ''
        ('**Time (local):** {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
        ('**Kind:** {0}' -f $kind)
        ''
        '## Trigger'
        '```'
        $triggerLine
        '```'
        ''
        '## Payload'
        $body
        ''
        '## Auto-harvested context (read FIRST - section 12)'
        '```'
        ($harvest -join $nl)
        '```'
        ''
        '## Triage'
        '- Read this file before code-read or theory.'
        '- Route per docs/TICKET_PIPELINE.md.'
        '- Ack when done: f8-ack.ps1  (acks THIS capture only; a queued backlog stays pending)'
        ''
    ) -join $nl

    $sumLen = [Math]::Min(120, $triggerLine.Length)
    $seq = Publish-F8Capture -Inbox $Inbox -Kind $kind -Md $md -Source 'f8' -BaseName 'capture' `
        -Summary $triggerLine.Substring(0, $sumLen)

    Write-Host ''
    Write-Host '============================================================'
    Write-Host (' F8 INBOX PING seq={0} - TRIAGE NOW' -f $seq)
    Write-Host (' {0}' -f $Latest)
    Write-Host '============================================================'
    Write-Host ''

    Alert-Owner -Title 'Defenders F8 Capture' -Body ('seq={0} {1}{2}{3}' -f $seq, $kind, $nl, $triggerLine)
}

# WO-965 second drop path: the daemon used to baseline $breakBase to the CURRENT line count on
# every start, so any capture the owner made while the daemon was down (machine reboot, seat
# restart, a crash) was skipped forever and silently. The break-log offset is now PERSISTED, so a
# restart resumes where it left off and the backlog is replayed - loudly.
$breakBase = 0
$curBreakLines = 0
if (Test-Path $BreakLog) { $curBreakLines = @(Get-Content $BreakLog -ErrorAction SilentlyContinue).Count }

# WO-1709 dedupe watermark. The break-log offset alone cannot survive a kill mid-replay, and
# $seenKeys is per-process, so a restart re-published rows the queue already held (5038 was
# byte-identical to 5031). The device bridge already solved this with a published-`lastUtc`
# watermark; this is that SAME mechanism on the desktop path, not a second one. Every break-log row
# carries `"utc":"<ISO-8601>"` and the file is append-only, so the newest PUBLISHED payload utc is a
# safe floor: anything at or below it has already reached the queue.
function ConvertTo-F8Utc([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return $null }
    try {
        return [datetime]::Parse($s, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    } catch { return $null }
}

# Fallback only: used the FIRST time a daemon starts after this fix, when daemon-state.json predates
# the watermark field. Derived from the RECORD (QUEUE.jsonl + the per-seq capture files), never from
# LATEST_CAPTURE.md - the newest seq is not the furthest row when replays overlap. Reads only the
# `## Payload` line of each capture, so an unrelated `utc` inside the auto-harvest block can never
# over-advance the floor.
function Get-PublishedUtcWatermark {
    $queue = Join-Path $Inbox 'QUEUE.jsonl'
    if (-not (Test-Path $queue)) { return '' }
    $rows = @(Get-Content $queue -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '"source"\s*:\s*"f8"' } |
        Select-Object -Last 500)
    $archive = Join-Path $Inbox 'archive'
    $best = $null
    $bestS = ''
    $scanned = 0
    foreach ($row in $rows) {
        if ($row -notmatch '"capturePath"\s*:\s*"([^"]+)"') { continue }
        $capPath = $Matches[1] -replace '\\\\', '\'
        if (-not (Test-Path $capPath)) { $capPath = Join-Path $archive (Split-Path $capPath -Leaf) }
        if (-not (Test-Path $capPath)) { continue }
        $txt = ''
        try { $txt = [System.IO.File]::ReadAllText($capPath) } catch { continue }
        $scanned++
        if ($txt -notmatch '(?m)^## Payload\r?\n(.+)$') { continue }
        $payload = $Matches[1]
        if ($payload -notmatch '"utc"\s*:\s*"([^"]+)"') { continue }
        $s = $Matches[1]
        $d = ConvertTo-F8Utc $s
        if ($d -and ((-not $best) -or ($d -gt $best))) { $best = $d; $bestS = $s }
    }
    if ($bestS) {
        Write-F8Event $Inbox 'info' ("no persisted publish watermark - derived {0} from {1} published capture file(s)" -f $bestS, $scanned)
    }
    return $bestS
}

$persisted = $null
if (Test-Path $StateFile) { try { $persisted = Get-Content $StateFile -Raw | ConvertFrom-Json } catch { } }

$lastPubUtc = ''
if ($persisted -and $persisted.lastPublishedUtc) { $lastPubUtc = [string]$persisted.lastPublishedUtc }

if ($persisted -and $persisted.breakLog -eq $BreakLog) {
    $breakBase = [int]$persisted.breakOffset
    if ($breakBase -gt $curBreakLines) {
        Write-F8Event $Inbox 'warn' ("break-log shrank ({0} -> {1} lines): rotated/cleared, replaying from 0" -f $breakBase, $curBreakLines)
        $breakBase = 0
    } elseif ($breakBase -lt $curBreakLines) {
        Write-F8Event $Inbox 'warn' ("daemon was DOWN for {0} break-log line(s) (offset {1} of {2}) - replaying them now, none dropped" -f ($curBreakLines - $breakBase), $breakBase, $curBreakLines)
        if (-not $lastPubUtc) { $lastPubUtc = Get-PublishedUtcWatermark }
        if ($lastPubUtc) {
            Write-F8Event $Inbox 'info' ("publish watermark {0}: backlog rows at or below it are ALREADY in QUEUE.jsonl and will be skipped (offset still advances, nothing is dropped)" -f $lastPubUtc)
        }
    }
} else {
    # first ever run against this break-log: baseline to now (do not replay months of history)
    $breakBase = $curBreakLines
    Write-F8Event $Inbox 'info' ("first run for $BreakLog - baselined at $breakBase line(s)")
}

# WO-1709: the write was `try { ... } catch { }` - a SILENT swallow, which is precisely why "the
# offset is stuck" could not be told apart from "the offset write is failing" without this lane
# measuring the emit path. It is now loud, and it READS THE FILE BACK: $ErrorActionPreference is
# 'SilentlyContinue' at the top of this script, so a non-terminating failure would never have
# reached the catch at all. A save that does not land is a capture-replay storm waiting to happen.
$script:saveFailures = 0
function Save-BreakOffset([int]$offset, [string]$publishedUtc) {
    $obj = @{
        breakLog         = $BreakLog
        breakOffset      = $offset
        lastPublishedUtc = $publishedUtc
        updatedUtc       = (Get-Date).ToUniversalTime().ToString('o')
    }
    try {
        Write-F8Text $StateFile ($obj | ConvertTo-Json -Depth 3)
        $back = [System.IO.File]::ReadAllText($StateFile)
        if ($back -notmatch ('"breakOffset"\s*:\s*{0}\b' -f $offset)) {
            throw ("read-back did not show breakOffset={0}" -f $offset)
        }
    } catch {
        $script:saveFailures++
        Write-F8Event $Inbox 'warn' ("Save-BreakOffset FAILED (#{0}) writing offset={1} to {2}: {3} - the next restart will replay from the stale offset" -f $script:saveFailures, $offset, $StateFile, $_.Exception.Message)
    }
}
Save-BreakOffset $breakBase $lastPubUtc

$logPositions = @{}
foreach ($p in @($EditorLog, $PlayerLog)) {
    if (Test-Path $p) { $logPositions[$p] = (Get-Item $p).Length } else { $logPositions[$p] = 0 }
}

$seenKeys = @{}
# "note" = FlowTrace.Capture (audit 2026-08-15): an EXPECTED lifecycle state dump (hero death,
# scene handoff) that must land in break-log.jsonl for post-hoc reading but must NEVER wake a
# triage seat. Before this channel existed, those dumps were written as FlowTrace.Fail - the only
# severity that survived to device - so every hero death raised an F8 error capture.
$kindSkip = 'session_start|scene_loaded|note|idle'

# WO-1460 liveness. The loop below used to be a bare while($true) with no heartbeat and no
# try/catch: a terminating error inside it (a locked log, a bad cast) killed the daemon SILENTLY,
# and a healthy-but-quiet daemon was indistinguishable from a dead one. Both are now visible -
# the loop survives a failing pass and logs why, and every ~30s it stamps HEARTBEAT.json.
$hbEvery = 30
$hbLast = [datetime]::MinValue
$passFails = 0
# WO-1624 sec.4.5: $logPositions is IN-MEMORY, so "the Editor/Player scan ran" was not observable
# from outside the process - which is how a scan that had NEVER executed still read healthy. These
# two counters make the read branch visible in the heartbeat detail (same shape the device producer
# already uses: "offset=1215/1215"). Liveness only, NOT triage state - captures still go to the
# queue via Emit-Capture and nowhere else.
$logReads = 0
$logBytes = 0
function Watch-Detail {
    $parts = @()
    foreach ($pair in @(@('editor', $EditorLog), @('player', $PlayerLog))) {
        $name = $pair[0]; $p = $pair[1]
        if (Test-Path $p) {
            $parts += ('{0}={1}/{2}' -f $name, [int64]$logPositions[$p], (Get-Item $p).Length)
        } else {
            $parts += ('{0}=absent' -f $name)
        }
    }
    return ('watching {0} reads={1} bytes={2}' -f ($parts -join ' '), $logReads, $logBytes)
}
function Beat([string]$detail) {
    $script:hbLast = Get-Date
    Write-F8Heartbeat $Inbox 'desktop' @{
        detail    = $detail
        breakLog  = $BreakLog
        breakOffset = $breakBase
        pollSeconds = $PollSeconds
        passFails = $passFails
    }
}
Beat 'armed'
$passCount = 0

Write-Host ('[f8-daemon] armed pid={0} poll={1}s' -f $myPid, $PollSeconds)
Write-Host ('[f8-daemon] break-log: {0}' -f $BreakLog)
Write-Host ('[f8-daemon] inbox: {0}' -f $Inbox)
Write-Host '[f8-daemon] auto-rearm: INFINITE (no manual re-arm needed)'
Write-Host ''

while ($true) {
    Start-Sleep -Seconds $PollSeconds

  try {
    if (Test-Path $BreakLog) {
        $lines = @(Get-Content $BreakLog -ErrorAction SilentlyContinue)
        $cur = $lines.Count
        if ($cur -lt $breakBase) { $breakBase = 0 }
        if ($cur -gt $breakBase) {
            # WO-1709: this was a `foreach` over a slice, with ONE `Save-BreakOffset $cur` after it.
            # That save was reachable only once every row had been emitted, and each emit cost ~155 s
            # (see Harvest-Context above), so a 1733-row backlog needed ~56 HOURS of uninterrupted
            # runtime to persist a single byte of progress. It never got it: the daemon was stopped
            # mid-backlog every time, the offset stayed at 1331 across 13+ restarts from 2026-09-12
            # to 2026-09-14, and each restart re-emitted the same 09-11 rows. An indexed loop that
            # persists after EVERY row makes progress survive a kill - the durability property the
            # single trailing save never actually had.
            $replaySkipped = 0
            for ($i = $breakBase; $i -lt $cur; $i++) {
                $line = $lines[$i]
                $emit = $true
                if ([string]::IsNullOrWhiteSpace($line)) { $emit = $false }
                elseif ($line -match ('"kind"\s*:\s*"({0})"' -f $kindSkip)) { $emit = $false }

                $rowUtc = ''
                if ($emit) {
                    if ($line -match '"utc"\s*:\s*"([^"]+)"') { $rowUtc = $Matches[1] }
                    # Already-published floor. A row with NO utc is never skipped - an unparseable
                    # timestamp must not silently drop an owner capture (WO-965).
                    $rowDt = ConvertTo-F8Utc $rowUtc
                    $markDt = ConvertTo-F8Utc $lastPubUtc
                    if ($rowDt -and $markDt -and ($rowDt -le $markDt)) {
                        $replaySkipped++
                        $emit = $false
                    }
                }

                if ($emit) {
                    # WO-1531: an owner FLAG is an EVENT, not a message. Two identical presses are two
                    # facts, so a flagged line is never suppressed by the seen table. (This key is the
                    # whole line, utc included, so ordinary entries were never suppressed forever the
                    # way the device bridge's kind+message key was - the flag carve-out is the half
                    # that matters here.) The utc watermark above is orthogonal: two presses carry two
                    # different utc values, so neither is ever below the floor at the time it arrives.
                    $isFlaggedLine = $line -match '"kind"\s*:\s*"flagged"'
                    $key = 'bl:' + $line.GetHashCode()
                    if ((-not $isFlaggedLine) -and $seenKeys.ContainsKey($key)) {
                        $emit = $false
                    } else {
                        if (-not $isFlaggedLine) { $seenKeys[$key] = $true }

                        # anchored on the "kind" FIELD: the old greedy 'kind.*:\s*"(\w+)"' walked past it and
                        # captured the LAST quoted word on the line - which is why PING.json kind read
                        # "Main_Castle_Overworld" (the scene) instead of "flagged" / "error".
                        $capKind = 'break-log'
                        if ($line -match '"kind"\s*:\s*"([^"]+)"') { $capKind = $Matches[1] }
                        Emit-Capture -kind $capKind -body $line -triggerLine $line
                        if ($rowUtc) { $lastPubUtc = $rowUtc }
                    }
                }

                $breakBase = $i + 1
                Save-BreakOffset $breakBase $lastPubUtc

                # WO-1460 liveness during a long backlog. The heartbeat used to be stamped only at the
                # BOTTOM of the pass, so a daemon grinding through a replay read as frozen at
                # detail="armed" - which is exactly how HEARTBEAT.json looked at 09:06:33Z while the
                # process was alive and working.
                if (((Get-Date) - $hbLast).TotalSeconds -ge $hbEvery) {
                    Beat ('replaying break-log row {0}/{1} (skipped {2} already-published)' -f $breakBase, $cur, $replaySkipped)
                }
            }
            if ($replaySkipped -gt 0) {
                Write-F8Event $Inbox 'info' ("replay: skipped {0} break-log row(s) at or below the published watermark {1} - already in QUEUE.jsonl, not re-published; offset advanced to {2}" -f $replaySkipped, $lastPubUtc, $breakBase)
            }
        }
    }

    foreach ($logPath in @($EditorLog, $PlayerLog)) {
        if (-not (Test-Path $logPath)) { continue }
        $len = (Get-Item $logPath).Length
        $pos = $logPositions[$logPath]
        if ($len -lt $pos) { $pos = 0 }
        if ($len -le $pos) { continue }

        # WO-1624: this argument was the STRING 'FileShare.ReadWrite' from 2026-07-09 (22ae4de5b)
        # until 2026-09-10. It carried the TYPE NAME as well as the member name, so PowerShell
        # could not match it to a FileShare enumerator ("Unable to match the identifier name
        # FileShare.ReadWrite to a valid enumerator name") and EVERY pass threw here - 9317 caught
        # throws on one process alone - meaning the Editor/Player scan below had never run.
        # TYPED form deliberately, not the bare 'ReadWrite' string: it cannot be mis-shortened
        # again. ReadWrite share is REQUIRED - Unity holds these logs open for writing.
        $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', [System.IO.FileShare]::ReadWrite)
        $fs.Seek($pos, 'Begin') | Out-Null
        $sr = New-Object System.IO.StreamReader($fs)
        $chunk = $sr.ReadToEnd()
        $sr.Close()
        $fs.Close()
        $logReads++
        $logBytes += ($len - $pos)   # BYTES off the stream, not $chunk.Length (chars)
        $logPositions[$logPath] = $len

        foreach ($line in ($chunk -split "`r?`n")) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $isFlagged  = $line -match '\[BreakCapture\].*flagged|kind.*flagged'
            $isError    = $line -match 'error CS\d+|Exception:|NullReferenceException|AssertionException'
            $isSoftlock = $line -match 'Infinite loop|stack overflow|Deadlock|softlock'
            if (-not ($isFlagged -or $isError -or $isSoftlock)) { continue }

            # WO-1531: same carve-out. A Player.log flag line carries no timestamp of its own, so
            # the whole-line hash WOULD have suppressed every later press for the life of the
            # daemon - the exact failure the device bridge shipped.
            $key = 'log:' + $line.GetHashCode()
            if (-not $isFlagged) {
                if ($seenKeys.ContainsKey($key)) { continue }
                $seenKeys[$key] = $true
            }

            if ($isFlagged) { $capKind = 'flagged' }
            elseif ($isSoftlock) { $capKind = 'softlock' }
            else { $capKind = 'error' }
            Emit-Capture -kind $capKind -body $line -triggerLine $line
        }
    }
  }
  catch {
      # SURVIVE, do not exit. A pass that throws (log locked by Unity, a bad cast, a transient IO
      # error) used to terminate the daemon with no trace anywhere. Log it and keep watching.
      $passFails++
      Write-F8Event $Inbox 'warn' ("desktop daemon pass failed (#{0}), continuing: {1}" -f $passFails, $_.Exception.Message)
      Beat ('pass-failed: ' + $_.Exception.Message)
  }

  # heartbeat on its own cadence, whether or not anything was captured: silence must be
  # distinguishable from death (WO-1460).
  if (((Get-Date) - $hbLast).TotalSeconds -ge $hbEvery) { Beat (Watch-Detail) }

  # WO-1709 test seam. 0 (the production default) never breaks: the daemon's contract is
  # auto-rearm INFINITE.
  $passCount++
  if ($MaxPasses -gt 0 -and $passCount -ge $MaxPasses) {
      Write-Host ('[f8-daemon] MaxPasses={0} reached - exiting (test seam). breakOffset={1} lastPublishedUtc={2}' -f $MaxPasses, $breakBase, $lastPubUtc)
      break
  }
}
