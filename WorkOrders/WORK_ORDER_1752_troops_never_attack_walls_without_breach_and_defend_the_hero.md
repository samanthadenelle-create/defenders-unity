# WORK ORDER 1752 — Troops NEVER attack walls unless Breach is on (10% fallback retired); a troop must break off to the hero's attacker

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, from two owner rulings during the IronBastion felt-test on tester build `2026.09.15.371206`.
**Silo:** `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` + `Assets/_Modules/Village/Troops/TroopController.cs` (the WO-1746 silo; WO-1746 is committed at `10c21b9bd`). ⚠ `TroopController.cs` ALSO carries WO-1748's UNCOMMITTED edits (`ReportVisualVerdict`, `:484-556`) — edit in place, never restore the file from HEAD. Off limits: `TroopFactory.cs` (1748), `RaidBaseDresser.cs`/`RaidNavBake.cs` (1749), `HeroHealth.cs` (1750), `SmartMobileCamera.cs` (1751), `EventTracker.cs` (1735).

## Owner rulings, verbatim (2026-09-15, supersede WO-1746 §1 / WO-1738 ruling 1)
1. ***"we need to set it so that the troops 100% ignore walls, unless explicitly told breach"***
2. Decision prompt, same minute — does the catapult follow the same rule? **"Siege still hits walls on its own."** So: the catapult keeps auto-attacking a blocking wall at full damage; every other troop never swings at a wall unless the Breach stance is ON.
3. ***"they are running around attacking walls while i am getting damaged and killed"*** — the lead's reading (owner to veto if wrong): a hostile that is damaging the HERO is an acquirable unit for every troop, regardless of the troop's own acquire radius — the warband defends the player before anything else. This is WO-1719's own rule (*"all together unless they have aggro"*) with the hero's aggro counting as the warband's.

**Retired by ruling 1:** `RaidAssaultAi.ReluctantWallDamageMultiplier = 0.1f` and the BLOCKED-no-Breach fallback that let a non-siege troop hit the nearest blocking wall at 10%. A blocked warband with Breach OFF and no siege now stands (or defends the hero); that is the owner's deliberate choice — DeepSeek's dead-end concern was raised and overruled.

## Evidence (captured, not theorised)
- Device logcat `09-15 13:40:59.705-706` (Grom's raid): seven non-siege troops in `phase=Breach in[peel=False …] routeObj=PathPartial … blocked=True wallDmgMult=0.10 has[unit=False,obj=False,wall=True]` — every one swinging at a wall under the 10% fallback — while at `13:46:29` the raid boss was `still steered at the hero` and the hero's HP bar read empty (WO-1750).
- `13:55:29 [Flow:TroopAI] id=troop-shieldguard role=tank IDLE/RALLY: no acquirable hostile inside radius=12.0m` / `troop-field-cleric … radius=16.0m` — the acquire sweep is a fixed radius around the TROOP; a hostile hitting the hero 20 m away is invisible to it, so the troop falls through to the wall bucket.
- `RaidAssaultAi.PickBucket` `:491` (WO-1746): `if (!breachStance && hasUnit && unitInAttackRange) return 0;` — units win only when a unit is already in THIS troop's range.

## The work
1. **Walls off without Breach:** in `RaidAssaultAi` (`PickBucket` / `SelectFocusBreach` / `WallDamageMultiplier`) a non-siege troop with `breachStance=false` never selects a `WallSegment` target and never applies wall damage — delete the 10% path rather than setting it to 0 (a zero multiplier still walks troops to walls). Siege (`siege=True`) keeps today's full-damage wall behaviour with or without Breach. The Breach stance behaviour from WO-1746 (persistent, auto-chaining, only AGGRO breaks it) is unchanged.
2. **Defend the hero:** expose "the unit currently damaging the hero" as an acquirable target for every troop (the seam: whatever `HeroHealth` / the damage pipeline already knows about the last attacker — find it, cite it; do not add a second damage listener if one exists). A troop with no unit in its own radius but a live hero-attacker targets that attacker; this out-ranks walls and objectives and does NOT break an active Breach stance unless the existing AGGRO rule says so (WO-1746 §4 — do not re-litigate).
3. **Trace:** the `RETARGET` line gains `heroAttacker=<id|none>`; the `wallDmgMult=` token now prints `0.00` for non-siege without stance (proves 1 on device).
4. **Regressions:** `WallBreachOrderRegression` — a case that a non-siege troop with `breachStance=false`, no unit in range and a blocking wall selects NO wall (today's Case 8 red-proof harness is the template); a case that siege still does; a case that a troop with an idle sweep and a live hero-attacker targets the attacker.

## Acceptance
- Headless raid: zero `wallDmgMult=0.10` tokens; every non-siege `breachStance=False` resolve has `has[wall=…]` irrelevant to its choice (target is a unit, the hero's attacker, or none); siege lines show walls at `1.00`.
- On device the owner sees troops peel to whatever is hitting her, and nobody hits a wall until she taps Breach.
- Lane flips this Status line and writes the `.RESULT.md`; the lead marks the retired clause in WO-1746 §1 / WO-1738.


## Lead decisions on the lane's four questions (2026-09-15, owner delegated decision authority for this window)

1. **A hero-attacker with a CLOSED route still loses to the objective — KEEP as the lane built it.** Overriding the WO-1438 reachability filter to steer a troop at an unreachable foe is the navmesh-edge freeze that ticket exists to prevent, and the lane's own red proof reproduced exactly that failure once walls stopped absorbing the blocked warband. "Out-ranks objectives" means priority among REACHABLE targets; it was never a licence to path into a wall.
2. **Towers shooting the hero are NOT adoptable — units only, as built.** Adopting a tower would send the warband at a STRUCTURE in the same breath as the ruling that stops them attacking structures. If the owner felt-tests and wants towers answered, that is a separate ruling with a separate ticket.
3. **Peel: a troop under tower fire walking off to the hero's attacker is ACCEPTED.** It is the literal reading of the owner's report - the warband defends the player first. Flagged for her felt test.
4. **The dead-end is the ruling working, not a regression.** A blocked warband with Breach off and no siege now stands. If "my troops are doing nothing" comes back, the answer is the Breach tap or a catapult - not a silent restoration of the 10% fallback.

Also noted, not acted on: `Assets/Editor/Regression/RaidAssaultAiRegression.cs` drives `PickBucket` through the 7-arg overload (`:100`, `:115`, `:132`, `:220-231`), so it defaults `otherStructIsWall:false` and its cases still pass unchanged. The suite count does not move.
