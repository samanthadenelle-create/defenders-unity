# WORK ORDER 1709 - RESULT (Defect A only: the daemon replay / stuck break-log offset)

**Lane:** implementation, PowerShell only. No Unity, no `.cs`, no commit (lead commits).
**Date:** 2026-09-14
**File changed:** `.claude/skills/run-defenders/f8-watch-daemon.ps1` (the only file touched)
**Defect B (Addressables INIT at `Fail`) is NOT in this RESULT** - see section 6.

---

## 1. Cause - PROVEN, and it is none of the four candidates the ticket listed

**The offset write was never failing.** Falsified at source this session:

| Candidate | Falsified by |
|---|---|
| the `try { } catch { }` at `:139` swallowed the write | `logs/f8-inbox/daemon-state.json` mtime `2026-09-14T09:06:33.2295029Z` matches `queue-events.log` `09:06:33.2227300Z` ("DOWN for 1733") and `HEARTBEAT.json` `09:06:33.2779387Z`. The write landed; it wrote the value it had just loaded. |
| read parses a different key than the write | both sides use `breakOffset`; the loaded value produced the correct `offset 1331 of 3064` arithmetic |
| a re-baseline branch resets `$breakBase` | `:126` logs "shrank", `:133` logs "first run" - **neither string appears in `queue-events.log`**; only the `:129` branch fired |
| the loop calls Save with a stale variable | `:231-232` is correct; it is simply **never reached** (below) |

**The real cause is the emit path's cost, and it makes the trailing save unreachable.**

`Save-BreakOffset` at `:232` sat **after** the `foreach` over the whole backlog slice - progress was
persisted only once every row had been emitted. Each `Emit-Capture` calls `Harvest-Context`, which
ran `Select-String -Path` over the **entire** `Editor.log` and `Player.log`. Measured on this machine
2026-09-14:

```
C:\Users\Elden\AppData\Local\Unity\Editor\Editor.log   bytes=5415468464  hits=60  ms=154657
C:\Users\Elden\...\Echoes of Elarion\Player.log        bytes=29354478    hits=60  ms=372
```

`Editor.log` has grown to **5.4 GB**, and one scan of it costs **154.7 s**. So:

- one capture costs ~155 s -> **this is the ticket's UNPROVEN "~150 s cadence"** (`:28-30`). Closed.
  The 07:42:31Z -> 08:01:24Z window (19 min, 7 captures) is ~160 s/capture; the 08:12-08:42 window
  (11 captures in 30 min) is ~165 s. Both match.
- a **1733-row backlog needed ~56 hours** of uninterrupted runtime before it could persist a single
  byte of progress. It never got them: the daemon was stopped mid-backlog every time, so
  `breakOffset` stayed at **1331** across 13+ restarts from `2026-09-12T02:01:56Z` to
  `2026-09-14T09:06:33Z`, and each restart re-emitted the same 2026-09-11 rows from row 1332.
- corroboration: `HEARTBEAT.json` desktop section is frozen at `detail: "armed"`, `passFails: 0`,
  `updatedUtc 09:06:33.2779387Z` - the loop never reached the bottom-of-pass `Beat`, and nothing
  threw. A **live, working** daemon was indistinguishable from a dead one.
- `5038 == 5031` byte-for-byte is exactly "a later restart re-emits the first non-skipped row from
  1331", because `$seenKeys` (`:148`) is per-process.

`Get-Process -Id 10308` -> **not running**; the daemon is confirmed stopped, as the lead stated. It
was not started or stopped by this lane.

## 2. The fix (one file, five changes)

1. **`Harvest-Context` reads the log TAIL, not the whole file** (`Read-LogTail`, 4 MB window, the
   same `FileShare::ReadWrite` FileStream pattern the Editor/Player scan already uses). Only the last
   60 signal lines were ever kept, so whole-file reads were always wasted work. 154.7 s -> tens of ms.
   Without this, incremental saves alone still leave the lead with an hours-long grind.
2. **Per-row persistence.** The `foreach` over the slice became an indexed
   `for ($i = $breakBase; $i -lt $cur; $i++)`, with `$breakBase = $i + 1; Save-BreakOffset ...` after
   **every** row (emitted, skipped or filtered). Progress now survives a kill - the durability the
   single trailing save never actually had.
