# WORK ORDER 1297 — World Clock and Pi Runtime Stability

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:20:16, build 2026.09.17.373943). PRIOR STATUS: FIXED — implemented by d2e0eb330 `fix(clock): find and instrument the 0.04 hit-stop leak` + 0a83928a1 `fix(pi): guard every localStorage call and add an escape from the landscape gate` (the Pi-orientation half landed with WO-1312, e6acd2935). Awaiting the owner's felt-verification (PO closes, CLAUDE.md §13). *(Board status audit 2026-09-02; body unchanged.)* *(Prior line:)* **Status:** IN PROGRESS — 2026-09-01

## Player reports

- Gameplay intermittently remains near `Time.timeScale = 0.1` after combat although the hit-stop leak was previously fixed.
- Pi Browser launches the game in portrait despite a landscape product requirement.
- During a suspected Pi CDN/SDK fetch, the game appears to reset.

## Root-cause direction

The hit-stop watchdog covers only `HitStopManager`; other cosmetic combat components still write the same engine-global clock and can be disabled while their restoring coroutine is running. Pi orientation and reload behavior require SDK/browser lifecycle evidence and must not change Windows, Android, or generic WebGL behavior.

## Patch constraints

- Cosmetic combat effects may not strand the global clock; every writer requires unscaled deadline, disable/destroy, and battle-end unwind—or is removed as a clock writer.
- Pi changes are guarded by Pi runtime/build detection.
- Pi authentication/CDN initialization is idempotent and does not reload the Unity page for recoverable SDK/network events.

## Acceptance

- Returning to the town after every battle path reports normal world time and responsive locomotion.
- Pi Browser receives landscape orientation requests after user activation and on visibility/orientation changes, with a readable fallback when the host refuses.
- Pi initialization/fetch retry preserves the running Unity instance and player state.
- Windows EXE, Android APK/Firebase release, and Pi WebGL/Vercel deployment are produced with evidence.

