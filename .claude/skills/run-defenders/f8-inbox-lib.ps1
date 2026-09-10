# f8-inbox-lib.ps1 -- the F8 inbox QUEUE (WO-965, 2026-08-10).
#
# WHY THIS EXISTS (the defect it fixes):
#   Every producer (f8-watch-daemon / websig-watch-daemon / bugreport-watch) used to publish a
#   capture by OVERWRITING two single-slot files: LATEST_CAPTURE.md and PING.json. A burst of
#   captures between two seat looks therefore collapsed to the NEWEST one, and f8-ack.ps1 then
#   acked that newest seq -- which silently marked every skipped seq as triaged.
#   Proven 2026-08-10: the seat acked seq 2306, the next ping it ever saw was seq 2309, and
#   seq 2307 ("both NPC and echo but no movement") + seq 2308 (a Tutorial STEP-STUCK error)
#   were never surfaced to any seat. The owner is NEVER the bug detector (CLAUDE.md s14), so a
#   harness that eats her flags defeats the whole passive-listener design.
#
# THE FIX: an APPEND-ONLY backlog, QUEUE.jsonl -- one JSON line per capture, never rewritten.
#   LATEST_CAPTURE.md + PING.json stay exactly as they were (the whole existing contract keeps
#   working); they are now a VIEW of the newest entry, and the QUEUE is the record. Consumers
#   walk the queue oldest-first, and f8-ack.ps1 acks ONE capture at a time.
#   An append-only log was chosen over "make LATEST_CAPTURE.md append" because a queue keeps each
#   capture's auto-harvest block intact in its own per-seq file, keeps a capture's identity (seq)
#   machine-readable, and cannot be corrupted by a concurrent producer the way a rewritten
#   aggregate file can. Per-seq capture files already existed -- they were simply unreachable.
#
# LOUDNESS: nothing here fails quietly. A superseded-but-unacked capture, a producer that did not
#   queue, and a seq gap all write to queue-events.log AND to the console.
#
# ---------------------------------------------------------------------------------------------
# WO-1018 (2026-08-22). The PRODUCER half of WO-965 is CLOSED and proven: QUEUE.jsonl holds 1272
# rows spanning seq 2316-3587 and WARN_UNQUEUED no longer fires. A full backfill sweep found ZERO
# lost captures -- every capture on disk has a matching ack in queue-events.log. Three real defects
# remained, and this file now carries all three fixes:
#
#   1. THE CONSUMER WAS COLLISION-BLIND -- the only genuine data-loss path left. Get-F8Pending keyed
#      its record map by seq (`$bySeq[$s] = $e`), LAST WRITER WINS, so when two captures shared a
#      number only ONE ever reached a seat and acking it closed both. capture-20260815-183806-seq2329
#      (the owner's flag, "[Main_Castle_Overworld] look at the overcrowding") and
#      capture-20260815-210117-seq2329 (an unrelated scene-open error) are two captures 2.5 hours
#      apart under seq 2329; the flag was never triaged. A seq now maps to a LIST, collided captures
#      are keyed BY FILE, and N collisions require N acks. ALLOCATION was already fixed forward
#      (minted under Enter-F8Lock); the 2329 pair predates it. Get-F8NextSeq only adds a
#      never-lowers guard + a loud shout, it does not change the formula.
#   2. NOTHING BELOW THE WATERMARK WAS REACHABLE. Get-F8Pending returned early when maxSeq <= wm and
#      scanned from wm+1, so a buried capture -- 2329 included -- could never be found again. See the
#      backfill block in Get-F8Pending + f8-backfill-sweep.ps1. NOTE THE ORDER: the sweep must run
#      BEFORE the deep scan has any authority, because ACK.json's `acked` set is EMPTY BY DESIGN
#      (Save-F8AckState folds contiguous acks into the watermark), so making that set the authority
#      would re-open ~1273 captures at once and read as a catastrophic regression. The sweep
#      reconciles against queue-events.log, which records one 'acked seq=N' line per ack.
#   3. NO PRUNE STEP, EVER, plus an O(files x gaps) scan. 2914 capture files had accumulated and
#      Resolve-F8CaptureFile did a full directory listing + sort PER MISSING SEQ, which is why
#      f8-check-inbox.ps1 timed out at two minutes. Lookups now hit one cached listing
#      (Get-F8CaptureIndex), and triage-archive.ps1 -InboxOnly MOVES acked captures older than N days
#      into logs/f8-inbox/archive. It never deletes one -- this repo's rule is "never wipe a ticket"
#      -- and the index reads the archive, so an archived capture stays findable.
# ---------------------------------------------------------------------------------------------

Set-StrictMode -Off

$script:F8Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-F8Text([string]$Path, [string]$Text) {
    # PowerShell 5.1 `Set-Content -Encoding UTF8` emits a BOM (and has bitten this repo before).
    [System.IO.File]::WriteAllText($Path, $Text, $script:F8Utf8NoBom)
}

function Append-F8Text([string]$Path, [string]$Text) {
    [System.IO.File]::AppendAllText($Path, $Text, $script:F8Utf8NoBom)
}