3. **A published-`utc` watermark, mirroring the device bridge - not a second mechanism.**
   `daemon-state.json` gains `lastPublishedUtc`; a backlog row whose payload `utc` is at or below it
   is **skipped, but the offset still advances** (nothing is dropped, WO-965 intact). A row with no
   parseable `utc` is never skipped. First run after this fix has no watermark, so
   `Get-PublishedUtcWatermark` derives one **from the record** - `QUEUE.jsonl` (last 500 `source=f8`
   rows) -> each capture's `## Payload` line only, so an unrelated `utc` in the auto-harvest block
   cannot over-advance the floor. Archived (acked) captures are followed into `archive/`.
   The WO-1531 flag carve-out is untouched and unaffected: two presses carry two different `utc`
   values, so neither is ever below the floor when it arrives.
4. **The silent catch is gone.** `Save-BreakOffset` now **reads the file back** and verifies the
   offset landed - necessary because `$ErrorActionPreference = 'SilentlyContinue'` (`:12`) means a
   non-terminating write failure would never have reached the catch at all - and logs
   `Write-F8Event ... 'warn'` on any failure.
5. **Heartbeat during a backlog** (`replaying break-log row N/M`), so a grinding daemon no longer
   reads as frozen at `armed`.

**Test seams**, production defaults unchanged (`'' / 0`): `-InboxOverride`, `-BreakLogOverride`,
`-EditorLogOverride`, `-PlayerLogOverride`, `-MaxPasses`. They exist so the proof below drives **the
real script**, not a retyped copy of its functions.

**Caller checked, not assumed:** `f8-watch-start.ps1:31` builds
`-NoProfile -ExecutionPolicy Bypass -File "<daemon>" -PollSeconds $PollSeconds` - a **named** param
only, so the new optional params cannot mis-bind. `grep -rn "f8-watch-daemon.ps1" .claude tools docs`
shows every other hit is a **comment reference**; nothing dot-sources the daemon or passes positional
arguments.

**Not touched:** `$kindSkip`, the `:126`/`:133` branches, the WO-965 no-drop contract, the WO-1531
flag carve-out, anything under `logs/`. No capture was acked; the daemon was not started or stopped.

## 3. Proof (headless, scratch inbox + scratch break-log, the real script)

Harness: `scratchpad/wo1709-proof.ps1`, `wo1709-proof2.ps1` (console PING/ALERT noise trimmed).

```
break-log rows: 10
=== TEST 1: restart with persisted offset 3 of 10 -> must replay 7 and PERSIST the new offset ===
[f8-queue] WARN: daemon was DOWN for 7 break-log line(s) (offset 3 of 10) - replaying them now, none dropped
[f8-daemon] MaxPasses=1 reached - exiting (test seam). breakOffset=10 lastPublishedUtc=2026-09-11T10:10:00.0000000Z
  offset persisted = 10   (expected 10)
  captures queued  = 7   (expected 7)
=== TEST 2: second run, 4 new rows appended -> DOWN-for-4, offset advances to 14 ===
[f8-queue] WARN: daemon was DOWN for 4 break-log line(s) (offset 10 of 14) - replaying them now, none dropped
  offset persisted = 14   (expected 14)
  captures queued  = 11   (expected 11)
  offset CHANGED across two consecutive restarts: 3 -> 10 -> 14
=== TEST 3: replay of an ALREADY-PUBLISHED backlog (the 1331 defect) -> rows skipped, not re-emitted ===
[f8-queue] WARN: daemon was DOWN for 14 break-log line(s) (offset 0 of 14) - replaying them now, none dropped
  captures queued before = 11  after = 11   (expected UNCHANGED)
  offset persisted = 14   (expected 14 - the offset still advances, nothing dropped)
```

```
break-log rows: 3000
=== TEST 4: kill the daemon MID-replay of a 3000-row backlog -> progress must SURVIVE ===
  daemon KILLED mid-replay
  offset persisted after the kill = 115 of 3000
  lastPublishedUtc = 2026-09-11T10:01:55.0000000Z
  VERDICT: PASS - partial progress survived a kill (old code would have persisted 0)
  --- restart from that surviving offset: it must RESUME, not replay from 0 ---
  resumed from 115 -> offset now 180; queue 115 -> 180
  VERDICT: PASS - offset ADVANCED across two consecutive restarts (the 1331 symptom is gone)
    2026-09-14T09:32:17.2457515Z [warn] daemon was DOWN for 3000 break-log line(s) (offset 0 of 3000) - replaying them now, none dropped
    2026-09-14T09:32:26.3005393Z [warn] daemon was DOWN for 2885 break-log line(s) (offset 115 of 3000) - replaying them now, none dropped
=== TEST 5: Save-BreakOffset failure is LOUD (a DIRECTORY squats the state-file path) ===
  Save-BreakOffset FAILED warn events: 1   (old code: 0 - silent catch)
    2026-09-14T09:32:35.1698354Z [warn] Save-BreakOffset FAILED (#1) writing offset=3 to ...\daemon-state.json:
    Exception calling "WriteAllText" with "3" argument(s): "Access to the path '...' is denied."
    - the next restart will replay from the stale offset
```

