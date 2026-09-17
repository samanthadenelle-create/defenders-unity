# VFX Particle Renderer Slot Classification — the 32 played keys

**Status:** COMPLETE (2026-09-17). Supersedes the 2026-09-16 "PARTIAL INVENTORY / full YAML
parsing deferred" draft in this same file — every "UNKNOWN / REQUIRES PARSE / LIKELY A/B" row
below is now a measured value read out of the prefab and material YAML.

**Method.** Every prefab named in `VFX_PLAYED_INVENTORY.md` was parsed directly: each `!u!199`
`ParticleSystemRenderer` document → `m_Enabled`, `m_RenderMode`, its `m_Materials` slot list;
each slot's material guid resolved through a project-wide `.meta` guid index (90,062 entries,
`Assets/` + `Library/PackageCache/` + `Packages/`) → the `.mat`'s `m_Shader` (resolved to the
shader's declared name), `_BaseMap`, `_MainTex`, `_Surface`, `_Blend`, `_SrcBlend`, `_DstBlend`,
`_AlphaClip`, `_BaseColor`, `stringTagMap: RenderType`; plus the owning `ParticleSystem`'s
`startSize` and `startColor`. No Unity, no runtime, no inference.

**Two corrections to the draft, both measured:**

* **`PP_MuzzleFlash` does NOT resolve to `Assets/Resources/VFX/Weapon/Cast_MuzzleFlash.prefab`.**
  The inventory row is wrong. `HovlVfxCatalog.asset:863`-region row `PP_MuzzleFlash` carries prefab
  guid → `Assets/UnityTechnologies/ParticlePack/EffectExamples/Weapon Effects/Prefabs/MuzzleFlash.prefab`.
  `Cast_MuzzleFlash` is a *separate* `VFXType` row. Both are classified below.
* **`Explosion_Arcane` and `Projectile_Arcane` are not keys at all** — they are the *prefab* names.
  The catalog rows are `VFXType.Impact_ExplosionAether` and `VFXType.Projectile_ArcaneBolt`.

---

## Buckets

| Bucket | Definition |
|---|---|
| **A** | URP **non-particle** shader (i.e. `Universal Render Pipeline/Lit`), authored opaque, **no `_BaseMap`**, on a ParticleSystemRenderer |
| **B** | Non-URP shader — a pack shader or shader graph (`HS_*.shadergraph`, `VFXMirror/*`, legacy built-in) |
| **C** | URP **particle** shader (`Particles/Unlit`, `Particles/Lit`) with **no `_BaseMap` bound** — a light tint only |
| **D** | Material name begins `MagentaFix` — the editor fixer's placeholder |
| **OK** | Everything else: a real albedo is bound |
| **OPAQUE-DRAW** | *(new, and it is the finding)* an **ENABLED** renderer, renderMode neither `None` nor `Mesh`, whose material is `_Surface: 0` (opaque) **and** `_AlphaClip: 0` (no cutout) — regardless of bucket |

> ⚠ **A stranded `_MainTex` is NOT counted as albedo on a URP shader.** URP samples `_BaseMap`;
> `AbilityVfxKit.cs:172-186` records that `URP/Particles/Unlit` does not even *declare* `_MainTex`,
> so the stranded texture is unrecoverable at runtime. Counting it would have hidden bucket C.

---

## Per-prefab counts (198 renderer slots across 32 prefabs)