function Get-F8Paths([string]$Inbox) {
    return @{
        Inbox    = $Inbox
        Ping     = (Join-Path $Inbox 'PING.json')
        Ack      = (Join-Path $Inbox 'ACK.json')
        Latest   = (Join-Path $Inbox 'LATEST_CAPTURE.md')
        Queue    = (Join-Path $Inbox 'QUEUE.jsonl')
        Events   = (Join-Path $Inbox 'queue-events.log')
        # WO-1018 additions
        Archive  = (Join-Path $Inbox 'archive')          # acked captures are MOVED here, never deleted
        SeqState = (Join-Path $Inbox 'SEQ.json')         # monotonic seq high-water, survives daemon restarts
        Backfill = (Join-Path $Inbox 'ACK_BACKFILL.json')# the sweep baseline; gates the below-watermark scan
        Index    = (Join-Path $Inbox 'capture-index.json')# cached seq->file map for LEGACY (un-seq-named) files
    }
}

function Write-F8Event([string]$Inbox, [string]$Level, [string]$Message) {
    $p = Get-F8Paths $Inbox
    $line = ('{0} [{1}] {2}' -f (Get-Date).ToUniversalTime().ToString('o'), $Level, $Message)
    try { Append-F8Text $p.Events ($line + [Environment]::NewLine) } catch { }
    if ($Level -ne 'info') { Write-Host ('[f8-queue] {0}: {1}' -f $Level.ToUpper(), $Message) }
}

# -- lock: producers and ackers serialise through one named mutex ------------------------------
function Enter-F8Lock {
    $m = $null
    try { $m = New-Object System.Threading.Mutex($false, 'EoaF8InboxQueue') } catch { return $null }
    try { [void]$m.WaitOne(5000) } catch { }
    return $m
}
function Exit-F8Lock($m) {
    if ($null -eq $m) { return }
    try { $m.ReleaseMutex() } catch { }
    try { $m.Dispose() } catch { }
}

# -- heartbeat: WO-1460 -------------------------------------------------------------------------
# WHY: on 2026-09-06 the device bridge ran all day, read every line of the phone's break-log, and
# published NOTHING after 13:42:43Z because its kind+message dedupe suppressed all 319 later signal
# entries. The inbox therefore looked IDENTICAL to a dead daemon. There was no way to tell "healthy
# and quiet" from "stopped" - which is the exact blindness CLAUDE.md section 14 exists to end.
# A heartbeat makes silence MEASURABLE: each producer stamps its own section every ~30s with its
# pid, the last device/log line it has consumed, and why it published nothing.
# HEARTBEAT.json is a SIBLING of PING.json on purpose - PING.json is the newest-capture VIEW and
# its shape is a contract (f8-poll-rewake.ps1 parses .seq); liveness must not ride on it.
function Write-F8Heartbeat([string]$Inbox, [string]$Producer, $Fields) {
    $path = Join-Path $Inbox 'HEARTBEAT.json'
    $m = Enter-F8Lock
    try {
        $obj = $null
        if (Test-Path $path) { try { $obj = Get-Content $path -Raw | ConvertFrom-Json } catch { $obj = $null } }
        $producers = @{}
        if ($obj -and $obj.producers) {
            foreach ($prop in $obj.producers.PSObject.Properties) {
                if ([string]$prop.Name -eq $Producer) { continue }
                $producers[[string]$prop.Name] = $prop.Value
            }
        }
        $entry = @{ pid = $PID; updatedUtc = (Get-Date).ToUniversalTime().ToString('o') }
        if ($Fields) { foreach ($k in @($Fields.Keys)) { $entry[[string]$k] = $Fields[$k] } }
        $producers[$Producer] = $entry
        $out = @{
            note       = 'WO-1460 liveness only. Each producer stamps its own section every ~30s. Not triage state.'
            updatedUtc = (Get-Date).ToUniversalTime().ToString('o')
            producers  = $producers
        }
        Write-F8Text $path ($out | ConvertTo-Json -Depth 6)
    } catch { } finally { Exit-F8Lock $m }
}

# Returns @{ Exists=bool; Producers=@( @{ name; pid; ageSec; updatedUtc; alive; detail; lastDeviceUtc } ) }
function Get-F8Heartbeat([string]$Inbox) {
    $path = Join-Path $Inbox 'HEARTBEAT.json'
    $res = @{ Exists = $false; Producers = @() }
    if (-not (Test-Path $path)) { return $res }
    $obj = $null
    try { $obj = Get-Content $path -Raw | ConvertFrom-Json } catch { return $res }
    if (-not $obj -or -not $obj.producers) { return $res }
    $res.Exists = $true
    $now = (Get-Date).ToUniversalTime()
    $list = @()
    foreach ($prop in $obj.producers.PSObject.Properties) {
        $v = $prop.Value
        $age = -1
        try { $age = [int]([Math]::Round(($now - ([datetime]::Parse([string]$v.updatedUtc)).ToUniversalTime()).TotalSeconds)) } catch { $age = -1 }
        $procPid = 0
        try { $procPid = [int]$v.pid } catch { $procPid = 0 }
        # WO-1625: the daemon's own failure counter. NOT every producer writes it (the device
        # producer has no such key), so an absent or unparsable value MUST read as 0 -- degrading a
        # healthy producer is the same false-signal defect pointed the other way.
        $passFails = 0
        try { $passFails = [int]$v.passFails } catch { $passFails = 0 }
        $alive = $false
        if ($procPid -gt 0) {
            $p = Get-Process -Id $procPid -ErrorAction SilentlyContinue
            if ($p) { $alive = $true }
        }
        $list += @{
            name          = [string]$prop.Name
            pid           = $procPid
            ageSec        = $age
            updatedUtc    = [string]$v.updatedUtc
            alive         = $alive
            passFails     = $passFails
            detail        = [string]$v.detail
            lastDeviceUtc = [string]$v.lastDeviceUtc
        }
    }
    $res.Producers = @($list | Sort-Object { $_.name })
    return $res
}

