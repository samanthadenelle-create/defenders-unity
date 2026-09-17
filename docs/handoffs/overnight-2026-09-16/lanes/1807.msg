fix(raid): WO-1807 - corner posts and watchtowers no longer stack a floating wooden tower under the stone clad

Owner: "corners seem upside down ... towers are inverted" (second base, 19:55). RaidBaseDresser.ReplaceChildrenWith destroyed only children, but Tower_Medieval_Wood is one root object, so the Synty clad sat on a polyperfect tower floating 2.48 m up. StripRootArt + CladLocalBox + a RaidPostAudit (RAID_POST_AUDIT_OK 59, was 49 defects) and RaidPostOrientationRegression. All four RaidBase scenes rebaked through BuildAllRaidScenes -> OwnedTownChain.RebuildFromRaid (OWNED_TOWN_CHAIN_OK); OwnedTown/ArenaPractice IronBastion + template + generated ground mats ride along.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
