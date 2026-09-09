# WO-1483: the EMPTY Overworld runs at 22 fps with zero towers and zero enemies

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:53, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT (instrument first; NO fix before the data names the cost)
**Silo:** Perf. Sibling of WO-1459 (raid frame floor); this one removes gameplay as a variable.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1483 -> 1484 in the same edit).

## 1. EVIDENCE

```
raid-no-abilities-2026-09-06.log:35005
  12:50:18.594 LOW fps=22 ms=45.1 mem=415MB scene=Main_Castle_Overworld towers=0 enemies=0
```

Nothing is spawned. Forty-five milliseconds a frame with an empty town is a floor cost, and it bounds
everything else: no gameplay optimisation can raise the ceiling above this.

There is nothing to read, because there is nothing measuring:

```
FlowTrace.Measure(   -- only 5 sites repo-wide, NONE on the frame path
```

## 2. FIX SHAPE

- Add `FlowTrace.Measure` scopes to the town frame path FIRST - rendering submit, HUD rebuild, world tick,
  navmesh, VFX pool - with `warnAboveMs` set so the dominant cost announces itself.
- Run one headless and one device capture of the empty town.
- Only then fix the cost the data names. No edit to any suspected system before that (CLAUDE.md sec.12).

## 3. WHAT NOT TO DO
- Do not drop quality settings, shadow distance, or draw distance to raise the number. That trades the look
  for a metric without learning anything.
- Do not assume it is the same cause as WO-1459. Empty town and populated raid may be two different costs.

## 4. ACCEPTANCE
- [ ] Measure scopes present on the frame path; the trace lines pasted in the RESULT.
- [ ] The dominant cost NAMED with its measured ms.
- [ ] A fix targeting it, with before/after fps in the empty town.
- [ ] `REGRESSION_OK n/n` on a fresh log.
