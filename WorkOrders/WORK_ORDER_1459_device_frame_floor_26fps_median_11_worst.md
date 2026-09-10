# WO-1459: device frame floor - 26 fps median, 11 fps worst in a raid, with timeScale at 1.00

**Status:** BLOCKED - awaiting the split capture on the Seeker (needs the next APK); INSTRUMENTED frame split (lane FRAME-SPLIT 2026-09-10)
**Silo:** Perf. Read-only profiling lane; touches nothing until the measurement lands.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1459 -> 1460 in the same edit).

## 1. EVIDENCE

237 `LOW fps` samples in the 2026-09-06 device session. Worst:

```
LOW fps=11 ms=87.4 mem=427MB gc=26MB scene=RaidBase_raider_camp_small towers=0 enemies=13
```

`timeScale=1.00` on 1,189 lines - so this is a real frame cost, not the frozen-clock class (WO-988) and not
the `timeScale=0.28` trap that produced a wrong theory on 2026-09-03.

Thirteen enemies and zero towers at 87 ms a frame is not an enemy-count problem.

## 2. FIX SHAPE

- Profile ONE raid on device with a capture attached. Do not edit code first (CLAUDE.md sec.12).
- Named suspects, in order, to be confirmed or eliminated by the capture:
  1. the `ProbeForStructure` log storm with stack frames (WO-1450, same batch) - 320 stack walks/second;
  2. 14 of 24 VFX loop slots held by `ArcaneTower_Aura` (WO-1473, same batch);
  3. per-frame `ProbeForStructure` raycasts from `Enemy:Update()`.
- Then fix the one the data names, and only that one.

## 3. WHAT NOT TO DO
- Do not lower quality settings to raise the number. That hides the cost and ships a worse-looking game.
- Do not act on any of the three suspects above before the capture; two of three are cheap to fix and would
  produce a false "fixed" if the real cost is the third.

## 4. ACCEPTANCE
- [ ] A device profile capture is attached and the dominant cost is NAMED with its measured ms.
- [ ] A fix targeting that cost, with before/after fps from the same raid.
- [ ] `REGRESSION_OK n/n` on a fresh log.

## DEVICE CAPTURE 2026-09-10

Read-only device-diagnosis lane. No file under `Assets/` touched, nothing committed, nothing
installed or uninstalled, no device setting changed. **Status line deliberately unchanged** — this
capture does NOT satisfy acceptance item 1 (see "Scope gap" below).

### Subject

```
$ adb devices -l
List of devices attached
SM02G4061955851        device product:seeker model:Seeker device:seeker transport_id:1
```

Build read off the device, not off the brief:
`versionName=2026.09.10.363195  versionCode=363195  minSdk=26 targetSdk=36`
`lastUpdateTime=2026-09-10 00:24:39` (`dumpsys package com.denellestudios.echoesofelarion`).
App pid for the profiled run: **19136**. `timeScale=1.00` throughout the town window
(the 92 `timeScale=0.00` samples are the modal-paused boot window before the town was clear).

### ⚠ Correction to the standing premise: the logcat ring is 16 MiB, not 256 KiB

```
$ adb logcat -g
main: ring buffer is 16 MiB (4 MiB consumed, 32 KiB readable), max entry is 5120 B, max payload is 4068 B
system: ring buffer is 16 MiB (4 MiB consumed, 16 KiB readable), max entry is 5120 B, max payload is 4068 B
crash: ring buffer is 16 MiB (4 MiB consumed, 0 B readable), max entry is 5120 B, max payload is 4068 B
kernel: ring buffer is 16 MiB (0 B consumed, 0 B readable), max entry is 5120 B, max payload is 4068 B
```

**Nothing was evicted.** The `-d` dump after the run holds 40,207 lines starting at
`--------- beginning of main` / first stamp `09-10 02:01:44.510`, i.e. the entire session from the
`logcat -c` onward, with 479 `[Flow:Perf]` lines, 389 `frame budget` roll-ups and 68 `LOW fps`
samples. A continuous `logcat -v time` stream was also run as a redundant net; the `adb` daemon
died and restarted twice mid-session, so that stream is split across two files. Both are kept.

### Files

