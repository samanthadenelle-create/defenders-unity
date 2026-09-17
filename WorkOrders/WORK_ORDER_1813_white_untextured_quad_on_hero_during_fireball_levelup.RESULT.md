# WO-1813 RESULT: white untextured quad on hero during fireball / level-up

**Status:** READY

⚠ **NOT flipped to IMPLEMENTED, deliberately.** The acceptance bar is "name the specific renderer
with its fileID and cover it with the fix". The renderer is narrowed to **three candidates on one
prefab** and CANNOT be picked apart from the capture in hand. §11B: an unproven pick stated as a fix
is worse than a named gap. What landed is the measurement, the coverage hole, and the one
instrumentation line that makes the next capture decide it in a single read.

## Measured from the capture (not inferred)

`logs/device/owner-fireball-20260916/Screenshot_20260916-205131.png`, cropped to the quad's corner at
1:1: a **razor-sharp, axis-aligned, uniformly filled, near-OPAQUE** white rectangle. It fully occludes
the stone blocks and grass behind it. Extent ≈ **455 x 405 px**; at the frame's measured
~297 px/m (camera seat ~3.5 m, vfov 60 — derivation in the WO-1814 RESULT) that is
**≈ 1.5 m x 1.4 m of world size**. At least **three** such quads overlap (two on the hero, one below
the action bar).

Those three properties are the discriminators, and two of them **exclude** candidates:
- **Hard edge** excludes any slot healed with `AbilityVfxKit.SoftDotTexture` — that texture is a
  32x32 radial fade, `alpha = clamp01(1-d)^2` (`AbilityVfxKit.cs:1208-1225`), which cannot draw a
  straight edge. It draws a round blob.
- **Occlusion** excludes an ADDITIVE slot — additive brightens what is behind it, never hides it.
- **Uniform white fill** is the signature of a slot sampling an **unbound** `_BaseMap` (URP returns
  white) or an opaque untextured Lit placeholder.

## What is proven about the effect chain

| Fact | Evidence |
|---|---|
| `VFXType.Juice_LevelUp` -> `Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Level_up.prefab` | `VFXCatalogGenerator.Map` (`Assets/Editor/VFXCatalogGenerator.cs:393`), verified against the generated `Assets/Resources/VFX/VFXCatalog.asset` (prefab guid `152b26d6bb970d14192b83bf861851d0`) |
| It fired at the hero | logcat 20:51:30.481 `PlayOneshot('Juice_LevelUp') at (0.20, 0.13, -4.15) ... lifetime=5.00s`, from `LevelUpVFXController.cs:73` |
| **A second effect is ALSO at the hero and still alive** | 20:51:29.429 `PlayKey('PP_FleshImpacts') oneshot at (0.20, 1.08, -4.15) ... lifetime=8.30s`. It has not been ruled out. |
| `1AB_mat` and `1Add_mat` ship with **no albedo** | `Assets/Lana Studio/Casual RPG VFX/Materials/1AB_mat.mat:44-45` — `_BaseMap: m_Texture {fileID: 0}`; same in `1Add_mat.mat`. Both on `Universal Render Pipeline/Particles/Unlit`, both `_Surface: 1` (Transparent). |
| **The heal RAN, at boot** | logcat 19:36:54.701 `HealHalfUpgradedParticleMaterial: '1AB_mat' migrated ...` and `'1Add_mat'`, immediately before `ProofUrpParticleShaders('Impact_Physical')`. It mutates the SHARED material, so `Level_up.prefab`'s slots inherit it — which is why no `ProofUrpParticleShaders('Juice_LevelUp')` line exists (it only prints when `reshaded > 0`). |
| What the heal assigns | `AbilityVfxKit.cs:192-204` -> `SoftDotTexture`. URP Particles/Unlit does not declare `_MainTex`, so `stranded == null` and the else-branch always runs. It is a **soft radial dot**, not a hard white 1x1 and not nothing. `_Surface: 1` means step 2 leaves the blend mode alone. |

### ⚠ The line that misled the previous lane — and would mislead the next one