Tests 4-5 are run by `wo1709-proof2.ps1` against its OWN fresh scratch inbox. `wo1709-proof.ps1`'s
own Test 4 attempt is superseded and not cited: Tests 1-3 run the daemon **in-process** via `& $Daemon`,
so `daemon.pid` in that scratch inbox held the harness's own live `powershell` pid and the
`Start-Process` daemon hit the "Already running" guard and exited 0 - nothing to do with the fix.

Test 4 also measures the new emit rate: **115 rows in 8 s (~14/s)** where the old path managed one
row per ~155 s. Test 5's count is 1, not 4, because a directory at the state path also defeats the
startup `ConvertFrom-Json`, so the daemon takes the `first run` baseline branch and emits nothing -
the one save it does attempt is the startup save, and it is loud. That is the correct shape.

Acceptance criterion 1 -> TEST 3 (queue unchanged across a full replay).
Acceptance criterion 2 -> TEST 2 and TEST 4 (a **new** offset in `queue-events.log` on two
consecutive restarts, `3 -> 10 -> 14` and `0 -> 115 -> 180`, never 1331 again).

Syntax gate (step 5):
```
powershell -NoProfile -Command "[scriptblock]::Create((Get-Content -Raw .claude/skills/run-defenders/f8-watch-daemon.ps1)) | Out-Null; 'PARSE_OK'"
PARSE_OK
```

## 4. ⚠ What the lead must expect on the next real restart - READ BEFORE RESTARTING

Predicted by running `Get-PublishedUtcWatermark` / `ConvertTo-F8Utc` **extracted from the shipped
file by AST** against the real inbox, read-only (`scratchpad/wo1709-predict.ps1`, nothing written):

```
derived watermark = 2026-09-11T16:55:31.3817228Z
backlog rows 1332..3064 that are NOT kindSkip : 1301
  would be SKIPPED (already published)        : 15
  would be EMITTED as genuinely new captures  : 1286
```

The 15 skipped are the 09-11 rows the stalled replay had already published (seq 5031-5052). The
**1286 are genuinely never-published rows** spanning 2026-09-11 16:55 -> 2026-09-14 00:33, so WO-965
canon (`:84`: *"The fix is dedupe, never skipping the backlog"*) requires emitting them.

**The ~90 s figure is EXTRAPOLATED from 115 rows and will degrade as the queue grows** - each emit
also calls `Get-F8Pending` over a lengthening backlog. Budget for **1286 `SystemSounds.Exclamation`
alerts** (`Alert-Owner`), **1286 `PING.json` / `LATEST_CAPTURE.md` rewrites** - which the
hook-enforced poller (CLAUDE.md section 14) treats as wakes - and 1286 `SUPERSEDES` warns. If that is
unacceptable, the PO decision is an **ack sweep**, not a re-baseline of `daemon-state.json`.

**This lane did not re-baseline them away - that is the PO's call, not an implementation choice.** If
the owner does not want that backlog walked, the sanctioned lever is an ack sweep, not a re-baseline
of `daemon-state.json`.

Also worth a separate ticket, proven here and out of this lane's scope: **`Editor.log` is 5.4 GB.**
The tail read makes the daemon immune to it, but every other tool that greps that file pays the
154 s.

## 5. Files

- Changed: `D:\eoa\.claude\skills\run-defenders\f8-watch-daemon.ps1`
- This RESULT: `D:\eoa\WorkOrders\WORK_ORDER_1709_f8_daemon_replays_old_break_log_and_addressables_init_logged_at_fail.RESULT.md`
- Harnesses (scratchpad, not committed): `wo1709-proof.ps1`, `wo1709-proof2.ps1`, `wo1709-predict.ps1`

## 6. What stays OPEN

- **Defect B - Addressables INIT logged at `Fail`** (`StructureContentWarmer.cs:1096`, `:909`,
  `EnemyContentWarmer.cs:674`). Untouched by this lane: it is a `.cs` change and belongs to a Unity
  lane. Acceptance criteria **3 and 4 are unmet** - including the gating question (does `FlowTrace.Warn`
  survive to the device log at all?) and the WO-1089 resident-structures confirm that must be
  recorded **before** the severity changes.
- Not committed. The lead gates and commits; the lead restarts the daemon.