| Path | What it shows |
|---|---|
| `Builds/device-frames/2026-09-10_0202_profile_title.png` | Title, ~45 s after a clean `force-stop` + LAUNCHER relaunch. CONTINUE / START NEW / PLAY INT… faces present. |
| `Builds/device-frames/2026-09-10_0203_profile_after_continue.png` | After CONTINUE: the offline-earnings "WELCOME BACK" modal, town already rendered behind it. |
| `Builds/device-frames/2026-09-10_0205_profile_town.png` | After COLLECT: "HARVEST RESULT" modal (wood/iron/stone all 3,000/3,000 FULL). |
| `Builds/device-frames/2026-09-10_0206_profile_town_clear.png` | After CLOSE: a second modal, "DAILY CHEST". |
| `Builds/device-frames/2026-09-10_0208_profile_town_idle_start.png` | Town clear, idle start. `Main_Castle_Overworld`, hero Grom Lv 3 standing still, camera static, 5-face dock (BUILD/TALK/HERO/JOURN/MANAGE), "Wave 7 — next wave in 13m 47s". |
| `Builds/device-frames/2026-09-10_0210_profile_town_idle_end.png` | Same view after the 120 s idle. Wave timer reads 11m 29s — proof the app stayed foregrounded, alive and unpaused for the whole window (screen timeout is 1,800,000 ms, so no sleep). |
| `Builds/device-frames/2026-09-10_profile_logcat.txt` | The instructed `adb logcat -d -v time \| tr -d '\000'` dump. 40,207 lines. |
| `Builds/device-frames/2026-09-10_profile_logcat_stream.txt` | Redundant live stream, part 1 (title/boot). |
| `Builds/device-frames/2026-09-10_profile_logcat_stream2.txt` | Redundant live stream, part 2 (town + idle). |

### Measured fps — town, idle, ZERO enemies, ZERO towers

`[Flow:Perf]` telemetry, verbatim, contiguous across the idle:

```
09-10 02:04:38.370 W/Unity (19136): [Flow:Perf] LOW fps=23 ms=44.3 mem=413MB gc=21MB scene=Main_Castle_Overworld towers=0 enemies=0
09-10 02:05:02.500 W/Unity (19136): [Flow:Perf] LOW fps=22 ms=45.1 mem=413MB gc=21MB scene=Main_Castle_Overworld towers=0 enemies=0
09-10 02:05:34.726 W/Unity (19136): [Flow:Perf] LOW fps=23 ms=43.8 mem=414MB gc=22MB scene=Main_Castle_Overworld towers=0 enemies=0
```

68 such samples; the whole town window sits in a **22–23 fps / 43.3–45.1 ms** band.

**Independently corroborated by SurfaceFlinger**, which knows nothing about our telemetry:

```
09-10 02:05:33.979 I/BufferQueueProducer(1412): [...UnityPlayerGameActivity(BLAST)...] queueBuffer: fps=22.74 dur=1011.28 max=51.28 min=32.12
09-10 02:05:37.044 I/BufferQueueProducer(1412): [...UnityPlayerGameActivity(BLAST)...] queueBuffer: fps=22.76 dur=1010.37 max=50.20 min=32.76
```

**The same app on the same device in the same session hit the cap at the Title:**

```
09-10 02:02:13.626 I/Unity (19136): [Flow:Perf] fps=60 ms=16.6 mem=379MB gc=15MB scene=Title towers=0 enemies=0
```

### Perf table — top scopes, verbatim

```
09-10 02:05:32.963 I/Unity (19136): [Flow:Perf] frame budget: HeroLocomotion.Update=41.4ms/s (x24 worst 2.7ms), AmbientNPC.Update=5.1ms/s (x552 worst 0.1ms), SmartMobileCamera.LateUpdate=5.0ms/s (x24 worst 0.3ms), HeroTargetIndicator.LateUpdate=3.0ms/s (x24 worst 0.6ms), WaveManager.Update=2.0ms/s (x24 worst 0.1ms) | measured total=62.4ms/s over 20 scopes
09-10 02:06:00.524 I/Unity (19136): [Flow:Perf] frame budget: HeroLocomotion.Update=39.9ms/s (x23 worst 5.2ms), AmbientNPC.Update=4.9ms/s (x529 worst 0.0ms), SmartMobileCamera.LateUpdate=4.8ms/s (x23 worst 0.5ms), HeroTargetIndicator.LateUpdate=2.6ms/s (x23 worst 0.4ms), WaveManager.Update=2.0ms/s (x23 worst 0.2ms) | measured total=60.3ms/s over 20 scopes
09-10 02:06:01.525 I/Unity (19136): [Flow:Perf] frame budget: HeroLocomotion.Update=36.7ms/s (x23 worst 3.0ms), AmbientNPC.Update=5.2ms/s (x529 worst 0.2ms), SmartMobileCamera.LateUpdate=4.9ms/s (x23 worst 0.4ms), HeroTargetIndicator.LateUpdate=2.6ms/s (x23 worst 0.3ms), WaveManager.Update=1.9ms/s (x23 worst 0.2ms) | measured total=56.5ms/s over 20 scopes
```

