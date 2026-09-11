# WO-1704 - continuous raid walls and connected inner gate assemblies

**Status:** READY TO IMPLEMENT - RCA handed to CLI, measured regression required
**Minted:** 2026-09-10, direct owner phone playtest; main-line banner 1704 -> 1705.
**Silo:** Raid geometry; separate from WO-1703 floor/material work.

## Owner finding and purpose

"holes in the exterior wall, the inner keep isnt connected to the gate"

"i want the raids to feel really polished as i feel its the way to get to arena competitions"

Primary evidence: `Builds/device-frames/owner-20260910/Screenshot_20260910-202900.png`,
pulled from the connected phone and opened by root and RCA. Visible exterior sections
have gaps. The keep connection is owner-observed; this frame does not expose its full plan.

## RCA handoff (read-only agent -> root CLI)

`Assets/Editor/WallTools/RaidBaseDresser.cs:518` fits wall pieces to `step * 0.98f`,
deliberately leaving seams. At :153-159 it clads an inner north-gated ring but only
places gatehouses on the outer ring. `PlaceGatehouse` places gate/flanks without a
measured adjacency postcondition. Root re-read these methods. Exact gate side-gap
width remains unmeasured; do not report a predicted width as captured fact.

## Bounded correction and proof

Use actual ring and gate geometry to join wall segments and gate assemblies with
existing approved modules. Place the missing inner north gate assembly. Preserve
the intended crossing between opposite outer/inner gates; do not invent connecting
walls through the kill zone. Maintain the measured usable gate opening, navigation,
colliders, destructible behavior and dressing idempotence. Broken decorative modules
must not substitute for a continuous enclosing wall where the owner expects closure.

First capture a regression failure against current placement. Verify measured
segment/gate adjacency and inner assembly presence, bake representative raid types,
check navigable gate-to-keep paths, and inspect four-side and gate screenshots.
Record delivered work as Fixed pending owner's test build, never self-close it.

Fence: RaidBaseDresser and meaningful geometry regression; RaidBaseGenerator layout
report only if needed to share the actual ring authority. Root alone runs Unity,
regenerates assets and commits. Preserve owner-closed WO-1632/1633/1634/1635.
