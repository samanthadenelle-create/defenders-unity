# WO-1624 - F8 desktop daemon: a type-qualified enum name inside a string kills every pass, so the Editor/Player log scan has NEVER run

**Status:** FIXED 2026-09-10 - lead reviewed the diff (typed [System.IO.FileShare]::ReadWrite, counters, catch block untouched), parse OK, daemon restarted pid 47444 with passFails=0 and detail=watching; the live reads>0 proof lands on the next Play session. (was: IMPLEMENTED - awaiting lead review, lane F8-DAEMON)
**Minted:** 2026-09-10 (CLI, main-line banner; bumped 1621 -> 1625 in the SAME edit)
**Silo / Lane:** Tooling / F8 watcher (`.claude/skills/run-defenders/`) - **no .cs, no Unity, no git**
**Severity:** P1 process, small fix. CLAUDE.md sec.14 is BINDING and its whole point is that
**the owner is never the bug detector**. Half the daemon's detection surface has been dead since
2026-07-09 (sec.1d) and the daemon reported itself healthy the entire time.
**Type:** EXISTING system. The daemon runs, survives, and heartbeats - by design (sec.1c).
**Owner words:** none - lane finding off the daemon's own heartbeat.

---

## 1. The defect, measured (read at source 2026-09-10)

### 1a. The captured error

`logs/f8-inbox/HEARTBEAT.json`, `producers.desktop` (file read this session; the daemon's own
`Beat`, so it is the daemon reporting on itself):

```
"pid": 5620,
"pollSeconds": 5,
"passFails": 9124,
"detail": "pass-failed: Cannot convert argument \"share\", with value: \"FileShare.ReadWrite\", for \"Open\" to type \"System.IO.FileShare\": \"Cannot convert value \"FileShare.ReadWrite\" to type \"System.IO.FileShare\". Error: \"Unable to match the identifier name FileShare.ReadWrite to a valid enumerator name. Specify one of the following enumerator names and try again:\r\nNone, Read, Write, ReadWrite, Delete, Inheritable\""
```

Surfaced to the seat by `.claude/skills/run-defenders/f8-check-inbox.ps1` on 2026-09-09 as an
`F8_DAEMON_OK producer=desktop pid=5620 ... detail=pass-failed: ...` line - i.e. **`_OK`, with the
failure riding along in a detail field.**

**`passFails: 9124`** at a 5-second cadence is roughly **12.7 hours of continuous failure** for this
process alone.

### 1b. The line

`.claude/skills/run-defenders/f8-watch-daemon.ps1:224`:

```
$fs = [System.IO.File]::Open($logPath, 'Open', 'Read', 'FileShare.ReadWrite')
```

**Read the error text, not the folk explanation.** PowerShell says *"Unable to match the identifier
name FileShare.ReadWrite to a valid enumerator name. Specify one of the following: None, Read,
Write, ReadWrite, Delete, Inheritable"*. The same call passes `'Open'` and `'Read'` as bare strings
and **those coerce fine** - to `FileMode.Open` and `FileAccess.Read`.

So the cause is precise: **the string carries the TYPE NAME as well as the member name.**
`'FileShare.ReadWrite'` is not a member of `System.IO.FileShare`; `'ReadWrite'` is. This is not
"PowerShell 5.1 fails to coerce a bare token" - it coerces bare tokens twice on the same line.

### 1c. What is actually broken, and what is NOT

The throw is caught. `f8-watch-daemon.ps1:255-261`:

```
catch {
    # SURVIVE, do not exit. A pass that throws (log locked by Unity, a bad cast, a transient IO
    # error) used to terminate the daemon with no trace anywhere. Log it and keep watching.
    $passFails++
    Write-F8Event $Inbox 'warn' ("desktop daemon pass failed (#{0}), continuing: {1}" -f $passFails, $_.Exception.Message)
    Beat ('pass-failed: ' + $_.Exception.Message)
}
```

**That guard is correct and is doing its job** - it is the reason the daemon survived 9124 throws
instead of dying silently. It is also the reason nobody noticed.

Reading the pass in order:

- The `break-log.jsonl` offset block runs **BEFORE the throw** (it ends with `Save-BreakOffset
  $breakBase` at **`:213`**), and `HEARTBEAT.json` shows `"breakOffset": 1331` - **non-zero**, which
  with the ordering means the block runs and persists. *(One snapshot shows a value, not
  advancement - do not write "advancing" anywhere.)* **So F8 flags still surface. Do not tell anyone
  F8 is dead.**
- The `foreach ($logPath in @($EditorLog, $PlayerLog))` loop begins at **`:217`**, and `:224` is
  inside it. **Every pass dies on the FIRST log path.**
- Which means the classifier at **`:234-236`** has **never executed**:
  ```
  $isFlagged  = $line -match '\[BreakCapture\].*flagged|kind.*flagged'
  $isError    = $line -match 'error CS\d+|Exception:|NullReferenceException|AssertionException'
  $isSoftlock = $line -match 'Infinite loop|stack overflow|Deadlock|softlock'
  ```

**CLAUDE.md sec.14 promises the daemon "watches `break-log.jsonl` + Editor/Player logs forever".
Exactly half of that has been true.** An uncaught exception or a softlock that never wrote a
break-log entry has had no path to any seat.

### 1d. Age, and the sibling copies

`git log -S"'FileShare.ReadWrite'" -- .claude/skills/run-defenders/f8-watch-daemon.ps1` returns
exactly one commit: **`22ae4de5b` (2026-07-09) "feat: persistent F8 inbox daemon with auto agent
ping"**. The line has been wrong since the daemon was born. `passFails: 9124` is one process's
share, not the total.

Grepped `'FileShare\.` across all `*.ps1` 2026-09-10 - **three hits, one live, two stale copies**:

- `./.claude/skills/run-defenders/f8-watch-daemon.ps1:224` - **the live one, the only one to fix**
- `./tmp/play-deploy-37837f585/.claude/skills/run-defenders/f8-watch-daemon.ps1:196`
- `./tmp/play-deploy-dcd25e9fe/.claude/skills/run-defenders/f8-watch-daemon.ps1:196`

The two under `tmp/` are throwaway deploy snapshots. **Do not edit them** - editing a snapshot
creates a second source of truth for a script that has exactly one, which is the duplicated-state
failure CLAUDE.md sec.2 / sec.5 / sec.16 each describe.

## 2. Target - what "fixed" means

The desktop producer completes a full pass: break-log offsets advance AND the Editor/Player log tail
is read and classified. Its heartbeat `detail` reads `watching`, and `passFails` stays at 0.

## 3. The fix

`.claude/skills/run-defenders/f8-watch-daemon.ps1:224` - use the TYPED enum value:

```
$fs = [System.IO.File]::Open($logPath, 'Open', 'Read', [System.IO.FileShare]::ReadWrite)
```

`'ReadWrite'` (bare member name in a string) would also work and matches the two neighbouring
arguments' style. **Prefer the typed form**: it cannot be mis-shortened again, and it is the shape
the error message's own remedy points at. State which you used and why in the RESULT.

`FileShare.ReadWrite` is required, not incidental: Unity holds `Editor.log` / `Player.log` open for
writing while it runs, so a share mode short of `ReadWrite` will re-throw with a different message
and land the daemon straight back in the same catch.

## 4. Acceptance - and it needs a RESTART, which is easy to get wrong

`HEARTBEAT.json`'s `detail` is the **last `Beat`** and `passFails` is a **counter that only resets
when the process starts**. Editing the file changes neither: pid 5620 is running the old text in
memory. So:

1. `powershell -File .claude\skills\run-defenders\f8-watch-stop.ps1`
2. `powershell -File .claude\skills\run-defenders\f8-watch-start.ps1` (idempotent, per CLAUDE.md
   sec.14)
3. Read `logs/f8-inbox/HEARTBEAT.json` across **at least three heartbeats** and assert on the
   `desktop` producer:
   - `detail` reads **`watching`** (or a real capture detail) and **contains no `pass-failed`**
   - **`passFails` is `0`** and stays 0
   - `pid` differs from `5620`
4. Run `.claude/skills/run-defenders/f8-check-inbox.ps1` and paste the verbatim line in the RESULT.
   It must carry **no `pass-failed`** text.
