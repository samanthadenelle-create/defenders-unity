# Returns exit 0 when one or more UN-ACKED F8 captures are waiting for triage (stdout = paths).
# Claude runs this each turn (see .cursor/rules/f8-auto-triage.mdc + .claude/settings.json hooks).
#
# WO-965: this used to compare PING.json's seq to ACK.json and surface ONLY the newest capture.
# A burst therefore surfaced its last member and the ack buried the rest (2026-08-10: seq 2307 and
# 2308 never reached any seat). It now walks QUEUE.jsonl and surfaces EVERY un-acked capture,
# OLDEST FIRST. Contract preserved exactly:
#   exit 0 + 'NEW_CAPTURE' when work is waiting, exit 1 + 'NO_CAPTURE' when clean;
#   seq= / kind= / firedAt= / latest= / capture= lines still printed.
# What changed: seq=/kind=/capture= now name the OLDEST pending capture (the one to triage NEXT),
# and pending= / PENDING lines list the rest. latest= still points at LATEST_CAPTURE.md.
param([switch]$Quiet, [string]$InboxOverride = '')   # -InboxOverride: tests only

. (Join-Path $PSScriptRoot 'f8-inbox-lib.ps1')

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$Inbox    = Join-Path $RepoRoot 'logs\f8-inbox'
if (-not [string]::IsNullOrWhiteSpace($InboxOverride)) { $Inbox = $InboxOverride }
$PingFile = Join-Path $Inbox 'PING.json'
$AckFile  = Join-Path $Inbox 'ACK.json'
$Latest   = Join-Path $Inbox 'LATEST_CAPTURE.md'

# -- WO-1460 liveness ---------------------------------------------------------------------------
# An empty inbox has TWO causes and they used to look identical: nothing went wrong, or the thing
# that would have told us went quiet. On 2026-09-06 the device bridge ran all day and published
# nothing after 13:42:43Z (its kind+message dedupe suppressed all 319 later signal entries,
# including 2 possible_softlock and one of the owner's own FLAG presses) and NO_CAPTURE read
# exactly the same as health. F8_DAEMON_STALE makes the difference visible on every poll.
# It NEVER changes the exit code - NEW_CAPTURE/NO_CAPTURE stay the contract.
$StaleSeconds = 90
function Write-F8Liveness {
    $hb = Get-F8Heartbeat $Inbox
    if (-not $hb.Exists) {
        Write-Host ("F8_DAEMON_STALE -1 producer=none reason=no-heartbeat-file - no F8 producer has ever beaten in this inbox. Start it: powershell -File .claude\skills\run-defenders\f8-watch-start.ps1")
        return
    }
    foreach ($p in @($hb.Producers)) {
        $age = [int]$p.ageSec
        # WO-1625: structured counter FIRST, string second. A missing/unparsable passFails is already
        # normalised to 0 by Get-F8Heartbeat, so a producer that never writes the key is never degraded.
        $passFails = 0
        try { $passFails = [int]$p.passFails } catch { $passFails = 0 }
        $degraded = ($passFails -gt 0) -or (([string]$p.detail) -match 'pass-failed')
        if ($age -lt 0 -or $age -gt $StaleSeconds) {
            $liveTxt = 'process-dead'
            if ($p.alive) { $liveTxt = 'process-alive-but-not-beating' }
            Write-Host ("F8_DAEMON_STALE {0} producer={1} pid={2} {3} last={4} lastDeviceUtc={5} detail={6}" -f $age, $p.name, $p.pid, $liveTxt, $p.updatedUtc, $p.lastDeviceUtc, $p.detail)
            Write-Host ("F8_DAEMON_STALE the {0} half of the section 14 chain is NOT proving itself alive. Captures may be going nowhere. Restart: powershell -File .claude\skills\run-defenders\f8-watch-start.ps1" -f $p.name)
        } elseif ($degraded) {
            # WO-1625: a producer that beats ON TIME while FAILING used to print F8_DAEMON_OK with the
            # failure text interpolated into the same line as detail= -- the script printed the proof
            # of the defect inside the verdict that denied it, for ~12.7 hours (WO-1624 RESULT :123).
            # Age is no longer the only predicate. STALE still wins: a producer that is not beating at
            # all is a STALE problem, and this branch sits BELOW it deliberately.
            # NOT gated on -Quiet, matching the STALE branch directly above: -Quiet exists to hide
            # HEALTH (the OK line below), never a failure.
            Write-Host ("F8_DAEMON_DEGRADED producer={0} pid={1} age={2}s passFails={3} lastDeviceUtc={4} detail={5}" -f $p.name, $p.pid, $age, $passFails, $p.lastDeviceUtc, $p.detail)
            Write-Host ("F8_DAEMON_DEGRADED the {0} producer IS beating but its own work is failing. Captures may be going nowhere even though it looks alive. Read the detail= above, then restart it: powershell -File .claude\skills\run-defenders\f8-watch-start.ps1" -f $p.name)
        } elseif (-not $Quiet) {
            Write-Host ("F8_DAEMON_OK producer={0} pid={1} age={2}s lastDeviceUtc={3} detail={4}" -f $p.name, $p.pid, $age, $p.lastDeviceUtc, $p.detail)
        }
    }
}

