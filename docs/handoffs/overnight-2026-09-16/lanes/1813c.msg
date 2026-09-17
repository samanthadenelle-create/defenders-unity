fix(vfx): WO-1813 - the level-up column no longer draws as an untextured slab (mesh slots with no albedo are suppressed by condition)

Drawer named by isolation (each Level_up child shot solo from the owner's seat): child 'area', renderMode Mesh, slot 0
1Add_mat, additive, startSize 3.46 - the only child with hard edges. No pack texture exists: 1AB_mat/1Add_mat carry
_BaseMap AND _MainTex fileID 0 (siblings kept theirs), so rebinding was impossible; a soft-edge texture was built and
REFUTED by measurement (a cylinder's silhouette sits mid-texture). Remedy = the tree's own nine-day-old ruling
(VFXManager.SuppressUntexturedImpactMesh, owner Seeker 2026-09-09, same pack, same materials, same artefact) applied by
condition on both spawn paths: AbilityVfxKit.RepairUntexturedMeshParticleSlots disables a Mesh-mode slot whose every
material is albedo-less. After frame: no hard jumps, arrows + rings + floor glow still read. Oracle
CheckEveryDrawingSlotEndsUpTextured runs the real repair chain on clones of the capture prefabs. Gate on fresh logs:
COMPILE_GATE_OK 23:37 (Builds/wo1813-compile2.log), REGRESSION_OK 564/564 23:41 (Builds/wo1813-regression2.log). PO
felt-verify on a bright town still owed.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
