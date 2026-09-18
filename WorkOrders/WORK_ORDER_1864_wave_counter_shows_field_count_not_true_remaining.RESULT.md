# WO-1864 RESULT — the wave counter now publishes live + cap-HELD

**Lane:** HUD / presentation (read-only accessor on `WaveManager`; no spawn logic, no scene files)
**Date:** 2026-09-18
**Owed and NOT claimed:** this lane ran **no Unity** — its brief forbade gating and committing, and
the working tree carries three other lanes' uncommitted edits, so firing batchmode here would have
been a gate over a half-written tree. `COMPILE_GATE_OK` + `REGRESSION_OK` and the owner's felt-verify
are the lead's and the PO's, in that order.

---

## What was proven, and with what

### 1. The defect — the owner's own device pull, no headless run needed

`Logs/device/raid-trace-20260918-080200.txt`, three lines 0.1s apart and then 101 seconds of silence:

```
L3399   07:26:39.667  [Flow:Wave] wave 26: concurrency cap 8 released 8 now, HOLDING 26 for
                      reinforcement (total roster unchanged at 34).
L3414   07:26:39.670  [Flow:Wave] wave 26: field has 8 live, 26 still HELD by the concurrency cap
L3486   07:26:39.774  [Flow:HUD]  Active wave 26/0 live 8/8 cd0.0
L15452  07:28:20.833  [Flow:HUD]  Active wave 26/0 live 7/7 cd0.0
```

Every `[Flow:HUD]` wave line the device produced for wave 26, extracted mechanically
(`(remaining, total)`):

```
(8,8) (7,7) (5,5) (4,4) (3,3) (2,2)
```

Six publishes for a 34-enemy wave, the first of them held for 101 seconds. The owner's "shows 8
remaining troops till it gets lower than 8" is that list, exactly.

The 48 captured frames of the capped phase all read `field has 8 live` — `live` distinct set = `{8}`
— while `held` walked `26 -> 1`. The field count is a constant by design; it is the one number that
cannot answer "how many are left".

### 2. The fixture is the log, verified — not hand-typed numbers that look right

`WaveCounterHonestyRegression.Wave26HeldWalk` was checked against the log by extraction:

```
captured wave-26 held-walk frames: 48
FIXTURE MATCHES LOG: True
```

### 3. The fixed arithmetic, run against that captured sequence

The same math as `WaveCounterMath.Remaining` + `WaveRosterTracker`, driven by the 48 captured
`(live, held)` pairs plus the observed tail:

```
starts at: 34                          (HEAD: 8)
distinct values during the capped phase: 16   (HEAD: 1)
monotonic non-increasing: True
ends at: 0
roster denominator: 34                 (matches the traced "total roster unchanged at 34")
ever published 8 while bodies were held: False
```

Published sequence: `34 34 34 ... 33 32 31 ... 26 24 23 22 21 17 16 15 14 9 ... 7 5 4 3 2 1 0`.

This verifies the **arithmetic on real data**, not the compile and not the rendered frame. Those two
are owed (below).

### 4. Brace + NUL gate, all seven touched files

```
$ python tools/gate_brace.py Assets/_Modules/Village/Waves/WaveManager.cs \
    Assets/_Modules/Core/HudModel/HudModels.cs Assets/_Modules/Core/HudModel/WaveCounterMath.cs \
    Assets/_Modules/Village/HUD/HudModelProducers.cs Assets/_Modules/HUD/Kit/HudKitController.cs \
    Assets/Editor/Regression/WaveCounterHonestyRegression.cs Assets/Editor/Regression/DataRegression.cs
GATE_BRACE_SUMMARY bad=0 of 7
EXIT=0
```

Raw counts + NUL scan (§1, WO-434): `WaveManager 643/643`, `HudModels 218/218`,
`WaveCounterMath 10/10`, `HudModelProducers 121/121`, `HudKitController 445/445`,
`WaveCounterHonestyRegression 20/20`, `DataRegression 1251/1251`, `NUL_BAD=0`.

---

## Files

