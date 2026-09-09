# WO-1582: Sheathed-Weapon Equip Trace Fills Logcat Ring (P2)

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:06, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT

Device logcat 2026-09-07 08:28-08:29: `[Flow:Equip] sheathed long axis on 'Hero (Blaise)': tiltFromVertical=0deg (must read ~0; ~90 means it is lying across the body) longAxisDotUp=1 ...` repeats every ~5 seconds (12 lines in one minute) with no value changes. This is a per-frame measurement inside `ApplyHoldPose` using `FlowTrace.Throttle(..., 5f, ...)` at `Assets/_Modules/Village/Hero/EquipmentController.cs:4047`.

## Root Cause

CLAUDE.md §12: the 3-arg Throttle form on a per-frame site floods logcat even with a 5-second throttle, evicting the boot window and masking real diagnostics on device (the 256 KiB ring fills too fast). Line 4044 comment confirms: *"Throttled: ApplyHoldPose re-asserts this every frame."* This measurement needs the 4-arg form.

## Solution

Choose one path:

**A (Preferred): 4-arg Measure** — Replace with `FlowTrace.Measure("Equip", "sheathe-rot-{main/off}-{name}", warnAboveMs: 4f, accumIntervalSec: 60f)`. Accumulates into a table, warns at most once per 60s, not every 5s. Only anomalous frames (> 4ms) log.

**B: FlowTrace.Once** — Emit only on equip change, keyed on `hero + weapon + result`. Fires when values move, never on repeated frames.

## Acceptance

- [ ] No more 5-second repeats on device logcat
- [ ] Boot window stays intact (trace no longer evicts)
- [ ] Value changes still logged (by whichever method chosen)
- [ ] Headless regression passes

## File to Edit

`Assets/_Modules/Village/Hero/EquipmentController.cs` lines 4047-4057. Do not change the rotation logic or strip the trace (§12: never strip instrumentation).
