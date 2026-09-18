# WO-1868 — Raid arena: tower shot VFX, ground fog, storming clouds

**Status: READY TO IMPLEMENT**

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
