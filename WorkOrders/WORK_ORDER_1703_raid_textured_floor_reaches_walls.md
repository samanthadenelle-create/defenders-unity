# WO-1703 - raid ground reaches the walls and carries texture

**Status:** READY TO IMPLEMENT - captured/source RCA first
**Minted:** 2026-09-10, direct owner request during READY clearance; banner 1702 -> 1704 includes WO-1702.

## Owner direction

"raids should have full floors to the walls some texture ground not just a solid color"

## Required outcome

All raid arenas have a continuous floor reaching their enclosing wall footprint,
with a visible appropriate existing ground texture instead of a flat color.
Keep collision, movement and placement consistent with the rendered ground.
Derive extent from the actual arena/wall authority, not a second hand-authored size.

## Method and acceptance

Inspect current raid capture/log evidence and builder/material paths first.
Reuse existing owned ground materials; do not replace approved walls or props.
Modify builders/runtime authority, never scene YAML. Verify representative raid
types with rendered views, floor-to-wall bounds and material/texture dependency
checks. Fresh compile/regression and relevant capture gates before check-in.
Record any test-build-only proof as Fixed under this session's owner ruling.

WO-1632/1633/1634/1635 received owner Pass in this session and stay closed;
this new ground requirement is a separate change.
