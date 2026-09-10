# WO-1618 - The scaled raid clock froze at 50s while the raid ran ~455s engaged

**Status:** BLOCKED - awaiting the raid capture that names the frozen step; INSTRUMENTED (lane RAID-CLOCK 2026-09-10)
**Minted:** 2026-09-09 (CLI, main-line banner; bumped 1615 -> 1619 in the SAME edit)
**Silo / Lane:** Raid (lane RAID hand-back)
**Severity:** P1 felt - the player fought a raid for over eight minutes and was scored on 50
seconds of it. Loot, stars and the under-time bonus were all computed from the frozen number.
**Type:** EXISTING system. `RaidScoring` owns exactly one clock; it stopped advancing.
**Source:** F8 capture seq=4980, `logs/f8-inbox/capture-20260909-144859-seq4980.md`, scene
`RaidBase_raider_camp_small`.

---

## 1. What was measured (read from the capture file this session)

From `logs/f8-inbox/capture-20260909-144859-seq4980.md`:

```
[BREAK] error: [Flow:Raid] RAID STRANDING WATCHDOG FIRED (last-resort arm) - 510s in a raid scene
with a 180s clock that NEVER finalized ...
   scene "RaidBase_raider_camp_small", t = 1546.019287109375, utc 2026-09-09T19:48:55.8724278Z

[Flow:Raid] stars settled: 0 (earned=0 heroDied=True cap=2)
            (cleared=False destruction=46 % elapsed=50s/180s underTime=True survival=100 % high=True @70 %).
[Flow:Raid] raid scored: 0 star(s), 46% razed, 50.2s, cleared=False, deployed=10.
```

Corroborating scene age in the same window: the VFX census in the same capture lists live
`Damage_Ruin` owners with `age=469s` / `467s` / `455s`, so real combat effects had been alive for
roughly 455-469 seconds while the raid clock read **50.2 s**.

**From the RAID lane's hand-back on this seq (break-log, NOT re-read in this lane):** raid
`scene_loaded` at `t=1036.537`, watchdog `Fail` at `t=1546.019` - a scene age of **509.482 s**,
which matches the watchdog's own "510s" text exactly. The hand-back also reports a
`[HeroDeath] death freeze armed` line; that string is **not present in this capture file** and was
not re-read at source here.

**The divergence, stated plainly:** ~455-509 s of scene life and combat, 50.2 s on the clock that
scores it. That is measured. Why is not.

## 2. STOP: What is NOT claimed

- **`timeScale == 0` is NOT claimed.** The `[HeroDeath] death freeze armed` line the lane reported
  is the **hero agent pin**, not a world hold. A hero-death freeze that pins the agent is not
  evidence that `Time.timeScale` was zeroed, and this ticket does not assert that it was.
- **It is NOT a watchdog defect.** The watchdog did its job and routed the player home. WO-1095
  has just landed on that arm (`IMPLEMENTED - awaiting gate`). Do not touch it.
- Nobody has yet read a `timeScale`, an `Engaged` value, or a pause flag from a frame during the
  frozen window. Section 4 exists to get exactly those three numbers.

## 3. What `RaidScoring.Update` charges - read at source 2026-09-09

`Assets/_Modules/Village/Troops/RaidScoring.cs`:

- `_elapsed` is declared at `:212`; `ElapsedSeconds => _elapsed` at `:245`;
  `RemainingSeconds => Mathf.Max(0f, _clockSeconds - _elapsed)` at `:249`.
- The comment at `:909-912` states the invariant: *"`_elapsed` is written in exactly two places
  (here and nowhere else), so there is no path that bills a player for the seconds they spend in
  the staging area."*
- The gate is `:913-921`: `if (!_engaged) { ...DetectEngagement()...; return; }` - the WO-1520
  early return.
- The only advance is `:924`: `_elapsed += Time.deltaTime;`

Two properties of that one line are load-bearing here:

1. It is gated on `_engaged` (`:257` exposes it, `:1028` sets it from
   `AwarenessState.Engaged`). If engagement **drops back to false** mid-raid, the clock stops and
   nothing in the current log says so.
