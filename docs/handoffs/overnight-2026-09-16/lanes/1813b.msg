fix(vfx): WO-1813 - the white square is the level-up column: untextured additive quads; opaque flesh-hit mist and Hovl trail slabs repaired

Overnight sweep (owner: "its always in combat graphics that is causing the broken color squares"). 32 played prefabs,
198 particle slots YAML-classified (docs/handoffs/overnight-2026-09-16/VFX_SLOT_CLASSIFICATION.md). Findings, all
proven by rendered frames in Builds/vfx-whitequad/:
- Juice_LevelUp (Lana Studio Level_up.prefab): all eleven renderer slots carry 1AB_mat/1Add_mat with NO base map, so
  the level-up column is a hard-edged untextured additive rectangle 2.29 m wide; additive over a sunlit town composes
  to the owner's (254,254,246) white square. Renderer not pinned to one fileID (her frame shows three quads); the
  remedy is the next commit.
- FleshImpacts.prefab child 'Mist' slot 0 (GoopMist, URP/Lit opaque, base map all-white with the sprite in alpha)
  drew a solid white square on every flesh hit: repaired by the shared helper (OPAQUE BILLBOARD REPAIRED line),
  StoneImpacts/WoodImpacts (alpha-clip) untouched by the rule.
- VFXManager.Hovl.CreateHovlInstance ran no proof pass, so five combat effects kept MagentaFix slabs in trail slot 1
  (SimpleCast_Cast, Spear_Impact, Lightningspellmaybe_Cast, lighteningOnSpellLand_Impact, ArcherTower_Projectile).
- WO-1806 positive control RUN: MagentaFix guid injected into Death_Brute slot 0 -> VFX_NULL_SLOT_FAIL naming
  prefab/child/slot; reverted byte-clean.
VfxProofCapture.RunWhiteQuadHunt (owner-seat camera, before/after pairs); VfxParticleNullSlotRegression proves the
repair covers 21 authored opaque drawing slots. Gate: COMPILE_GATE_OK 22:56, REGRESSION_OK 564/564 23:13 (fresh logs
Builds/wo1813-compile.log, Builds/wo1813-regression.log). WO-1813 stays READY for the level-up remedy.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
