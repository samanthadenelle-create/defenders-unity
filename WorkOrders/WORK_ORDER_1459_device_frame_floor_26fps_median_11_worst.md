# WO-1459: device frame floor - 26 fps median, 11 fps worst in a raid, with timeScale at 1.00

**Status:** READY TO IMPLEMENT (instrument first; NO fix before the data names the cost)
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