2. `Time.deltaTime` is the **scaled** delta. Any world hold that zeroes `Time.timeScale` freezes
   `_elapsed` while `Time.unscaledTime` - which the stranding watchdog uses - keeps running. That
   is precisely the shape of a 50 s clock inside a 510 s scene.

Both mechanisms produce this exact symptom. The log cannot currently tell them apart.

WARNING: **These line numbers were read against the WORKING TREE, which carries lane RAID's uncommitted
edits** (`RaidScoring.cs` and `RaidDeployController.cs` both show ` M` in `git status` 2026-09-09).
Proof that the file has already moved under this ticket: the seq-4980 stack frame reads
`RaidDeployController/<StrandingWatchdog>d__32 ... :399`, while WO-1095's capture from the same day
reads `d__31 ... :398`. **Re-read `RaidScoring.cs` after lane RAID commits** before citing any line.

## 4. The discriminating trace (this IS the first deliverable)

Add ONE permanent `[Flow:Raid]` line, emitted at most once per 5 seconds from `RaidScoring.Update`,
naming all four values in one place so no correlation across sources is needed:

```
[Flow:Raid] clock tick: timeScale=<Time.timeScale:0.00> engaged=<_engaged> reason='<EngagedReason>'
            paused=<the pause/hold term> elapsed=<_elapsed:0.0>s/<_clockSeconds:0.0>s
            scene=<Time.unscaledTime - _sceneStart:0.0>s
```

Use `FlowTrace.Throttle` (or `Once`-per-transition for the flags) - NOT a per-frame `Step`. A hot
per-frame line evicts the boot window out of the logcat ring and destroys the evidence
(`FlowTrace.cs:293-300`; memory `logcat-ring-buffer-destroys-evidence`).

**Also log the transition itself:** one `FlowTrace.Warn` whenever `_engaged` goes true -> false, and
one whenever a `WorldHold` reason is acquired or released while a raid scene is live, naming the
reason string. A clock that stops needs a line at the moment it stops, not only a sample after.

**Read the capture before editing anything else.** The routing is unambiguous:
- `timeScale=0.00` in the samples -> a `WorldHold` reason was left armed. Fix the holder's release;
  do NOT make `_elapsed` unscaled to route around it (that would hide a real stuck hold and let the
  raid clock run through every legitimate freeze).
- `engaged=False` in the samples -> engagement is dropping. Fix the engagement latch; a raid that
  has been engaged once should not un-engage back into the staging-free-time arm.
- Both true and `elapsed` still frozen -> `Update` is not running at all on that instance, which is
  a third mechanism and needs its own read.

## 5. Acceptance criteria

- [ ] **RED-first evidence:** the RESULT quotes the new `clock tick` lines from a capture of a real
      raid, taken BEFORE any behavioural edit, and names which mechanism the numbers proved.
- [ ] The RCA sentence states the cause with its log line. No "probably" / "should be" (sec.11B).
- [ ] After the fix, one raid capture shows `elapsed` tracking engaged scene time within a couple of
      seconds, and the settle line's `elapsed=` agrees with the raid's felt duration.
- [ ] The stranding watchdog does NOT fire in that run.
- [ ] The WO-1520 invariant survives: staging time is still free. A regression case proves a raid
      that never engages still reports `elapsed=0`.
- [ ] A regression case pins whatever the data named (a released hold, or the engagement latch).
- [ ] The instrumentation STAYS in the code after the fix (sec.12 - never strip).
- [ ] Owner felt-verifies a raid on device and closes (PO closes, not CLI).

## 6. Files

- `Assets/_Modules/Village/Troops/RaidScoring.cs` - `Update` `:900-931`, the engagement gate
  `:913-921`, the single advance `:924`, `DetectEngagement` `:1010-1030`
- `Assets/_Modules/Core/UI/WorldHold.cs` (path resolved by `find` 2026-09-09) - the hold/release
  trace only
- A new or extended suite under `Assets/Editor/Regression/`; the registration line goes back to the
  lead (`DataRegression.cs` is lead-owned, no lane edits it).

## 7. What NOT to touch

