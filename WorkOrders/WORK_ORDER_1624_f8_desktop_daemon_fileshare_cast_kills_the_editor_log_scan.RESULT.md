# WO-1624 RESULT - F8 desktop daemon: type-qualified enum name in a string killed every pass

**Status:** IMPLEMENTED - awaiting lead review (lane F8-DAEMON 2026-09-10)
**Lane:** Tooling / F8 watcher. No `.cs` touched, no Unity run, no gate run, NOT committed (lead is sole committer).
**Host:** Windows PowerShell **5.1.26100.9444** (`PSEdition = Desktop`) - the daemon's own host.

---

## 1. Files changed (one file, plus this WO)

- `.claude/skills/run-defenders/f8-watch-daemon.ps1` - the fix + the WO sec.4.5 observability counters
- `WorkOrders/WORK_ORDER_1624_f8_desktop_daemon_fileshare_cast_kills_the_editor_log_scan.md` - Status line
- `WorkOrders/WORK_ORDER_1624_...RESULT.md` - this file

**NOT touched, per sec.1d / sec.7:** `./tmp/play-deploy-37837f585/...` and `./tmp/play-deploy-dcd25e9fe/...`
(the two stale snapshot copies), `f8-check-inbox.ps1`, `f8-ack.ps1`, `f8-poll-rewake.ps1`,
`.claude/settings.json`, and the RUNNER lane's `run-unity-method.ps1` / `tools/regression/checkin_gate.ps1`.