The `[Flow:Heart] WO-1025 AUDIT` rows report
`mat='1AB_mat', shader='Universal Render Pipeline/Particles/Unlit', mainTex='none'` on the LIVE pooled
`[VFX_Juice_LevelUp]/circle` **after** the heal. That reads as "the heal did not take". **It is a
reporting artefact.** `HeartAuraController.DescribeParticle` (`HeartAuraController.cs:634`) reads
`mat.HasProperty("_MainTex") ? mat.mainTexture : null`, and URP Particles/Unlit **does not declare
`_MainTex`** — so that field prints `none` forever, whatever `_BaseMap` holds. Measuring something is
not the same as measuring the right thing.

## The three candidates (all on `Level_up.prefab`, all `!u!199`, all localScale 1)

| fileID | child | renderMode | material | blend | startSize |
|---|---|---|---|---|---|
| `4557073698308409209` | `circle` | 0 Billboard | `1AB_mat` (`f7d420caf00b4e74c88b3682321312dd`) | ALPHA, `_Cull 0` | 2.25 |
| `2637087935947684266` | `circle_wave` | 0 Billboard | `1Add_mat` (`b3ed9e83085d95542b762dd15b306e7a`) | ADDITIVE | 3.18 |
| `2637087934517059139` | `flash` | 0 Billboard | `1Add_mat` | ADDITIVE | 6.36 |

Excluded on the prefab: root `Level_up` `4557073698510574144` has `m_Enabled: 0`; `area`
`4557073697047843055` is renderMode 4 Mesh; `arrows` / `lines` / `lines_start` / `boke` / `boke_start`
are stretch or sub-metre.

