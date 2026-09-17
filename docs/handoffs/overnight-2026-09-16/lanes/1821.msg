fix(raid): WO-1821 + WO-1822 - catapults keep their siege art (no tower clad) and corner posts render at wall top + 2.5 m

WO-1821: the dresser clad every combat host with the kit tower, so ten authored catapults (6 garrison, 4 camp) showed a
slender stone tower; ReskinCombatArt now skips siege hosts (RaidBaseDresser.IsSiegeHost -> RaidBaseGenerator
.IsAuthoredSiegeMachine, one decider), 10 skips on the rebake, both oracles assert >= 1 renderer on a siege host. The
"green pill" the lead saw is the hexagon-green kit art's own sub-mesh (ReportOwnMeshes names it), not a primitive.
WO-1822: CornerPost_* had no authored height (the generator deliberately never scaled them) and rendered clad-native
1.11 / 7.52 / 17.96 m; now wallH + CornerRise (RaidBaseGenerator.CornerCadenceHeight, 2.5 m - the garrison's own
7.52-over-5.00 reading, UNRULED default) -> 6.50 / 7.50 / 6.50. The gate pins the invariant (corner top 1-4 m above the
scene's tallest wall collider), not the constant. One rebake chain: "baked 4 raid scene(s)", OWNED_TOWN_CHAIN_OK,
RAID_POST_AUDIT_OK 63, red proof 28/63 (exactly the corners) then reverted byte-identical. Gate on fresh logs:
COMPILE_GATE_OK 01:10 (Builds/wo1822-compile.log), REGRESSION_OK 564/564 01:16 (Builds/wo1822-regression.log).
Frames: Builds/raid-post-audit/RaidBase_fortified_garrison_Watchtower_Archer_0.png (a catapult where a tower stood),
RaidBase_mage_enclave_CornerPost_Keep1_E.png (6.5 m, was 17.96).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
