# WO-1625 RESULT - the F8 liveness verdict now agrees with the heartbeat it is printing

**Status:** IMPLEMENTED - awaiting lead review (lane INBOX-VERDICT 2026-09-10)
**Lane:** Tooling / F8 watcher. No `.cs`, no Unity, no gate, no git, no ack.
**Host:** `$PSVersionTable.PSVersion` = **5.1.26100.9444** (Windows PowerShell 5.1 - no ternary, no
`??`, no `&&` used anywhere in the diff).

---

## 1. Files changed - the two script files WO section 4 specified, plus the board flip

| File | What changed |
|---|---|
| `.claude/skills/run-defenders/f8-inbox-lib.ps1` | `Get-F8Heartbeat` now projects `passFails`, defaulting a missing/unparsable key to `0`. |
| `.claude/skills/run-defenders/f8-check-inbox.ps1` | The non-stale branch is split: `F8_DAEMON_DEGRADED` when the producer is failing, `F8_DAEMON_OK` (unchanged text) otherwise. |
| `WorkOrders/WORK_ORDER_1625_...md` | `**Status:**` flipped (WO section 8). |

Nothing else was touched. `f8-watch-daemon.ps1`, `f8-ack.ps1`, `f8-poll-rewake.ps1`,
`f8-watch-start.ps1`, `f8-watch-stop.ps1`, `.claude/settings.json`, `logs/f8-inbox/*` and every
`tmp/play-deploy-*/` snapshot are untouched (WO section 7).

### 1a. `f8-inbox-lib.ps1` - the projection (was `:160-168`, the WO's citation)

Added beside the existing `pid` guard, mirroring its `try { [int] } catch { 0 }` shape exactly as
WO section 4a required (`:153-154` was the model):

```
$passFails = 0
try { $passFails = [int]$v.passFails } catch { $passFails = 0 }
```

and `passFails = $passFails` added to the emitted hashtable. **No other key changed and the sort at
the end of the function is untouched** (WO section 6, last pin).

`[int]$null` is `0` in PowerShell 5.1 without throwing, so the `device`-shaped producer that carries
no `passFails` key at all resolves to `0` through the normal path; the `catch` covers an unparsable
value. This is the WO section 1d regression and it is proven green in section 3 below.

### 1b. `f8-check-inbox.ps1` - the predicate and the third token

Computed once per producer, immediately after `$age`, **structured counter first, string second**
(WO section 2's reasoning - `pass-failed` is the only failure vocabulary measured, so it is the
fallback, not the primary):

```
$degraded = ($passFails -gt 0) -or (([string]$p.detail) -match 'pass-failed')
```

The branch is an `elseif` **after** the age check, not a separate `if`. That is structural, not
incidental: it is what makes WO section 5 step 4 hold - **STALE always wins over DEGRADED**, because
a producer that is not beating at all is a STALE problem.

Field order is flat and greppable, matching the two existing tokens:

```
F8_DAEMON_DEGRADED producer= pid= age= passFails= lastDeviceUtc= detail=
F8_DAEMON_DEGRADED <plain-language remedy line>
```

Two lines, verdict then what-to-do, copying the `F8_DAEMON_STALE` shape. The `detail` is carried
**verbatim** - the whole point of the ticket is that the evidence was already being printed and only
the verdict disagreed with it.

### 1c. The `-Quiet` decision (WO section 6 asked for it to be deliberate and stated)

**`F8_DAEMON_DEGRADED` is NOT suppressed under `-Quiet`.**

The file had already made this decision and the fix follows it rather than inventing a new rule: the
`F8_DAEMON_STALE` branch is ungated, and `-Quiet` gates only the `F8_DAEMON_OK` line. So the
established meaning of `-Quiet` in this script is *hide health, never hide a failure*. DEGRADED is a
failure. Proof in section 3, GREEN 3: under `-Quiet` the run prints the two DEGRADED lines and
nothing else - no OK line, no `NO_CAPTURE`.

Checked before deciding: **no caller passes `-Quiet`**, and the only programmatic consumer,
`f8-watch-poll.ps1:23`, judges by `$LASTEXITCODE` alone (`if ($LASTEXITCODE -eq 0)`) and never parses
stdout. `.cursor/rules/f8-auto-triage.mdc:32` likewise instructs the seat to branch on the exit code.
So an extra line under `-Quiet` breaks no consumer.

## 2. The exit-code contract is intact (WO section 6, first pin)