- STOP: **The stranding watchdog.** `RaidDeployController.StrandingWatchdog` (`:399` in the captured
  stack) is the net that caught this, WO-1095 just fixed that arm, and it is awaiting a gate.
  Raising its bound, softening its severity or editing its file would duel with that lane.
- STOP: **Do not make `_elapsed` unscaled** to "fix" the symptom. If a hold is stuck, the correct
  outcome is that the hold is released, not that the raid clock runs through every hit-stop.
- Do not touch the WO-1520 staging exemption, the loot/star maths, `RaidHudController` or
  `TroopController`.
- Do not re-open WO-1437 on this evidence. Its arm is the reporter here, not the cause.

## 8. Unproven, recorded honestly

- The mechanism. That is the whole of section 4 and it is deliberately not guessed.
- `scene_loaded t=1036.537` and the `[HeroDeath] death freeze armed` line come from the RAID lane's
  hand-back; neither string is in the seq-4980 capture file and neither was re-read from
  `break-log.jsonl` in this lane. The 509.482 s subtraction is consistent with the watchdog's own
  "510s" text, which is corroboration, not independent proof.
- Whether the hero's death is causal or merely coincident. `heroDied=True` is in the settle line;
  nothing yet ties it to the frozen clock.

---

## INSTRUMENTED 2026-09-10 (lane RAID-CLOCK)

Section 4 delivered, and ONLY section 4. **Nothing about the clock's behaviour changed** - every
line below reads state and writes none of it. No fix is claimed; the mechanism is still unproven.
No RESULT file, because nothing is fixed.

### Base note - the tree moved under this ticket

The lane worktree was based at `f5d39acd1`, which **predates both this WO and lane RAID's
`RaidScoring.cs` edits**. It was fast-forwarded to `refs/heads/dev` (`bb12c728e`) before anything
was read, exactly as section 3's WARNING instructs.

