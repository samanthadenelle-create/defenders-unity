# WORK ORDER 1719 - Breach button + direct wall-segment tap to override auto-targeting

**Status:** READY TO IMPLEMENT - owner design ruling, RCA-lane groundwork already done in WO-1717
**Minted:** 2026-09-14 by the CLI lead (Fable seat), split out of WO-1717 Finding 2 per that RCA's own
recommendation (new feature, not a bug fix, needed owner rulings before any code)

## 1. Owner design ruling, verbatim (collected across several messages)

> "tap the wall segment directly, and it overrides or add a button for breach and select a wall
> segment" ... "the most damanged [stays the fallback]" ... "all together unless they have aggro"

Resolved design, in the lead's words, confirmed against the owner's exact phrasing above:

1. A new **Breach** button/mode gates the gesture - tapping a wall segment without it stays Rally
   (existing behavior, per WO-1717's finding that a raw world tap already means Rally). With Breach
   active, tapping a specific wall segment selects it as the breach target.
2. That tap **overrides** the existing automatic "most-damaged wall, ties by nearest muster" selection
   (`RaidAssaultAi.SelectFocusBreach`, `RaidAssaultAi.cs:194-219` per WO-1717's citation).
3. **The automatic most-damaged selection remains the fallback** when no explicit tap has been made -
   this ticket does not remove that logic, only makes it overridable.
4. On a tap, **every currently breach-phase troop retargets together** to the newly selected wall
   segment - **except** any troop that currently has aggro (the existing `peelThreat=True` /
   `phase=Peel` state seen live in WO-1717's device log capture) - an aggro'd troop keeps fighting its
   current threat and does not abandon it to chase the new wall target.

## 2. Where this plugs in (from WO-1717's already-proven groundwork - do not re-derive)

- There is currently **no wall-target verb at all**. World taps only reach `Deploy` and `Rally`
  (`RaidDeployController.cs:17`); `RaycastGround` falls through to `~0` on a wall tap
  (`TroopRally.cs:731-736`), silently becoming a ground muster point instead.
- The actual attacked wall is chosen scene-wide by `SharedBreachFocus` -> `SelectFocusBreach`
  (`RaidAssaultAi.cs:194-219`), and that pick **overrides the local scan unconditionally**
  (`TroopController.cs:961-966`) - this is exactly the override mechanism a Breach-tap needs to plug
  into, by writing the tapped segment into whatever `SharedBreachFocus` reads instead of only running
  its most-damaged/nearest-muster computation.
- Live confirmation (WO-1717 device capture, same session): a troop with 76 retargets sat stuck out of
  range of `Wall_Keep1_SE_10`, and every Breach-phase troop in that log window carried
  `has[unit=False,obj=False,wall=True]` with no reference to a SPECIFIC segment - consistent with
  "a wall" being chosen for them, never the one the player is standing at.

## 3. Scope

- Add a Breach button/affordance to the raid HUD (find the existing Rally/Deploy button pattern and
  match its shape - do not invent a new UI paradigm for one button).
- While Breach mode is active, a world tap on a `WallSegment` sets that segment as an explicit,
  player-chosen focus target - write it into `SharedBreachFocus` (or extend it with an explicit-override
  field) rather than adding a second, parallel targeting system.
- `SelectFocusBreach` must prefer the explicit player pick over its own most-damaged/nearest-muster
  computation when one is set, and fall back to that computation when it is not (or once the tapped
  segment is fully breached/destroyed - confirm and state what "clears" an explicit pick).
- On setting a new explicit focus, every troop currently in `phase=Breach` (not `peelThreat=True`)
  retargets to it in the same tick/update, matching the "all together unless they have aggro" ruling.
- A tap on a wall segment while Breach mode is NOT active must continue to behave exactly as today
  (Rally) - this ticket adds a mode, it does not change the default world-tap behavior.

## 4. What NOT to touch

- Do not touch WO-1717 Finding 1 (damage calculation - already correct, no defect) or re-litigate it.
- Do not touch the rally-arrival trace question (WO-1717's "6B", a separate, not-yet-confirmed
  hypothesis about `RallyArrivalEpsilon` - out of scope here, do not edit `TroopRally.cs`'s arrival
  logic speculatively).
- Do not touch `Assets/_Modules/Village/Vfx/StructureHitReaction.cs` or anything from the structure-
  feedback fix (WO-1717's "6C", a separate, already-dispatched lane) - file-disjoint, keep it that way.
- Do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` -
  unrelated, already shipped.

## 5. Acceptance criteria

- [ ] A Breach button exists in the raid HUD, matching the existing button pattern.
- [ ] With Breach active, tapping a `WallSegment` sets it as the explicit focus target; without Breach
      active, the same tap still behaves as Rally (unchanged).
- [ ] The explicit focus target overrides `SelectFocusBreach`'s automatic pick; the automatic pick
      remains the fallback when no explicit target is set (or after one clears).
- [ ] Every `phase=Breach` troop (not `peelThreat=True`) retargets to a newly set explicit focus in the
      same update; an aggro'd troop is unaffected.
- [ ] Headless or capture-based proof: a simulated Breach-tap on a specific wall segment results in
      breach-phase troops' target resolving to that segment, not the auto-picked one.
- [ ] A live device retest (owner) confirms troops stop thrashing (the 76-retarget pattern from the
      WO-1717 capture) once a segment is explicitly targeted.
