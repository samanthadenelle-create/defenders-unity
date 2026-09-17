# WO-1821 RESULT — the dresser clad CATAPULT hosts with the kit's tower

**Status:** IMPLEMENTED
**Date:** 2026-09-17
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*` + `Assets/Editor/Regression/RaidPostOrientationRegression.cs`
**Not done by this lane (by rule):** gate, commit, push. PO felt-verify + close (CLAUDE.md §13).

---

## 1. TWO PREMISE CORRECTIONS — read these first

**(a) There is no green pill, and the green is now PROVEN, not guessed.** The ticket proposed three
suspects (`BuildFallbackTurret`'s primitive, a MagentaGuard placeholder, an unstripped host root). All
three are dead. `RaidPostAudit.ReportNeighbours` (new) names every renderer standing inside a
photographed post: across 12 posts in 4 scenes the only primitives are `KeepPlatform` (Cube) and
`RaidGround` (Plane), both legitimate. `RaidPostAudit.ReportOwnMeshes` (new) then names the post's own
sub-meshes, and settles the colour outright (`Builds/wo1821-audit-after.log`):

```
OWN 'Visual' mesh='building_watchtower_green'  mat='hexagons_medieval_URP'
OWN 'Visual' mesh='building_tower_base_green'  mat='hexagons_medieval_URP'
```

**The green is the clad's OWN MESH — authored `hexagon-green` kit art.** Not a pill, not a fallback.

**(b) Iron Bastion has no catapults at all** — `turrets for 'iron_bastion': … types [10xtower_arcane_spire]`
(`Builds/wo1820-rebake.log`). The frame that raised the ticket shows archer towers, not siege platforms.
Catapults exist only in `fortified_garrison` (6) and `raider_camp_small` (4).

## 2. The real defect, proven

`ReskinCombatArt` clad **every** `Watchtower_*` including the ten hosts the generator armed as siege
machines, destroying the authored siege art. Measured before the fix (`Builds/wo1821-diag.log`):
`fortified_garrison/Watchtower_Archer_0` is `tower_catapult` yet rendered `size=(1.38,3,1.39) ratio=2.16`
— a slender tower. Same class as WO-1617, same shape as WO-1619's "the dresser undoing the model the
generator had just fitted".

## 3. Rule implemented

> **A catapult host keeps its authored siege art and gets NO tower clad.**

`ReskinCombatArt` skips `ReplaceChildrenWith` when `RaidBaseDresser.IsSiegeHost` is true, which routes to
`RaidBaseGenerator.IsAuthoredSiegeMachine` — the ONE decider, no second id list in the dresser.
**Exactly 10 skips** on the rebake (`grep -c "authored SIEGE art kept"` → 10), matching
`[6xtower_catapult]` + `[4xtower_catapult]`.

**Both oracles exempt siege hosts from the clad pins and assert `≥1 renderer` instead** — a catapult
with neither clad nor art would be an INVISIBLE turret that still fires, and a silent skip would pass it
green.

⚠ **The regression keeps a SECOND COPY of the two siege ids** (`IsAuthoredSiegeId`) because
`DeNelle.EditorRegression` cannot reference `DeNelle.EditorWallTools` — importing the subject's own
predicate is what this file's header forbids. The divergence is **caught, not silent**: a third siege id
would arrive unclad, fall through to PIN 2 and RED as "no '/Visual' clad child". Written into the code.

## 4. Markers (fresh logs)

`Builds/wo1821-compile.log` **COMPILE_GATE_OK** · `Builds/wo1821-rebake.log` `baked 4 raid scene(s)`,
10 siege skips · `Builds/wo1821-chain.log` **OWNED_TOWN_CHAIN_OK** ·
`Builds/wo1821-audit-after.log` **RAID_POST_AUDIT_OK 63** · final compile + full suite: see WO-1822
RESULT (one shared run for both tickets).

## 5. Frames (opened)

`Builds/raid-post-audit/RaidBase_fortified_garrison_Watchtower_Archer_0.png` — **now a real catapult**
(wheels, throwing arm, frame) where a slender stone tower stood before. Before-state is the same path in
`Builds/wo1820-*` / `Builds/wo1817-before-newframe/`.

## 6. Not proven / open

1. **PO felt-verify on device** — all headless (§13).
2. **Whether catapults SHOULD be visible at those ten positions is still the owner's to confirm.** The
   lead ruled it for this lane and the code now honours the config author's own choice, but it changes
   what the player sees on 10 of 31 turrets in two scenes — she should see it once.
3. `RaidSpireSiegeRegression` / `RaidPolishSavedProof` still not read by this lane.
