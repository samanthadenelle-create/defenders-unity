# WO-1868 — Raid arena: tower shot VFX, ground fog, storming clouds

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/cg-1868-1884.log`) + `REGRESSION_OK` (`Builds/r-1885.log`). Does not go FIXED until lead opens a raid PNG (storm sky + dense fog + textured rim). — 2026-09-19 bounce redirect + grey rim. SME claim in `WorkOrders/WORK_ORDER_1868_raid_tower_shot_vfx_ground_fog_storm_clouds.RESULT.md`. ⛔ Lead must OPEN a raid PNG (storm sky + yard fog + textured rim) before FIXED — markers alone are not enough (bounce lesson). No commit / no Unity compile gate / no bake from this lane. PRIOR: READY TO IMPLEMENT — BOUNCED 2026-09-18 20:40 by `Logs/device/raid-fog-203029.png`: two grey puffs under clear blue sky, no ground fog (pool-SUSPENDED ~10s). PRIOR: FIXED on green markers without opening a frame (process defect).

## Owner request (verbatim, three messages)

> "can we have the towers in raids shoot more than yellow pellets? something with vfx? and add a
> fog to the ground?"
> "maybe storming clouds overhead"

Pure visual/atmosphere polish for the raid arena. No balance, damage, or gameplay-timing change —
this is presentation only.

## Scope

1. **Tower shot VFX.** Find the raid defender tower's current attack-projectile VFX (search for
   the "yellow pellet" visual — likely a plain particle/sprite in the tower's attack script or a
   VFX-catalog entry the towers reference). Replace it with something more visually distinct per
   this project's existing VFX conventions — read `docs/*VFX*` or the existing VFXManager/catalog
   pattern other structures/abilities already use (e.g. how hero spell-cast VFX are wired) before
   inventing a new pattern. The owner is colorblind and delegates the exact visual pick to
   CLI/agent judgment (do not ask her to choose a color) — pick something that reads clearly as
   "tower is firing" against the raid arena's existing palette, distinct in SHAPE/motion as well as
   color so a colorblind player can tell it apart from other effects.
2. **Ground fog.** Add a fog layer to the raid arena ground — check whether this project already
   has a fog/atmosphere system used elsewhere (Unity's built-in RenderSettings.fog, a shader-based
   ground fog prefab, or a VFX Graph fog volume) before building a new one from scratch. Should read
   as low-lying ground fog, not a full-screen haze that hurts readability of enemies/towers/UI.
3. **Storming clouds overhead.** Add a sky/cloud treatment overhead in the raid arena scene(s) that
   reads as stormy — check for an existing skybox/cloud system (a cloud shader, a skybox material,
   a weather/atmosphere prefab) before building new. Keep this a raid-arena-only atmosphere change
   unless there's already a clean per-scene skybox seam — do NOT change the peaceful town's sky.

## What NOT to touch

- Tower damage, range, fire rate, or targeting logic — VFX/presentation only.
- The peaceful town/hub scene's lighting or sky — this is raid-arena-scoped.
- Any raid balance/economy numbers.

## Acceptance criteria

- [ ] Brace/NUL gate clean on every `.cs` touched.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] A device/headless capture (or in-editor screenshot if headless can't render raid VFX per this
  project's known `-nographics` limitation) showing: the new tower shot VFX firing, ground fog
  visible in the raid arena, and stormy clouds overhead.
- [ ] Enemies, towers, and HUD readouts remain legible through the fog — this is atmosphere, not an
  obstruction. If the fog measurably hurts readability, dial it back and say so rather than ship it
  as authored.
- [ ] Owner felt-verifies the look on device — flag the exact VFX/fog/cloud choices as a first pass
  she can redirect (per this project's own colorblind-delegation convention).

## 2026-09-18 SME lane implementation notes

**Files touched:**
- `Assets/_Modules/Village/World/Camps/GarrisonTurretArmer.cs` — the SHARED "arm the
  Watchtower_* props" scan (used by both `GarrisonController` and `RaidGarrisonSpawner`).
  Root cause (proved from source, not guessed): an EnemyOwned garrison turret never carries a
  `structures-catalog.json` row, so `DefenseTower.ProjectileStyle` stayed at its unset default
  (`""` -> `BoltStyle.Pellet`) — the legacy 0.4m emissive sphere (`BuildPelletVisual`), tinted by
  `BoltColor = (0.95, 0.3, 0.2)` (a red-orange the owner, colorblind, reads as "yellow"). Fix:
  `dt.ProjectileStyle = "bolt"`, one line, reusing the SAME arrow-shaft-and-tip primitive
  (`BuildBoltVisual`) + per-tier Hovl arrow catalog key every player Archer/Ballista tower
  already uses — no new VFX asset, no new pattern. `TowerProjectileTierTests` already documents
  a no-`PlacedStructure` tower (exactly this shape) as Tier 1, so this does not fight that test.
  Added a `FlowTrace.Step` naming the style change.
- `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs` — the runtime controller for the
  config-driven `RaidBase_<id>.unity` scenes (confirmed the CURRENT live raid path from a real
  capture, `Logs/device/raid-window-1955.txt`: `RAID START config='fortified_garrison' ...
  scene='RaidBase_fortified_garrison'`). Added `SpawnAtmosphereFx(baseRadius)`, called once after
  `ArmWatchtowers` in `ActivateRoutine`:
  - Ground fog: 4 patches of the existing pooled Hovl catalog key `PP_GroundFog` (already proven
    live as ground mist on the dungeon world portals, `DungeonWorldPortalSpawner.AttachGateVfx`),
    NavMesh-snapped, ring at `baseRadius * 0.55`, scale clamped 3-6.
  - Storm clouds: 2 instances of `PP_LightnigStormCloud`, an already-imported, already
    normalized (`VFXManager.Hovl.cs` vendor-material fix) Hovl prefab with **zero prior callers**
    anywhere in the tree — an unused, ready-made asset, not a new skybox/cloud shader. Placed at
    24m height, scale clamped 8-16.
  - Both are pooled `VFXManager.PlayKey` loops (particle-system world geometry, not a
    `RenderSettings.skybox`/`fog` write), parented under a `[RaidAtmosphereFx]` anchor that is a
    CHILD of this raid's own root — so they render regardless of this scene's camera clear-flags
    and can never leak into the peaceful hub's `RenderSettings` the way a skybox/fog write on an
    additively-loaded scene could. Handles are tracked and explicitly `Stop(true)` in `OnDestroy`.
  - Deliberately did NOT touch `RaidBaseDresser`'s baked per-camp `RenderSettings.fog`
    (`ConfigureAtmosphere`) — that is edit-time, ruled camp identity (WO-1637), and re-baking
    every `RaidBase_*` is out of this lane's scope (memory: never `BuildAllRaidScenes` alone).
    Also deliberately did NOT touch `WeatherManager` — it is DORMANT BY OWNER DECISION, reserved
    for the Realm Map zones (WO-992), and its own header forbids speculative wiring.

**Scope note (not creep):** `GarrisonTurretArmer` is shared by `RaidGarrisonSpawner` (RaidBase_*
config-driven raids) AND `GarrisonController` (Village2 + open-world `Garrison_*` camps), so the
tower-VFX fix applies to every EnemyOwned watchtower project-wide — correct, since the "yellow
pellet" is the same unset-style bug everywhere it fires, not raid-specific code. The ground-fog
and storm-cloud additions are scoped ONLY to `RaidGarrisonSpawner` (RaidBase_* scenes) — `Village2`
was deliberately left untouched pending a scope call, since it already gets a different, ruled
atmosphere pass (`WorldFeelInjector`'s warm dusk look, `OutdoorScenes` allowlist) that a storm
tint could visually fight; flagging this for the lead/owner rather than silently extending it.

**Unproven, stated as such (§11B):** the "yellow = colorblind read of a red-orange sphere" theory
is the most consistent explanation of the code (BoltColor is objectively red-orange, not yellow,
and pellet-style is objectively what fires with no catalog row) but is NOT directly confirmed by
a captured trace of an armed watchtower actually shooting — the two raid logs on disk
(`Logs/device/raid-window-1955.txt`, `Logs/device/raid-trace-20260918-080200.txt`) show the
turrets being armed and layered but no `[Flow:DefenseTower]` fire-time trace or `hovl-play:` line
for `ArcherTower_Projectile`/`ArcherTowerLevel1_Projectile` in the captured windows. The primitive
sphere->shaft swap is unconditional (Guard-wrapped, always builds) so it fixes the shape/motion
regardless of whether the Hovl overlay was drawing; the Hovl key change (base "ArcherTower_
Projectile" -> tier-1 "ArcherTowerLevel1_Projectile") is presentation-only either way.

**Verification status:**
- `python tools/gate_brace.py Assets/_Modules/Village/World/Camps/GarrisonTurretArmer.cs
  Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs` -> `GATE_BRACE_SUMMARY bad=0 of 2`.
- NOT run by this lane (per dispatch instructions — the lead batches this with other lanes):
  Unity `COMPILE_GATE_OK`, `REGRESSION_OK`, any capture/screenshot.
- **What a capture needs to show to close this ticket:**
  1. Tower VFX: an armed `Watchtower_*` firing at the hero/party in a `RaidBase_*` raid — the
     projectile should read as an elongated arrow silhouette (not a round dot) that reorients
     along its flight path. A device/editor Play-mode capture is required — headless
     (`-nographics`) renders no particles/visible geometry per this project's known limitation
     (`VfxProofCapture.cs` is the in-editor equivalent capture path if a live device isn't handy).
  2. Ground fog: low mist patches visible pooling on the arena floor near the wall band,
     distinguishable from the existing distance haze, without obscuring enemies/towers/HUD.
  3. Storm clouds: 2 dark grey cloud volumes visible overhead from ground level inside the raid
     perimeter (`RaidGarrisonSpawner` logs `'<config>' atmosphere fx: ground fog N/4 ... storm
     clouds N/2 ...` on `[Flow:Garrison]` at raid start — a fresh log's `Warn` line with `BOTH
     layers spawned 0` would mean the catalog/pack wasn't ready and nothing rendered).
  4. Readability: a screenshot with the fog on, confirming enemy/tower silhouettes and the HUD
     are still legible; if not, the ring/scale/alpha constants in `SpawnAtmosphereFx` are the
     dial to turn down (documented inline).