| Prefab | A | B | C | D | OK | **OPAQUE-DRAW** |
|---|--:|--:|--:|--:|--:|--:|
| `2D Projectile 20 pink arrow.prefab` | 0 | 2 | 0 | 0 | 0 | 0 |
| `Aura_TalentNode.prefab` | 0 | 2 | 0 | 0 | 1 | 0 |
| `BigExplosion.prefab` | 0 | 0 | 0 | 0 | 8 | 0 |
| `Burst_rings.prefab` | 0 | 0 | 4 | 1 | 0 | 0 |
| `Cast_MuzzleFlash.prefab` | 0 | 0 | 0 | 0 | 2 | 0 |
| `Casting_Fire.prefab` | 0 | 3 | 0 | 0 | 2 | 0 |
| `Character_status_sleep.prefab` | 0 | 0 | 4 | 1 | 0 | 0 |
| `Damage_BreakBurst.prefab` | 0 | 0 | 0 | 0 | 5 | 0 |
| `Damage_CriticalBeacon.prefab` | 0 | 0 | 0 | 4 | 4 | 0 |
| `Damage_Fire.prefab` | 0 | 0 | 0 | 0 | 2 | 0 |
| `Damage_Ruin.prefab` | 0 | 0 | 0 | 0 | 3 | 0 |
| `Damage_Smolder.prefab` | 0 | 0 | 0 | 0 | 1 | 0 |
| `Death_Brute.prefab` | 0 | 0 | 0 | 0 | 5 | 0 |
| `Electro splash.prefab` | 0 | 10 | 0 | 11 | 1 | 0 |
| `Elite_Spawn.prefab` | 0 | 0 | 0 | 0 | 4 | 0 |
| `Explosion_Arcane.prefab` | 0 | 3 | 0 | 0 | 0 | 0 |
| `Flash 16 fire.prefab` | 0 | 3 | 0 | 3 | 0 | 0 |
| `Flash_circle.prefab` | 0 | 0 | 3 | 0 | 0 | 0 |
| `Flash_dubble_circle.prefab` | 0 | 0 | 3 | 0 | 0 | 0 |
| **`FleshImpacts.prefab`** | 0 | 0 | 0 | 1 | 4 | **1** |
| `Hit 11 orange arrow.prefab` | 0 | 6 | 0 | 6 | 0 | 0 |
| `Hit 24 green explosion.prefab` | 0 | 6 | 0 | 7 | 1 | 0 |
| `Hit_magic.prefab` | 0 | 0 | 5 | 1 | 0 | 0 |
| `Level_up.prefab` | 0 | 0 | 11 | 0 | 0 | 0 |
| `Lightning strike.prefab` | 0 | 12 | 0 | 12 | 0 | 0 |
| `MuzzleFlash.prefab` | 0 | 0 | 0 | 0 | 2 | 0 |
| `Orbs_electric.prefab` | 0 | 0 | 4 | 0 | 1 | 0 |
| `PlasmaExplosionEffect.prefab` | 0 | 0 | 0 | 0 | 5 | 0 |
| `Poof_generic.prefab` | 0 | 0 | 5 | 0 | 0 | 0 |
| `Projectile 13 red laser.prefab` | 0 | 2 | 0 | 0 | 0 | 0 |
| `Projectile_Arcane.prefab` | 0 | 0 | 0 | 3 | 3 | 0 |
| `Slash_stone_once.prefab` | 0 | 0 | 5 | 1 | 0 | 0 |
| **TOTAL** | **0** | **49** | **44** | **51** | **54** | **1** |

### How to read those totals

* **A = 0.** The "opaque URP/Lit with no albedo" shape does not occur in the played set. That is
  the shape `AuditParticleSlotsAfterRepair` already watches for, and it is genuinely absent —
  which is exactly why that instrument logged nothing all session and the defect still shipped.
* **D = 51, and 50 of them are harmless.** Every `MagentaFix_DefaultLit` occurrence in the played
  set except one is a **trail slot (`i > 0`)**, which
  `AbilityVfxKit.TryRepairOpaqueLitParticleSlot` already re-points at slot 0 at spawn. The
  remaining one (`FleshImpacts/Streaks` slot 0) sits on a `renderMode: None` renderer and draws
  nothing.
* **B = 49** is almost entirely the Hovl `HS_*.shadergraph` family. Those are *not* a defect: they
  are the pack's own URP shader graphs, all authored `_Surface: 1`. "Non-URP-named" is not the
  same as "broken", and the draft's "LIKELY A/B risk" flags on the Hovl prefabs are withdrawn.
* **C = 44** is the Lana family (`1AB_mat`, `1Add_mat`) plus a few others: URP particle shaders
  with **no `_BaseMap` at all**. `AbilityVfxKit.HealHalfUpgradedParticleMaterial` binds the
  generated soft dot into these at first spawn (observed on device, logcat 19:36:54.701; observed
  again in the 2026-09-17 capture run). **All eleven `Level_up.prefab` slots are in this bucket,
  and this is where WO-1813 actually lands** — see the RESULT. The previous lane excluded the
  level-up candidates on the reasoning "a soft dot cannot draw a hard edge, and additive cannot
  occlude". **A rendered frame refutes both** (`Builds/vfx-whitequad/Juice_LevelUp__AFTER.png`,
  taken AFTER the heal fired in the same run).
* **OPAQUE-DRAW = 1, and it is the answer.** See below.

---

## The one offender: `PP_FleshImpacts` → `FleshImpacts.prefab` → `Mist`

