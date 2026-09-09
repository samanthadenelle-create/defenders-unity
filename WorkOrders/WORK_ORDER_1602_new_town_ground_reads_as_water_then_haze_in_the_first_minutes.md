# WORK ORDER 1602 - In the first minutes of a new game the town ground reads as blue-green WATER, then the scene sits under heavy haze with pale walls

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:07:41, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 evening gate (COMPILE_GATE_OK Builds/cg-wave11.log, REGRESSION_OK 456/456 Builds/reg-wave11.log 14:19); reaches the Seeker with the next tester build; owner felt-test closes it. (instrumentation only - the AtmosphereProbe timeline + writer traces land; no cause named yet, the fleet run reads it) PRIOR STATUS: READY TO IMPLEMENT (instrument first) - minted 2026-09-07 (CLI) from the owner's reset screenshots
**Silo / Lane:** Environment/terrain + atmosphere - `ExteriorTerrain` (`[Flow:FloorDiag] TERRAIN 'ExteriorTerrain' mat='ExteriorTerrainMaterial' shader=URP Terrain/Lit`), the terrain layer content (Assets/Generated/Terrain/Layers), day-night / fog writers (`RenderSettings`), `PP_GroundFog` VFX owners (dungeon portals)
**Type:** EXISTING system, VISUAL (transient) - needs data first
**Priority:** P2

## Evidence

`Screenshot_20260907-132930.png` (13:29, hero Lv2, wave 2): the entire ground is a shimmering blue-teal
like a sea; grass absent; wall and buildings normal. `Screenshot_20260907-133243.png` (13:32, Lv3): ground
green again but the whole scene sits under a dense pale haze, walls washed out.
`logs/f8-inbox/device/.../break_00_possible_softlock.png` (13:39): ground normal green, sky clear. Both
states are TRANSIENT inside the first ~10 minutes of a new game. Log: FloorDiag at 13:35 lists the
terrain and the Ponds (baseColor 0.18/0.38/0.42 a=0.82 - the same teal as the 13:29 ground); PP_GroundFog
owners for six dungeon portals are checked out / suspended in town. No `[Flow:Terrain]` layer-load trace
exists to say when the terrain layers arrived.

## What to do

- Instrument first: `FlowTrace.Step("Terrain", ...)` when the terrain material/layers bind (layer count,
  texture names, whether any is a placeholder); `FlowTrace.Step("Atmos", ...)` on every RenderSettings
  fog / fogDensity / skybox write naming the writer; a per-minute `Throttle` of fog density for the first
  5 minutes after a scene load.
- Reproduce headless from a fresh save (WO-1500's lane: `run-autopilot-fleet.ps1 -Lane freshsave-ftue`)
  with a screenshot at 30 s / 120 s / 300 s.
- Fix the named cause (candidates only: terrain layers streamed late so the terrain shows its base colour;
  a fog writer from the dungeon/portal or day-night system not reset on new game; PP_GroundFog volumes not
  cleared on reset). Pin with a capture regression on the first-minute frame.

## Acceptance
- Fresh-game frames at 30 s and 120 s show grass and a clear sky; the trace names the writer of every fog
  change. Owner felt-test closes.
