# WO-1868 RESULT — CLAIM (bounce redirect + arena rim)

**Status of claim:** IMPLEMENTED PENDING LEAD GATE — not FIXED.  
**Date:** 2026-09-19  
**Lane:** SME implement (grok-4.5). **No commit. No Unity compile gate. No bake. No APK. No BOARD.html.**

## Bounce named (proved)

Device frame `Logs/device/raid-fog-203029.png`: two grey puffs under a **clear blue sky**, no ground fog. Prior impl put 4 thin `PP_GroundFog` patches at `baseRadius*0.55` and 2 `PP_LightnigStormCloud` at 24 m; VFXManager off-camera Suspend (~6 s grace) killed fog loops. Rim still shipped `Dungeon_Pillar_Stone_Square` + `Dungeon_Wall_Stone` backing on empty-albedo `M_21_Grey_Light_LPUP` (WO-1758).

## What changed (claim)

### 1. Raid sky darkened (raid-only)
- `RaidGarrisonSpawner.ApplyRaidSky` / `RestoreRaidSky`: camera `SolidColor` storm tint + flat ambient drop; saved/restored in `OnDestroy` so hub is not left dirty. No skybox write. WeatherManager untouched.

### 2. Dense yard fog + sky cloud layer; Suspend exempt
- Fog: centre + inner + outer rings (9 patches), larger scale (~10–18), denser blue-grey tint, across the whole yard.
- Storm: 6 large `PP_LightnigStormCloud` at ~48 m spanning the sky (layer + lightning flash), not two puffs.
- `VFXManager.PlayKey(..., visibilityExempt: true)` + `LoopRecord.VisibilityExempt` + `VfxLoopReleasePolicy.Decide/ShouldHaveReleased(visibilityExempt)` — **off-camera Suspend skipped**; owner-destroy still releases. Accessibility allowlist unchanged.

### 3. Arena exterior rim (grey box)
- `ArenaBoundaryRing.RockPaths` → tracked `Assets/Resources/Arena/Rock_*_Color1.fbx` (KayKit / `forest_texture_URP`). Absolute paths via `ResolvePrefabPath`.
- Removed `Dungeon_Pillar_Stone_Square` / Round from palette.
- `RaidBaseGenerator` boundary backing arg → `null` (no more `Dungeon_Wall_Stone` continuous grey panel).
- **Requires lead OwnedTownChain / Bastion bake** before the scene on disk shows the new rim (no `.unity` hand-edit; no bake from this lane).

### 4. Regressions
- New `RaidAtmosphereFxRegression` (+ DataRegression registration).
- `VfxLoopFlagRegression`: visibilityExempt Keep / owner-destroy Suspend cases.
- `RaidArenaShapeRegression`: RockPaths source lint bans grey pillar / requires Arena rocks.

## Files touched

| Path | Role |
|---|---|
| `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs` | atmosphere redirect + raid sky |
| `Assets/_Modules/Village/Vfx/VFXManager.cs` | VisibilityExempt + policy |
| `Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs` | PlayKey visibilityExempt |
| `Assets/Editor/ArenaBoundaryRing.cs` | RockPaths + ResolvePrefabPath |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | drop Wall_Stone backing |
| `Assets/Editor/Regression/RaidAtmosphereFxRegression.cs` | new |
| `Assets/Editor/Regression/RaidArenaShapeRegression.cs` | RockPaths lint |
| `Assets/Editor/Regression/VfxLoopFlagRegression.cs` | policy cases |
| `Assets/Editor/Regression/DataRegression.cs` | register suite |

## Gates this lane ran

- `python tools/gate_brace.py` on all 9 touched `.cs` → `GATE_BRACE_SUMMARY bad=0 of 9`
- NUL scan clean on the same set
- **NOT run:** Unity `COMPILE_GATE_OK`, `REGRESSION_OK`, bake, APK, BOARD.html (per dispatch)

## Lead must do before FIXED

1. Batch `COMPILE_GATE_OK` + `REGRESSION_OK` on a fresh log (includes `[raid-atmosphere-fx]`).
2. **Bake** Bastion/raid via sanctioned OwnedTownChain (rim is bake-time).
3. **OPEN a raid PNG** showing: darkened storm sky (not clear blue), dense ground fog across the yard, textured rock rim (not grey boxes), and ideally a tower arrow in flight.
4. Only then flip past IMPLEMENTED / commit by explicit path.

## Unproven (stated)

- Live device/editor frame of the new look — this lane did not capture one.
- KayKit rock footprints vs band-fit on a real bake — MeasureFootprints runs at bake; designed-fit suite uses regime constants only (EditorRegression cannot reference DeNelle.Editor).
