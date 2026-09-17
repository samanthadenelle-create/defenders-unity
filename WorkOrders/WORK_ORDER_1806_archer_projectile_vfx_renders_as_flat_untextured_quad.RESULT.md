# WORK ORDER 1806 — RESULT

**Status of the work:** IMPLEMENTED, NOT YET GATED (no Unity gate, no commit — per the lane brief).
**Date:** 2026-09-16
**Lane:** VFX / materials

---

## What was proven, and with what

| claim | proving line |
|---|---|
| The defective material is URP/Lit, opaque, 0.70 grey, no albedo | `logcat-after-372984.txt:2518323` — `mat='MagentaFix_DefaultLit' shader='Universal Render Pipeline/Lit' baseColor=(0.70,0.70,0.70,1.00) baseMap=False mainTex=False` |
| …and that is what the asset says | `Assets/Materials/MagentaFix_DefaultLit.mat:10-11`, `stringTagMap: RenderType: Opaque`, `_BaseMap: m_Texture: {fileID: 0}` |
| The producer stamps it into EVERY renderer's null slots with no particle branch | `Assets/Editor/MagentaMaterialFixer.cs:284 → :300 → :356-382`, default built at `:385-398` |
| A correct particle default already existed and was never consulted there | same file `:157` (`ParticleFixMatPath`), `:244 GetOrCreateUrpDefaultParticleMaterial`, used only at `:171` |
| The legacy remap cannot see it | `VFXManager.cs` `IsLegacyParticleShader` returns false on any `"Universal Render Pipeline"` name |
| The half-upgrade heal cannot see it | `AbilityVfxKit.cs:164-166` — `particleLike` requires `"Particles"` or `"Unlit"`; `"Universal Render Pipeline/Lit"` has neither |
| The only MagentaFix-aware guard was `i > 0`, in three copies | `VFXManager.cs:985-995`, `ProjectileVFXCatalog.cs:322-331`, `VfxProofCapture.cs:1451-1456` (all pre-edit) |
| Nothing had ever audited a PARTICLE material | `logcat:406` — `[Flow:RaidArt] UNTEXTURED CENSUS … 401 offending slot(s) across 680 mesh renderer(s) / 1221 slot(s)` — **mesh renderers only** |

### Census output (run 2026-09-16, working tree)

```
carrier prefabs: 969
total MagentaFix slots on PSRs: 4197
  inert (renderMode NONE or renderer disabled): 990
  trail slot i>0 on a live PSR (covered by the existing i>0 re-point): 3186
  SLOT 0 on a LIVE PSR (repaired by NOTHING today): 21
      … all 21 in Assets/Mirza Beig/Particle Systems/Ultimate VFX/** demo prefabs
```

None of the 21 is in a shipped catalog, which is why the new `[vfx-null-slot]` assertion passes today
and is worth pinning before the next one lands.

---

## ⚠ Unproven — stated as unproven (CLAUDE.md §11B)

1. **Which prefab drew the two quads in the owner's frame is NOT proven.** Every specific candidate
   alive at the corpse coordinate was eliminated by its own renderer state or by timing — the table
   is in the WO §3. I did not substitute a plausible one for a proven one.
2. **Therefore it is NOT proven that this fix removes the owner's flat plane.** It removes a real,
   shipped defect class whose visual signature matches the screenshot exactly, and it adds the
   instrument that will name the actual drawer on the next device run.
3. `MagentaFix_DefaultParticle_URP.mat`'s runtime appearance was not observed — only its YAML
   (URP/Particles/Unlit, `RenderType: Transparent`, queue 3000) was read.
4. Whether `FixNullSlotsInPrefabs` or the sibling built-in-particle pass wrote these slots **last**
   cannot be determined from here. The material guid proves the producer class, not the run date.

---

## Files changed (all gate_brace-clean, zero NUL bytes)

```
Assets/_Modules/Village/Hero/AbilityVfxKit.cs                    braces  81/81  NUL 0
Assets/_Modules/Village/Vfx/VFXManager.cs                        braces 206/206 NUL 0
Assets/_Modules/Village/Buildings/ProjectileVFXCatalog.cs        braces  61/61  NUL 0
Assets/Editor/MagentaMaterialFixer.cs                            braces  54/54  NUL 0
Assets/Editor/Regression/VfxParticleNullSlotRegression.cs        braces  22/22  NUL 0
Assets/Editor/VfxProofCapture.cs                                 braces 154/154 NUL 0
```

`python tools/gate_brace.py <all six>` → `GATE_BRACE_SUMMARY bad=0 of 6`, exit 0.

### Three narrowings applied after review, each measured

1. **The repair predicate is NAME-GATED (`MagentaFix*`), not the wider "opaque URP material with no
   `_BaseMap`" heuristic.** That wider rule also matches a legitimate
   `ParticleSystemRenderMode.Mesh` particle — debris and shards are vertex-coloured URP/Lit,
   untextured and opaque by design (13 Mesh-mode renderers in a 60-prefab sample). The wider rule
   would have rewritten one into a soft-dot billboard AND turned `[vfx-null-slot]` red, from a
   heuristic no evidence asked for. The wide shape is still WATCHED, read-only, by the audit line.
2. **`renderMode == Mesh` is skipped** in both the slot-0 rebuild and the audit.
3. **Rebuilt materials are cached per source material** (static dictionary, mirrors
   `ProjectileVFXCatalog._fixedMaterials`). `SpawnFlying`/`ReskinFlying` run per shot and hand the
   same shared asset in every time, so an uncached `new Material(...)` would have leaked one Material
   per projectile until `Resources.UnloadUnusedAssets`.

### Suite-scope measurement (the proof `[vfx-null-slot]` passes today)

```
Resources/VFX: prefabs=22 slots=38 slot0=27 slot0_on_a_live_billboard=0 trail=11 inert=27
```

All 27 slot-0 occurrences are on renderers that are `renderMode: None` or `m_Enabled: 0`.
(An earlier draft of this WO said "14 slots, 12 slot-0" — that was a recalled number, not a measured
one, and it was wrong. Corrected before hand-back; the argument it supported got stronger, not weaker.)

**No `.unity` file, no `SmartMobileCamera.cs`, no `Troops/TroopController.cs`, no `DataRegression.cs`,
no WO banner, and no YAML asset** was modified. No new registration line is needed: `[vfx-null-slot]`
is already registered in `DataRegression.RunAll`.

## Owner-facing notes

* **No colour was chosen and no owner-tagged VFX key was touched** (memories
  `owner-colorblind-delegate-visual-creative`, `vfx-map-owner-tags-no-creative-pick`). The slot-0
  rebuild reuses the tree's own existing remedy for a texture-less particle material
  (`AbilityVfxKit.cs:194-204`: shared soft dot, white tint, alpha blend).
* **Town is in scope too, not just raids.** Slot-0 carriers include `Harvest_Gold`, `Harvest_Iron`,
  `Damage_CriticalBeacon`, `Aura_slowdown`, `Aura_acceleration` — today all on inert renderers, so no
  change is expected in town. If the owner sees any harvest/status effect look different after this
  ships, that is the place to look.

## Next step for the lead

1. Gate the combined tree once (`COMPILE_GATE_OK`, then `REGRESSION_OK <n>/<n>` on a **fresh** log —
   judge the marker, not the exit code).
2. Run the `[vfx-null-slot]` positive control in the WO §7 before trusting its pass.
3. On the next device build: `adb logcat | grep "PARTICLE SLAB after repair"`. That line closes the
   attribution gap in §3 by itself.
