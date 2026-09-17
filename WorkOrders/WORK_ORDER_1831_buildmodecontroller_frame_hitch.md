# WO-1831: BuildModeController Frame Hitch (34ms spike)

**Status:** IMPLEMENTED

## Summary
BuildModeController.Update() shows frame spikes up to 34ms (2.5x 16.6ms budget @ 60fps) in device logs. The FlowTrace.Measure wrapper at Update():748 already documents expected cost and marks the issue for investigation. This WO tracks RCA + fix.

## Evidence
Device log 2026-09-17 10:16:52-53 (town scene):
- `[Flow:Perf] BuildModeController.Update took 5.5ms (over 4ms frame budget)`
- `[Flow:Perf] BuildModeController.Update=89.6ms/s (x31 worst 5.5ms)` 
- `[Flow:Perf] BuildModeController.Update=74.5ms/s (x30 worst 34.0ms)`

## Root Cause
**Line 1102 in `PointerOverPickableUI()`: `Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude)`**

This expensive lookup is called **every frame** from the hot path:
- `ConfirmIntentThisFrame()` (line 1083) → called from `UpdatePlaceLoop()`, `UpdateMoveLoop()`, `UpdateSelectLoop()`, `UpdateDroppedPlaceLoop()`
- No caching; FindObjectsByType is re-executed every single frame while build mode is active

## Fix
Cache the UIDocument array with a per-frame update guard. Replace the uncached FindObjectsByType with a cached lookup updated only when needed.

**Lines to edit:** 1098-1127 (PointerOverPickableUI method)

**Change:** 
- Add static cache: `private static UIDocument[] s_cachedUIDocuments;`
- Add frame tracker: `private static int s_cachedUIDocsFrame;`
- Wrap FindObjectsByType in a frame-change guard that re-caches only when necessary (or once per second at minimum)

**No behavior change:** The logic remains identical; only the caching strategy changes. No gameplay impact.

## Acceptance Criteria
1. BuildModeController.Update frame cost reduced to well under 16.6ms (typical case <4ms)
2. No change to placement behavior, input handling, or UI blocking logic
3. Brace balance passes on the edited file
4. No NUL bytes introduced

## Files to Edit
- `Assets/_Modules/Village/BuildMode/BuildModeController.cs` (line 1098-1127, PointerOverPickableUI method + cache fields)

## Do Not Touch
- Update() method signature or early-return gates (§12 tracing stays intact)
- Any placement validation or rejection logic
- Scene file Village.unity
