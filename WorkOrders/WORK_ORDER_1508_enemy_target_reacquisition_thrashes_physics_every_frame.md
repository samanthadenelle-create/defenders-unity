# WO-1508: enemy target re-acquisition thrashes physics every frame - all-layers OverlapSphere per enemy per frame

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT (headless trace FIRST))
**Silo:** `Assets/_Modules/Village/AI/Enemy.cs`. Sibling of WO-1450 - land them TOGETHER.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1508 -> 1509 in the same edit).

## 1. EVIDENCE

```
Enemy.cs:1812   probes whenever _currentTarget == null   (forward SphereCast + OverlapSphere)
Enemy.cs:2390-2406   mask = ~0    -- ALL layers
```

`raid-stuck-2026-09-06.log`, 13:01:53 to 13:04:16:

```
hit branch (:1821)        6,479 occurrences
throttled "probe-in"        194 occurrences
```

A target is being FOUND and then DROPPED, continuously, 33x more often than the throttled entry line admits.
Thirteen enemies doing that produced `fps=11`.

WHICH null assignment fires is UNPROVEN. The three candidates are `Enemy.cs:1791` (!IsAlive),
`Enemy.cs:1806` (drop-distance) and `Enemy.cs:3140`.

## 2. FIX SHAPE

- Instrument the THREE null-assignment sites and run headless. Read which one fires. No edit before that
  (CLAUDE.md sec.12).
- Then the fix is one of: hold the acquired target across the condition that is dropping it, or rate-limit the
  probe. The trace decides which.
- Narrow `mask = ~0` to the layers that can hold a target, regardless of outcome.

## 3. WHAT NOT TO DO
- **Do not land WO-1450's log throttle alone.** Throttling the probe log HIDES this signal - the 6,479 vs 194
  ratio is exactly what named the defect. Land the diagnosis with the throttle, or the next reader has neither
  the spam nor the evidence.

## 4. ACCEPTANCE
- [ ] The firing null path NAMED from a captured trace line, quoted in the RESULT.
- [ ] Probe rate measured before and after; `fps` from the same raid before and after.
- [ ] `mask` narrowed from `~0`.
- [ ] `REGRESSION_OK n/n` on a fresh log.
