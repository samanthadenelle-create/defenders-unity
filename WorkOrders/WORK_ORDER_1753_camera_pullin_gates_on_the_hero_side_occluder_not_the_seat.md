# WORK ORDER 1753 — The camera pull-in is gated on the occluder nearest the HERO, so a raid wall 3.9 m in front of the seat yanks the camera to 1.2 m (a 3.75x zoom)

**Status:** READY TO IMPLEMENT — hold for the owner's felt test on the next tester build
**Minted:** 2026-09-15 by the lead, from WO-1751's arithmetic (its pass-2 hand-back). Not a defect report from the owner — this is a PREDICTED felt regression, minted before it can bite.
**Silo:** `Assets/_Modules/Village/Hero/SmartMobileCamera.cs` (`ApplyCollision` / the pull-in gate) + `Assets/Editor/Regression/CameraWallOcclusionRegression.cs`. Nothing else.

## Why this is minted now rather than fixed inside WO-1751
WO-1751 added the `Structure` layer (8) to the camera's collision mask, which is correct and necessary — 158 `Wall_*` colliders in a raid were invisible to the occlusion cast, so nothing faded and nothing pulled in. But the same change puts those walls into the **pull-in backstop** for the first time, and the backstop's threshold was never sized for them.

The lead's decision (owner delegated, 2026-09-15): **ship WO-1751 as it stands and felt-test it.** The fade now genuinely works — the wall is faded while any jolt happens, so the player never loses sight of the fight behind an opaque mesh, which is strictly better than today. The threshold change below is real but it is a FEEL question, and guessing a number for a colour-and-motion judgement is worse than one felt test.

## The arithmetic (WO-1751 pass 2, measured at source — not a guess)
- `nearestOccluderDist` comes from `SphereCast(pivot, ...)`, i.e. distance from the **hero's chest**, not the camera seat.
- `_followOffset = (0, 2.6, -4.5)`, `_lookAtHeight = 2.5` → `fullDist ≈ 4.5 m`.
- So the backstop fires when a wall is within 0.6 m of the **chest** = **≈3.9 m in front of the camera seat** — nowhere near embedding the camera.
- When it fires: `clamp(0.6 - 0.2, 1.2, 4.5)` = **1.2 m**, a **3.75x zoom-in**. At 1.2 m a small yaw is an enormous screen rotation.
- Raid corridors: `MaxSegmentWidth 3.0 m`, `MinGateWidth 3.5 m` → frequency goes from **zero** (walls were not in the mask) to **common**.

This is very likely the owner's 2026-09-14 report — *"the camera and targetting still pulls towards walls and since its tighter pathways makes camera spin and targetting very challenging"* — expressed as arithmetic.

## The fix WO-1751 proposed but deliberately did not apply
Gate on the occluder nearest the **SEAT**, not the hero:
- track `farthestOccluderDist = max(hitDist)` across the sweep's hits;
- gate on `(fullDist - farthestOccluderDist) < _occluderPullInDistance`.

⚠ **It must be the FARTHEST, not `nearestOccluderDist`.** That value is a *min*: with one occluder 0.5 m behind the hero and another at the seat, it yields 4.0, no pull-in fires, and the camera sits **inside** the seat-side wall. The existing 0.6 value stays correct under the new gate (sphere radius 0.35 + near clip 0.08 = 0.43 m needed; ~0.17 m margin).

## Acceptance
1. A wall 3.9 m in front of the seat no longer triggers the backstop; a wall AT the seat still does.
2. `CameraWallOcclusionRegression` gains a case pinning the farthest-vs-nearest choice, including the two-occluder arrangement above (a `nearestOccluderDist` implementation must FAIL it).
3. The owner felt-tests a raid corridor and reports no spin.
4. Lane flips this Status line and writes the `.RESULT.md`.

## Related, out of scope
WO-1751 also found the 16 `Clad_Corner_*` are parented to `Zone_Clad`, which carries **no collider** — so the eight ring corners have no occluder at all and can never fade or pull in. Separate ticket when someone reports it.