`git diff --stat` for this lane's file: `.claude/skills/run-defenders/f8-watch-daemon.ps1 | 32 ++++++++++++++++++++--`.
Edits were made with the Edit tool, not `sed -i`; `file` still reports **`ASCII text, with CRLF line
terminators`**, so no line-ending or BOM churn. *(Note for the lead: 58 unrelated `WorkOrders/*.md`
files and `Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset` are also dirty in this
shared tree - another lane's work, not this one. Stage by explicit path per CLAUDE.md sec.11.)*

## 2. The fix (`f8-watch-daemon.ps1`, was `:224`)

```diff
-        $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', 'FileShare.ReadWrite')
+        $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', [System.IO.FileShare]::ReadWrite)
```

**Typed form chosen** (WO sec.3's stated preference): it cannot be mis-shortened back into a string,
and it is the shape the PowerShell error's own remedy points at. `'Open'` and `'Read'` were left as
bare strings - they coerce correctly to `FileMode.Open` / `FileAccess.Read` and the WO does not ask
for them. A seven-line comment above the call records the defect, its birth commit (`22ae4de5b`,
2026-07-09) and why `ReadWrite` share is required, so the next reader cannot "tidy" it back.

### Observability added (WO sec.4.5, explicitly allowed for this lane)

`$logPositions` is an in-memory hashtable, so "the Editor/Player scan ran" was unobservable from
outside the process - which is exactly how a scan that had **never** run still read healthy. Added:

- `$logReads` / `$logBytes`, initialised beside `$passFails = 0`, incremented after `$fs.Close()`
  (`$len - $pos` **bytes**, not `$chunk.Length` chars)
- `function Watch-Detail` building `watching editor=<pos>/<len> player=<pos>/<len> reads=N bytes=N`
  - the same shape the `device` producer already uses (`offset=1215/1215`)
- `Beat 'watching'` -> `Beat (Watch-Detail)` at the heartbeat cadence line only

**Pins honoured (sec.5):** the `catch` block and its comment are byte-identical; `passFails` and the
Beat contract are unchanged (liveness only, no capture state in the heartbeat); `Beat 'armed'` and
the catch's `Beat` are untouched; the break-log offset block and `Save-BreakOffset` are untouched;
the anchored `"kind"` regex is untouched; `Emit-Capture` queue semantics are untouched; the
classifier regexes at the (now-executing) scan are **unchanged** - not widened, not narrowed.

`Beat (Watch-Detail)` sits at exactly the position `Beat 'watching'` already occupied - the heartbeat
cadence line, **outside** the pass `try/catch` - so it is not a new failure surface in kind; and
everything `Watch-Detail` calls (`Test-Path`, `Get-Item` under the script's `$ErrorActionPreference =
'SilentlyContinue'`, and `-f` formatting) is non-terminating.

Parse check: `[System.Management.Automation.Language.Parser]::ParseFile(...)` -> **`PARSE OK: 0 errors
(1631 tokens)`**.

**Post-fix line numbers, re-read at source 2026-09-10** (the block moved: the WO's `:218` / `:222` /
`:224` are PRE-fix): `:237` `if (-not (Test-Path $logPath)) { continue }`, `:241` `if ($len -le $pos)
{ continue }`, `:250` the fixed `Open` call, `:293` `Beat (Watch-Detail)`.

## 3. RED-first proof (WO sec.6) - verbatim, PowerShell 5.1

```
PSVersion = 5.1.26100.9444
PSEdition = Desktop
EditorLog = C:\Users\Elden\AppData\Local\Unity\Editor\Editor.log
Exists    = True
Length    = 14541019

=== RED: the shipped line (f8-watch-daemon.ps1:224) ===
RED THREW: Cannot convert argument "share", with value: "FileShare.ReadWrite", for "Open" to type "System.IO.FileShare": "Cannot convert value "FileShare.ReadWrite" to type "System.IO.FileShare". Error: "Unable to match the identifier name FileShare.ReadWrite to a valid enumerator name. Specify one of the following enumerator names and try again:
None, Read, Write, ReadWrite, Delete, Inheritable""

=== GREEN: the typed enum ===
GREEN OK: opened, seeked to 14536923/14541019, read 4096 chars, 47 lines
```

RED reproduces the captured `HEARTBEAT.json` message **character for character**. Unity was running
(`Get-Process Unity` -> pid 17436) while GREEN opened `Editor.log`, so `FileShare.ReadWrite` was
genuinely exercised against a file Unity holds open.

Script: `<scratchpad>/wo1624-redgreen.ps1` (temp; not added to the repo - there is no PowerShell test
harness here, sec.6).

## 4. Success-path proof - the scan block actually READS AND CLASSIFIES

Per `prove-the-success-path-not-just-the-refusal`: not throwing is not the same as working, and
`:218`'s `if (-not (Test-Path $logPath)) { continue }` would hide a silenced daemon. So the **entire
post-fix scan block was copied verbatim** into a scratch harness with `$logPositions` forced back
64 KiB (guaranteeing the read branch executes) and `Emit-Capture` replaced by a counter:

```
PSVersion = 5.1.26100.9444
  CLASSIFIED[error]: Lifecycle ERROR : Failed to exit code reload scopes (post serialization) and shut down the Lifecycle Management core due to exception System.NullReferenceExcept
  CLASSIFIED[error]: NullReferenceException: Object reference not set to an instance of an object
OK Editor.log: opened+read 65536 bytes from offset 14475483/14541019; classifier ran over 659 non-blank lines; 2 would-be capture(s)
OK Player.log: opened+read 65536 bytes from offset 431351/496887; classifier ran over 311 non-blank lines; 0 would-be capture(s)
TOTAL reads=2 bytes=131072 wouldEmit=2
```

Both real logs opened with `FileShare.ReadWrite`, 131072 bytes read, and the classifier at the
`$isFlagged/$isError/$isSoftlock` lines **executed for the first time**, classifying two real
`Editor.log` errors. Script: `<scratchpad>/wo1624-block-proof.ps1`.

That preview is also the sec.7 warning made concrete: once the scan runs live it will surface a
backlog of Editor/Player log signal. **That is data - triage it per CLAUDE.md sec.13/sec.14; do not
suppress it by tightening the pattern that finally started working.**

## 5. Restart + acceptance (WO sec.4)

**The lead's condition ("restart only if the WO says to") is met - WO sec.4 steps 1-2 mandate it and
sec.6 calls it "the real acceptance". So: NO restart is owed. It was performed.**

Baseline immediately before (verbatim from `logs/f8-inbox/HEARTBEAT.json`):

```
"desktop": { "pid": 5620, "passFails": 9360, "breakOffset": 1331,
             "detail": "pass-failed: Cannot convert argument \"share\", with value: \"FileShare.ReadWrite\" ..." }
"device":  { "pid": 14776, "detail": "published=0 dupSuppressed=0 offset=1215/1215" }
```

```
=== STOP ===
[f8-stop] Stopped pid=5620
=== START ===
[f8-device-bridge-start] Already running (pid=14776). Inbox: D:\EoA\logs\f8-inbox
[f8-start] Daemon started. pid=47444
```

Four consecutive distinct `desktop` heartbeats:

```
[2026-09-10T05:52:17.8116597Z] BEAT #1 pid=47444 passFails=0 breakOffset=1331 detail=armed
[2026-09-10T05:52:47.9441154Z] BEAT #2 pid=47444 passFails=0 breakOffset=1331 detail=watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0
[2026-09-10T05:53:18.1012127Z] BEAT #3 pid=47444 passFails=0 breakOffset=1331 detail=watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0
[2026-09-10T05:53:48.3594435Z] BEAT #4 pid=47444 passFails=0 breakOffset=1331 detail=watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0
```

- `detail` reads `watching` and contains **no `pass-failed`** - PASS
- `passFails` is **0** and stayed 0 across four beats (the old process was accruing ~1 every 5 s) - PASS
- `pid` is **47444**, differs from 5620 - PASS
- `device` producer **pid 14776, detail unchanged** on every beat - untouched, as pinned (sec.5)

`f8-check-inbox.ps1`, verbatim (read-only; **nothing acked**, per sec.7):

```
F8_DAEMON_OK producer=desktop pid=47444 age=16s lastDeviceUtc= detail=watching editor=14541019/14541019 player=496887/496887 reads=0 bytes=0
F8_DAEMON_OK producer=device pid=14776 age=10s lastDeviceUtc=2026-09-10T05:25:26.3727270Z detail=published=0 dupSuppressed=0 offset=1215/1215
NO_CAPTURE ack=4983 ping=4983
```

No `pass-failed` text. `NO_CAPTURE`, so there was nothing to ack.

### What is proven, and what is NOT (CLAUDE.md sec.11B)

`reads=0` in the live heartbeat is **expected and is not a failure**: `$logPositions` baselines to
current length at daemon start, and `:241`'s `if ($len -le $pos) { continue }` means `:250` executes
only when a log **grows** (post-fix line numbers, sec.2). Neither log grew during the observation window - `Editor.log` mtime
2026-09-03 12:06, `Player.log` mtime 2026-09-09 18:20.

- **Proven:** the daemon no longer throws; the loop reaches the log branch on every pass; both files
  are present and their positions/lengths are read (`editor=14541019/14541019`,
  `player=496887/496887`) - so this is *not* the `:237` "silenced because the file is missing" case.
- **Proven under the same PS 5.1 host, against the same real logs:** the full
  Open/Seek/StreamReader/ReadToEnd/classify sequence, sec.4 above.
- **NOT proven this session:** the read executing *inside the running daemon*, because no log grew
  and I did not launch Unity or write to a Unity log (that would be fabricating evidence). The first
  Play session with this daemon up is the live confirmation, and the new `reads=`/`bytes=` counters
  are what make it a one-glance check. **Hand-off to the lead: on the next Editor/Play session,
  `f8-check-inbox.ps1` should show `reads=` > 0 for the desktop producer.**

## 6. Second-order finding - handed back, NOT taken in this lane (sec.7)

`f8-check-inbox.ps1` printed **`F8_DAEMON_OK producer=desktop ...`** for ~12.7 hours while carrying
`pass-failed: ...` inside its `detail` field. A verdict token that reads `_OK` while a failure rides
along in a detail string is precisely `gates-report-success-without-proving-it`. **Candidate
follow-up: make the `F8_DAEMON_OK` verdict conditional on `passFails == 0` and a `detail` free of
`pass-failed` (`F8_DAEMON_DEGRADED` otherwise).** Deliberately not done here - out of scope per WO
sec.7, and it is `f8-check-inbox.ps1`, a pinned file.

## 7. Open question

None blocking. One for the lead: whether to mint the sec.6 `F8_DAEMON_DEGRADED` verdict ticket now,
given the daemon self-reported healthy through 9360 failures.
