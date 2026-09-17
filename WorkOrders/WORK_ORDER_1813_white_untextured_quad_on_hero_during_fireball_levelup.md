# WO-1813: white untextured quad on hero during fireball / level-up

**Status:** IMPLEMENTED

## Issue

During level-up after the mage fires a Fireball at a Troll (wave 13, owned screenshot 2026-09-16 20:51:31):
- A large white translucent untextured quad appears centred on the hero (0.20, 0.08, -4.15)
- The quad renders billboard-style, approximately 400x400 px at the screen 2000 px width
- It persists throughout the Juice_LevelUp effect (5s lifetime)
- Device log timestamps: 20:51:30.479 text spawned, 20:51:30.481 VFX plays

## ⚠ Root Cause FOUND (third pass, 2026-09-17) — read the RESULT, then §6 of it

`VFXType.Juice_LevelUp` -> `Level_up.prefab`. All ELEVEN of its slots carry `1AB_mat`/`1Add_mat`
with **`_BaseMap: {fileID: 0}` — no albedo at all**, so every quad is an untextured tinted
rectangle. The 2026-09-16 exclusion below ("a soft dot cannot draw a hard edge", "additive cannot
occlude") is **REFUTED by a rendered frame**: `Builds/vfx-whitequad/Juice_LevelUp__AFTER.png`,
taken AFTER the heal ran in the same batch, steps `(15,17,20)`->`(140,96,34)` inside one 10 px
step and holds a uniform fill for 680 px (2.29 m) — a razor-sharp axis-aligned slab. Composited
additively over a sunlit town that fill saturates to `(255,255,147)`..`(255,255,255)`, which is the
owner's measured `(254,254,246)` / `(254,230,208)`.
Two OTHER defects of the same class were found and FIXED on the way (`PP_FleshImpacts/Mist` opaque
billboard; five combat effects' MagentaFix trail slabs on the un-swept PlayKey path).
**RESOLVED under the lead's 2026-09-17 repair ruling.** The drawer is `Level_up.prefab` child
**`area`** — proven by ten one-child isolation frames, not by elimination: `area` is the ONLY child
whose solo frame carries the hard edge (jumps at x=995 / x=1675 on y=400; the other nine have none).
It is a MESH-mode particle on `1Add_mat`, whose `_BaseMap` and `_MainTex` are both `{fileID: 0}` —
the pack's texture is gone and no guid survives to recover. Fixed by the tree's own 2026-09-09
ruling for this exact class, applied by condition instead of by type name. After: zero hard jumps on
y=400 and y=550; the arrows and ground rings still read.

<details><summary>Superseded 2026-09-16 second-pass reasoning (kept, not rewritten)</summary>

## Root Cause — NOT YET PROVEN (second pass, 2026-09-16). Three candidates, no discriminator.

~~"The prefab is instantiated fresh from the catalog entry, NOT from the healed pool instance...
the fresh instantiation bypasses the pool's repair."~~ **DISPROVEN.** `Instantiate(` appears exactly
once in `VFXManager.cs` (`:830`, inside `CreatePooledInstance`), and `ProofUrpParticleShaders` runs on
that one path — there is no bypassing instantiation. The heal is further proven to have RUN at
19:36:54.701 (`HealHalfUpgradedParticleMaterial: '1AB_mat' / '1Add_mat' migrated ...`), and because it
mutates the SHARED material, `Level_up.prefab` inherits it.

⚠ The `WO-1025 AUDIT ... mainTex='none'` rows that appear to contradict that are a **reporting
artefact** — `HeartAuraController.cs:634` reads `_MainTex`, which URP Particles/Unlit does not declare,
so the field prints `none` regardless of `_BaseMap`.

What IS measured: a razor-sharp, near-opaque, ~1.5 m white billboard. That rules OUT any soft-dot
healed slot (a radial fade cannot draw a hard edge) and rules OUT additive (which cannot occlude).
Candidates, all `Level_up.prefab`: `circle` `!u!199 &4557073698308409209` (1AB_mat, alpha, 2.25),
`circle_wave` `&2637087935947684266` (1Add_mat, additive, 3.18), `flash` `&2637087934517059139`
(1Add_mat, additive, 6.36) — plus `PP_FleshImpacts`, which is at the hero's exact position with an
8.30 s lifetime and has not been examined. Full record in the RESULT.

</details>

## Files to examine

- `Assets/Resources/Data/Canonical/*.json` — VFX catalog JSON
- `Assets/Resources/VFX/VFXCatalog.asset` — serialized catalog
- VFXManager PlayOneshot path (VFXManager.cs:625 / :728)
- The Juice_LevelUp prefab in Assets/Resources/VFX/
- `Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs` (Hovl-specific prefab handling)

## Acceptance Criteria

1. Identify the exact Juice_LevelUp prefab path and 1AB_mat material location (YAML: _BaseMap, _Surface, render mode)
2. Verify whether PlayOneshot calls ProofUrpParticleShaders or misses it
3. Route the PlayOneshot path through the existing heal pass (or confirm it already does)
4. Ensure Impact_Physical (Death_Brute, Cast_FireCharge, Impact_Flame, Slash_stone) paths also run the heal
5. Add regression case to VfxParticleNullSlotRegression.cs asserting Juice_LevelUp has no untextured URP Particles/Unlit slot
6. Verify braces in all edited .cs files

## Do NOT touch

- DataRegression.cs
- Any .unity scene files
- CastleHubBuilder.cs, WallTools/*.cs, ArmyMuster* files, CLI_LANES_WO_NUMBERS.md

## Notes

- The aid mentioned IsMagentaFixParticlePlaceholder is too narrow for this class (1AB_mat is not MagentaFix*)
- HealHalfUpgradedParticleMaterial already exists at AbilityVfxKit.cs:158
- The suppress-per-type hack at VFXManager.cs:846-892 only covers Impact_Physical, not Juice_LevelUp
