# WO-1625 - f8-check-inbox prints an OK verdict while the same line carries the daemon's own failure

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; bumped 1625 -> 1628 in the SAME edit)
**Silo / Lane:** Tooling / F8 watcher (`.claude/skills/run-defenders/`) - **no .cs, no Unity, no git**
**Severity:** P1 process, small fix. CLAUDE.md sec.14 is BINDING and its whole point is that
**the owner is never the bug detector**. This is the detector's own self-report lying, which is the
memory `gates-report-success-without-proving-it` class applied to the F8 chain itself.
**Type:** EXISTING system. The script runs, is hook-invoked every turn, and its capture-surfacing
half is correct. Only the liveness VERDICT is wrong.
**Owner words:** none - lane finding, handed back by the WO-1624 lane and re-proven at source here.

---

## 1. What was measured (read at source 2026-09-10)

### 1a. The captured evidence

`WorkOrders/WORK_ORDER_1624_f8_desktop_daemon_fileshare_cast_kills_the_editor_log_scan.RESULT.md:123-124`
records the state the check script was reading:

```
"desktop": { "pid": 5620, "passFails": 9360, "breakOffset": 1331,
             "detail": "pass-failed: Cannot convert argument \"share\", with value: \"FileShare.ReadWrite\" ..." }
```

and `:180-185` of that same RESULT states the consequence and hands it back:

> `f8-check-inbox.ps1` printed **`F8_DAEMON_OK producer=desktop ...`** for ~12.7 hours while carrying
> `pass-failed: ...` inside its `detail` field. A verdict token that reads `_OK` while a failure rides
> along in a field is [...] follow-up: make the `F8_DAEMON_OK` verdict conditional on `passFails == 0`
> and a `detail` free of `pass-failed` (`F8_DAEMON_DEGRADED` otherwise).

`passFails: 9360` at the desktop producer's `pollSeconds: 5` cadence is roughly **13 hours of
continuous failure**, reported to every seat, every turn, as an OK.

### 1b. The line that decides the verdict

`.claude/skills/run-defenders/f8-check-inbox.ps1:37-47`, `Write-F8Liveness`:

```
foreach ($p in @($hb.Producers)) {
    $age = [int]$p.ageSec
    if ($age -lt 0 -or $age -gt $StaleSeconds) {          # :39
        ... F8_DAEMON_STALE ...                          # :42-43
    } elseif (-not $Quiet) {
        Write-Host ("F8_DAEMON_OK producer={0} pid={1} age={2}s lastDeviceUtc={3} detail={4}" -f ...)   # :45
    }
}
```

**AGE IS THE ONLY PREDICATE.** `$StaleSeconds = 90` (`:30`). A producer that beats on time is
declared OK regardless of what it says while beating. The failure text is then interpolated into the
same line as `detail={4}`, so the script prints the proof of the defect inside the verdict that
denies it.

### 1c. The second half of the defect - the field is not even available yet

`.claude/skills/run-defenders/f8-inbox-lib.ps1:160-168`, `Get-F8Heartbeat`, builds the producer
hashtable the loop above iterates:

```
$list += @{
    name          = [string]$prop.Name
    pid           = $procPid
    ageSec        = $age
    updatedUtc    = [string]$v.updatedUtc
    alive         = $alive
    detail        = [string]$v.detail
    lastDeviceUtc = [string]$v.lastDeviceUtc
}
```

**There is no `passFails` key.** It is present in the JSON and dropped by the projection. So this is
a TWO-FILE fix, not a one-line predicate change in the check script.

### 1d. Not every producer has the field - read this before writing the predicate

`logs/f8-inbox/HEARTBEAT.json`, read twice this session (06:08:21Z and 06:08:46Z):

```
"desktop": { ..., "passFails": 0, "pollSeconds": 5, "pid": 47444,
             "detail": "watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0" },
"device":  { ..., "pollSeconds": 30, "pid": 14776,
             "detail": "published=0 dupSuppressed=0 offset=1215/1215",
             "reason": "no-new-signal", "serial": "SM02G4061955851" }
```