`F8_DAEMON_DEGRADED` touches no exit path. GREEN 1, GREEN 3 and GREEN 4 below each show a degraded
producer with an empty inbox still exiting **1**, and the live run (GREEN 5) shows the healthy
`NO_CAPTURE ack=4983 ping=4983` line unchanged, also exit 1. The capture-surfacing half below
`Write-F8Liveness` was not edited at all - proven independently by the 39/39 selftest in section 4.

## 3. Proof - verbatim runs

`-InboxOverride` was used for every fixture run (WO section 5). **`logs/f8-inbox/` was never written
to and nothing was acked.**

Fixture generator: `<scratchpad>/f8fix/make-fixture.ps1`, three producers exactly as WO section 5
step 1 specified - `a_failing` (`passFails: 9360`, detail begins `pass-failed: ...`), `b_healthy`
(`passFails: 0`, `detail: watching ...`), `c_nokey` (**no `passFails` key**, the `device` shape from
WO section 1d).

### RED - the pre-fix script, run first

```
> & "$sp\make-fixture.ps1" -Dir "$sp\inbox" -AgeSeconds 0
> & 'D:\EoA\.claude\skills\run-defenders\f8-check-inbox.ps1' -InboxOverride "$sp\inbox"

F8_DAEMON_OK producer=a_failing pid=5620 age=0s lastDeviceUtc= detail=pass-failed: Cannot convert argument "share", with value: "FileShare.ReadWrite" ...
F8_DAEMON_OK producer=b_healthy pid=47444 age=0s lastDeviceUtc= detail=watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0
F8_DAEMON_OK producer=c_nokey pid=14776 age=0s lastDeviceUtc=2026-09-10T05:25:26.3727270Z detail=published=0 dupSuppressed=0 offset=1215/1215
NO_CAPTURE
exit=1
PSVersion=5.1.26100.9444
```

Line 1 is the defect, reproduced: **`F8_DAEMON_OK` on a producer whose own `detail` says
`pass-failed`.** This is WO section 5 step 3.

### GREEN 1 - fresh fixture, post-fix

```
> & "$sp\make-fixture.ps1" -Dir "$sp\inbox" -AgeSeconds 0
> powershell -NoProfile -ExecutionPolicy Bypass -File .claude\skills\run-defenders\f8-check-inbox.ps1 -InboxOverride "$sp\inbox"

F8_DAEMON_DEGRADED producer=a_failing pid=5620 age=0s passFails=9360 lastDeviceUtc= detail=pass-failed: Cannot convert argument "share", with value: "FileShare.ReadWrite" ...
F8_DAEMON_DEGRADED the a_failing producer IS beating but its own work is failing. Captures may be going nowhere even though it looks alive. Read the detail= above, then restart it: powershell -File .claude\skills\run-defenders\f8-watch-start.ps1
F8_DAEMON_OK producer=b_healthy pid=47444 age=0s lastDeviceUtc= detail=watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0
F8_DAEMON_OK producer=c_nokey pid=14776 age=0s lastDeviceUtc=2026-09-10T05:25:26.3727270Z detail=published=0 dupSuppressed=0 offset=1215/1215
NO_CAPTURE
exit=1
```

WO section 5 step 2 satisfied line for line: DEGRADED first, carrying `passFails=9360` **and** the
detail; `b_healthy` OK; **`c_nokey` OK - the missing key did not degrade a healthy producer**, which
the WO names as the regression that matters.

### GREEN 2 - same fixture stamped 200s old: STALE still wins

```
> & "$sp\make-fixture.ps1" -Dir "$sp\inbox" -AgeSeconds 200

F8_DAEMON_STALE 200 producer=a_failing pid=5620 process-dead last=2026-09-10T06:18:49.7242733Z lastDeviceUtc= detail=pass-failed: Cannot convert argument "share", with value: "FileShare.ReadWrite" ...
F8_DAEMON_STALE the a_failing half of the section 14 chain is NOT proving itself alive. Captures may be going nowhere. Restart: powershell -File .claude\skills\run-defenders\f8-watch-start.ps1
F8_DAEMON_STALE 200 producer=b_healthy pid=47444 process-alive-but-not-beating ...
F8_DAEMON_STALE 200 producer=c_nokey pid=14776 process-alive-but-not-beating ...
NO_CAPTURE
exit=1
```

`a_failing` is both stale AND failing, and it reads **STALE, not DEGRADED** - WO section 5 step 4.
The STALE lines are byte-identical to the pre-fix ones.