| Path | Change |
|---|---|
| `Assets/_Modules/Village/Waves/WaveManager.cs` | `+ public int HeldReinforcements => _heldSmartReinforcements;` (read-only, next to `LiveEnemies`), doc comment carrying the proving lines. **Nothing else in this file changed** — no cap, no budget, no drain, no timing. |
| `Assets/_Modules/Core/HudModel/WaveCounterMath.cs` | NEW. `WaveCounterMath.Remaining(live, held)` + `WaveRosterTracker.Observe(wave, remaining)`. Pure, no Unity types. |
| `Assets/_Modules/Core/HudModel/HudModels.cs` | `WaveModel.EnemiesLive` -> **`EnemiesRemaining`**; `EnemiesTotal` redefined as the wave ROSTER; `Set`'s `live` param -> `remaining`; trace line now `remaining N/T`. |
| `Assets/_Modules/Village/HUD/HudModelProducers.cs` | `WaveProducer` publishes `WaveCounterMath.Remaining(live, wm.HeldReinforcements)`; roster from `_roster.Observe`; permanent `FlowTrace.Step` **after** the change-gate printing the split. |
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | `hud.hud_kit.enemies_remain` fed from `EnemiesRemaining`; progress bar denominator = roster, so it drains. |
| `Assets/Editor/Regression/WaveCounterHonestyRegression.cs` | NEW oracle, 6 cases, markers `WAVE_COUNTER_HONESTY_OK` / `_FAIL`. |
| `Assets/Editor/Regression/DataRegression.cs` | +1 registration line, `wave-counter-honesty suite`. **Shared file — the harvest lane also has it modified.** |
| `CLI_LANES_WO_NUMBERS.md` | banner bumped 1864 -> 1865 in the same edit as the mint (§2). |

## Instrumentation added (permanent, §12)

`HudModelProducers.cs`, in `WaveProducer.Poll`, **after** the change-gate:

```
[Flow:HUD] wave counter PUBLISH: wave 26 Active — field 8 live + 26 HELD by the concurrency cap
           = 34 remaining of roster 34.
```

Placed after the gate on purpose: `Poll` runs at 5 Hz, and a bare `Step` on that cadence is a
firehose that evicts the boot window out of the device logcat ring (memory
`logcat-ring-buffer-destroys-evidence`). Gated, it fires only when the published number moves — and
it prints the **split**, so the next "the counter is wrong" report is decidable from one line instead
of pairing two systems' traces by timestamp, which is what this ticket had to do.

## Owed — exactly what is not proven

1. **Compile.** No `COMPILE_GATE_OK`. The rename `EnemiesLive` -> `EnemiesRemaining` has exactly two
   call sites in the tree (`HudModelProducers`, `HudKitController`), both updated; a grep for
   `EnemiesLive` now returns only two historical mentions inside comments.
2. **The suite has never been observed RED or GREEN.** Cases 2, 3 and 5 are RED-by-construction
   against HEAD (the types they drive did not exist there), but that RED was not watched.
   Standalone: `run-unity-method DeNelle.Editor.Regression.WaveCounterHonestyRegression.RunAll`.
3. **The rendered frame.** Nobody has seen the counter on a screen. A device screencap during a
   wave past 20, or `UI_CAPTURE_OK` PNGs, closes it.

## Known gaps, reported rather than guessed

- **The apex dragon is still not counted.** It is not in `LiveEnemies` (kinematic flight, tracked in
  `_liveApexBosses`), and it was not counted before this change either. An apex wave's counter
  therefore omits the boss. Needs an owner ruling on whether a boss belongs in a troop count.
- **Between-waves bar state — checked, not assumed.** `HudKitController:4748` hides the progress
  track on `EnemiesTotal == 0`, and on HEAD `EnemiesTotal` fell to 0 the moment the field emptied, so
  the bar vanished on clear. The roster peak now survives until the wave NUMBER changes, which raised
  the question of a bar reading full through the clear. **It cannot linger:** `CompleteWave`
  (`WaveManager.cs:3879`) calls `EnterCountdown(cleared + 1)` **synchronously, in the same frame**
  (`:3917`), so the number bumps and the peak resets inside one frame — far inside the producer's
  0.20s poll. The schedule-exhausted branch (`:1784`) and the endless awaiting-player branch both
  reach `EnterCountdown` with `cleared + 1` too, so both also reset. Net effect between waves is
  unchanged from HEAD.
- **Roster peak window.** The denominator is the peak of `remaining` since the wave number changed,
  established on the first `Poll` after the composition defers. If the very first poll landed after a
  kill (a ≤0.2s window at 5 Hz), the roster would read a couple low. Display-only, self-corrects
  never — but it cannot make the counter dishonest, only the bar slightly generous.
- **`WaveHudBridge` is a dead path.** It reflects into `VillageHudController.SetEnemyCount`, which is
  an empty stub (`VillageHudController.cs:292`). Left untouched; it displays nothing today.
