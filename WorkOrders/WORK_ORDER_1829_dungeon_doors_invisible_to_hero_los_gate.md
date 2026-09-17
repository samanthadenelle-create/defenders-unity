# WO-1829: Dungeon Doors Invisible to Hero LoS Gate

**Status:** IMPLEMENTED

## Problem
F8 flag: hero can target enemies through closed dungeon doors in Ember Deep. The reticle locks onto enemies on the far side of a closed door as if the door doesn't exist for line-of-sight checks.

Device log: `[Flow:Reticle] AUTO PICK 'OutpostEnemy (troll)' WHY=unit-over-wall (WO-1734 priority gate: a hostile UNIT was acquirable).` while standing at a closed orange door with the reticle actively locking onto a Cave Troll visible in the corridor beyond.

## Root Cause (verified at source)
- `HeroTargetIndicator.cs:87` defines `_losMask` (layers that BLOCK line-of-sight)
- `:488` seeds `_losMask = LayerMask.GetMask("Structure")` if unset
- `:1543-1563` `HasLoS()` does `Physics.Linecast(eye, torso, out hit, _losMask, QueryTriggerInteraction.Ignore)`
- `:965` and `:984` in `RebuildCandidates()` call `HasLoS(d)` to filter every hostile candidate
- `CommonDungeonDoor.cs:256` creates the blocker collider with `leaf.AddComponent<BoxCollider>()` but NOWHERE in this file (or its callers `ComposedLockedPort.cs`, `ComposedPropVisuals.cs`) is `leaf.layer` or `blocker.layer` set
- Door GameObjects inherit Default layer (layer 0), NOT Structure layer
- Linecast with `_losMask` (Structure only) cannot hit a Default-layer door, so LoS always returns true

## Solution
Assign the door leaf's GameObject to the Structure layer at construction, matching how `WallSegment.cs:646-647` sets its layer:
```csharp
int structureLayer = LayerMask.NameToLayer("Structure");
if (structureLayer >= 0) gameObject.layer = structureLayer;
```

Add this immediately after the BoxCollider is created (`:256`), with FlowTrace instrumentation.

## Acceptance Criteria
1. Door blocker collider's GameObject is assigned to Structure layer at build time
2. Regression test asserts door blocker is on Structure layer in closed state
3. Regression registered in `DataRegression.Run()`
4. FlowTrace Step/Warn when layer is set (or degrades due to missing layer)
5. Braces balanced, no NUL bytes, all files compile

## Parallel Lanes
None—self-contained fix to CommonDungeonDoor + new regression test.

## Do NOT Touch
- CI_LANES_WO_NUMBERS.md (pre-assigned)
- Any scene files
- HeroTargetIndicator.cs beyond reading
