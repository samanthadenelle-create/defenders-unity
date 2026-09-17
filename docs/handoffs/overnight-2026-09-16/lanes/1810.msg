feat(raid): WO-1810 - a raid loss costs troops; a failed raid pays no loot

Owner rulings 2026-09-16: any troop killed is dead; a FAILED raid (timeout/wipe/hero death) loses 100% of the survivors; a player RETREAT loses 60% (nearest, floor one, rookies first); rebuild at the barracks; a fail pays NO loot (retreat keeps its damage-scaled share). RaidCasualtyPolicy (pure), ArmyStorage.RemoveOwned (first troop deletion path), RaidDeployController casualty + loot branch on the declared RaidExitOutcome, victory declared early, EndStateVM troops-lost line on victory/retreat/fail, HeroHealth copy corrected. Tunables raid.lossPctFail=100 / raid.lossPctRetreat=60 on the remote rail; this commit also carries WO-1811 army.dismissReturnPercent in the shared registry files (knobs 75 -> 78, manifest regenerated, TUNABLE_MANIFEST_GEN_OK). RaidCasualtyRegression cases A-F.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
