# WO-1455: the offline sync queue has no cap and its depth warning only fires on exact multiples of 25

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Core/State/GameStateService.cs` (the offline sync queue).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1455 -> 1456 in the same edit).

## 1. EVIDENCE

```
GameStateService.cs:2747   enqueue - no bound on queue length
GameStateService.cs:2757   depth warning gated on an exact-multiple-of-25 test
```

The live device session reached a queue depth of 112 and emitted NO warning, because the enqueues that landed
on 25/50/75/100 were not the ones evaluated - an exact-multiple test only fires when the counter is sampled
on the exact value. Unbounded growth plus a warning that structurally misses is the worst pair: the memory
climbs and the log says nothing.

## 2. FIX SHAPE

- Warn once per CROSSING of the threshold (`depth >= threshold && !warned`), not on equality; reset the latch
  when the queue drains below it.
- Cap the queue with COALESCING: the queue carries full-state snapshots, so dropping an older entry loses
  nothing. `FlowTrace.Warn` on every coalesce so the drop is never silent.

## 3. WHAT NOT TO DO
- Do not cap by dropping the NEWEST entry; the newest snapshot is the current truth.

## 4. ACCEPTANCE
- [ ] Depth warning fires exactly once per crossing; regression drives depth 24 -> 26 -> 24 -> 26.
- [ ] Queue bounded, coalescing proven by a regression that enqueues 200 and asserts the cap and the trace.
- [ ] `REGRESSION_OK n/n` on a fresh log.
