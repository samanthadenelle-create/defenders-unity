# WO-1618 - The scaled raid clock froze at 50s while the raid ran ~455s engaged

**Status:** READY TO IMPLEMENT - INSTRUMENT FIRST. The divergence is MEASURED; the mechanism is
**not proven**. No behavioural edit before the discriminating trace names which of the two
candidates is live (CLAUDE.md sec.12 hard gate).
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
