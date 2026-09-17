# WO-1814: "LEVEL UP! Lv.8" feedback text spans entire screen

**Status:** IMPLEMENTED

## Issue

When the hero reaches level 8 (fireball kill on Troll, wave 13, 2026-09-16 20:51:30):
- Feedback text "LEVEL UP!  Lv.8" is rendered as enormous translucent green letters
- Text appears to span the full screen width (~2000 px at the captured resolution)
- Rendered behind the action bar HUD but in front of the world
- Device log: `[Flow:Feedback] text label spawned 'LEVEL UP!  Lv.8'` at 20:51:30.479

## Root Cause (PROVEN 2026-09-16, second pass — supersedes the theory below)

Measured, not inferred. `DamageNumberSpawner` builds a world-space `TextMesh` at
`characterSize 0.11 * fontSize 96 / 10 * scale 1.4` = **1.478 m of world em height**
(`ProgressionManager.cs:152-153` asks for 1.4; `Acquire` parents it to nothing, so no
inherited scale). The town over-the-shoulder camera logs a seat **3.3-4.7 m** from the hero
(`[Flow:Camera] seat 3.9m of 3.9m`, 20:50:40) at **vfov 60** (logcat `fov=60.0`). At 3.5 m on a
1200 px frame that is `1.478 * 1039.2 / 3.5` = **439 px em / ~314 px cap**. The PNG measures
**318 px of cap** (green ink occupies rows y=160..478). The code did exactly what it said.

**The wrong term is the one that is absent: nothing compensates for camera distance.** A label
sized in metres is enormous when it is attached to the closest object in the scene.

~~Original theory: "scale/size driven by a distance factor ... with no maximum clamp."~~
There was no distance factor at all, and a max clamp does not fix it: clamping 1.4 -> 1.0 still
leaves 1.056 m = 19% of screen height and a string 1.3x wider than the frame. That first-pass fix
was reverted. Full derivation in the RESULT.

## Files to examine

- `Assets/_Modules` — grep `"text label spawned"` to find spawn site
- The spawned GameObject's TextMesh / TextMeshPro component
- Scale computation logic (distance-based size, camera distance, canvas scaler)
- The feedback system that routes level-up signals

## Acceptance Criteria

1. Locate the exact code that logs `[Flow:Feedback] text label spawned`
2. Trace how the TextMesh scale is computed (world-space transform, size property, canvas distance factor)
3. Add a `max(computed_size, ceiling)` clamp to keep the label readable but not oversized
4. Keep FlowTrace in the code; log the final computed size in the trace message: `"text label spawned 'LEVEL UP!  Lv.8' size=<computed> (clamped)"`
5. Verify no regression in other feedback labels (damage numbers, xp, etc.)
6. Create a small regression test case in Assets/Editor/Regression/ if the feedback label system has no existing suite; otherwise add to an existing one
7. Verify braces in all edited .cs files

## Do NOT touch

- DataRegression.cs — **EXCEPT the single registration line** for the new suite
  (lead's explicit allowance, 2026-09-16); nothing else in that file was edited
- Any .unity scene files
- CastleHubBuilder.cs, WallTools/*.cs, ArmyMuster* files, CLI_LANES_WO_NUMBERS.md

## Notes

- The label should be readable at distance but not fill the screen
- World-space text can vary with camera zoom and distance — the clamp protects against extreme scales
- The FlowTrace.Once pattern used elsewhere in diagnostics may be appropriate if the label is spawned multiple times per session