Aggregated over the **205 roll-ups** in the town window (02:04:30 onward), computed from
`2026-09-10_profile_logcat.txt` — the `-d` dump cited above, so the numbers and the artifact match:

| Scope | mean ms/s | share of measured total |
|---|---|---|
| `HeroLocomotion.Update` | **37.32** (n=205) | **64%** |
| `AmbientNPC.Update` | 5.14 (n=205) | 9% |
| `SmartMobileCamera.LateUpdate` | 5.06 (n=205) | 9% |
| `HeroTargetIndicator.LateUpdate` | 2.75 (n=205) | 5% |
| `WaveManager.Update` | 1.93 (n=198) | 3% |
| all remaining scopes (below the top-5 cut, so never named in the log) | ~5.7 | ~10% |
| **measured total** | **57.9** (max 76.8) | over 20 scopes |

(`PostureEvaluator.Update` 5.40 and `HudContextEvaluator.Poll` 3.77 surface in the top-5 only 1 and 6
times out of 205 respectively — spikes, not steady cost.)

Every over-budget warn in the session belongs to essentially one site:
`HeroLocomotion.Update` x14, `StructureContentWarmer.Host.Update` x2, `PostureEvaluator.Update` x1,
`AmbientNPC.Update` x1.

### Thermal — before / +60 s / +120 s

| | `dumpsys battery` temp | Thermal Status | SKIN | CPU | GPU | SOC |
|---|---|---|---|---|---|---|
| before (02:01) | 41.0 °C | **3 (SEVERE)** | 44.93 (status 3) | 47.34 | 47.34 | 46.34 |
| +60 s | 40.0 °C | **3 (SEVERE)** | 44.18 (status 3) | 56.90 | 56.90 | 56.98 |
| +120 s | 40.0 °C | **3 (SEVERE)** | 44.11 (status 3) | 57.07 | 57.07 | 57.09 |

`SKIN mHotThrottlingThresholds=[NaN, 39.0, 40.0, 41.0, 56.0, 58.0, 80.0]` — SKIN at ~44 °C is past
the level-3 (SEVERE) threshold of 41.0. CPU/GPU/SOC thresholds are `[…, 85.0, 90.0, …]` and the
silicon never exceeded 57 °C, so **the throttle is driven entirely by SKIN, not by die temperature**.
Note two disagreeing sources, both recorded rather than reconciled: `dumpsys battery` reports 40.0–41.0 °C
while `thermalservice`'s cached BATTERY entry reports 27.0 °C. The device was **USB-powered
(`USB powered: true`, `status: 2`, level 89)** throughout, which is itself a heat source — a
confounder, recorded as one. The curve was flat (44.93 → 44.18 → 44.11), so the run was steady-state
throttled, not heating up under load.

### Suspects from §2

- **Suspect 1 and 3 (`ProbeForStructure` log storm / per-frame probe raycasts): NOT PRESENT.**
  `grep -c ProbeForStructure` over the full capture returns **0**. This eliminates them *for the town*,
  and only for the town — both are enemy-driven and there were zero enemies. Neither is confirmed
  nor eliminated for the raid.
- **Suspect 2 (`ArcaneTower_Aura` VFX loop slots): not evaluable here** — `towers=0`.
  `VfxPerformanceGate.Update` and `VfxAuraProximityCuller.Update` do appear in the boot-window
  roll-ups at 0.3–2.1 ms/s, i.e. negligible.
- Unrelated but captured, not chased: two `[Flow:StructureAssets]` errors at boot about an invalid
  Addressables INIT handle (the handled/continuing path), and OS-level `serviceDiscovery` mDNS
  `ParseException` noise from pid 1915.

### ⚠ Scope gap — this is NOT the capture acceptance item 1 asks for

§1's evidence is `scene=RaidBase_raider_camp_small enemies=13 ms=87.4`. This lane profiled
**`Main_Castle_Overworld`, idle, 0 enemies, 0 towers**, as directed. It therefore establishes the
frame **floor** and proves the cost exists with nothing in the scene; it does **not** measure the raid,
and it cannot close acceptance item 1. A raid capture is still owed.

### FINDING

