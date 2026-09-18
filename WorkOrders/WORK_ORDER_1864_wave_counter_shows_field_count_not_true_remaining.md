# WO-1864 — the wave counter showed the FIELD count, not the enemies remaining

**Status:** IMPLEMENTED — code is in the working tree; owed: the lead's `COMPILE_GATE_OK` +
`REGRESSION_OK` on a fresh log, and the owner's felt-verify (PO closes, §13). No Unity was run by
this lane — its brief forbade gating and committing.

**Minted:** 2026-09-18 (main line, banner bumped 1864 -> 1865 in the same edit)
**Silo:** HUD / presentation (§9 lane: no scene files, no spawn logic)
**Reported by:** owner, live build, 2026-09-18

---

## The report

> *"every wave shows 8 remaining troops till it gets lower than 8 can we reflect actual troop counts?"*

## The proving lines — her own device pull, not an inference

`Logs/device/raid-trace-20260918-080200.txt`:

```
L3399   07:26:39.667  [Flow:Wave] wave 26: concurrency cap 8 released 8 now, HOLDING 26 for
                      reinforcement (total roster unchanged at 34).
L3414   07:26:39.670  [Flow:Wave] wave 26: field has 8 live, 26 still HELD by the concurrency cap
L3486   07:26:39.774  [Flow:HUD]  Active wave 26/0 live 8/8 cd0.0     <-- what the player was shown
   ... 101 seconds, no further [Flow:HUD] wave line, while [Flow:Wave] walked the held count
       26 -> 25 -> 24 -> 23 -> 22 -> 21 -> 18 -> 16 -> 15 -> 14 -> 13 -> 9 -> 8 -> 7 -> 6 -> 1 ...
L15452  07:28:20.833  [Flow:HUD]  Active wave 26/0 live 7/7 cd0.0     <-- first movement, at the tail
```

Wave 27 is the same shape: `HOLDING 27 ... total roster unchanged at 35` (L104497) and
`[Flow:HUD] Active wave 27/0 live 8/8` (L104590).

## Root cause

**Not the spawner.** `WaveManager`'s concurrency cap (WO-1113, `_maxSimultaneousEnemies`, 8 in the
owner's session) is a deliberate pacing + phone frame-budget mechanism: everything over the cap is
held in the composition and released as reinforcements when a body dies. It therefore pins the
number of enemies **on the field** at the cap for almost the whole wave. That is working as designed
and is untouched by this ticket.

**The display.** `WaveProducer.Poll` (`Assets/_Modules/Village/HUD/HudModelProducers.cs`) built both
of the HUD's wave numbers out of `WaveManager.LiveEnemies` alone:

```csharp
total = enemies.Count;
for (...) if (enemies[i].IsAlive) live++;
```

so the counter published the capped field count — a constant — and the progress bar's numerator and
denominator were the *same list*, leaving the bar pinned at ~100% for every capped wave.
`HudKitController:4742` formats that value into `hud.hud_kit.enemies_remain` ("{0} enemies remain").

The honest number already existed, in two buckets the wave loop tracks separately:
`LiveEnemies.Count` + `_heldSmartReinforcements`. They are disjoint by construction — the held count
is recomputed in the same synchronous block that registers the released squad into the live list
(`WaveManager.cs` drain, `RegisterSmartSquad` -> `_heldSmartReinforcements = CountOf(deferred)`), so
`live + held` cannot double-count.

## The fix (display only)

| File | Change |
|---|---|
| `Assets/_Modules/Village/Waves/WaveManager.cs` | **+1 read-only accessor** `HeldReinforcements => _heldSmartReinforcements`, with the proving lines in its doc comment. No timing, no cap, no spawn path touched. |
| `Assets/_Modules/Core/HudModel/WaveCounterMath.cs` | **NEW.** `WaveCounterMath.Remaining(live, held)` + `WaveRosterTracker` (per-wave high-water mark). Pure arithmetic in Core so it is testable with no scene. |
| `Assets/_Modules/Core/HudModel/HudModels.cs` | `WaveModel.EnemiesLive` **renamed** `EnemiesRemaining` (it is no longer the field count — a name that lies is the next seat's bug); `EnemiesTotal` is now the wave ROSTER. Trace line updated. |
| `Assets/_Modules/Village/HUD/HudModelProducers.cs` | `WaveProducer` publishes `live + held`; roster total from the tracker; **`FlowTrace.Step` added after the change-gate** printing the SPLIT (`field L live + H HELD = R remaining of roster T`). Permanent. |
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | reads `EnemiesRemaining`; the progress bar's denominator is the roster, so the bar now drains. |
| `Assets/Editor/Regression/WaveCounterHonestyRegression.cs` | **NEW** oracle, 6 cases, driven by the owner's captured wave-26 sequence. |
| `Assets/Editor/Regression/DataRegression.cs` | +1 registration line (`wave-counter-honesty suite`). |

**Roster total is owned by the presentation layer, deliberately.** It is the progress bar's
denominator and nothing else, so it is a per-wave peak in `WaveProducer` rather than a fourth piece
of wave-loop state with four reset sites to keep in sync. The peak is established on the wave's first
observation (composition has released its first chunk and deferred the rest), which is exactly the
roster `WaveManager` traces as "total roster unchanged at 34" / "at 35".

## Acceptance criteria

1. During a capped wave the counter reads the TRUE remaining (`live + held`) and moves while the
   field is still full — 34 at the start of a wave-26-shaped wave, not 8. ✅ pinned by
   `[captured-sequence-moves]`.
2. The concurrency cap, spawn timing, side ladder and clear gate are byte-unchanged. ✅ only a
   read-only accessor was added to `WaveManager`.
3. The wave progress bar drains instead of sitting full. ✅ pinned by `[roster-is-the-denominator]`.
4. Instrumentation is permanent and names both buckets. ✅ `WaveProducer`'s `FlowTrace.Step`, gated
   behind the change-gate so it cannot flood the device logcat ring.
5. `python tools/gate_brace.py` clean on every `.cs` touched. ✅ `GATE_BRACE_SUMMARY bad=0 of 7`.

## What NOT to touch

- `_maxSimultaneousEnemies` / `SmartSpawnBudget` / `DrainSmartReinforcements` release cadence — the
  owner asked for an honest counter, not an easier wave.
- `WaveHudBridge` -> `VillageHudController.SetEnemyCount` — a **dead path**
  (`VillageHudController.cs:292` is an empty stub). Left alone; do not "fix" it into life.

## Known gap, reported not guessed

The apex dragon is **not** in `LiveEnemies` (it owns kinematic flight, tracked in `_liveApexBosses`)
and was not counted in this number before this change either, so an apex wave's counter still omits
the dragon(s). Out of scope here; needs an owner ruling on whether a boss belongs in a troop count.
