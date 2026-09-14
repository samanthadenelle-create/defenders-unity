# WORK ORDER 1719 - RESULT (implementation lane, edit-only)

**Status:** IMPLEMENTED - awaiting lead gate 2026-09-14
**Branch:** `dev`. No Unity run, no gate, no build, no commit by this lane. No `.unity` scene touched.
**Fence honoured:** `StructureHitReaction.cs`, `AdminOverlay.cs`, `TroopRally.cs`, `RaidBaseGenerator.cs`,
`RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` and `WallSegment.ApplyDamage`'s maths are
**untouched** - `git status` confirms none of them is in this lane's changed list.

---

## 1. Changed files

| File | What changed |
|---|---|
| `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` | **NEW** - the one explicit player breach target |
| `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` | `SelectFocusBreach` override overload + `RallyHoldsMarch` 4-arg overload |
| `Assets/_Modules/Village/Troops/TroopController.cs` | `SharedBreachFocus` answers the order before its cache; the local scan and the rally march read it |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | Breach button + mode + wall tap + the three clears |
| `Assets/Editor/Regression/WallBreachOrderRegression.cs` | **NEW** - `[wall-breach-order]`, marker `WALL_BREACH_ORDER_OK` |
| `Assets/Editor/Regression/DataRegression.cs` | **ONE line**, `:1474` |

⚠ `RaidDeployController.cs` and `TroopController.cs` already carried **other lanes' uncommitted edits**
when this lane opened them (a `Food` -> `Stone` economy rename, and the `ToggleRally` clear-on-off
change). Every edit here is additive and none of those hunks was disturbed - the lead should stage by
explicit path and expect a mixed diff in those two files.

---

## 2. The override mechanism, exactly

**`RaidAssaultAi.SelectFocusBreach(walls, muster, explicitFocus)`** (`RaidAssaultAi.cs:243-268`, the early return at `:247`) - a new
3-arg overload whose **entire addition is one early return**:

```
if (explicitFocus != null && explicitFocus.IsAlive) return explicitFocus;
```

The existing most-damaged / nearest-muster body below it is **byte-for-byte unchanged**, and the old
2-arg signature survives as `SelectFocusBreach(walls, muster) => SelectFocusBreach(walls, muster, null)`
(`:216-220`), so every existing caller and `RaidAssaultAiRegression.Case_FocusBreach_MostDamagedWins`
keep passing against unmodified logic. That is deliberate: an "override" implemented by restructuring
the selection method could not be proven not to have moved the fallback.

The explicit pick is **not required to be in the candidate list** - the scene scan is 0.4 s-cached and
can trail a tap by a frame; refusing the order in that window would be an invisible, intermittent
failure. Liveness is checked instead.

**Where the order enters the live path:** `TroopController.SharedBreachFocus`
(`TroopController.cs:1170-1190`) reads `TroopBreachOrder.Target` **before its own 0.4 s cache** and
returns it directly. Answering ahead of the cache is what delivers the owner's "all together": every
`phase=Breach` troop resolves the same panel on its **next update**, not up to 0.4 s later in whatever
order they happen to tick. A new `_sharedBreachOrderVersion` field (`:51-55`, compared at `:1192-1196`)
invalidates the cached automatic pick the moment an order is set **or cleared**, so the fallback also
resumes immediately. The local overlap scan passes the same order through at `:968-973`.

**The aggro exclusion needed no code at all, and that is the point.** `peelThreat` ->
`ResolvePhase` -> `RaidAssaultPhase.Peel` -> `PickBucket` returns bucket **0** (the unit), so the
resolved wall is never an aggro'd troop's winner. Nothing reaches into a peeling troop's target; the
existing phase gate already answers it. Case 4 of the regression pins both halves.