5. **PROVE THE SUCCESS PATH, not only the absence of the error** (memory
   `prove-the-success-path-not-just-the-refusal`): confirm the Editor/Player scan actually READS
   something. A daemon that no longer throws because the file is simply missing has not been fixed;
   it has been silenced - `f8-watch-daemon.ps1:218` `if (-not (Test-Path $logPath)) { continue }` is
   the branch that would hide it.
   WARNING: **`$logPositions` is an IN-MEMORY hashtable** (`:230` `$logPositions[$logPath] = $len`) -
   nothing found in this file persists it, so the offset is **not observable from outside the
   process**. Surfacing it in the `Beat` detail (e.g. `watching editorOffset=<n>/<len>`, the shape
   the `device` producer already uses: `"detail": "published=0 dupSuppressed=0 offset=1215/1215"`)
   is **explicitly ALLOWED for this lane** - it is the same file, it is liveness not triage state,
   and it is the only way to prove the branch runs.

## 5. Pins - what must not move

- **The `catch` at `:255-261`.** It is correct and its comment records why. Do not narrow it, do not
  rethrow, do not turn a pass failure fatal. Its `Beat` is the only reason this defect was findable.
- **`passFails` and the `Beat` contract.** `HEARTBEAT.json`'s note says *"WO-1460 liveness only ...
  Not triage state."* Keep that separation - do not start writing captures into the heartbeat.
- **The `device` producer section** (pid 14776, serial `SM02G4061955851`, `pollSeconds: 30`). A
  different producer, healthy, untouched.
- **The break-log offset block above `:217`** and its `Save-BreakOffset` (`:137`, called at `:141`
  and `:213`) - it works and it is the half that has been carrying the whole system.
- **The anchored `"kind"` regex** and its comment about the old greedy pattern that captured
  `Main_Castle_Overworld` instead of `flagged`. Do not "tidy" it.
- **`Emit-Capture`'s queue semantics.** CLAUDE.md sec.14 / WO-965: `QUEUE.jsonl` is the append-only
  record; `LATEST_CAPTURE.md` and `PING.json` are single slots. This fix must not change how
  captures are enqueued or acked.
- **`f8-check-inbox.ps1` / `f8-ack.ps1` / `f8-poll-rewake.ps1` / `.claude/settings.json` hooks.** Out
  of scope (memory `f8-passive-listener-hooks`).

## 6. RED-first proof

There is no PowerShell test harness in this repo (listed 2026-09-10: `tools/regression/` holds
`MANUAL_QA_CHECKLIST.md`, `README.md`, `checkin_gate.ps1`, `static_gate.py`, `static_gate.sh` - no
test runner, no `tools/tests/`; the same finding is written up in WO-1622 sec.6). So the RED-first
proof here is a **two-line direct reproduction**, run and pasted verbatim into the RESULT:

```
# RED - reproduces the captured message
[System.IO.File]::Open('<any file>', 'Open', 'Read', 'FileShare.ReadWrite')
# GREEN - the fix
[System.IO.File]::Open('<any file>', 'Open', 'Read', [System.IO.FileShare]::ReadWrite)
```

Run both under **Windows PowerShell 5.1** (`$PSVersionTable.PSVersion` in the RESULT) - the daemon's
host, and the one this project standardises on (memory `owner-prefs-powershell`). Plus the sec.4
restart evidence, which is the real acceptance.

## 7. Not in scope

- Do **not** edit the two `tmp/play-deploy-*/` copies (sec.1d).
- Do **not** edit any `.cs` file, run Unity, or touch a gate.
- Do **not** change `f8-check-inbox.ps1`'s `F8_DAEMON_OK` verdict shape. That it printed `_OK` while
  carrying `pass-failed` in a detail field is a real second-order finding
  (memory `gates-report-success-without-proving-it`) - **record it in the RESULT as a candidate
  follow-up and hand it back. Do not take it in this lane.**
- Do **not** widen the classifier regexes at `:234-236`. Once the scan actually runs it may surface a
  backlog of Editor/Player log noise; **that is data, and it is triaged per CLAUDE.md sec.13/sec.14,
  not suppressed by tightening the pattern that finally started working.**
- Do **not** ack anything in the F8 inbox as part of this work. `f8-ack.ps1` acks exactly ONE, oldest
  first (CLAUDE.md sec.14, WO-965).
- Do **not** commit. The lead is the sole committer.
