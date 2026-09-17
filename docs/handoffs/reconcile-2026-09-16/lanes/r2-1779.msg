fix(perf): WO-1779 - the town activity probe no longer ticks inside raid scenes

The Bastion raid ran 18-27 fps on the Seeker; the second-largest named cost was TownActivityProbe (19 over-budget warns up to 10.5 ms inside the raid window) because OnSceneLoaded discarded its Scene argument and the DDOL probe re-spawned into every scene. Gated on HubScenes.IsRaid (disabled, not destroyed - dungeons and garrisons keep their observer), idempotent on sceneLoaded and activeSceneChanged, 4-arg Perf measure kept; TownSuspendSceneFloorRegression case (h) proves it is off for RaidBase_* and still on for the hub, dungeons and garrisons. HeroLocomotion.Update (23 warns, 11.2 ms) is the remaining named cost and is re-laned.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