**One scope addition, named rather than smuggled:** `RaidAssaultAi.RallyHoldsMarch(rallySet, arrived,
peel, hasExplicitBreachOrder)` (`RaidAssaultAi.cs:190-210`), consumed at `TroopController.cs:961-967`.
Phase and rally are independent axes - a troop walking to a flag IS phase Breach - so without this the
ticket's own acceptance bullet ("every phase=Breach troop retargets in the same update") fails for the
whole warband whenever a rally is set, which mid-raid is most of the time. It releases the march **only**
for a panel the player explicitly tapped; the implicit wall-ring farm the owner banned on 2026-09-12 is
untouched, and WO-1717 §6A asks for exactly this ("RallyHoldsMarch does not suppress an EXPLICIT breach
order"). `TroopRally.cs` itself is **not** edited and no arrival logic is touched (the 6B fence).

---

## 3. What CLEARS an explicit pick - the choice and the reasoning

There are **two axes**, and keeping them separate is the ruling this lane had to make:

- **Mode** (`_breachMode`) - which tap verb is armed. Exclusive with Rally and tile-arm, exactly as
  those two already zero each other (`ArmTile`, `ToggleRally`).
- **Order** (`TroopBreachOrder.Target`) - the panel the warband is committed to.

**The order clears on, and only on:**

1. **The ordered segment dying** (`IsAlive == false`) - self-cleared inside `TroopBreachOrder.Target`,
   so the automatic most-damaged rule takes back over on the very next resolve. This is the primary
   clear and it is the owner's own "the most damanged [stays the fallback]" made literal; it is also
   WO-1717 §6A's "the order clears on wall collapse".
2. **Breach toggled OFF** - mirrors `ToggleRally`, whose in-code comment states the toggle is the
   player's *only visible way to cancel* a standing command. A button reading "Breach" while the
   warband still obeys an invisible old pick is the precise confusion that comment records.
3. **Retreat / raid teardown / raid entry** - the same three lifetime points as `TroopRally.Clear()`
   (`RaidDeployController.cs:156-158`, `:274`, `:1185`), plus a fake-null guard in the getter, because
   the static holds a **scene object**: a destroyed `WallSegment` read for `WorldPosition` throws, and
   `Collapse()` only sinks the ruin, so `IsAlive` alone never sees teardown.

**Explicitly NOT clears:**

- **A tap on empty ground while Breach is armed** - a no-op with a status hint. Losing a standing order
  to a stray tap is the failure the player can neither see nor undo; the toggle is the undo.
- **Arming a troop tile, or toggling Rally** - these turn the breach **mode** off but leave the
  **order**, exactly as `ArmTile` leaves `TroopRally.Point` alone. The player may re-aim a rally while
  the warband stays committed to a panel.
- **Tapping a different wall** - it *replaces*, it does not clear.

---

## 4. The HUD button - location and wiring

- **Face:** `"Breach"` / `"Breach ON"`, `ElarionUiKit.ButtonKind.Quiet` (the Rally kind), on the raid
  command bar, built at **`RaidDeployController.cs:2026-2027`** beside Deploy All and Rally. Wiring:
  `ToggleBreach` (`:938-955`) -> `HandleBreachTap` (`:887-936`) -> `TroopBreachOrder.Set`;
  label refresh `RefreshBreachButton` (`:957-961`); tap branch `Update()` `:690`.
- **Why the bar and not a new band:** the right column is `RaidReadoutBand` (y 0.475-0.835) over
  `RaidRetreatBand` (0.850-0.970); the only gap between the readout and this bar's status line (top
  0.360) is 0.115 of screen = **111 reference px** at `HudLayoutBands.CanvasReferenceSize`'s 965.4,
  **under `ElarionUiKit.MinTouchPx` (112)**. A face that cannot be touched is not a button.
- **The x re-split, and why it does not regress WO-1639.** The in-tree 0.715 / 0.955 edges are **not**
  device-proven minimums: `git diff` shows they came from the (still uncommitted) change that moved
  Retreat off the bar to `RaidRetreatBand`, after which the two survivors simply spread into the
  vacated 0.145. WO-1639's *proven* sizes were Deploy All 0.240 and Rally 0.160. New split, by the
  file's own formula (92% usable, ~0.68 em advance, bar span 1503.5 reference px):

  | face | band | width px | label | predicted seat |
  |---|---|---|---|---|
  | Deploy All | 0.410-0.630 | 330.8 | "Deploy All" (10) | ~44.7 pt |
  | **Breach** | **0.645-0.800** | **233.0** | "Breach ON" (9) | **~35.0 pt** |
  | Rally | 0.815-0.955 | 210.5 | "Rally ON" (8) | ~35.5 pt |

  All clear `ElarionUiKit.FontFloor` (30) with margin, so no face reaches the ellipsis; the narrowest
  is 210 px, far over MinTouchPx. **The bar's own edges and the derived `FaceY0`/`FaceY1` do not move**,
  so `DeployBarBand`, `DeployStatusBand` and every y-band case in `RaidHudThumbBandRegression` are
  untouched - only x within the bar changes, exactly as WO-1639 did it.
- ⚠ **Those seat sizes are ARITHMETIC, not measurement.** The measured half is permanent and already
  wired: `LogFaceFit("breach", _breachButton)` added to `WO1639BarProbe` (`:2098`). **The lead's
  headless capture / device frame is what proves the face fits** - this lane could not run Unity.

