fix(hub): WO-1809 - the invisible CastleBarracks husk is out of the baked hub

Owner: "I think it still has the invisible barracks object". PrefabInstance 1439394418 had MeshRenderer+MeshFilter stripped but kept the polyperfect MeshCollider at (16,0,-4); the WO-1716 StructureHuskCleanup tool had never been run. Ran RemoveBatch (STRUCTURE_HUSK_CLEANUP_OK 1) + NavMeshBakeFinal (NAVMESH_BAKE_OK, no RENDERS NOTHING); HubHuskFreeSceneRegression (HUB_HUSK_FREE_OK) keeps it out; StructureRemovalHuskRegression re-pointed.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
