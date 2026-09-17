# WO-1816 RESULT — Synty castle materials URP conversion (three iterations)

**Completion:** 2026-09-16

## Summary

Converted raid-clad non-URP Synty materials to Universal Render Pipeline/Lit across three tool runs. Skip predicate extended to exclude backdrop/sky/particle materials (separate ticket scope).

## Run markers

1. **Run 1:** `SYNTY_CASTLE_URP_OK 58 material(s)` — Synty/Generic_Basic (guid: 0730dae39bc73f34796280af9875ce14)
2. **Run 2:** `SYNTY_CASTLE_URP_OK 17 material(s)` — Synty/Generic_Standard (guid: 3b44a38ec6f81134ab0f820ac54d6a93)
3. **Total converted:** 75 materials
4. **Skipped:** 37 (FX/Flags/Water/Decals path patterns) + backdrop/sky/particle shaders

## Regression scope

**Non-URP shaders CONVERTED (raid clad):**
- `0730dae39bc73f34796280af9875ce14` → Synty/Generic_Basic
- `3b44a38ec6f81134ab0f820ac54d6a93` → Synty/Generic_Standard

**Non-URP shaders SKIPPED (separate ticket):**
- `0736e099ec10c9e46b9551b2337d0cc7` → Synty/Generic_ParticlesUnlit (backdrop/particles, out of scope)
- `de1d86872962c37429cb628a7de53613` → SkyDome (skybox, out of scope)
- `3d532bc2d70158948859b7839127e562` → Skybox_Generic (skybox, out of scope)
- `e17f8fe2503580447a3784d34b316d11` → Triplanar_Basic (terrain, out of scope)

## Files updated

1. **Assets/Editor/SyntyCastleUrpMaterials.cs**
   - Calls SyntyCastleUrpRegression.ShouldSkipMaterial for skip logic
   - Converts both Generic_Basic and Generic_Standard
   - Property mapping: _Albedo_Map→_BaseMap, _Normal_Map→_NormalMap, _Color→_BaseColor, render queue, _Cutoff/_AlphaClip

2. **Assets/Editor/Regression/SyntyCastleUrpRegression.cs**
   - Public static ShouldSkipMaterial predicate (path + shader checks)
   - Path skips: FX/, Flags/, Water, Decals/
   - Shader skips: ParticlesUnlit, SkyDome, Skybox_Generic, Triplanar_Basic (by name)
   - Both files: brace bad=0, NUL clean

## Next steps

**Lead:** Run tool final time; regression should pass 564/564 (all raid clad on URP, backdrop materials skipped).