The **`device` producer carries NO `passFails` key at all.** A predicate written as
"degraded unless passFails is 0" will read `$null` for the device producer. **A missing key must be
treated as 0 / not-degraded**, or this fix degrades a healthy producer on its first run - which is
the same false-signal defect pointed the other way.

### 1e. Who consumes the token

Grepped `F8_DAEMON_` across `.claude/hooks/`, `.claude/skills/run-defenders/` and `.cursor/rules/`
2026-09-10: **every hit is inside `f8-check-inbox.ps1` itself** (`:28`, `:34`, `:42`, `:43`, `:45`).
No hook, no poller and no rule parses the token. So adding a THIRD token is safe for machines; its
audience is the seat reading the turn's output.

## 2. What is NOT claimed

- **Not claimed:** that any capture was lost because of this. The capture path is separate and its
  own comment says the liveness block "NEVER changes the exit code" (`:29`). Nothing here has been
  measured to drop a capture, and the WO-1624 lane proved the break-log half kept working throughout.
- **Not claimed:** that `detail` has any other failure vocabulary than `pass-failed`. That is the one
  prefix the daemon's catch emits (`f8-watch-daemon.ps1:255-261`, quoted in WO-1624 sec.1c). Whether
  other producers will ever write a failure word into `detail` is unknown - which is why the spec
  below keys on the STRUCTURED counter first and the string second.
- **Not claimed:** any number for how many seats saw the false OK. `passFails` is one process's
  counter, reset at process start (WO-1624 sec.4).
- **Not investigated:** whether the `device` producer should gain a `passFails` counter of its own.
  Out of scope; do not add one.

## 3. Target - what "fixed" means

A producer whose heartbeat carries a failure is never described with an OK token. The seat reading
its turn output can tell "the F8 chain is healthy" from "the F8 chain is alive but failing" without
reading a detail field.

## 4. The fix

Two files.

**(a) `.claude/skills/run-defenders/f8-inbox-lib.ps1:160-168`** - project the counter, defaulting a
missing key to 0:

```
passFails = <parsed [int] of $v.passFails, 0 when the key is absent or unparsable>
```

Use the same defensive shape the file already uses for `pid` (`:153-154`: `try { [int] } catch { 0 }`).
Do not change any other key, and do not change the sort at `:170`.

**(b) `.claude/skills/run-defenders/f8-check-inbox.ps1:44-46`** - split the non-stale branch in two:

- **degraded** when `passFails -gt 0` **OR** `detail` matches `pass-failed`
  -> print `F8_DAEMON_DEGRADED` with `producer=`, `pid=`, `age=`, `passFails=` and the full `detail=`,
  plus one plain-language second line naming the remedy (the STALE branch at `:43` is the shape to
  copy: a verdict line then a what-to-do line).
- **ok** otherwise -> the existing `F8_DAEMON_OK` line, unchanged text.

The degraded line must carry the detail verbatim. The whole point is that the evidence was already
being printed; what was missing was the verdict agreeing with it.

**Keep the token vocabulary flat and greppable** - `F8_DAEMON_OK`, `F8_DAEMON_STALE`,
`F8_DAEMON_DEGRADED`, one per line, same field order. Do not nest, do not colour, do not reformat the
existing two.

## 5. Acceptance

`-InboxOverride` (`:12`, `# -InboxOverride: tests only`) is the seam that makes this testable without
touching the live inbox. Use it - do NOT edit `logs/f8-inbox/HEARTBEAT.json`.

1. Build a scratch inbox directory with a hand-written `HEARTBEAT.json` containing three producers:
   - one with `passFails: 9360` and a `detail` beginning `pass-failed: ...`, freshly stamped
   - one with `passFails: 0` and `detail: watching ...`, freshly stamped
   - one with **no `passFails` key at all** and a healthy detail (the `device` shape from sec.1d)
2. Run `f8-check-inbox.ps1 -InboxOverride <scratch>` and paste the verbatim output into the RESULT.
   Assert: line 1 is `F8_DAEMON_DEGRADED` and carries `passFails=9360` plus the detail; lines 2 and 3
   are `F8_DAEMON_OK`. **The third one is the regression that matters** - a missing key must not
   degrade.