### GREEN 3 - `-Quiet`

```
> powershell ... -File f8-check-inbox.ps1 -InboxOverride "$sp\inbox" -Quiet

F8_DAEMON_DEGRADED producer=a_failing pid=5620 age=0s passFails=9360 lastDeviceUtc= detail=pass-failed: Cannot convert argument "share", ...
F8_DAEMON_DEGRADED the a_failing producer IS beating but its own work is failing. ...
exit=1
```

The failure survives `-Quiet`; the two healthy producers and `NO_CAPTURE` are suppressed as before.

### GREEN 4 - the string half of the predicate, on a producer with NO counter key

A one-producer fixture with `detail: "pass-failed: some producer with no counter key"` and no
`passFails` key at all:

```
F8_DAEMON_DEGRADED producer=d_strOnly pid=14776 age=0s passFails=0 lastDeviceUtc= detail=pass-failed: some producer with no counter key
F8_DAEMON_DEGRADED the d_strOnly producer IS beating but its own work is failing. ...
NO_CAPTURE
exit=1
```

Proves the `-or` arm carries a failure that no counter reports - and shows the honest `passFails=0`
alongside it rather than inventing a number.

### GREEN 5 - the LIVE inbox, read-only (WO section 5 step 5)

Daemon running, pid 47444, `logs/f8-inbox/HEARTBEAT.json` read at source this session showing
`"passFails": 0` for `desktop` and no such key for `device`:

```
> powershell -NoProfile -ExecutionPolicy Bypass -File .claude\skills\run-defenders\f8-check-inbox.ps1

F8_DAEMON_OK producer=desktop pid=47444 age=28s lastDeviceUtc= detail=watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0
F8_DAEMON_OK producer=device pid=14776 age=21s lastDeviceUtc=2026-09-10T05:25:26.3727270Z detail=published=0 dupSuppressed=0 offset=1215/1215
NO_CAPTURE ack=4983 ping=4983
exit=1
```

Both live producers are genuinely healthy, so both still read OK - including `device`, the
no-`passFails`-key producer, against the real file rather than a fixture. **Nothing was acked.**
The `F8_DAEMON_OK` and `NO_CAPTURE` lines are byte-identical to their pre-fix text.

## 4. No-regression proof on the half the WO says not to touch

`.claude/skills/run-defenders/f8-inbox-selftest.ps1` is the WO-1018/WO-1145 regression suite for this
exact script and it runs in a throwaway `$env:TEMP` inbox. Run **before** the edit and **after**:

```
before:  F8_SELFTEST_OK 39/39
after:   F8_SELFTEST_OK 39/39
```

Judged by the marker, never the exit code (memory `gates-report-success-without-proving-it`). The
capture-surfacing half - collision handling, oldest-first walk, the ack watermark, the sweep, the
archive - is unchanged.

## 5. Two premise corrections (CLAUDE.md section 11B - stated, not silently absorbed)

1. **The `UserPromptSubmit` hook does NOT call this script.** The lane brief said it did.
   `.claude/hooks/f8-prompt-check.ps1` re-implements the pending check inline (its own `PING.json` /
   `ACK.json` read plus `Get-F8Pending` from the shared lib) and names `f8-check-inbox.ps1` only in
   prose it injects for the seat. So the hook could not have been broken by an output-shape change
   either way. Byte-compatibility of the `F8_DAEMON_OK` and `NO_CAPTURE` lines was preserved anyway,
   and is shown in GREEN 5.
2. **WO section 5 step 3's "there is no PowerShell test harness in this repo" is true of
   `tools/regression/` and false of this skill folder.** `f8-inbox-selftest.ps1` exists, has 39
   cases, and drives `f8-check-inbox.ps1` in a child process precisely because `Write-Host` is not
   capturable in 5.1. The before/after verbatim pair the WO asked for was still produced (section 3),
   and the selftest was run as an *additional* proof. The selftest itself was **not edited** - it is
   not in the WO's named scope.

## 6. Open question for the lead

`f8-inbox-selftest.ps1` has no liveness/heartbeat case at all - it fixtures no `HEARTBEAT.json`, so
neither the STALE branch (WO-1460) nor this new DEGRADED branch is pinned by a runnable suite; the
proof above is a pasted transcript, which goes stale the moment someone edits the predicate. Should a
follow-up WO add a heartbeat case group (fresh-failing / fresh-healthy / no-key / aged) to that
suite, so the verdict logic is pinned the way the queue logic already is?
