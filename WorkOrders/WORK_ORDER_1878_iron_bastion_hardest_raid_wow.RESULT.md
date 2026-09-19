# WO-1878 RESULT — CLAIM (not lead-verified)

**Status of claim:** IMPLEMENTED PENDING LEAD GATE  
**Agent:** Grok Build subagent (implement-only; no commit, no Unity compile gate, no APK, no BOARD.html)  
**Date:** 2026-09-19

## Scene identity (proven this session)

Opened `Assets/Scenes/RaidBase_IronBastion.unity`:
- `configId: iron_bastion` present
- root `m_Name: RaidBase_iron_bastion` present
- `configId: fortified_garrison` **absent**

So the on-disk scene is Bastion, not garrison leftovers. The Seeker PNG dress still matched garrison wood because Bastion authored **no `raidDress`** and `towers[]` was spire-only — that is what this ticket fixes in data + generator. **The baked scene on disk is still the pre-fix bake** (10× `CatalogId: tower_arcane_spire`, 107× `Wall_Outer_*`) until lead rebakes.

## What changed (tree evidence)

### A. Config twins (dual-write)

`Assets/Resources/Data/Canonical/scene-configs.json` + `Assets/StreamingAssets/Data/Canonical/scene-configs.json` — `iron_bastion`:
- `towers[]` → `tower_ground_archer`×7 + `tower_arcane_spire`×3 (palette weights; placed totals stay 7+3)
- `outerEnclosure: "Landscape"`
- authored `raidDress` kit `dungeon-stone` with cover props (not empty, not `synty-castle`)
- `towerPlacementStyle` remains `OverlappingFire`

### B. Generator / dresser

`Assets/Editor/WallTools/RaidBaseGenerator.cs`:
- `ResolveTowerTypes(def, TurretRole, fallback)` — archer and mage slots get **disjoint** palettes (`MatchesTurretRole` / `IsMageTowerType`). Stops feeding both slots the same `towers[]` list.
- Extreme turrets use `VisualPathForLevel` at `clamp(repo.maxLevel, 1, RepoProps.MaxStructureLevel)` and stamp `PlacedStructure.level` so `DefenseTower.Tier` can pick max archer VFX.
- Landscape outer: skip `BuildRing(..., "Outer")`; synthetic `RingReport`; still builds `ArenaBoundaryRing`.

`Assets/Editor/WallTools/RaidBaseDresser.cs`:
- `LayoutContext.LandscapeOuter` — skip Outer clad + outer gatehouses when Landscape.

`Assets/_Modules/Village/World/SceneConfigCatalog.cs`:
- `outerEnclosure` + `UsesLandscapeOuterEnclosure`.

### C. Forced encounters

No greenfield AI. Runtime path already seats `GarrisonSlot_*` and `SetDefendPost` (`RaidGarrisonSpawner`). Bastion scene already has Gate/Yard/Keep slots (19). **Fight frames (bodies engaged, not HUD Troops 0/0) are NOT proven here** — need post-bake play / headless capture by lead. Troops 0/0 on the PNG is the player army HUD, not garrison census.

### D. Regression

New `Assets/Editor/Regression/IronBastionHardestRegression.cs` registered in `DataRegression.RunAll` as `[iron-bastion-hardest]`. Pins palette, role-split source, Landscape outer, dress, twin parity, and simulated TypeSummary `7x tower_ground_archer, 3x tower_arcane_spire`. Spire-only `towers[]` RED.

`RaidBaseLayoutRegression` kit fallback for Bastion updated to `dungeon-stone`.

## Explicitly NOT done (lead)

- **No Unity bake** of `RaidBase_IronBastion` (user forbade compile gate; bake not run). Scene YAML still pre-fix.
- **No nav bake.**
- **No COMPILE_GATE_OK / REGRESSION_OK** on a fresh Unity log.
- **No device/editor PNGs** of max-tier archer+wizard fire, landscape enclosure, or yard fight.
- Fog/storm left to **WO-1868** (no fake storm puffs).
- No hero mesh-swap; no hub skybox leak.
- No commit / no APK / no `BOARD.html`.

## Brace + NUL

```
python tools/gate_brace.py <6 touched .cs>
GATE_BRACE_SUMMARY bad=0 of 6
NUL clean on all six
```

## Acceptance mapping

| Criterion | Claim |
|---|---|
| 7 archer + 3 wizard from real types | Config + role-split generator; simulated TypeSummary 7+3. **Bake needed to land in scene.** |
| Landscape enclosure, not targetable Wall_Outer | `outerEnclosure: Landscape` + generator/dresser skip Outer. **Bake needed to remove existing Wall_Outer_*.** |
| Forced overlapping fire | Style already OverlappingFire; role-split makes crossfire real types. Fight frame unproven. |
| Wow VFX | Max-tier mesh + PlacedStructure.level for Extreme. Projectile style polish stays WO-1868. |
| Fog/storm | Out of scope (1868). |

## Global bake note (WO Not-in-scope carve-out)

`ResolveTowerTypes` role-split is **global**, not Bastion-only. Any camp whose `towers[]` was mage-only (notably `mage_enclave`) previously fed that list to **both** palettes; archer slots now fall back to `tower_ground_archer`. Lead should rebake **all** generated `RaidBase_*` (or at least Bastion + Enclave) after this lands — matching the WO line "unless Bastion's generator change is global (then say so and bake all)."

## Lead next steps

1. Unity editor closed → rebuild `RaidBase_IronBastion` (and other RaidBase_* affected by the global palette fix) via sanctioned raid bake path + nav bake.
2. Confirm Bastion bake log TypeSummary contains both `tower_ground_archer` and `tower_arcane_spire`; Outer WallSegment count 0; ArenaBoundary present.
3. Run CompileGate + DataRegression; require `COMPILE_GATE_OK` + `REGRESSION_OK` + `IRON_BASTION_HARDEST_OK` on a **fresh** log.
4. Capture frames: scene name `RaidBase_IronBastion`, max-tier archer shot, wizard shot, landscape rim (no grey outer box), garrison engaged.
5. Owner felt-verify closes.