function Get-F8PingSeq([string]$Inbox) {
    $p = Get-F8Paths $Inbox
    if (-not (Test-Path $p.Ping)) { return 0 }
    try { return [int]((Get-Content $p.Ping -Raw | ConvertFrom-Json).seq) } catch { return 0 }
}

function Get-F8Queue([string]$Inbox) {
    $p = Get-F8Paths $Inbox
    $out = @()
    if (-not (Test-Path $p.Queue)) { return $out }
    foreach ($line in (Get-Content $p.Queue -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $out += ($line | ConvertFrom-Json) } catch { }
    }
    return $out
}

# ACK state. `lastAckSeq` is the CONTIGUOUS watermark and keeps its old meaning, so every existing
# reader (f8-prompt-check, f8-poll-rewake, any other seat) keeps working untouched. `acked` records
# out-of-order acks above the watermark.
#
# WO-1018 adds `ackedFiles`: leaf filenames of captures acked BY FILE rather than by seq. A seq is
# not a unique key in practice -- 2026-08-15 produced TWO different captures both numbered seq 2329
# (capture-20260815-183806-seq2329.md, an owner flag, and capture-20260815-210117-seq2329.md, an
# unrelated scene-open error). Acking "seq 2329" closed both, so the owner's flag was never triaged.
# Anything the backfill sweep surfaces as an orphan is therefore acked by FILE, not by number.
function Get-F8AckState([string]$Inbox) {
    $p = Get-F8Paths $Inbox
    $state = @{ lastAckSeq = 0; acked = @(); ackedFiles = @() }
    if (-not (Test-Path $p.Ack)) { return $state }
    try {
        $j = Get-Content $p.Ack -Raw | ConvertFrom-Json
        $state.lastAckSeq = [int]$j.lastAckSeq
        if ($j.PSObject.Properties.Name -contains 'acked' -and $j.acked) {
            $state.acked = @($j.acked | ForEach-Object { [int]$_ })
        }
        if ($j.PSObject.Properties.Name -contains 'ackedFiles' -and $j.ackedFiles) {
            $state.ackedFiles = @($j.ackedFiles | ForEach-Object { [string]$_ })
        }
    } catch { }
    return $state
}