3. **RED-first proof:** run step 2 against the PRE-fix script first and paste that output too. It
   must show `F8_DAEMON_OK` on the failing producer. There is no PowerShell test harness in this repo
   (`tools/regression/` holds `MANUAL_QA_CHECKLIST.md`, `README.md`, `checkin_gate.ps1`,
   `static_gate.py`, `static_gate.sh` - no runner; the same finding is recorded in WO-1622 sec.6 and
   WO-1624 sec.6), so a before/after pair of verbatim runs IS the proof.
4. Re-stamp the scratch heartbeat older than `$StaleSeconds` (90) and confirm the STALE branch still
   wins over the degraded branch - a producer that is not beating at all is a STALE problem, not a
   DEGRADED one, and STALE must remain the louder verdict.
5. Run `f8-check-inbox.ps1` against the LIVE inbox read-only and paste the line. **Do not ack
   anything** (CLAUDE.md sec.14 / WO-965: `f8-ack.ps1` acks exactly one capture and an ack is not
   reversible).
6. Record `$PSVersionTable.PSVersion` in the RESULT - Windows PowerShell 5.1 is the host
   (memory `owner-prefs-powershell`).

## 6. Pins - what must not move

- **The EXIT-CODE CONTRACT.** `:8` states it and `:29` states that liveness never touches it:
  exit 0 + `NEW_CAPTURE` when work waits, exit 1 + `NO_CAPTURE` when clean. `F8_DAEMON_DEGRADED` must
  **NOT** change the exit code. A degraded daemon with no pending captures still exits 1.
- **The whole capture-surfacing half** below `Write-F8Liveness`: `Get-F8AckState` / `Get-F8Pending`
  (`:56-57`), the oldest-first walk, the `seq=` / `kind=` / `firedAt=` / `latest=` / `capture=` /
  `pending=` lines, and both `NO_CAPTURE` prints (`:52`, `:87`). WO-965 exists because this half was
  once a single slot and two of the owner's own captures were silently buried (CLAUDE.md sec.14).
  **Do not touch it.**
- **The `F8_DAEMON_STALE` branch (`:39-43`)** and its `$StaleSeconds = 90` (`:30`), including the
  WO-1460 comment block at `:23-29` explaining why it exists. Add beside it, never inside it.
- **`-Quiet`.** The OK line is already suppressed under `-Quiet` (`:44`). Decide deliberately and say
  so in the RESULT whether DEGRADED is also suppressed - the argument for printing it even under
  `-Quiet` is that it is a failure, and `-Quiet` exists to hide health.
- **`f8-ack.ps1`, `f8-poll-rewake.ps1`, `f8-watch-daemon.ps1`, `f8-watch-start.ps1`,
  `f8-watch-stop.ps1`, `.claude/settings.json` hooks.** Out of scope (memory
  `f8-passive-listener-hooks`).
- **`Get-F8Heartbeat`'s other keys and its sort** (`f8-inbox-lib.ps1:160-170`). One key added,
  nothing else.

## 7. What NOT to touch

- Do **not** edit `logs/f8-inbox/HEARTBEAT.json`, `QUEUE.jsonl`, `ACK.json`, `PING.json` or
  `LATEST_CAPTURE.md`. They are live state.
- Do **not** ack a capture while testing.
- Do **not** edit the `tmp/play-deploy-*/` snapshot copies of these scripts (WO-1624 sec.1d - editing
  a snapshot creates a second source of truth, the duplicated-state failure CLAUDE.md sec.2 / sec.5 /
  sec.16 each describe).
- Do **not** edit any `.cs` file, run Unity, or fire a gate. This lane touches no Unity surface.
- Do **not** add a `passFails` counter to the device producer.
- Do **not** restart the daemon as part of this fix. The check script is read-only against the
  heartbeat; nothing here needs a live daemon to prove.

## 8. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and `WorkOrders/WORK_ORDER_1625_f8_check_inbox_prints_ok_while_the_heartbeat_carries_a_failure.RESULT.md`
is written, with both paths reported (CLAUDE.md sec.11 cadence). The lead regenerates `BOARD.html`.
