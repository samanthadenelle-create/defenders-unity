fix(raid): WO-1808 - enemy-owned towers honour the wall line-of-sight gate

107 FireAtParty (EnemyOwned) calls and zero TowerLoS lines in the owner raid window: BlockedByWall was only called from the player-owned pick. AcquireParty now runs BlockedByWallToParty (shared BlockedByWallAt core, flyer exemption kept in the pinned shape at both entry points); EnemyTowerWallLosRegression (clear / walled / walled flyer / source wiring; missing file is a FAIL).

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
