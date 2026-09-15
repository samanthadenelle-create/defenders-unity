# WORK ORDER 1751 — Two raid visuals from the Seeker: a giant untextured grey box in the IronBastion courtyard, and the camera parked inside a watchtower with no occluder fade

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, from the owner's felt-test on the Seeker (tester build `2026.09.15.371127`, scene `RaidBase_IronBastion`).
**Silo:** (A) raid dressing — `RaidBaseDresser.cs` corner/keep/tower props and `MagentaGuard` (⚠ WO-1749's lane is inside `RaidBaseDresser.cs` at `:1715-1760` KeepPlatform/KeepRamp — do not touch that span; coordinate through the lead). (B) `Assets/_Modules/Village/Hero/SmartMobileCamera.cs` occluder path (`ApplyCollision`, `FadeOccluder`, restored by WO-1734).

## Evidence (captured, not theorised)
- **(A)** Owner screenshot `Screenshot_20260915-135355.png` (Thrain Lv15, 1:15 on the clock, Razed 22%): a flat, untextured light-grey box roughly wall-height and ~6 m long standing in the courtyard just inside the outer ring, with a troop clipping through it; a KayKit watchtower with its red targeting beam beside it. Nothing in the device log in the 13:52-13:54 window names an untextured or magenta-guarded object; `RuinStep` is ruled OUT — it is a bare `BoxCollider` with no renderer (`RaidBaseDresser.cs:890-898`).
- **(B)** Owner screenshot `Screenshot_20260915-134636.png`: the camera sits inside/behind a dark watchtower silhouette that fills the lower-right third of the frame while the hero fights in the smoke beyond. The WO-1734 occluder trace (`OCCLUDER PULL-IN ENTERED/RELEASED`, `OCCLUDER FADED`) emitted NOTHING in the 13:46:2x-3x window — the tower was not classified as an occluder at all, so no fade and no pull-in ran.

## What is NOT proven
(A) which object the grey box is (a dresser primitive that lost its material on device? a collapsed segment's `WallRuinPresenter` visual? an addressable prop whose bundle failed?). The lane proves it from the scene: enumerate every renderer under the IronBastion raid root whose material is the URP default/white and no albedo, and match by size (~wall height x ~6 m).
(B) why the tower is not an occluder: layer mask on the camera's occlusion cast, the tower's collider layer, or the cast origin. Cite `SmartMobileCamera.cs` lines.

## Ask
1. (A) Prove the object (headless: load `RaidBase_IronBastion`, list renderers with default/unassigned materials near the outer ring; or add a `FlowTrace.Once` in `MagentaGuard`/dresser that names any primitive left with the default material at scene load). Fix at source (material or exclusion) — never by hiding the renderer.
2. (B) Add the tower's layer to the occlusion mask or fix the classification, with the WO-1734 trace proving `OCCLUDER FADED` fires for `Watchtower_*` in a headless camera run; extend `CameraWallOcclusionRegression` to cover towers.

## Acceptance
- Headless capture PNG of the IronBastion courtyard shows no untextured primitive; the trace names zero default-material renderers.
- `OCCLUDER FADED` fires for a watchtower between camera and hero in the headless camera suite.
- Lane flips this Status line and writes the `.RESULT.md`.
