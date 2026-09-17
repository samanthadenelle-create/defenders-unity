feat(raid): WO-1804 - Enemy Battle Plans after wave 2 introduce the first raid; Bastion Plans from the Ember Deep boss unlock the Iron Bastion

Owner 2026-09-16: introduce raids the way the Castle Defense Plans drop introduces plans after wave 3; later the same day: the video's act 2 starts at the end of a dungeon - kill the boss, the plans open the Bastion, go raid it; "Make it the one that's the ember deep". Two plans kinds on the WO-1013 seam, every resolution by component (never a tag): Enemy Battle Plans spawn at the wave-2 clear beat, one-shot, skipped forever for a save that has raided; Bastion Plans drop from the boss of iron_bastion.plansDungeonId (dg_ember_deep, data). One reveal screen, spoils from the RaidSelectionVM projection, ONE CTA (build the Barracks / raid). iron_bastion is locked until the plans are held ("Find the Bastion Plans in The Ember Deep"), never re-locked for a save with prior Bastion contact. VFX via the owner-tagged Treasure_Aura key, no new spawner. Owner ruling later the same day: "keep the reveal in the boss room" - the reveal plays at the boss on pickup and its CTA routes through the sanctioned dungeon exit so the run is banked before the raid screen opens.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
