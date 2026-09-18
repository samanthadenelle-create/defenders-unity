---
name: run-defenders
description: Build, run, and drive the Defenders of the Realm / Echoes of Elarion Unity game. Use when asked to run, launch, build, smoke-test, headless-test, autopilot, fleet-test, screenshot, or verify the game / the Unity project / "defenders" / "eoa". Covers the batchmode compile+data gates, the Windows player build, and the headless AutoPilot fleet that actually drives the running game.
---

# Run Defenders of the Realm (Unity 6 game)

Unity 6 (URP) tower-defense + dungeon-crawler. It is **developed and driven HEADLESS**:
you don't open a window — you build the Windows player and drive it with the **AutoPilot
fleet** (`run-autopilot-fleet.ps1`), then **observe via captured JSON**, not pixels
(the fleet runs `-nographics`). The committed driver is the trio of repo-root scripts
(`build-windows.ps1`, `run-autopilot-fleet.ps1`, `run-unity-method.ps1`) plus the
**harvest** helper in this skill dir. All paths below are relative to the repo root — **that root
is machine-dependent** (`C:\eoa` on one box, `D:\eoa` on another), so never hardcode a drive letter.

**Golden rule: the Unity editor must be CLOSED for any batchmode command** (build/gate/fleet) —
it holds a project lock. Unity *Hub* running is fine. The fleet `.exe` needs NO Unity license.