| field | value | read from |
|---|---|---|
| prefab | `Assets/Resources/VFX/Impact/FleshImpacts.prefab` | `HovlVfxCatalog.asset` row `PP_FleshImpacts`, prefab guid `40227cd0102dc5f4eb21080c708a1bbf` |
| child | `Mist` | prefab YAML |
| renderer | `!u!199` fileID **`199462925942737768`**, `m_Enabled: 1`, `m_RenderMode: 0` (Billboard) | prefab YAML |
| slot | **0** | `m_Materials[0]` |
| material | `GoopMist`, guid `8197b9eb112c6a1428518197b3ad2dbb` → `Assets/Resources/VFX/_Shared/Materials/GoopMist.mat` | guid index |
| shader | `Universal Render Pipeline/Lit` — **not a particle shader** | `m_Shader` guid `933532a4fcc9baf4fa0491de14d08ed7` |
| surface | `_Surface: 0`, `_SrcBlend: 1` (One), `_DstBlend: 0` (Zero), `_ZWrite: 1`, `RenderType: Opaque`, **`_AlphaClip: 0`** | `GoopMist.mat` |
| albedo | `_BaseMap` → `DustPuffSmallParticleSheet.png` | `GoopMist.mat:44-45` |
| **the albedo's RGB** | **`(255,255,255)` at every sampled texel.** The entire sprite lives in the ALPHA channel (alpha min 0, max 227, mean 12). | direct pixel read of `Assets/Resources/VFX/_Shared/Textures/DustPuffSmallParticleSheet.png` |
| startSize | 1.0 | owning `ParticleSystem` |

**An opaque material discards that alpha.** So the billboard paints its whole square footprint in
solid white, lit by the warm key light. That is a real, shipped defect on every flesh hit and it is
now repaired at spawn.

> ⛔ **IT IS NOT THE QUAD IN THE OWNER'S 20:51:31 FRAME, and saying so would have been a guess.**
> `Mist`'s `startLifetime` is `minMaxState: 3` between **0.08 s and 0.5 s**. The last
> `PP_FleshImpacts` in that session fired at **20:51:29.429** (`logcat.txt:827735` — the only one
> after 20:51:12), and `[Flow:Pause]` shows the clock at **1.00** across that whole span (the
> combat dips at 29.795-29.867 and 29.999-30.071 are 0.07 s each). So every Mist particle was dead
> by ~20:51:29.9, **1.1 s before the capture**. The 8.30 s in the log line is the POOLED HOST's
> despawn timer, not the particles' life — reading it as the latter is exactly the kind of
> near-miss §11B is about.

### The carve-out that keeps the rule honest

Two sibling prefabs in the same surface-impact family are **also** opaque billboards and are
**not** offenders, measured rather than assumed:

| prefab | child | material | `_Surface` | `_AlphaClip` | verdict |
|---|---|---|---|--:|---|
| `Assets/Resources/VFX/Impact/StoneImpacts.prefab` | `ImpactDebris` | `TinyStonesParticle` | 0 | **1** | fine — the cutout carves the sprite's shape |
| `Assets/Resources/VFX/Impact/WoodImpacts.prefab` | `WoodSplinters` | `WoodSplintersParticle` | 0 | **1** | fine — same |
| `Assets/Resources/VFX/Impact/FleshImpacts.prefab` | `Mist` | `GoopMist` | 0 | **0** | **solid white rectangle** |

Without the `_AlphaClip` carve-out the repair would have "fixed" two effects that render correctly.

---

## Project-wide scope of the OPAQUE-DRAW class

Scanned every prefab reachable from `VFXCatalog` + `HovlVfxCatalog` + all of
`Assets/Resources/VFX/**` — **264 prefabs**. **19 slots** match:

| prefab | children | note |
|---|---|---|
| `Assets/Resources/VFX/Impact/FleshImpacts.prefab` | `Mist` | every flesh hit, 0.08-0.5 s |
| `Assets/Mirza Beig/.../Prefabs/Loop/pf_vfx-ult_demo_psys_loop_portalBlue.prefab` | `Portal`, `Border`, `Inner Embers`, `Inner Embers 2`, `Outer Embers` | **shipped** — this is the catalogued `Portal_Threshold_Aura` |
| `Assets/Mirza Beig/.../Prefabs/Loop/pf_vfx-ult_demo_psys_loop_fire.prefab` | `fire` x2, `smoke` | |
| `Assets/Mirza Beig/.../Demos/Fireworks/Fireworks.prefab` | 10 slots | demo |

Every one of the Mirza Beig materials names its own intended blend in its filename
(`…-add-[1.0]`, `…-alpha-[1.0]`) while carrying `_Surface: 0` — the same URP-upgrade damage,
independently confirmed.

---

## What was done with this

`AbilityVfxKit.RepairOpaqueDrawingParticleSlots` (WO-1813) repairs the class at spawn on **both**
paths, and `VfxParticleNullSlotRegression` now proves the repair covers every authored offender
rather than baselining a list of them. Details in
`WorkOrders/WORK_ORDER_1813_white_untextured_quad_on_hero_during_fireball_levelup.RESULT.md`.
