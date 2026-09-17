# WO-1829 RESULT: Dungeon Doors Invisible to Hero LoS Gate

**Status:** IMPLEMENTED

## Root Cause
`CommonDungeonDoor.BuildLeafAsset()` and `BuildFallbackLeaf()` never assigned the door leaf GameObject to the Structure layer. Hero targeting's LoS linecast (`HeroTargetIndicator.HasLoS()`, line 1550) is masked to Structure layer only, so Default-layer doors were invisible to the linecast. Result: closed doors never blocked target acquisition.

## Fix Applied

### 1. CommonDungeonDoor.cs layer assignment (two locations)

**BuildLeafAsset (lines 262-275):** After the BoxCollider is added to the leaf (line 256), added Structure layer assignment with guard + FlowTrace:
```csharp
int structureLayer = LayerMask.NameToLayer("Structure");
if (structureLayer >= 0)
{
    leaf.layer = structureLayer;
    FlowTrace.Step("DungeonDoor", $"door leaf '{leaf.name}' assigned to Structure layer (WO-1829 LoS gate).");
}
else
{
    FlowTrace.Warn("DungeonDoor", $"door leaf '{leaf.name}' could not resolve Structure layer — door will not block LoS (WO-1829).");
}
```

**BuildFallbackLeaf (lines 330-343):** Identical pattern before `return root;`

Pattern mirrors `WallSegment.cs:646-647` exactly: check for >= 0, assign only if valid, degrade gracefully with FlowTrace warning.

### 2. Regression Test Enhancement
Extended `DungeonDoorShapeRegression.Run()` (lines 137-146) to assert the closed door leaf is on Structure layer:
```csharp
int structureLayer = LayerMask.NameToLayer("Structure");
if (closed.Leaf != null && structureLayer >= 0)
{
    if (closed.Leaf.layer != structureLayer)
        failures.Add($"leaf is on layer {closed.Leaf.layer} ('{LayerMask.LayerToName(closed.Leaf.layer)}'), " +
                     $"expected Structure layer {structureLayer} (WO-1829 LoS gate)");
}
```

Regression already registered in `DataRegression.cs:595` (no additional registration needed).

## Verification
- CommonDungeonDoor.cs: 73 braces, balanced ✓; no NUL bytes ✓
- DungeonDoorShapeRegression.cs: 59 braces, balanced ✓; no NUL bytes ✓
- Both layer checks guard on `LayerMask.NameToLayer() >= 0` (same guard as WallSegment)
- FlowTrace instrumentation: Step on success, Warn on degradation
- Regression integrated into existing DungeonDoorShapeRegression (no new file)

## Cannot Verify Without Unity
- Runtime test: closed door linecast blocked, open door linecast passes
- F8 triage on device: hero cannot target enemies through closed Ember Deep doors

## Files Changed
1. `Assets/_Modules/Dungeons/RoomForge/CommonDungeonDoor.cs` — layer assignment in both leaf builders
2. `Assets/Editor/Regression/DungeonDoorShapeRegression.cs` — layer validation in oracle

## Next Step
Run Unity gate (`COMPILE_GATE_OK`) and headless regression (`REGRESSION_OK`) to confirm the layer check pins.
