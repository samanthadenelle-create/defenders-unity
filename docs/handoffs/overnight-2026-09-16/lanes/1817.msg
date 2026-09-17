fix(raid): WO-1817 + WO-1820 - watchtowers render at their authored cadence and the raid spire is 14.4 m in every base, not 14 cm

WO-1817: the dresser hung the clad as a child and never fitted it, so a watchtower rendered at hostLossyScale x
cladNativeHeight - 0.05 m in IronBastion, 0.86 m in mage_enclave, 7.52 m (unfitted) in the garrison; not one at the
fitted 4.80 m. Rule: the clad renders at the host's authored turret cadence (RaidBaseGenerator.TurretCadenceHeight)
and the CLAD moves to seat it, so collider, visual and muzzle agree; garrison muzzle margin 0.50 -> 2.99 m.
WO-1820: the RaidSpire (the win target) rendered 0.14 m in three of four bases because ReplaceChildrenWith applied the
clad prefab's own localScale twice (0.010 x 0.010); the camp was right by coincidence (scale 1). Rule: the clad renders
at RaidSpire.VisualHeight, the height the bake fitted and recorded for the collider (14.40 m in all four); one additive
getter on RaidSpire.cs. Hollow-pass guards in RaidPostOrientationRegression now fail by name (the lint only sees a
list named `failures`). RaidPostAudit photographs spires; GroundShot picks its side by linecast. All four RaidBase
scenes rebaked through BuildAllRaidScenes -> OwnedTownChain.RebuildFromRaid (OWNED_TOWN_CHAIN_OK, census 169),
RAID_POST_AUDIT_OK 63, red proof 35/63 then reverted byte-identical. Gate on fresh logs: COMPILE_GATE_OK 00:41
(Builds/wo1820-compile4.log), REGRESSION_OK 564/564 00:43 (Builds/wo1820-regression3.log). Named, not fixed: the
dresser clads catapult hosts with the kit tower (WO-1617 class); CornerPost_* still clad-native; CompileGate's
Packages/ classifier can be defeated by an interleaved log line. Frames: Builds/raid-post-audit/RaidBase_*_RaidSpire.png.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