if (-not (Test-Path $PingFile)) {
    Write-F8Liveness
    if (-not $Quiet) { Write-Host 'NO_CAPTURE' }
    exit 1
}

Write-F8Liveness

$ack     = Get-F8AckState $Inbox
$pending = @(Get-F8Pending $Inbox)

# WO-1018 -- 'CLEAN' MUST BE PROVEN, NOT ASSUMED. Get-F8Pending only ever looked ABOVE the ack
# watermark, so a capture buried underneath it read as clean forever. Before printing NO_CAPTURE we
# reconcile against the capture files actually on disk; if any un-acked file exists, or the inbox
# has never been swept (so nothing under the watermark has ever been reconciled), we say so LOUDLY
# and do not claim the inbox is clean.
if ($pending.Count -eq 0) {
    $ping  = Get-F8PingSeq $Inbox
    $state = Test-F8InboxClean $Inbox

    if (@($state.Unacked).Count -gt 0) {
        Write-Host 'NEW_CAPTURE'
        Write-Host ("seq={0}" -f @($state.Unacked)[0])
        Write-Host 'kind=on-disk-unacked'
        Write-Host "latest=$Latest"
        Write-Host ("capture={0}" -f (Resolve-F8CaptureFile $Inbox ([int]@($state.Unacked)[0])))
        Write-Host ("pending={0}" -f @($state.Unacked).Count)
        Write-Host ''
        Write-Host ("ERROR_UNRECONCILED {0} capture file(s) on disk are above the ack watermark ({1}) and NOT acked: {2}" -f @($state.Unacked).Count, $ack.lastAckSeq, (@($state.Unacked) -join ','))
        Write-Host 'ERROR_UNRECONCILED The queue and the disk disagree. Run f8-backfill-sweep.ps1 before trusting any ack.'
        exit 0
    }

    if (-not $state.Swept) {
        Write-Host ("WARN_NO_SWEEP inbox has NEVER been reconciled below the watermark ({0} capture files on disk, {1} pre-queue). Nothing under ack={2} has been proven triaged." -f $state.Files, $state.Legacy, $ack.lastAckSeq)
        Write-Host 'WARN_NO_SWEEP Run: powershell -File .claude\skills\run-defenders\f8-backfill-sweep.ps1'
    }
    if (-not $Quiet) { Write-Host "NO_CAPTURE ack=$($ack.lastAckSeq) ping=$ping" }
    exit 1
}

$next = $pending[0]
$nextPath = $next.capturePath
if ([string]::IsNullOrWhiteSpace($nextPath) -or -not (Test-Path $nextPath)) { $nextPath = $Latest }

Write-Host 'NEW_CAPTURE'
Write-Host "seq=$($next.seq)"
Write-Host "kind=$($next.kind)"
Write-Host "firedAt=$($next.utc)"
Write-Host "latest=$Latest"
Write-Host "capture=$nextPath"
Write-Host "pending=$($pending.Count)"

if ($pending.Count -gt 1) {
    Write-Host ''
    Write-Host "BACKLOG - $($pending.Count) un-acked captures. TRIAGE OLDEST FIRST; f8-ack.ps1 acks ONE at a time."
    foreach ($e in $pending) {
        $p = $e.capturePath
        if ([string]::IsNullOrWhiteSpace($p)) { $p = '(no capture file)' }
        Write-Host ("  seq={0} kind={1} {2}" -f $e.seq, $e.kind, $p)
        Write-Host ("      {0}" -f $e.summary)
    }
}

# LOUD, never silent: a pending seq with no queue entry means a producer did not queue it (an old
# daemon process) or the capture content is gone. Get-F8Pending has already written queue-events.log.
$orphans = @($pending | Where-Object { $_.unqueued })
if ($orphans.Count -gt 0) {
    Write-Host ''
    Write-Host ("WARN_UNQUEUED {0} capture(s) had no QUEUE.jsonl entry: {1}" -f $orphans.Count, (($orphans | ForEach-Object { $_.seq }) -join ','))
    Write-Host 'WARN_UNQUEUED cause: a producer started before WO-965 is still running. Restart it: f8-watch-stop.ps1 then f8-watch-start.ps1.'
    $lost = @($orphans | Where-Object { [string]::IsNullOrWhiteSpace($_.capturePath) })
    if ($lost.Count -gt 0) {
        Write-Host ("ERROR_LOST_CAPTURE seq(s) {0} have NO capture file - content unrecoverable. Tell the owner." -f (($lost | ForEach-Object { $_.seq }) -join ','))
    }
}
exit 0