---

## 5. Instrumentation (CLAUDE.md §12 - permanent, never stripped)

- `TroopBreachOrder.Set` / `DropInternal` - `FlowTrace.Step("RaidAI", "BREACH ORDER set|cleared ...")`
  with the panel name, hp and version, so the device log says which panel was ordered and when.
- `SharedBreachFocus` - the existing throttled `breach-focus` line now carries **`source=order`** vs
  **`source=auto`** (`TroopController.cs:1174-1186` `source=order`, `:1214` `source=auto`). **This is the line that proves the owner's
  live-retest bullet**: the WO-1717 capture's 76-retarget thrash should read `source=order focus='<the
  tapped panel>'` and stop moving.
- A missed breach tap logs the collider it hit and says the standing order is unchanged.
- No `FlowTrace` / `Guard` call was removed anywhere.

---

## 6. Regression

`Assets/Editor/Regression/WallBreachOrderRegression.cs` - `[wall-breach-order]`, markers
`WALL_BREACH_ORDER_OK` / `WALL_BREACH_ORDER_FAIL`. Registered with **ONE** line at
**`Assets/Editor/Regression/DataRegression.cs:1474`** (directly under the `raid-assault-ai` suite;
nothing else in that file was touched, other lanes have uncommitted edits in it).

Six cases, all pure except the wiring case - no scene, no play mode:

1. `Case_NoOrder_AutoMostDamagedStillWins` - **the fallback**: 2-arg and 3-arg-with-null both pick the
   most-damaged wall, and the nearest-muster tie-break survives.
2. `Case_ExplicitOrder_OverridesMostDamaged` - the WO-1717 §3d pairing: a **full-HP, far** tapped panel
   beats a 12 hp wall sitting at the muster. Also proves the order wins when the cached scan has not
   caught up with it.
3. `Case_CollapsedOrder_FallsBackToAuto` - a dead order returns the warband to the automatic rule.
4. `Case_WarbandRetargetsTogether_AggroTroopKeepsItsFight` - **the ticket's fixture**: four
   breach-phase troops at four scattered musters (so a per-troop nearest rule would give four different
   answers) all resolve the ordered segment; a fifth with `peelThreat=true` resolves **its own foe**.
   Plus an inverse guard - that same troop *without* aggro does take the wall - so case 4 is proving
   the peel gate and not an accident of the fixture. Resolution goes through the *same* call chain
   `TroopController` uses (`SelectFocusBreach` -> `RallyHoldsMarch` -> `ResolvePhase` -> `PickBucket`
   -> bucket switch), assembled in one helper so a fixture troop cannot drift from the real order.
5. `Case_RallyMarch_ReleasedByExplicitOrderOnly` - the implicit ring-farm suppression still holds; only
   an explicit order releases it.
6. `Case_OverrideWiredIntoTheLivePath` - source-text proof that `SharedBreachFocus` consults
   `TroopBreachOrder`, that the HUD sets **and** clears it, and that **the default tap is unchanged**
   (both the `_breachMode` gate and the `_rallyMode` -> `HandleRallyTap` line must still be present).

**RED PROOF (stated, not run - this lane cannot fire Unity):** deleting the early return in
`SelectFocusBreach` fails cases 2 and 4. Deleting the `TroopBreachOrder` read in `SharedBreachFocus`
fails **only** case 6 while 1-5 still pass - which is exactly the "a pure rule nothing calls" failure
case 6 exists to catch.

---

## 7. Checks run by this lane

```
python tools/gate_brace.py <6 files>
GATE_BRACE_SUMMARY bad=0 of 6
GATE_BRACE_EXIT=0

NUL scan (embedded/trailing \x00, CompileGate.Run's WO-434 rule)
NUL_CLEAN x6 - NUL_SCAN_SUMMARY bad=0 of 6, NUL_EXIT=0
```

---

## 8. Left for the lead / PO, deliberately not done

- **`COMPILE_GATE_OK` + `REGRESSION_OK` on a fresh log**, and a device/headless frame of the raid
  command bar - the only proof the third face seats (see §4's warning).
- **No wall reticle / highlight was built.** WO-1717 §6A wanted one; WO-1719's acceptance does not ask
  for it, and the owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) so a tint
  would be the wrong affordance anyway. The order is currently read back only from the status line and
  the `source=order` trace. **Recommend a follow-up WO** for a motion-based marker on the ordered panel.
- **The owner's live retest bullet** (troops stop thrashing) - felt-verify + close is the PO's, per §13.