In town, idle, with zero enemies and zero towers and `timeScale=1.00`, the device holds a steady
**22–23 fps / ~43.9 ms per frame** — corroborated independently by SurfaceFlinger's own
`queueBuffer: fps=22.7` — while the *same app in the same session* rendered the Title at
**60 fps / 16.6 ms**, so this is a scene-specific cost and not a global frame-rate cap. The perf table
**did fire and did survive** — 389 roll-ups, nothing evicted — and it names `HeroLocomotion.Update` as
by far the dominant *instrumented* site at a mean **37.3 ms/s (64% of all instrumented cost, ~1.6 ms of
every frame, worst single pass 5.2 ms)**, with every other scope an order of magnitude smaller. But the
load-bearing number is the total: **all 20 instrumented scopes together account for a mean of only
57.9 ms of each 1,000 ms of wall clock — about 5.8% of the frame time — leaving roughly 41 ms of every
43.9 ms frame outside every `FlowTrace.Measure` scope in the build.** Deleting `HeroLocomotion.Update`
outright would therefore be worth **at most ~0.9 fps** (22.8 → ~23.7), and only if the frame is
main-thread-bound; if it is GPU- or render-thread-bound the gain is zero. So the honest reading is that
this capture **eliminates the 20 instrumented scopes** — *not* managed code in general — rather than
naming the cause. The uncovered territory is everything else: un-instrumented MonoBehaviour
`Update`/`LateUpdate`, UI layout and canvas rebuild, Animator, physics, culling and draw submission,
the render thread, and the GPU. The WO's acceptance item 1 — "the dominant cost is NAMED with its
measured ms" — is **not yet met**. `HeroLocomotion.Update` is a real, worth-recording secondary
finding and the one site that repeatedly breached its own 4 ms budget, but on this data it cannot
explain the frame floor and a fix aimed at it would produce a false "fixed".

**Two limits on the absolute number, recorded rather than papered over (11B.A).** First, the device sat
in `Thermal Status: 3 (SEVERE)` for the entire run — bracketed by readings at 02:01 and 02:05, both
status 3 — and was USB-powered throughout. The Title-at-60 comparison rules out a frame-rate *cap*, but
it does not rule out reduced clocks inflating a heavy scene's millisecond cost; the **ranking** inside
the perf table is unaffected by throttling, the **absolute 43.9 ms may be inflated**. The discriminator
is a cooled, unplugged rerun, which could not be performed tonight — recorded as unproven, not ticked.
Second, the remaining ~94% of the frame is currently unattributed, and the one cheap measurement that
would split it is a **CPU-main vs render-thread vs GPU breakdown** — a Development-build Profiler
attach over `adb`, or a `FrameTimingManager` (`cpuFrameTime` / `cpuRenderThreadFrameTime` /
`gpuFrameTime`) readout folded into the existing `PerfReporter` roll-up. That is the next
*instrumentation* step this WO's own status demands, and it is named here as the next measurement, not
as a fix.

## INSTRUMENTED 2026-09-10 (frame split)

