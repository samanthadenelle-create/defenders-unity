feat(raid): WO-1763 - per-camp remote raid difficulty on the tunables rail; Bastion seed ruled 160% / offset 5

The Iron Bastion was a field-for-field clone of the Veiled Enclave on every difficulty field and the owner nearly 3-starred it with a level-4 hero. No raid.* key was difficulty. Eight int knobs (raid.difficultyMultPct{Camp1,Camp2,Camp3,Bastion}, raid.levelOffset{...}) registered in all six places (RemoteTunables registry, defaults oracle, PROD022 doc rows, api/_lib/tunables.js allowlist, tunable-manifest.js, regenerated manifest), resolved by RaidDifficultyTunables and folded at the one spawner seam; RAID START now names source and both numbers. Every default is exact identity - the code ships inert until the DB row lands. Stale "2.8" reward-multiplier comments now point at scene-configs.json.

Owner rulings 2026-09-16: seed Bastion at 160 (multiplicative on the authored 1.3 -> x2.08) and level offset 5 (REPLACES the authored 3), other camps identity; applied to Neon after the api deploy.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 15:15) + REGRESSION_OK 550/550 suites (Builds/data-regression.log, 2026-09-16 15:31); [raid-difficulty] and [tunable-defaults] green.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
