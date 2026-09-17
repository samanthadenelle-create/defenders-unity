feat(waves): WO-1773 - town-wave difficulty on the tunables rail: out-of-combat regen gate, post-band strength and count growth, hero-damage instrumentation

An external tester at Lv 52 stood still at wave 176 and took nothing. Two dead steps proven from source: SafeZoneRecovery's town regen (12% of max HP per second) ran with no combat gate, and WaveScalingCurve clamps at its last keyframe so wave 176 is wave 20 - only the dragon ever kept scaling. Seven int knobs, every default exact identity (town.regenSuppressSecondsAfterHit, town.regenPctDuringWave, wave.hp/dmgGrowthPctPerWave modelled on the dragon's additive growth, wave.maxCountPct, wave.countCapPct, wave.maxSimultaneousPct), registered in all six places; consumers TownRegenTunables / WaveDifficultyTunables; [Flow:HeroHealth] TakeDamage, [Flow:SafeZone] regen gate and a per-wave WAVE SUMMARY line. Owner direction 2026-09-16: "enemies need to really start scaling with wave". This commit also carries the shared tunables-rail files (RemoteTunables.cs, the defaults oracle, api/_lib tunables + manifest, the PROD022 doc) with the rows WO-1799/1803/1805 added today, plus the committer's DataRegression registrations for this round.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