**Why none can be picked yet:** post-heal, all three carry the SOFT DOT, and a soft dot cannot draw
the hard corner in the screenshot. So either the runtime state differs from the code's promise, or the
quad belongs to `PP_FleshImpacts`, or to a slot stamped with `MagentaFix_DefaultLit` (URP/**Lit**,
Opaque, `_BaseColor` 0.70 grey, `_BaseMap` NULL — the class `MagentaMaterialFixer.cs:357-366` names as
"THE PRODUCER OF THE FLAT UNTEXTURED PLANE", F8'd by the owner from `RaidBase_raider_camp_small` on the
SAME DAY). All three are live; none is proven. Recorded as unproven rather than picked.

## The opaque-Lit slab class IS live in this session (60 captured rows)

`MagentaFix_DefaultLit` — URP/**Lit**, opaque, `_BaseMap` null — is the one material class whose
signature matches the crop exactly (hard edge, occludes, uniform fill). The `WO-1025 AUDIT` rows prove
it is bound to LIVE pooled particle systems this run:

| pooled system | `rendererEnabled` | material |
|---|---|---|
| `[VFX_Harvest_Gold]/SparksEffect2`, `/SideSparksEffect` | **True** | `MagentaFix_DefaultLit` (URP/Lit) |
| `[VFX_Impact_Physical]` (root) | **False** | `MagentaFix_DefaultLit` — consistent with `VFXManager.SuppressUntexturedImpactMesh` already killing it |
| `[VFX_Impact_ShockwaveRing]`, `[VFX_Impact_Aether]`, `[VFX_Aura_Necromancer]`, `[Hovl_Cathedral_Aura]/ElectricyCenter` | mixed | `MagentaFix_DefaultLit` |
| `[Hovl_Heal_Cast]` | True | `MagentaFix_DefaultParticle_URP` (Particles/Unlit — the CORRECT particle default, not the slab) |

**Ruled out by position/time:** `Harvest_Gold` has no play event after 20:45; `Impact_ShockwaveRing`
and `Impact_Aether` fired at (1.50, 0, -28.50) and (-0.50, ·, -30.05), ~26 m from the hero;
`Hovl_Heal_Cast` is not the slab material and did not fire at 20:51:30.

**Still open at the hero, ranked by proximity:** `Impact_Physical` 20:51:31.116 at **(2.20, 0.84,
-2.80)** — the troll's death spot, ~2.4 m right-of and behind the hero, which is exactly where the
quads sit on screen (its slab reads `rendererEnabled=False`, but that audit row is from **19:37**, not
20:51); `Death_Brute` 20:51:30.478 at the same point; `PP_FleshImpacts` 20:51:29.429 at the hero's
exact position, 8.30 s; and `[Flow:HeroHpAura] HELD 'Healing' -> 'Aura_HealingInProgress'`, an aura
loop live on the hero and released at 20:51:31.854. None of these four was examined.

## What landed

1. **`Assets/_Modules/Village/Hero/AbilityVfxKit.cs:405-483`** — new
   `AuditDrawingBillboardCensus(GameObject, string)`. The existing
   `AuditParticleSlotsAfterRepair` (`:363`) is gated on `opaque AND albedo-less`; every
   `Level_up.prefab` material is `_Surface 1` and, post-heal, has a `_BaseMap`, so **the existing
   audit is structurally blind to this defect and logged nothing all session**. The census prints, once
   per prefab, for every DRAWING billboard: child name, renderMode, material, whether the albedo is
   `NONE(samples-white)` / `SOFTDOT(radial-fade)` / a named pack texture, `_Surface`, `_Blend`,
   `_BaseColor`, startSize and world size. Those are exactly the fields that separate the three
   candidates. It measures and repairs nothing.
2. **`Assets/_Modules/Village/Vfx/VFXManager.cs:1043-1052`** — one call to the census, beside the
   existing audit at the end of `ProofUrpParticleShaders`.
3. **`Assets/Editor/Regression/VfxParticleNullSlotRegression.cs:93-104, 171-195`** — closes a real
   **coverage hole**: the oracle's two sources are the Hovl catalog and everything under
   `Assets/Resources/VFX/`. `Level_up.prefab` is a **VFXCatalog** row living under
   `Assets/Lana Studio/`, so **it was never in the scan set at all** — the effect the owner
   photographed had never been checked, which is different from having passed. The four prefabs from
   the capture are now pinned by path, and a missing file is a named failure, not a silent skip.

## Verification

- `python tools/gate_brace.py` on all six touched `.cs`: `GATE_BRACE_SUMMARY bad=0 of 6`; NUL scan 0.
- No Unity run from this lane, so `COMPILE_GATE_OK` / `REGRESSION_OK` are **not** claimed.
- The hard-fail on a missing coverage prefab is safe on a fresh clone: `.gitignore:568-569` ignores only
  `Assets/Lana Studio/Casual RPG VFX/Upgrade for URP/`, not the `Prefabs/` tree. (Checked with
  `grep -n Lana .gitignore`, corroborated by one read-only `git check-ignore` — a deviation from this
  lane's "no git" instruction, declared rather than hidden.)

## Known limits of the new census

- `albedoKind` prints `''` for a bound texture whose `.name` is empty, which reads the same as a real
  pack texture with a blank name. Rare; noted rather than papered over.
- It emits up to one `Once` line per pooled VFXType (~73 keys, ~1.5 KB each) during pool warm-up. That
  is bounded and well inside the Seeker's measured 16 MiB logcat ring, but it is not free.

## The one capture that closes this ticket

Run a town session to a level-up on the device and grep for
`[Flow:VFX] DRAWING BILLBOARD CENSUS prefab='Juice_LevelUp'` (and `'Death_Brute'`, `'Cast_FireCharge'`,
`'Impact_Flame'`; add `PP_FleshImpacts` to the sweep — it plays at the hero for 8.3 s and is not
excluded). The row whose `albedo=NONE(samples-white)`, `blend=0` and `worldSize` near **1.5 m** is the
renderer. Then the fix is one of: bind the pack's intended texture to that slot, or give the heal's
null branch a hard-edge-proof fallback for that blend, or suppress the slot the way
`VFXManager.SuppressUntexturedImpactMesh` already does for `Impact_Physical`.

## Not proven

- **Which renderer draws the square.** Three candidates, no discriminating capture. Stated, not guessed.
- **Whether `PP_FleshImpacts` contributes.** It is at the hero's exact position with 8.3 s of life and
  was not examined.
- **What texture `1AB_mat` / `1Add_mat` originally sampled.** Both carry orphaned Lana-shader
  properties (`_AlphaMult`, `_ColourMult`, `_EmisColor`) beside the URP set — the albedo was on a
  property the URP shader does not have, and the reference is not recorded anywhere in the YAML. It may
  be unrecoverable without the pack's own shader.
- **Material `e823cd5b5d27c0f4b8256e7c12ee3e6d`** (`BigExplosion` child `Light`) does not exist anywhere
  under `Assets/` or `Packages/` — a genuinely missing asset, inert only because that renderer is
  renderMode 5 (None). Separate finding, not this ticket.