Edit-only lane (FRAME-SPLIT). No Unity run, no gate, no commit, nothing installed, no device touched.
This section records the instrument that was ADDED; it measures nothing by itself — the numbers come
from the next device capture. **No frame cost was changed.** Two `.cs` files touched, both via the
Write/Edit tools on the Windows path (CLAUDE.md §0/§1 forbid bash redirects on `.cs`, which overrides
the harness's "prefer bash for edits" hint); `python tools/gate_brace.py` reports
`GATE_BRACE_SUMMARY bad=0 of 2` and both files carry zero NUL bytes.

### What was added

`Assets/_Modules/Core/Diagnostics/PerfReporter.cs` (+169 lines)

| Line | What |
|---|---|
| `:65` | `public static string LastFrameSplit` — dev-HUD readout, mirrors `LastSummary` |
| `:81-124` | frame-split state + the reasoning block (why, how, dedupe, availability) |
| `:158` | `_lastSplitEmitTime` seeded in `Awake` |
| `:184` | `SampleFrameSplit()` — per-frame ACCUMULATE, emits nothing |
| `:197` | `ReportFrameSplit()` — called inside the existing `BudgetRollupInterval` branch, **first**, so it still prints when the scope table is empty (`ReportFrameBudget` early-outs at `:220`) |
| `:267-302` | `SampleFrameSplit` body — `Guard.Try`-wrapped |
| `:309-382` | `ReportFrameSplit` body — averages, worst, resets, and the asymmetric emit at `:373/:375` |

**The two cadences are asymmetric on purpose.** The *available* line is data and earns one
`FlowTrace.Step` per roll-up window (`:375`), adjacent to `frame budget:` so the pair reads as one
window. The *unavailable* line goes through `FlowTrace.Once("Perf", "frame-split-unavailable", ...)`
(`:373`) and therefore prints on the first window of the play session and never again — `Once` dedupes
on `system + "/" + key` against a `HashSet`, `FlowTrace.cs:225-237`, cleared only by `ResetSession`
(`:240`). The unavailable state is static for a whole run, so repeating it every second would add a log
line per second and a WebTrace POST per second to restate a fact nothing can act on before a rebuild —
the §12 log-volume failure the accumulating overload exists to prevent. `LastFrameSplit` still
refreshes every window, so a dev HUD reads the current state either way.

`Assets/Editor/Regression/FrameBudgetMeasureRegression.cs` (+107 lines)

| Line | What |
|---|---|
| `:46-53` | header item 6, `[split]`, stating what it pins and why |
| `:249-362` | the `[split]` case: the `frame split: ` literal, the API identifiers, the unavailable branch, `ReportFrameSplit` emits through `FlowTrace`, **and through `FlowTrace.Once` for the unavailable state** (`:307`), plus the cadence pins — `Update` must call `SampleFrameSplit()` every frame, must contain **exactly one** `ReportFrameSplit()` and it must sit **after** the `BudgetRollupInterval` guard, and `Update` must emit no trace line itself |
| `:24-32`, `:188-189` | the stale flat **256 KiB** logcat-ring figure replaced: the ring is **per device and must be read with `adb logcat -g`** (16 MiB measured on the Seeker 2026-09-10, nothing evicted). The rule is unchanged — a per-frame emit is forbidden whatever the ring size, precisely because the ring you are logging into is unknown |

Literal checks read `perfNoComments`, identifier checks read `perfCode` — the same split the existing
`[rollup]` case uses at `:186` vs `:194`, so neither prose nor stripped literals can fake a pass.

### The API, read at source this session

`UnityEngine.FrameTimingManager` / `UnityEngine.FrameTiming` in `UnityEngine.CoreModule.dll`, read out
of **this project's own editor**, `6000.4.8f1` (`ProjectSettings/ProjectVersion.txt:1`), two ways:

- doc XML: `.../6000.4.8f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.xml:14374-14422`
  (the manager) and `:14314-14357` (the fields).
- Cecil over the same DLL, for the exact signatures: `public static` class; `Boolean IsFeatureEnabled()`,
  `Void CaptureFrameTimings()`, `UInt32 GetLatestTimings(UInt32 numFrames, FrameTiming[] timings)`;
  `FrameTiming` is a public struct whose `cpuFrameTime` / `cpuMainThreadFrameTime` /
  `cpuMainThreadPresentWaitTime` / `cpuRenderThreadFrameTime` / `gpuFrameTime` are `Double` and
  `frameStartTimestamp` is `UInt64`.

Field meanings quoted verbatim from that XML: `cpuFrameTime` = *"total CPU frame time calculated as the
time between ends of two frames, which includes all waiting time and overheads"*; `cpuMainThreadFrameTime`
= *"total time between start of the frame and when the main thread finished the job"*;
`cpuRenderThreadFrameTime` = *"the frame time between start of the work on the render thread and when
Present was called"*; `cpuMainThreadPresentWaitTime` = *"the CPU time the last frame spent in waiting for
Present on the main thread"*; `gpuFrameTime` = *"the GPU time for a given frame"*. All in ms.

**Dedupe, and why it matters:** `GetLatestTimings` hands back the latest *completed* frame, which lands
a few frames behind, so consecutive `Update` calls can return the SAME frame. A repeated
`frameStartTimestamp` is skipped and counted as a `repeats` term instead. Without this the `n=` count
and every mean would be inflated — a number that looks like a mean and is not.

### ⚠ The player setting, read this session — the lead's one-line switch

```
ProjectSettings/ProjectSettings.asset:157   enableFrameTimingStats: 0
```

And the Seeker APK is a **RELEASE** player: `Assets/Editor/AndroidBuild.cs:141` sets
`options = BuildOptions.None` (no `BuildOptions.Development` anywhere in that file).

**This lane did NOT edit ProjectSettings** — it never rides a lane commit. Flipping that field to `1` is
the lead's call.

⚠ **Stated at its actual tier (§11B.A).** What is PROVEN: the setting reads `0`, and the Android build
sets `BuildOptions.None`. What is NOT proven from here: the exact rule by which Unity gates
`FrameTimingManager` — `UnityEditor.CoreModule.xml:39769-39773` documents
`PlayerSettings.enableFrameTimingStats` as nothing more than *"Enable frame timing statistics"* and says
nothing about editor / development-player / release-player behaviour. So the honest expectation is:
**if** the manager returns no timings on the device, the line will say
`frame split: unavailable (... IsFeatureEnabled=false ...)` — **not zeros** — and that negative is
itself the answer, because it names the reason and points at this flag. **The one cheap way to close
it:** read the split line on the very next device log. If it carries numbers, the flag was never the
gate; if it says unavailable, flip the flag, rebuild, recapture. Either way one capture settles it.

📋 **Log volume:** the unavailable line prints **once per play session**, not once per second — see the
asymmetric-cadence note above. So on a device where the setting is off the whole run costs exactly one
extra line, and `grep -c "frame split: unavailable"` returning `1` is the expected reading, not `0`.

**Graphics API context, recorded not asserted:** `ProjectSettings.asset:575-578` lists a
`m_BuildTargetGraphicsAPIs` entry for `WindowsStandaloneSupport` only — there is **no Android entry**, so
Android runs the automatic default. Whether that default's API exposes a GPU timer on this device is
unproven from here; that is exactly why a zero GPU sum prints `gpu=n/a` rather than `gpu=0.0ms`.

### The grep the lead runs on the next device logcat

```
grep -E "\[Flow:Perf\] frame split:" <logcat.txt> | tail -40
grep -c "frame split: unavailable" <logcat.txt>
```

Line shape, at the same 1 s cadence as the existing `frame budget:` line and adjacent to it:

```
[Flow:Perf] frame split: cpuFrame=43.8ms cpuMain=12.1ms cpuRender=9.4ms gpu=31.7ms presentWait=4.9ms (worst main=20.1 render=12.0 gpu=41.0; n=23 frames over 23 polls, 0 repeats, window 1.00s)
```

(That example is FORMAT, not data — no split has been captured on any device yet.)

### How to route the ticket from those numbers

The frame to explain is ~43.9 ms (22-23 fps), of which the 20 `Measure` scopes explain ~5.8%. Compare
each term against `cpuFrame`:

| Reading | Verdict | Where the ticket goes |
|---|---|---|
| `cpuMain` ≈ `cpuFrame`, `cpuRender` and `gpu` well below | **CPU main-thread bound** | the ~94% is un-instrumented main-thread work — Animator, physics, UI/canvas rebuild, culling, engine callbacks, un-measured `Update`s. Widen the `Sites` table; do NOT touch `HeroLocomotion.Update` first (it is worth ≤ ~0.9 fps by this WO's own arithmetic) |
| `cpuRender` ≈ `cpuFrame`, `cpuMain` well below | **render-thread bound** | draw submission / batching / material and shader variant churn |
| `gpu` ≈ `cpuFrame`, and/or `presentWait` large on the main thread | **GPU bound** | fill rate, shader cost, resolution/`heightScale`, overdraw. `presentWait` large + `cpuMain` small is the classic "main thread is waiting for the GPU" signature |
| all three well below `cpuFrame` | **waiting, not working** | vsync interval or thermal throttling, not a workload — pair with the `Thermal Status: 3 (SEVERE)` reading above and re-run cooled and unplugged before any code change |
| `gpu=n/a` with healthy CPU terms | GPU timer absent on this API | not a finding about cost; the GPU branch stays unresolved |
| `unavailable` | the switch above was never flipped | flip `enableFrameTimingStats`, rebuild, recapture |

⚠ **Do not fix anything off this section.** It adds an instrument. Acceptance item 1 stays open until a
device capture carries the split line and one row above is actually satisfied.

### Stale number corrected in the same lane (was flagged, then ruled in by the lead)

`FrameBudgetMeasureRegression.cs` said the Android logcat ring is **256 KiB** in two places — the header
at `:26` and the `[4-arg]` reason string at `:175`. The 2026-09-10 device capture in this very WO
(`adb logcat -g`, quoted at `:60-70`) measured **16 MiB per buffer with nothing evicted**. Both now say
the ring size is **per device and must be read with `adb logcat -g`**, with the Seeker measurement named
as a dated observation rather than a new constant to copy. The reasoning is untouched: a per-frame emit
stays forbidden *because* the ring you are logging into is unknown. Replacing one hard number with
another would have re-seeded the same duplicated-state failure CLAUDE.md §2/§5/§16 each describe.
