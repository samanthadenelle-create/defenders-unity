# WO-1450: ProbeForStructure logs 320 lines a second with stack frames and destroys the device evidence window

**Status:** IMPLEMENTED - d6511b8e5 on HEAD 2026-09-09 (was READY); owner felt-test closes. See `WORK_ORDER_1450_enemy_aggro_probe_log_evicts_the_logcat_ring.RESULT.md`. PRIOR STATUS: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT
**Silo:** `Assets/_Modules/Village/AI/` (`Enemy`/`EnemyAggro` `ProbeForStructure`). Diagnostics only; no combat
behaviour changes.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1450 -> 1451 in the same edit).

## 1. EVIDENCE

Device log, 2026-09-06:

```
ProbeForStructure hit 'X' -> stopping agent to attack
```

38,018 occurrences between 12:59:05.857 and 14:37:52.894 - about 320 per second. Info level, emitted from
`Enemy:Update()`, each carrying a full managed stack trace. In the single second at 12:59:49 the log holds
12,305 Unity lines.

The Android main ring is 256 KiB. At that rate the boot window and every other trace are evicted in under two
seconds, so a post-hoc `adb logcat -d` reads as "the feature never ran" (memory
`logcat-ring-buffer-destroys-evidence`). This one line is why the troop-AI session below has no usable trace.

## 2. FIX SHAPE

- Convert the call to `FlowTrace.Throttle` (roughly 1/sec) or `FlowTrace.Once` keyed on the target, and drop
  the stack trace (`Debug.Log` without stack, or the FlowTrace path which already omits it).
- Keep the line. It is real instrumentation; the defect is the cadence and the stack frames, not the trace.

## 3. WHAT NOT TO DO
- Do NOT delete the trace (CLAUDE.md sec.12: instrumentation is permanent - throttle or flag it, never strip).
- Do not raise the logcat ring size as the fix; that hides the emitter and every other device session pays.

## 4. ACCEPTANCE
- [ ] A one-minute device capture in combat shows fewer than 100 `ProbeForStructure` lines and no stack frames.
- [ ] The trace still fires at least once per newly acquired target.
- [ ] `REGRESSION_OK n/n` on a fresh log.