⛔ **EVERY LINE NUMBER IN THIS SECTION IS POST-EDIT** - taken from the working tree as this lane
leaves it (`bb12c728e` + this lane's diff), because that is the file the next reader opens. Mixing
bases is the exact hearsay §11B forbids. Re-cited at the post-edit numbers, section 3's landmarks
now read: `Update` at `:1314` (section 3 says `:900`), the WO-1520 staging gate at `:1338-1348`
(section 3 says `:913-921`), the single advance `_elapsed += Time.deltaTime` at `:1349` (section 3
says `:924`), the `_engaged` latch at `:1621`, the `_finalized` latch at `:1732`. **Section 3's numbers
are stale in every case; do not cite them again.**

### Files changed

- `Assets/_Modules/Village/Troops/RaidScoring.cs` - **the only file touched.**
- `Assets/_Modules/Core/UI/WorldHold.cs` - **NOT touched**, deliberately. It already traces every
  acquire and release with its reason (`:552`, `:564`, `:713`, `:722`), but under the **`Pause`**
  tag, and no capture in evidence proves `Pause` lines survive the device filter. The **`Raid`** tag
  IS proven present in seq 4980. So the hold state is quoted INTO the `Raid` line instead of adding
  a second trace under an unproven tag - and the ticket stays inside one file and one lane, which
  also keeps it clear of the WO-1095 watchdog lane (section 7).

### The trace lines added

| # | file:line | tag text (the grep handle) | what its value discriminates |
|---|---|---|---|
| 1 | `RaidScoring.cs:1525` | `[Flow:Raid] clock tick: timeScale=… dt=… avgScaleWin=… avgScaleTotal=… dips=… engaged=… reason=… finalized=… holds=… holdScale=… hold=[…] elapsed=…s/…s scene=…s frames=… fps=… inst=… isInstance=…` | The whole section-4 routing in one line - see the routing table below. Emitted from `Update` at most once per **5 UNSCALED seconds** (`ClockTickSeconds`, `:327`), gated **before** the string is built. Its `FlowTrace.Throttle` key is **per-instance** (`_tickKey`, built in `Awake` at `:1182`): the throttle state is a STATIC dictionary keyed `system/key` (`FlowTrace.cs:205-221`), so a shared key would let one scorer's line suppress another's and silently hide the second-scorer case this line exists to detect. |
| 2 | `RaidScoring.cs:1184` | `[Flow:Raid] clock origin armed: inst=… unscaledTime=…s frame=… timeScale=… holds=…` | Emitted in `Awake` (`:1165`). Fixes the scene-age origin and names the instance id, so a SECOND scorer shows a second origin and can never be misread as one frozen scorer. |
| 3 | `RaidScoring.cs:1426` | `[Flow:Raid] clock ENGAGEMENT DROPPED true->false: …` | Engagement drop, **edge**-triggered (the `_everEngaged` latch is re-armed at `:1434`, so a drop prints once, not once per frame for the rest of the raid). **On this tree it should never print** - see the finding below - so if it does, a later edit added a second writer to `_engaged` and the capture says so. |
| 4 | `RaidScoring.cs:1445` | `[Flow:Raid] world hold CHANGED under a live raid clock: holds N->M effectiveScale x->y reasons=[…] …` | Names the WorldHold reason string at the instant a hold is acquired or released while the raid clock is live (section 4's "log the transition itself"). `WorldHold.Describe()` is called ONLY inside this edge, never per frame. |
| 5a | `RaidScoring.cs:1498` | `[Flow:Raid] world timeScale LEFT 1.00 and STAYED there under a live raid clock: timeScale=… for …s unscaled holds=… …` | A non-1 clock with `holds=0` names a scale writer **WorldHold does not own** - the line lists the candidates WorldHold itself names (`HitStopManager`, `CombatFeedbackManager` 0.05/0.30, `WaveCelebrationManager` 0.28, `HeroHitReaction` death 0.30, `ArenaDeathCam`). Armed because the settle line carries `heroDied=True`. **Gated on PERSISTENCE, not on the transition** (`ScaleDepartureReportSeconds = 1.5f`, `:322`): a hit-stop is a legitimate, frequent departure from 1.00, and two ~450-byte lines per hit would evict the boot window out of the 256 KiB device logcat ring. Only a departure that outlives every deliberate dip in the tree (longest 1.2 s, `WorldHold.cs:568` and `:763`) is worth a line - and a clock held low for minutes is exactly the shape being hunted. |
| 5b | `RaidScoring.cs:1483` | `[Flow:Raid] world timeScale REJOINED 1.00 after a reported departure: … heldLowFor=…s unscaled …` | Closes 5a and states, in seconds, how long the clock under-billed. Printed **only** if the departure was itself reported, so it can never outnumber 5a. |
| 5c | (the `dips=` term in line 1) | `dips=N` | The brief departures 5a deliberately does not print, counted per window. Cheap evidence that hit-stops were or were not firing, without the volume. |
| 6 | `RaidScoring.cs:1224` | `[Flow:Raid] clock owner DISABLED before finalize: … activeSelf=… isInstance=…` | `OnDisable` (`:1219`). Section 4's mechanism 3 ("`Update` is not running at all on that instance") has **no Unity notification** - the disable edge IS the notification. ⚠ `OnDisable` also fires on scene unload and on application quit, so this line at a deliberate mid-raid quit is a TRUE statement about the clock, not the bug; read it together with the tick lines around it. |
| 7 | `RaidScoring.cs:1205` | `[Flow:Raid] clock owner DESTROYED before finalize: …` | `OnDestroy` (`:1191`) on an unfinalized scorer. After either 6 or 7, the **absence** of further `clock tick` lines is proof rather than a hole in the capture. |

The call site is `RaidScoring.cs:1320` - `TraceClockTick()` is the **first** statement of `Update`
(`:1314`), **before** `if (_finalized) return;` on purpose: "finalized flipped early" is itself a
candidate answer, and a probe that returns before it can say so cannot rule it out. The method is
`:1394-1535`.

### The load-bearing value is `avgScaleWin`, not `timeScale`

The single advance is `_elapsed += Time.deltaTime` (`:1245`) - the **scaled** delta. So the ratio of
scaled to unscaled seconds accumulated inside `TraceClockTick` **is** the fraction of real time the
clock was allowed to bill. A `timeScale` SAMPLE cannot answer this: a world held at 0.11 for the
whole raid and a world that ran at 1.00 for 50 s and then froze sample identically as whatever the
last frame happened to be. 50.2 s billed against ~455 s of engaged scene life is a mean scale of
**~0.11**, so the mean is the discriminator. Both the sample and the mean are logged, plus the
per-window mean (`avgScaleWin`, since the last tick) and the cumulative one (`avgScaleTotal`).

### Cost, and why not a per-frame `Step`

Per frame: two float adds, three cheap compares. The interpolated string is built **inside** the
unscaled 5 s gate - `FlowTrace.Throttle` interpolates its message *before* deciding to drop it, so
calling it unguarded would allocate every frame on the 22 fps device the WO-1373 lane already paid
to keep allocation-free in this class. The gate is **unscaled** (`Time.unscaledTime`), because a
scaled gate on a frozen clock would never fire - and silence from a frozen clock is precisely the
case that must not go unrecorded. `Throttle` is still called, at half the outer interval, so the
FlowTrace-side cap stands if a later edit breaks the outer gate. A hot per-frame line would evict
the boot window out of the device logcat ring and destroy the evidence
(`FlowTrace.cs:293-300`; memory `logcat-ring-buffer-destroys-evidence`).

### A finding that narrows section 3, read at source on `bb12c728e`

**Section 3's candidate 2 - "engagement drops back to false mid-raid" - is UNREACHABLE on this
tree.** `grep -nE "_engaged\s*=|ref _engaged" Assets/_Modules/Village/Troops/RaidScoring.cs` returns
**exactly one hit**: `:1340`, `_engaged = true`, inside `NotifyEngagement`. There is no write of
`false` anywhere in the file and no `ref`/`out` alias of the field. `_finalized` likewise has one
write (`:1451`).

This is a narrowing, **not a conclusion, and it is not a licence to skip the capture**: `_elapsed`
reached 50.2 s, so the clock demonstrably billed for a while. What the single-writer invariant means
is that `engaged=False` appearing in a live tick line can only come from a **different scorer
instance** - which is why `inst=` and `isInstance=` are in the sample line, and why trace #2 stamps
the origin. Trace #3 is kept anyway, to pin the invariant against a future edit.

Also checked: `grep -rn "RaidScoring"` across `Assets/**/*.cs` finds **no external caller** that
disables, deactivates or destroys the scorer's GameObject, so mechanism 3 would have to arrive via a
scene unload or a second instance - both of which traces #2, #6 and #7 name directly.

### What the next capture must show, and how each reading routes

Run one real raid to the same symptom and read the `[Flow:Raid] clock tick:` lines **before any
behavioural edit** (acceptance criterion 1, RED-first). Then:

- **`avgScaleWin` well below 1.00 with `holds` > 0** -> a `WorldHold` reason was left armed. Trace
  #4 names which reason and when it was acquired. Fix the **holder's release**. ⛔ Do NOT make
  `_elapsed` unscaled (section 7).
- **`avgScaleWin` well below 1.00 with `holds=0`** -> a scale writer WorldHold does not own. Trace
  #5 is the edge; start from the five candidates it names.
- **`avgScaleWin` ~1.00 and `elapsed` still lagging `scene`** -> the clock is not being pumped for
  part of the raid. Read `frames=` and `fps=`: a tick that reports far fewer frames than
  `fps x window` means `Update` stalled, and traces #6/#7 say whether the instance was disabled or
  destroyed.
- **No `clock tick` lines at all after a given point, with #6 or #7 present** -> mechanism 3,
  proven, with the moment named.
- **No `clock tick` lines and NO #6/#7** -> the scorer was never pumped in that scene at all; check
  for trace #2 (`clock origin armed`) to see whether it even awoke.
- **`engaged=False` in a live tick line** -> compare `inst=` against trace #2. A different `inst=`
  means a SECOND scorer owns the clock; the same `inst=` means the single-writer invariant broke
  (and trace #3 will be in the log).
- **`finalized=True` with ticks still flowing and `elapsed` frozen** -> the raid settled early and
  the scene kept running; that is a fourth mechanism and needs its own read.

### Sections honoured

- **Section 7 (do not touch):** `RaidDeployController` / the stranding watchdog - untouched.
  `_elapsed` is **not** made unscaled. The WO-1520 staging exemption (`:1234-1243`), the loot/star
  maths, `RaidHudController` and `TroopController` - all untouched. WO-1437 not re-opened.
- **Section 2 (not claimed):** no `timeScale=0`, no engagement drop and no watchdog defect is
  asserted here. The capture decides.
- **CLAUDE.md §12:** these lines are **permanent**. When the mechanism is fixed and the system is
  proven stable, flag FlowTrace off - never strip the calls.

### Not done in this lane (deliberately out of scope for section 4)

- The regression cases in section 5 - they pin whatever the data names, and the data does not exist
  yet.
- Any registration line in `Assets/Editor/Regression/DataRegression.cs` - lead-owned, no lane edits
  it (section 6).
- The gate. This lane is edit-only: no Unity run, no gate, no commit, no push. `gate_brace.py`
  reports `bad=0` on the one file changed, and it carries zero NUL bytes.

### INCREMENT - the trace turned a source-text pin RED, and the pin was RIGHT

`Builds/wave2-reg1` (fresh, 491/493) failed:

```
Case5: _elapsed is advanced in 2 place(s), not 1 - there is no single clock authority,
       so the engagement gate can be bypassed.
```

**The pin is `RaidStagingMarkerRegression.Case5_ClockGatedOnFirstEngagement`,
`Assets/Editor/Regression/RaidStagingMarkerRegression.cs:376-387`.** It counts regex matches of the
assignment shapes across the **whole file text**, with **no comment or string model** - which is
exactly how a single-writer invariant should be pinned, because a scanner that trusts comment
boundaries can be walked around. **The pin is correct and its pattern is unchanged.**

**The defect was mine, and it was in prose, not in code.** Two of my explanatory comments quoted the
literal assignment shapes, so the scanner counted them as writers:

| shape | before (line, comment text) | after |
|---|---|---|
| `_elapsed +=` | `:1364` - *"The single advance in Update is `_elapsed += Time.deltaTime`, the SCALED delta"* | `:1371` - *"The single advance in Update adds `Time.deltaTime` to the clock - the SCALED delta"* |
| `_engaged = true` | `:1404` - *"`_engaged` has exactly ONE write in this file (`_engaged = true`, in NotifyEngagement)"* | `:1410-1419` - *"a single assignment of TRUE, inside NotifyEngagement"*, plus a block naming the pin and the rule |

Only the second of those had been reported (Case5 returns on its first failure); the
`_engaged = true` count was a **latent second RED** on the same run, caught here by porting the
pin's own regexes and running them over the file rather than waiting for another gate.

Verified over the post-increment file with the pin's exact patterns: `_elapsed\s*\+=` = **1**,
`_engaged\s*=\s*true` = **1**, `_finalized\s*=\s*true` = **1**, `if\s*\(\s*!_engaged\s*\)` present,
the gate precedes the advance, and `clock started reason=` still present (the WO-1520 acceptance
line). `RaidTerminalStateRegression` Case A's two assertions also still hold: `Update` contains
`if (_finalized) return;` and the advance line, both inside the body it extracts.

**Standing rule for this file, now written INTO it at `:1417-1423`:** never spell an assignment
shape in a comment or a trace string here - read the value into a local and format the local. A
trace that describes the invariant must not read as a violation of it.

**One more increment, from the same reading.** `OnDestroy` (`:1203`) and `OnDisable` (`:1222`) now
report only when `Application.isPlaying`. `RaidTerminalStateRegression` Case A builds a scorer, ticks
it 60 times and `DestroyImmediate`s it **without finalizing** - a correct harness lifecycle, not the
defect - so the unguarded warn would have put a false *"the clock died"* line in **every gate log**.
Headless batchmode PLAY sessions still report, so the capture path this ticket depends on is
untouched.
