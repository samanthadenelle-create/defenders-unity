feat(army): WO-1803 - the starter army is ten: five footmen and five archers

Owner 2026-09-16: "Instead of giving them three troops, because that's shit, let's give them ten troops. Let's give them five footmen and five archers." One knob (raid.starterArmySize, default 3 -> 10) with the split DERIVED (even, remainder to footmen) so no DB row can turn it into ten archers; fits the fresh ArmyStorage cap of 10 exactly; over-cap grants FlowTrace.Fail naming the cap; saves that already took the three-troop squad are not re-granted. The seventh registration site (RaidLootCurrencyRegression's map literal) moved with it.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
