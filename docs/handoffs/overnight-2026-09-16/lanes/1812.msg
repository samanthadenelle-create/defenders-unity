test(raid): WO-1812 - raid wall collider-vs-art audit oracle

Every WallSegment in the four baked raid scenes must sit on Structure with an enabled non-trigger collider; enemy tower muzzles must sit below the neighbouring wall top, margin printed per scene (garrison 0.50 m, others 2.00 m). Names the art-pivot muzzle fragility for a later seam.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
