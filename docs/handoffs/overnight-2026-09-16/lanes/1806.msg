fix(vfx): WO-1806 - particle slots stamped with the opaque Lit placeholder are repaired on every play path

MagentaMaterialFixer stamped MagentaFix_DefaultLit (URP/Lit, no base map) into 4,197 empty ParticleSystemRenderer slots across 969 prefabs, and three copy-pasted runtime guards skipped slot 0, so the owner saw flat grey quads on archer fire (flag 00:36, build 372984). One name-gated repair helper in AbilityVfxKit now serves VFXManager, ProjectileVFXCatalog and VfxProofCapture; the fixer uses the particle default; a drawing-billboard census (WO-1813) logs every candidate at play time; VfxParticleNullSlotRegression pins slot 0 and the Level_up prefab. The exact quad in the owner flag is NOT attributed - said so in the ticket.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