## Prerequisites
- Unity **6000.4.8f1** installed via Unity Hub (`C:\Program Files\Unity\Hub\Editor\6000.4.8f1\`).
- Windows + PowerShell (batchmode) and Git Bash + `python3` (for `harvest.sh`).
- Gitignored art packs (`Assets/polyperfect`, `Assets/Models/KayKit`, …) are absent on a fresh
  clone — the game still builds/runs (committed `Resources/` art survives); world looks "black".

## Run — AGENT PATH (the drive loop)

The canonical loop is **gate → build → drive → observe**. Every command here was run this
session and produced the marker shown.

**1. Compile gate** (authoritative "does it compile" — brace + leak + NUL scan):
```bash
powershell -ExecutionPolicy Bypass -File ./run-unity-method.ps1 -Method DeNelle.Editor.CompileGate.Run -LogName compile-gate.log
# -> prints  COMPILE_GATE_OK :: scripts compiled clean
```

**2. Data/logic gate** (headless "real object in -> assert -> one marker"; catalogs, save, equip):
```bash
powershell -ExecutionPolicy Bypass -File ./run-unity-method.ps1 -Method DeNelle.Editor.DataRegression.RunAll -LogName data-regression.log
# -> prints  REGRESSION_OK <n>/<n> suites   (or REGRESSION_FAIL: <n> failure(s) ...)
```

The marker carries its suite COUNT on the same line on purpose. Grep the SHAPE
(`REGRESSION_OK \d+/\d+ suites`), never the bare token — until 2026-08-02 three
different classes emitted a bare `REGRESSION_OK` and the check-in gate was judging
the 22-case legacy battery while every RESULT file read it as this ~90-suite one.
Sibling markers, all disjoint now:
| Entry point | Marker |
|---|---|
| `DeNelle.Editor.DataRegression.RunAll` (**THE** gate) | `REGRESSION_OK <n>/<n> suites` |
| `DeNelle.Editor.RegressionSuite.RunAll` (22-case battery) | `CHECKIN_SUITE_OK <p>/<n> cases` |
| `DeNelle.Editor.SessionRegression.RunAll` | `SESSION_GUARDS_OK 6/6 checks` |
`DeNelle.Editor.Regression.RegressionMarkerRegression` ([regression-marker], registered
in the data gate) keeps those disjoint and fails if a new oracle is written but never
registered, or a gate script greps a marker nobody emits.

**3. Build the Windows player** (ALWAYS wipe `Builds/Windows` first — stale exe-stub = level3 crash):
```bash
powershell -ExecutionPolicy Bypass -Command "Remove-Item -Recurse -Force 'Builds\Windows' -ErrorAction SilentlyContinue; .\build-windows.ps1"
# -> prints  [build] SUCCESS -> <repoRoot>\Builds\Windows\DefendersOfTheRealm.exe
```

**4. Drive it — launch the headless AutoPilot fleet** (N player instances, distinct seeds,
each drives boot -> vendors -> economy -> equip -> HUD -> wave -> scene-cross and asserts
oracles; writes per-run break-logs + a ranked ticket file). Run in the background; it takes
~`TimeoutMin` minutes:
```bash
powershell -ExecutionPolicy Bypass -File ./run-autopilot-fleet.ps1 -Count 12 -SeedStart 1000 -TimeoutMin 15
# launches 12 instances; on exit writes Builds/autopilot-tickets.md + .json
```

**5. Observe — harvest the run** (the OBSERVE step; this is your "screenshot"):
```bash
bash .claude/skills/run-defenders/harvest.sh
# prints: per-run talk-route verdict, high-signal counts (talk violations / dialogue
# No-node / softlocks), NEW real errors (render artifacts + guard-handled magenta filtered),
# and the ranked ticket file path.
```
Raw artifacts live under `%LOCALAPPDATA%Low\DeNelle\Defenders of the Realm\autopilot-runs\<n>\`:
`break-log.jsonl` (error-level lines + F8 flags), `autopilot-summary.json` (per-phase pass/fail
+ details), `break_*.png` (screenshots — **blank under -nographics**). Ranked, deduped tickets:
`Builds/autopilot-tickets.md`.

## Standing lanes — a named fleet run judged by its own marker

A **lane** is a coverage question that must be answered every night, reduced to one word.
`-Lane <name>` sets the phase filter and the defaults that question needs, and then judges the
run by a marker the **bot** prints — not by "the fleet finished".

```bash
powershell -ExecutionPolicy Bypass -File ./run-autopilot-fleet.ps1 -Lane freshsave-ftue
# -> [fleet] FLEET_LANE_OK 1/1 instance(s) printed 'FRESH_SAVE_FTUE_OK'
```

| Lane | Question it answers | Phase | Marker | Refusal |
|---|---|---|---|---|
| `freshsave-ftue` | found a NEW town, walk the guide beats, prove the first welcome-back claims nothing | `AssertFreshSaveFtue` | `FRESH_SAVE_FTUE_OK` | `FLEET_LANE_FAIL`, exit 5 |

**Why the marker and not the run (WO-1500, 2026-09-07).** All FIVE fleet logs captured on
2026-09-06 had ZERO `[Flow:Onboard*]` lines — every run booted a RETURNING save, so every
fresh-save assertion in the driver went N/A and the fleet reported green while asserting nothing
about the first ten minutes. `AutoPilot complete` + `aborted:false` prove the bot ran; only the
phase's own marker proves the question got answered. **Marker absence on a fresh log is a
FAILURE, not an unknown.** The lane defaults to `-Count 1 -Graphics` on purpose: it founds a New
Game and reads process-scoped state (`TutorialFlow.RanThisSession`), so extra instances add no
coverage, and a `-nographics` run would write flat-black FTUE frames. Anything you pass
explicitly still overrides the preset.

Wiring is pinned by `DeNelle.Editor.Regression.FreshSaveFtueLaneRegression`
(`FRESH_SAVE_FTUE_LANE_OK`, registered in the data gate): the phase is in the sequence, it still
founds its own town, it still asserts the fresh-clock/zero-window/no-popup trio, and this script
still refuses without the marker.

## Run — HUMAN PATH (real visuals)
The fleet is `-nographics` (no pixels). For actual visuals, launch the built player directly:
```bash
powershell -ExecutionPolicy Bypass -Command "& 'Builds\Windows\DefendersOfTheRealm.exe'"
# a window opens (Title -> HeroSelect -> PetSelect -> MainCastle_Hall). F8 = capture+flag. Close to quit.
```
Useless on a headless box; this is the only path that renders. Boot a single scene with
`& 'Builds\Windows\DefendersOfTheRealm.exe' -bootScene MainCastle_Hall`.

## Gotchas (battle scars — verified this session)
- **`-nographics` = NO pixels.** Fleet `break_*.png` are blank; observe behaviour via
  `break-log.jsonl` + `autopilot-summary.json`, never screenshots. Render bugs (magenta) and
  UITK panels (dialogue) **cannot** be reproduced headless — they need the human path / F8.
- **break-log captures ERROR-LEVEL ONLY.** `FlowTrace.Step`/`Warn` (Debug.Log/LogWarning) do
  **not** land in `break-log.jsonl` — only `FlowTrace.Fail`/exceptions/softlocks/F8 flags. To
  assert a non-error signal headless, make the oracle emit `FlowTrace.Fail` on violation (that's
  how `AssertVendorTalkRoute` works). **Step lines now land PER INSTANCE** (WO-1102, 2026-08-16):
  the fleet passes `-logFile` so each instance writes `autopilot-runs/<i>/player.log` next to its
  break-log — read THAT for Step-level trace; the fleet prints `FLEET_PLAYERLOG_MISSING run=<i>`
  if one is missing/empty. *(History: before WO-1102 no `-logFile` was passed, every instance
  contended on the ONE root `Player.log`, and Step evidence was destroyed → the old gotcha
  "Player.log is overwritten per fleet instance → unreliable for fleets".)*
- **License "505 / LICENSE ERROR" line is transient.** Judge success by the marker
  (`COMPILE_GATE_OK` / `REGRESSION_OK <n>/<n> suites` / `[build] SUCCESS`), not the wrapper exit line. Re-run if
  a batchmode call reports a license error at *shutdown* but produced its marker. Do NOT kill processes.
- **Editor lock.** If `tasklist | grep Unity.exe` shows a process, a build/gate is running or the
  editor is open — defer; don't collide. (Unity *Hub* is fine.)
- **Fleet wipes stale run logs at launch** (clean aggregation slate) — harvest BEFORE relaunching,
  or you lose the prior run's break-logs.
- **Coverage is HUB-capped.** The fleet exercises MainCastle_Hall + a warp to Village2; the open-world
  outpost/combat/walk loop is blocked (WO-453) → "no outpost realized — skipped" every run is EXPECTED.
- **Video/shader-pass errors in the log are -nographics artifacts** (`VideoDecode`, "custom render path
  shader needs ≥1 passes") — the emitter filters them from tickets; `harvest.sh` excludes them. Not bugs.

## Troubleshooting
| Symptom | Fix |
|---|---|
| `compileErrors=True` + `CS####` in log tail | real compile error — read the tail, fix the named file, re-run the gate |
| Batchmode reports a license error but no marker | transient; re-run the same command (it succeeded for us on retry). Don't kill procs. |
| `Player exe not found` from the fleet | run step 3 (build) first; confirm `Builds/Windows/DefendersOfTheRealm.exe` exists |
| Player build crashes with `level3 corrupted` | you skipped the `Remove-Item Builds\Windows` wipe — incremental builds keep a stale exe stub |
| `harvest.sh` finds no runs | the fleet hasn't completed (or wiped on relaunch); check `autopilot-runs/` for `*/break-log.jsonl` |

## F8 Live-Triage — persistent daemon (no manual re-arm)

While the owner felt-tests, every F8 flag / error / softlock must land on the CLI without the owner
saying "rearm" or "watch". Use the **inbox daemon** (not the one-shot `f8-watch.sh`).

**1. Start once** (idempotent; survives the whole play session):
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .claude\skills\run-defenders\f8-watch-start.ps1
```

**2. Agent session** — `.cursor/rules/f8-auto-triage.mdc` (alwaysApply) requires:
- Background `f8-watch-poll.ps1` with notify on `F8 INBOX PING` (mid-session wake)
- Every turn: `f8-check-inbox.ps1` first; if `NEW_CAPTURE`, read the file on the `capture=` line (the OLDEST pending capture) before any code-read
- After triage: `f8-ack.ps1` (acks ONE) — repeat until `NO_CAPTURE`, then re-launch poll

**3. Stop daemon** (end of day): `f8-watch-stop.ps1`

| Script | Role |
|--------|------|
| `f8-inbox-lib.ps1` | Shared queue lib (WO-965): `Publish-F8Capture` / `Get-F8Pending` / ack state |
| `f8-watch-daemon.ps1` | Persistent watcher → `QUEUE.jsonl` + `PING.json` + per-seq capture files |
| `f8-watch-poll.ps1` | Agent background poller; exits on un-acked capture |
| `f8-check-inbox.ps1` | Sync poll (`NEW_CAPTURE` + `pending=N`, oldest first / exit 1) |
| `f8-ack.ps1` | Ack ONE capture after triage (`-Seq n`, `-All`) |
| `f8-device-bridge.ps1` | **WO-1227** DEVICE producer: pulls the phone's `break-log.jsonl` + `break_*.png`/`flag_*.png` over adb and publishes new captures into the SAME queue |
| `f8-device-bridge-start.ps1` / `-stop.ps1` | Background 30 s loop for the above (auto-started by `f8-watch-start.ps1`) |
| `f8-device-backfill-digest.ps1` | One-shot digest of a device log's history -> `logs/f8-inbox/DEVICE_BACKFILL_<date>.md` |

**WO-1227 - the device half.** `f8-watch-daemon.ps1` watches ONLY the desktop persistentDataPath.
Nothing moved a capture off the phone, so on the one platform the owner actually plays the section 14
chain was severed at the first link: on 2026-08-26 the inbox read `NO_CAPTURE ack=3607` all day while
the Seeker held 736 unread entries back to 2026-07-20 - including 8 of the owner's own FLAG presses
and 9 `BATTLE_QUIESCENCE_FAIL` softlocks. `f8-device-bridge.ps1` closes it as an ADDITIVE second
producer into the same `QUEUE.jsonl` (no second inbox, no second ack state). It keeps a DEVICE-SIDE
read watermark in `logs/f8-inbox/device-state.json` (line offset + `lastUtc` + a rolling dedupe set)
so a poll is incremental and idempotent, filters to `flagged`/`error`/`exception`/`possible_softlock`
exactly as the desktop daemon does, and is a **silent exit-0 no-op with no phone attached**.
`adb` is resolved (never assumed on PATH) and always invoked from PowerShell - Git Bash rewrites
`/sdcard/...` into `C:/Program Files/Git/sdcard/...` and the pull fails.

**WO-965:** captures are an append-only queue (`logs/f8-inbox/QUEUE.jsonl`). `LATEST_CAPTURE.md` and
`PING.json` show only the NEWEST — before the queue existed, a burst collapsed into them and one ack
buried the rest (2026-08-10: the owner's seq 2307 + 2308 never reached a seat). Drain `pending=` to 0.

Legacy: `f8-watch.sh` (bash, exits on first fire, needs manual re-arm).

## Device felt-test from the PC (scrcpy)

`scrcpy` mirrors the Seeker's screen onto this PC so an agent can WATCH a felt-test and record it.
Installed 2026-09-16 via `winget install --id Genymobile.scrcpy -e --accept-source-agreements
--accept-package-agreements` -> `scrcpy 4.1`, at
`%LOCALAPPDATA%\Microsoft\WinGet\Packages\Genymobile.scrcpy_*\scrcpy-win64-v4.1\`. It needs an `adb`
on PATH; this repo's is the Unity one — put it on PATH for the session, never assume it:
`$env:PATH = "C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools;$env:PATH"`.

### ⛔ SAFETY (verbatim from memory `device-lanes-overlay-apps-and-start-new`) — the device holds the OWNER'S live save
- Before any scripted taps on the owner's device:
  `adb shell dumpsys window windows | grep -i "SYSTEM_ALERT\|TYPE_APPLICATION_OVERLAY"`;
  if an overlay exists, STOP and report — do not disable apps on her device.
- Never tap the title row by coordinates while a save exists; CONTINUE only after a screencap proves
  the row layout, and treat START NEW's position as a hazard.
- Device lanes report every deviation first and leave the device untouched after an incident.

**A scrcpy window forwards every mouse click and keypress to the phone by default.** For an
observation lane that is exactly the hazard above — so **always pass `--no-control`**. Only a lane
with explicit owner approval to drive may drop it.

### Mirror (read-only)
```powershell
scrcpy -s SM02G4061955851 --no-control --window-title "EoA felt-test (read-only)" --time-limit 3
# run 2026-09-16 -> "[server] INFO: Device: [Solana Mobile Inc.] solanamobile Seeker (Android 16)",
#                   "Time limit reached", process gone afterwards. Drop --time-limit for a live watch.
```
`--stay-awake` keeps the screen on while mirroring (it flips a device setting, restored on exit) —
listed as an option; NOT exercised in the install lane, so treat it as unproven here.
Never leave a scrcpy window running after the lane ends (`Get-Process scrcpy` to confirm it is gone).

### Record a clip
```powershell
scrcpy -s SM02G4061955851 --no-control --no-playback --no-audio --video-codec=h264 `
       --record logs\device\felt-<stamp>.mp4 --time-limit 6
```
⚠ **`--video-codec=h264` is load-bearing on this Seeker.** The DEFAULT codec produced
`WARN: Recording stopped before headers were processed` / `ERROR: Recording failed` and a **0-byte**
file on three attempts (3 s, 8 s, and 5 s with `repeat-previous-frame-after`); the same command with
`--video-codec=h264` wrote **155193 bytes**, header `ftyp isom`. A static screen also starves the
encoder — record while something moves.

### Screenshot
```bash
# Bash tool, NOT PowerShell:
adb -s SM02G4061955851 exec-out screencap -p > logs/device/shot-<stamp>.png
```
⚠ PowerShell's `>` **corrupts** this — it wrote a UTF-8 BOM + replacement bytes (`ef bb bf ef bf bd
50 4e`) over the PNG magic and the file would not open (memory
`powershell-set-content-mangles-git-show-files`). Use the Bash redirect, or
`adb shell screencap -p /sdcard/x.png` + `adb pull` + `adb shell rm`. Verified 2026-09-16:
`logs/device/scrcpy-proof-20260916-143307.png`, 2 829 677 bytes, `PNG image data, 1200 x 2670`.

### Logcat / F8 alongside
The device's own break-log + flag screenshots already flow into the §14 inbox — start
`.claude\skills\run-defenders\f8-device-bridge-start.ps1` (auto-started by `f8-watch-start.ps1`) and
drain with `f8-check-inbox.ps1` / `f8-ack.ps1`; `f8-device-backfill-digest.ps1` for history. For raw
lines beside the mirror: `adb -s <serial> logcat -v time Unity:V '*:S'` (check `adb logcat -g` first —
the ring size is per device and the Flow firehose can evict the boot window).

### Scripted device scenarios (WO-1775)

The mirror/record recipe above only WATCHES a run — it cannot SET ONE UP (a Lv 52 hero at
wave 176, an Iron Bastion raid at Lv 4 with 10 troops, etc.). `tools\device-scenario.ps1` adds
the setup half: a scenario string, sent as Android intent EXTRAS to a `QA_SCENARIO_BUILD` APK
(`overnight-apk-build.ps1 -Scenario`, which implies `-Tester`'s `TESTER_BUILD` define and stamps
`QA_SCENARIO_BUILD` alongside it — never a store/Firebase-tester artifact; the file it produces
is renamed with a `-scenario` suffix so it can never be mistaken for the tester upload). The
intent is read by `Assets/_Modules/DevTools/DevScenarioIntent.cs` and dispatched in two phases —
PRE-HUB (`newgame`, `onboarded`, `wave`, `resources`, `buildings`, `troops`, `ff.*`, `tun.*`,
applied at Title before the first wave loop begins) and POST-HUB (`level`, `raid`, `town`,
applied once the hub scene + hero exist). `camera=` is NOT implemented (WO-1775 §8 Q2 open).

```powershell
# Observe only (the DEFAULT) — capture, no intent, no force-stop:
tools\device-scenario.ps1 -Serial <serial> -Seconds 20

# Scripted scenario on the emulator or a Seeker guest user — NEVER the owner's own Seeker
# session without -ConfirmSeekerScenario (device policy below):
tools\device-scenario.ps1 -Serial <serial> -Target emulator -Seconds 30 `
    -Scenario "newgame=knight;onboarded=1;level=52;wave=176;resources=max"
```

Ordering inside the wrapper is load-bearing and non-negotiable: overlay check FIRST (never
scripts taps if one is present), `logcat -g` (ring size) THEN `-d` (drain) and ONLY THEN `-c`
(clear) — clearing before the drain destroys evidence. The launcher activity is resolved from
the DEVICE every run (`adb shell cmd package resolve-activity --brief <pkg>`), never hardcoded.
Recording uses `--video-codec=h264 --no-control` (the default codec wrote 0-byte files on this
Seeker three times). Contact sheets (`ffmpeg -vf "fps=1,scale=320:-1,tile=6x5"`) are the read
path for frames — never read the raw video frame-by-frame; grep `run.log` for the scenario's
judging `[Flow:*]` token, never read the whole file. Success prints `DEVICE_SCENARIO_OK <dir>` —
judge that marker on a fresh console read, never the exit code.

**Device policy (three tiers, WO-1775 §3):**
1. **The owner's Seeker — OBSERVE ONLY.** A scripted intent state change needs
   `-ConfirmSeekerScenario` AND her explicit OK for that specific run; the wrapper refuses
   `-Target seeker -Scenario ...` without it, and never sends `am force-stop` to her device
   either way.
2. **Emulator AVD — the scripted target.** ⚠ UNPROVEN whether the current `Pixel_10_Pro_XL`
   x86_64 image can run this ARM64 IL2CPP APK — check with `adb install` and read
   `INSTALL_FAILED_NO_MATCHING_ABIS` before assuming it works.
3. **Seeker guest/second user — the fallback.** Every step (`pm create-user`, `switch-user`,
   whether wallet/Seed Vault binding is per-user) is UNPROVEN on this Seeker — read-only checks
   first, and never `create-user`/`switch-user` without an explicit owner OK.

Scenario keys read at source, WO-1775 §1.6 — `newgame=<knight|ranger|mage|cleric>` (only on a
save-free target; refused with a `FlowTrace.Fail` on an existing save), `onboarded=1` (required
or the wave loop never starts — the FTUE gate is `!Onboarded`, checked every tick), `level=N`
(via the real `HeroProgression.AddXp` loop), `wave=N` (via `GameStateService.RecordRun(N-1)` —
must land BEFORE the first `BeginLoop` or it silently no-ops), `troops=N`, `buildings=max`,
`resources=max|wood:N,food:N,iron:N,crystals:N`, `raid=<sceneId>`, `town=granted`, and arbitrary
`ff.<key>=N` / `tun.<key>=N`. Full provenance for each: `WORK_ORDER_1775_device_scenario_harness_scrcpy_devkit.md`.

## Reference
Full operating SOP + the latest run ledger: `OVERNIGHT_AUTOPILOT_LOG.md`. Build/gate/bake cycle
table: `docs/HANDOVER.md` §4. Instrumentation method (`FlowTrace`/`Guard`/break-log):
`docs/INSTRUMENTATION_STANDARD.md`.
