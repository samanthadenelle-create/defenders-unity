fix(dungeon): WO-1805 lanes A+C - the composed dungeon teaches its own lantern-oil mechanic; lantern balance on the tunables rail

Owner 2026-09-16: "nobody understands why the torch runs out and why it suddenly becomes dark ... a bug that I didn't want to report". The teach existed in the owner's own words (dun_torch_warden) but only in the legacy Healer's Cottage no player door reaches; composed dungeons skipped it, the starter dungeon has one oil stone for eleven rooms, and at 30 s of oil left the light collapses to ~1 m with a fog wall while the ambush multiplier arms. New dun_lantern_intro (Bryn, three lines) plays from ComposedDungeonHost after the oil HUD, one-shot per save, satisfied by reading it OR by the first refill (new Lantern.OilStoneUsed event); a one-shot "Your flame gutters - find an oil stone." toast on Lantern.FinalWarningEntered; four dungeon.* int knobs at identity (drain x100, stone refill %, still refill %, final-warning s) that defer to dungeon-balance.json while at default; the drain path finally traces (stone spent, flask empty, final warning, darkness armed, entry net with stones/rooms/ceiling). Lane B (more oil, a bag consumable, the torch doing something) stays SPEC.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