function Save-F8AckState([string]$Inbox, $State) {
    $p = Get-F8Paths $Inbox
    $wm = [int]$State.lastAckSeq
    $set = @{}
    foreach ($s in @($State.acked)) { $set[[int]$s] = $true }
    # roll the contiguous watermark forward over any acked run sitting just above it
    while ($set.ContainsKey($wm + 1)) { $wm++ ; $set.Remove($wm) }
    $above = @($set.Keys | Where-Object { $_ -gt $wm } | Sort-Object)
    $files = @()
    if ($State.ContainsKey('ackedFiles')) {
        $fset = @{}
        foreach ($f in @($State.ackedFiles)) {
            if ([string]::IsNullOrWhiteSpace($f)) { continue }
            $fset[[string]$f] = $true
        }
        $files = @($fset.Keys | Sort-Object)
    }
    $obj = @{
        lastAckSeq = $wm
        acked      = $above
        ackedFiles = $files
        ackedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    Write-F8Text $p.Ack ($obj | ConvertTo-Json -Depth 4)
    return $wm
}

# -- WO-1018: THE CAPTURE INDEX -----------------------------------------------------------------
# seq -> @(full paths). Built from FILENAMES ONLY ('<base>-<stamp>-seq<N>.md'), across the inbox AND
# logs/f8-inbox/archive, so it costs one directory listing and zero file reads. That matters: the
# old Resolve-F8CaptureFile head-read up to 400 files PER MISSING SEQ, and on 2026-08-22, with 2914
# capture files on disk, f8-check-inbox.ps1 TIMED OUT at two minutes because of it.
#
# Pre-WO-965 captures ('capture-<stamp>.md', no seq in the name) cannot be indexed by name. Their
# seq lives in the first line of the file, so they are read ONCE by f8-backfill-sweep.ps1 and cached
# in capture-index.json; nothing on the hot path ever reads them.
$script:F8IndexCache = @{}

function Clear-F8IndexCache([string]$Inbox) { $script:F8IndexCache.Remove($Inbox) }

function Get-F8CaptureIndex([string]$Inbox) {
    if ($script:F8IndexCache.ContainsKey($Inbox)) { return $script:F8IndexCache[$Inbox] }
    $p = Get-F8Paths $Inbox
    $bySeq   = @{}
    $noSeq   = @()
    $all     = @()
    foreach ($dir in @($Inbox, $p.Archive)) {
        if (-not (Test-Path $dir)) { continue }
        foreach ($f in (Get-ChildItem -Path $dir -Filter '*.md' -File -ErrorAction SilentlyContinue)) {
            if ($f.Name -eq 'LATEST_CAPTURE.md' -or $f.Name -eq 'README.md') { continue }
            if ($f.Name -notmatch '^capture') { continue }
            $all += $f
            if ($f.Name -match '-seq(\d+)\.md$') {
                $s = [int]$Matches[1]
                if (-not $bySeq.ContainsKey($s)) { $bySeq[$s] = @() }
                $bySeq[$s] += $f.FullName
            } else {
                $noSeq += $f.FullName
            }
        }
    }
    # fold in the cached legacy map (built by the sweep) so recovery can still reach those files
    if (Test-Path $p.Index) {
        try {
            $j = Get-Content $p.Index -Raw | ConvertFrom-Json
            foreach ($prop in $j.PSObject.Properties) {
                $s = 0
                if (-not [int]::TryParse($prop.Name, [ref]$s)) { continue }
                foreach ($path in @($prop.Value)) {
                    if (-not (Test-Path $path)) { continue }
                    if (-not $bySeq.ContainsKey($s)) { $bySeq[$s] = @() }
                    if ($bySeq[$s] -notcontains $path) { $bySeq[$s] += $path }
                }
            }
        } catch { }
    }
    $maxSeq = 0
    foreach ($k in $bySeq.Keys) { if ([int]$k -gt $maxSeq) { $maxSeq = [int]$k } }
    $idx = @{ BySeq = $bySeq; NoSeq = $noSeq; MaxSeq = $maxSeq; FileCount = $all.Count; Files = $all }
    $script:F8IndexCache[$Inbox] = $idx
    return $idx
}

# ALL capture files claiming this seq. The old Resolve-F8CaptureFile returned on the FIRST match, so
# a second file under the same number could never be surfaced -- that is half of the 2329 burial.
# It was also a Get-ChildItem + Sort-Object over the WHOLE directory PER MISSING SEQ; with 2914
# files that product is what made f8-check-inbox.ps1 time out at two minutes on 2026-08-22. This is
# now a hashtable lookup into one cached directory listing.
function Resolve-F8CaptureFiles([string]$Inbox, [int]$Seq) {
    $idx = Get-F8CaptureIndex $Inbox
    if ($idx.BySeq.ContainsKey($Seq)) { return @($idx.BySeq[$Seq]) }
    return @()
}

# Back-compat single-file form (kept: other seats call it). Prefer Resolve-F8CaptureFiles.
function Resolve-F8CaptureFile([string]$Inbox, [int]$Seq) {
    $all = @(Resolve-F8CaptureFiles $Inbox $Seq)
    if ($all.Count -gt 0) { return $all[0] }
    return ''
}

# -- WO-1018: ALLOCATION GUARD (an ADDITION to the WO-965 formula, not a replacement) -----------
# WO-965 already mints max(PING.json, QUEUE.jsonl)+1 under Enter-F8Lock, and that is what stopped
# new collisions; the seq 2329 pair predates it and was never an allocation bug at mint time.
# This guard only ever moves the number UP, never down, so it cannot regress that working path:
#   - it also consults every capture FILE on disk (including the archive) and a monotonic
#     high-water in SEQ.json, so a rotated queue or a restored PING.json cannot walk a number back;
#   - if the chosen number is somehow still taken it SHOUTS to queue-events.log and steps past it.
# A collision is never allowed to be silent again, which is the point.
function Get-F8SeqHighWater([string]$Inbox) {
    $p = Get-F8Paths $Inbox
    if (-not (Test-Path $p.SeqState)) { return 0 }
    try { return [int]((Get-Content $p.SeqState -Raw | ConvertFrom-Json).highWaterSeq) } catch { return 0 }
}

function Set-F8SeqHighWater([string]$Inbox, [int]$Seq) {
    $p = Get-F8Paths $Inbox
    $cur = Get-F8SeqHighWater $Inbox
    if ($Seq -le $cur) { return $cur }
    $obj = @{ highWaterSeq = $Seq; updatedUtc = (Get-Date).ToUniversalTime().ToString('o') }
    try { Write-F8Text $p.SeqState ($obj | ConvertTo-Json -Depth 3) } catch { }
    return $Seq
}

# MUST be called with the inbox lock held.
function Get-F8NextSeq([string]$Inbox) {
    $seq = 0
    $ping = Get-F8PingSeq $Inbox
    if ($ping -gt $seq) { $seq = $ping }
    foreach ($e in (Get-F8Queue $Inbox)) { if ([int]$e.seq -gt $seq) { $seq = [int]$e.seq } }
    Clear-F8IndexCache $Inbox
    $idx = Get-F8CaptureIndex $Inbox
    if ([int]$idx.MaxSeq -gt $seq) { $seq = [int]$idx.MaxSeq }
    $hw = Get-F8SeqHighWater $Inbox
    if ($hw -gt $seq) { $seq = $hw }
    $seq++

    $guard = 0
    while ($idx.BySeq.ContainsKey($seq)) {
        Write-F8Event $Inbox 'error' ("SEQ COLLISION: seq=$seq is already on disk ($(@($idx.BySeq[$seq]) -join '; ')) - stepping past it. A producer or a hand-written capture re-used a live number (WO-1018).")
        $seq++
        $guard++
        if ($guard -gt 10000) { break }
    }
    [void](Set-F8SeqHighWater $Inbox $seq)
    return $seq
}

# -- WO-1018: THE BACKFILL BASELINE -------------------------------------------------------------
# Written by f8-backfill-sweep.ps1. Until it exists, the below-watermark scan stays OFF, because
# ACK.json's `acked` set is EMPTY BY DESIGN (Save-F8AckState compacts contiguous acks into the
# watermark and drops them), so treating that set as the authority would instantly re-open every
# capture ever taken -- ~1273 of them on 2026-08-22 -- and read as a catastrophic regression.
# The sweep reconciles against queue-events.log, which records one 'acked seq=N' line per ack.
function Get-F8Backfill([string]$Inbox) {
    $p = Get-F8Paths $Inbox
    if (-not (Test-Path $p.Backfill)) { return $null }
    try { return (Get-Content $p.Backfill -Raw | ConvertFrom-Json) } catch { return $null }
}

# Seqs proven acked by the append-only event log. This is the authority the sweep reconciles against.
function Get-F8AckedSeqsFromEvents([string]$Inbox) {
    $p = Get-F8Paths $Inbox
    $set = @{}
    if (-not (Test-Path $p.Events)) { return $set }
    foreach ($line in (Get-Content $p.Events -ErrorAction SilentlyContinue)) {
        if ($line -match 'acked seq=(\d+)') { $set[[int]$Matches[1]] = $true }
    }
    return $set
}

# THE consumer entry point: every capture between the last ack and now, OLDEST FIRST.
# Synthesises entries for any seq the queue is missing and flags it LOUDLY -- a missing entry is
# never silently skipped, which is the exact failure this WO exists to kill.
function Get-F8Pending([string]$Inbox) {
    $ack   = Get-F8AckState $Inbox
    $queue = Get-F8Queue $Inbox
    $ping  = Get-F8PingSeq $Inbox
    $wm    = [int]$ack.lastAckSeq
    $ackedSet = @{}
    foreach ($s in @($ack.acked)) { $ackedSet[[int]$s] = $true }

    # WO-1018 -- THE COLLISION-BLIND CONSUMER. This used to be `$bySeq[$s] = $e`: LAST WRITER WINS.
    # When two captures share a sequence, only ONE record ever reached a seat and acking it closed
    # both. That is exactly how capture-20260815-183806-seq2329.md -- the owner's flag "[Main_Castle
    # _Overworld] look at the overcrowding" -- was closed by an ack of an unrelated scene-open error
    # (capture-20260815-210117-seq2329.md) and never triaged. A seq now maps to a LIST of records,
    # so N collided captures surface N times and require N acks.
    $bySeq = @{}
    $maxSeq = $ping
    foreach ($e in $queue) {
        $s = [int]$e.seq
        if (-not $bySeq.ContainsKey($s)) { $bySeq[$s] = @() }
        $bySeq[$s] += $e
        if ($s -gt $maxSeq) { $maxSeq = $s }
    }
    # WO-1018: a capture FILE that reached disk without a queue row and without a ping used to be
    # invisible to the whole consumer. The index is filename-only, so this costs one dir listing.
    $idx = Get-F8CaptureIndex $Inbox
    if ([int]$idx.MaxSeq -gt $maxSeq) { $maxSeq = [int]$idx.MaxSeq }

    # WO-1018 -- BELOW THE WATERMARK. The scan below used to start at $wm+1 and return early when
    # $maxSeq -le $wm, so nothing under the watermark could EVER be found again. Orphans identified
    # by f8-backfill-sweep.ps1 are re-surfaced here, oldest first, and are acked BY FILE (their seq
    # is not unique -- see Get-F8AckState). No baseline on disk = no deep scan; run the sweep first.
    $pending = @()
    $bf = Get-F8Backfill $Inbox
    if ($bf -and $bf.orphans) {
        $ackedFiles = @{}
        foreach ($f in @($ack.ackedFiles)) { $ackedFiles[[string]$f] = $true }
        foreach ($o in @($bf.orphans)) {
            $leaf = [string]$o.file
            if ($ackedFiles.ContainsKey($leaf)) { continue }
            if (-not (Test-Path ([string]$o.path))) { continue }
            $pending += [pscustomobject]@{
                seq         = [int]$o.seq
                utc         = [string]$o.utc
                kind        = [string]$o.kind
                source      = 'backfill'
                capturePath = [string]$o.path
                summary     = ('BURIED (below watermark, never acked): {0}' -f [string]$o.summary)
                unqueued    = $false
                orphan      = $true
                ackKey      = ('file:' + $leaf)
            }
        }
        $pending = @($pending | Sort-Object { [int]$_.seq }, { [string]$_.capturePath })
    }

    $ackedFileSet = @{}
    foreach ($f in @($ack.ackedFiles)) { $ackedFileSet[[string]$f] = $true }

    if ($maxSeq -le $wm) { return $pending }
    for ($s = $wm + 1; $s -le $maxSeq; $s++) {
        if ($ackedSet.ContainsKey($s)) { continue }

        # every record that claims this seq: the queue row(s), PLUS any capture file on disk that no
        # queue row names. Both halves matter -- a queue row without a file and a file without a row
        # are different failures, and two files under one number is the 2329 defect.
        $records = @()
        $claimed = @{}
        foreach ($e in @($bySeq[$s])) {
            if ($null -eq $e) { continue }
            $records += $e
            $leaf = ''
            try { if ($e.capturePath) { $leaf = Split-Path ([string]$e.capturePath) -Leaf } } catch { }
            if ($leaf) { $claimed[$leaf] = $true }
        }
        foreach ($cap in (Resolve-F8CaptureFiles $Inbox $s)) {
            $leaf = Split-Path $cap -Leaf
            if ($claimed.ContainsKey($leaf)) { continue }
            $records += [pscustomobject]@{
                seq         = $s
                utc         = ''
                kind        = 'unqueued'
                source      = 'recovered'
                capturePath = $cap
                summary     = 'RECOVERED from capture file (no QUEUE.jsonl row names this file)'
                unqueued    = $true
            }
            Write-F8Event $Inbox 'warn' ("seq=$s has a capture file no QUEUE.jsonl row names: $cap")
        }

        if ($records.Count -eq 0) {
            Write-F8Event $Inbox 'error' ("seq=$s has NO queue entry and NO capture file - a capture was LOST")
            $pending += [pscustomobject]@{
                seq = $s; utc = ''; kind = 'unqueued'; source = 'recovered'; capturePath = ''
                summary = 'NO QUEUE ENTRY AND NO CAPTURE FILE - capture content is LOST'
                unqueued = $true; orphan = $false; ackKey = ("seq:$s")
            }
            continue
        }

        if ($records.Count -gt 1) {
            Write-F8Event $Inbox 'error' ("SEQ COLLISION: seq=$s names $($records.Count) DIFFERENT captures. Each must be triaged and acked separately (WO-1018).")
        }
        foreach ($r in $records) {
            # one record for the seq keeps the old `seq:` key so `f8-ack.ps1 -Seq n` still works.
            # A collided seq is keyed BY FILE, which is what forces one ack per capture.
            $key = "seq:$s"
            $leaf = ''
            try { if ($r.capturePath) { $leaf = Split-Path ([string]$r.capturePath) -Leaf } } catch { }
            if ($records.Count -gt 1 -and $leaf) { $key = 'file:' + $leaf }
            if ($ackedFileSet.ContainsKey($leaf)) { continue }
            $pending += [pscustomobject]@{
                seq         = $s
                utc         = [string]$r.utc
                kind        = [string]$r.kind
                source      = [string]$r.source
                capturePath = [string]$r.capturePath
                summary     = $(if ($records.Count -gt 1) { ('COLLIDED seq (1 of {0}): {1}' -f $records.Count, [string]$r.summary) } else { [string]$r.summary })
                unqueued    = [bool]$r.unqueued
                orphan      = $false
                ackKey      = $key
            }
        }
    }
    return $pending
}

# -- WO-1018: THE PRUNE STEP THE INBOX NEVER HAD ------------------------------------------------
# 2914 capture files had accumulated by 2026-08-22 because nothing has ever removed one. This MOVES
# acked captures older than -Days into logs/f8-inbox/archive. It NEVER deletes a capture (this
# repo's fleet rule is "never wipe a ticket") and Get-F8CaptureIndex reads the archive, so an
# archived capture is still resolvable by seq. A capture is archive-eligible only when it is
# provably acked: below the watermark, or in ACK.json's acked / ackedFiles sets. Anything the
# backfill sweep flagged as an orphan is NEVER archived -- it still needs a seat.
# Called by triage-archive.ps1 (-InboxOnly runs this alone, touching no live logs).
function Invoke-F8InboxArchive([string]$Inbox, [int]$Days = 14, [switch]$WhatIf) {
    $p = Get-F8Paths $Inbox
    $ack = Get-F8AckState $Inbox
    $wm  = [int]$ack.lastAckSeq
    $cut = (Get-Date).AddDays(-[Math]::Abs($Days))

    $ackedSet = @{}
    foreach ($s in @($ack.acked)) { $ackedSet[[int]$s] = $true }
    $ackedFiles = @{}
    foreach ($f in @($ack.ackedFiles)) { $ackedFiles[[string]$f] = $true }

    # a sweep orphan that is STILL open never gets archived - it has not reached a seat yet. One that
    # has since been acked by file is ordinary history and archives like anything else.
    $protect = @{}
    $bf = Get-F8Backfill $Inbox
    if ($bf -and $bf.orphans) {
        foreach ($o in @($bf.orphans)) {
            if ($ackedFiles.ContainsKey([string]$o.file)) { continue }
            $protect[[string]$o.file] = $true
        }
    }

    Clear-F8IndexCache $Inbox
    $idx = Get-F8CaptureIndex $Inbox
    $moved = 0; $kept = 0; $failed = 0
    $candidates = @($idx.Files | Where-Object { $_.DirectoryName -ne $p.Archive })
    if ($candidates.Count -eq 0) {
        Write-Host 'F8_ARCHIVE_FAIL no capture files found in the inbox - refusing to report a clean sweep over nothing.'
        return @{ Moved = 0; Kept = 0; Failed = 0; Ok = $false }
    }

    foreach ($f in $candidates) {
        if ($f.LastWriteTime -gt $cut) { $kept++; continue }
        if ($protect.ContainsKey($f.Name)) { $kept++; continue }
        $isAcked = $ackedFiles.ContainsKey($f.Name)
        if (-not $isAcked) {
            $seq = -1
            if ($f.Name -match '-seq(\d+)\.md$') { $seq = [int]$Matches[1] }
            else {
                foreach ($k in $idx.BySeq.Keys) {
                    if (@($idx.BySeq[$k]) -contains $f.FullName) { $seq = [int]$k; break }
                }
            }
            if ($seq -ge 0 -and ($seq -le $wm -or $ackedSet.ContainsKey($seq))) { $isAcked = $true }
        }
        if (-not $isAcked) { $kept++; continue }
        if ($WhatIf) { $moved++; continue }
        New-Item -ItemType Directory -Force -Path $p.Archive | Out-Null
        $dest = Join-Path $p.Archive $f.Name
        if (Test-Path $dest) { $dest = Join-Path $p.Archive ($f.BaseName + '-' + $f.LastWriteTime.ToString('yyyyMMddHHmmss') + $f.Extension) }
        try { Move-Item -LiteralPath $f.FullName -Destination $dest -Force; $moved++ }
        catch { $failed++; Write-F8Event $Inbox 'warn' ("archive could not move $($f.Name): $($_.Exception.Message)") }
    }
    Clear-F8IndexCache $Inbox
    Write-F8Event $Inbox 'info' ("inbox archive: moved=$moved kept=$kept failed=$failed olderThanDays=$Days")
    if ($failed -gt 0) {
        Write-Host ("F8_ARCHIVE_FAIL moved={0} kept={1} failed={2}" -f $moved, $kept, $failed)
        return @{ Moved = $moved; Kept = $kept; Failed = $failed; Ok = $false }
    }
    # a dry run must NEVER emit the success marker -- markers are what this repo judges by (CLAUDE.md
    # section 16), and an _OK that moved nothing is exactly the kind of false green that gets shipped.
    if ($WhatIf) {
        Write-Host ("F8_ARCHIVE_DRYRUN wouldMove={0} kept={1} olderThanDays={2} - nothing was moved" -f $moved, $kept, $Days)
        return @{ Moved = 0; Kept = $kept; Failed = 0; Ok = $false }
    }
    Write-Host ("F8_ARCHIVE_OK moved={0} kept={1} olderThanDays={2} archive={3}" -f $moved, $kept, $Days, $p.Archive)
    return @{ Moved = $moved; Kept = $kept; Failed = 0; Ok = $true }
}

# An ack key identifies WHAT is being acked: a seq for a normal queued capture, a file for an
# orphan the sweep surfaced (whose seq may be shared with another capture -- see Get-F8AckState).
function Get-F8AckKey($Entry) {
    if ($null -eq $Entry) { return '' }
    $k = ''
    try { $k = [string]$Entry.ackKey } catch { }
    if (-not [string]::IsNullOrWhiteSpace($k)) { return $k }
    return ('seq:' + [int]$Entry.seq)
}

# Cheap disk-vs-ack reconciliation for f8-check-inbox.ps1. Returns a hashtable:
#   Swept    - has f8-backfill-sweep.ps1 ever run against this inbox
#   Unacked  - count of capture files on disk above the watermark that are not acked
#   Files    - total capture files on disk (inbox + archive)
# An inbox that has never been swept is NOT provably clean, and must not be reported as clean.
function Test-F8InboxClean([string]$Inbox) {
    $ack = Get-F8AckState $Inbox
    $idx = Get-F8CaptureIndex $Inbox
    $wm  = [int]$ack.lastAckSeq
    $ackedSet = @{}
    foreach ($s in @($ack.acked)) { $ackedSet[[int]$s] = $true }
    $ackedFiles = @{}
    foreach ($f in @($ack.ackedFiles)) { $ackedFiles[[string]$f] = $true }
    $unacked = @()
    foreach ($k in $idx.BySeq.Keys) {
        $s = [int]$k
        if ($s -le $wm) { continue }
        if ($ackedSet.ContainsKey($s)) { continue }
        # a collided seq is acked FILE BY FILE, so it is only covered once every file for it is in
        # ackedFiles -- checking the seq alone would report a fully-triaged collision as unreconciled
        $anyOpen = $false
        foreach ($path in @($idx.BySeq[$s])) {
            if (-not $ackedFiles.ContainsKey((Split-Path $path -Leaf))) { $anyOpen = $true; break }
        }
        if (-not $anyOpen) { continue }
        $unacked += $s
    }
    return @{
        Swept   = ($null -ne (Get-F8Backfill $Inbox))
        Unacked = @($unacked | Sort-Object)
        Files   = [int]$idx.FileCount
        Legacy  = @($idx.NoSeq).Count
    }
}

# THE producer entry point. Builds nothing: the caller hands over the finished markdown with a
# __F8SEQ__ placeholder wherever the seq belongs. Allocates the seq, writes the per-seq capture
# file, refreshes LATEST_CAPTURE.md + PING.json (unchanged contract) and APPENDS to QUEUE.jsonl.
# Returns the allocated seq.
function Publish-F8Capture {
    param(
        [string]$Inbox,
        [string]$Kind,
        [string]$Md,
        [string]$Summary,
        [string]$Source = 'f8',
        [string]$BaseName = 'capture',
        [string]$PingMessage = 'F8 capture - triage now (read LATEST_CAPTURE.md or run f8-check-inbox.ps1)'
    )
    $p = Get-F8Paths $Inbox
    New-Item -ItemType Directory -Force -Path $Inbox | Out-Null
    $lock = Enter-F8Lock
    try {
        # WO-1018: allocation is no longer max(ping, queue)+1. Those are both REWRITABLE files, so a
        # daemon restart over a rotated queue re-used a live number (seq 2329 named two unrelated
        # captures). Get-F8NextSeq also consults every capture FILE on disk and a monotonic
        # high-water in SEQ.json, and shouts if the number it picks is already taken.
        $seq = Get-F8NextSeq $Inbox

        # Loud supersede notice: LATEST_CAPTURE.md is about to be overwritten while the previous
        # capture is still un-acked. Not a loss any more (the queue holds it) but it must be VISIBLE.
        $ack = Get-F8AckState $Inbox
        $prevPing = Get-F8PingSeq $Inbox
        $backlog = 0
        if ($prevPing -gt [int]$ack.lastAckSeq) {
            $backlog = @(Get-F8Pending $Inbox).Count
            Write-F8Event $Inbox 'warn' ("seq=$seq SUPERSEDES un-acked seq=$prevPing in LATEST_CAPTURE.md - $backlog capture(s) now queued; they are NOT lost, walk them with f8-check-inbox.ps1")
        }

        # per-seq filename: the old 'capture-<yyyyMMdd-HHmmss>.md' collided (and overwrote!) when two
        # captures landed inside the same second. The seq makes it collision-proof.
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $capPath = Join-Path $Inbox ('{0}-{1}-seq{2}.md' -f $BaseName, $stamp, $seq)

        $body = $Md -replace '__F8SEQ__', "$seq"
        if ($backlog -gt 0) {
            $nl = [Environment]::NewLine
            $banner = ('> BACKLOG: {0} un-acked capture(s) are queued BEHIND this one. Triage OLDEST FIRST via f8-check-inbox.ps1; f8-ack.ps1 acks one at a time.{1}{1}' -f ($backlog + 1), $nl)
            $body = $banner + $body
        }

        # last line of defence: never overwrite an existing capture, even if the allocator was wrong.
        if (Test-Path $capPath) {
            Write-F8Event $Inbox 'error' ("SEQ COLLISION at write: $capPath already exists - writing alongside it, NOT over it (WO-1018).")
            $capPath = Join-Path $Inbox ('{0}-{1}-seq{2}-dup{3}.md' -f $BaseName, $stamp, $seq, (Get-Random -Maximum 99999))
        }

        Write-F8Text $capPath $body
        Write-F8Text $p.Latest $body
        Clear-F8IndexCache $Inbox

        $sum = $Summary
        if ($null -eq $sum) { $sum = '' }
        if ($sum.Length -gt 160) { $sum = $sum.Substring(0, 160) }

        $ping = @{
            seq         = $seq
            firedAtUtc  = (Get-Date).ToUniversalTime().ToString('o')
            kind        = $Kind
            capturePath = $capPath
            summary     = $sum
            source      = $Source
            message     = $PingMessage
        }
        Write-F8Text $p.Ping ($ping | ConvertTo-Json -Depth 4)

        $entry = @{
            seq         = $seq
            utc         = $ping.firedAtUtc
            kind        = $Kind
            source      = $Source
            capturePath = $capPath
            summary     = $sum
        }
        Append-F8Text $p.Queue (($entry | ConvertTo-Json -Depth 4 -Compress) + [Environment]::NewLine)
        Write-F8Event $Inbox 'info' ("queued seq=$seq kind=$Kind source=$Source file=$capPath")
        return $seq
    }
    finally { Exit-F8Lock $lock }
}
