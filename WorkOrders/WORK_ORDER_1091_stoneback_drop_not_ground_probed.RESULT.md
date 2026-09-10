# WO-1091 RESULT - Stoneback biome drop ground-probe: IMPLEMENTED on HEAD

**Verified:** 2026-09-09 (SILO 0B verify-and-flip pass, read-only; no `.cs` touched)
**Landed in:** `6a5c7a36d`
**Ancestry proof:** `git merge-base --is-ancestor 6a5c7a36d HEAD` -> exit 0 (HEAD = `184c8ff06`, `dev`)
**Compile proof:** `Builds/compile-gate-recurring-dragon-spells.log` (2026-09-09 13:49) ->
`COMPILE_GATE_OK :: scripts compiled clean`.

## THE TRAP, ANSWERED

The working-tree note records `HollowRoadsDropInjector.cs` as **killed mid-rework**, "logic
incomplete". **`git diff f4e4630e3 6a5c7a36d -- <file>` (159 lines changed) was read in full and
judged COMPLETE and COHERENT.** Both halves of the ticket landed, the call-site signature change is
consistent, and every symbol resolves at HEAD.

## FILE:LINE PROOF AT HEAD (`Assets/_Modules/Village/World/HollowRoadsDropInjector.cs`)

**Half 1 - the unhonoured `BiomeRoads.ResolveDrops` contract is now honoured**
- `:387` `if (!NavMesh.SamplePosition(drop.Point, out NavMeshHit groundHit, ArrivalSampleRadius, NavMesh.AllAreas))`
  -> `FlowTrace.Fail(...)` + `Notify($"The road to {BiomeRoads.ZoneName(drop.Region)} is closed.")` +
  `return false`. **Fail-closed: a drop that cannot be grounded seats NO door.**
- `Vector3 groundedPoint = groundHit.position;` is what BOTH consumers get - the seam
  (`seam.targetPosition = groundedPoint`) and the arrival promise
  (`announce.PromisedPoint = groundedPoint`). One point, one authority; the drift test can no longer
  measure against a coordinate nothing uses.
- The seat trace names the correction: `"(GROUNDED from derived {drop.Point}, navmesh probe moved it
  {Vector3.Distance(...):F1}m)"`.
- Symbols confirmed at HEAD: `using UnityEngine.AI;` `:81`; `ArrivalSampleRadius = 12f` `:125`;
  `ArrivalSettleRadius = 8f` `:138`; `Notify(...)` used at `:277 :308 :397 :626 :720`.

**Half 2 - the alarm that named the wrong owner is fixed**
- `float closestDrift` is tracked through the settle loop (`if (d < closestDrift) closestDrift = d;`)
  and passed to `VerifyArrival(sceneName, hero, waited, closestDrift)` `:591`. The signature at `:599`
  matches and there is exactly one call site.
- The failure branch now yields THREE verdicts instead of the flat, false `"THE WARP DID NOT HAPPEN"`:
  `everReached` (settle poll saw the hero within the radius), the clamp signature
  (`promisedOutsideClamp && sittingOnClampEdge`, using a BAND not an equality because seq 4706 landed at
  x=-50.34 from a clamp to -50), and the genuine never-warped case.
- The `owner` string routes the reader to `[Flow:HeroLoco] playable-bounds CLAMP relocated the hero`
  rather than asserting the clamp - the file does not duplicate a line HeroLocomotion already emits.
- `HeroLocomotion`'s own defects stay in WO-1094 and were NOT folded in, as the WO required.

## WHAT THE OWNER FELT-TESTS

Walk all four biome road drops. Each should deliver you standing on walkable ground inside the
region the prompt named, with no 3 s settle timeout.

## WHAT IS **NOT** PROVEN - READ THIS FIRST

- **The probe may now REFUSE doors rather than deliver them, and that would look like a new bug.**
  `drop.Point.y` is `worldBounds.center.y` = **17 m** (`BiomeRoads.cs:420`), and the seat probe searches
  `ArrivalSampleRadius = 12 m`. If the actual navmesh at a drop's x/z sits lower than about y=5, the
  probe MISSES, the door is refused, and the player sees **"The road to <X> is closed."** with the arm
  dead-ending. That is the fail-closed design working - but it satisfies acceptance item 1 while
  FAILING item 2. **No ground height at any of the four drop points has been measured this session.**
  If the owner sees that toast, the finding is "the probe radius is too small for a 17 m derived Y",
  not "the fix did not land".
- **No runtime proof at all.** No biome door has been walked since the change; the settle poll has not
  been observed succeeding.
- **No regression pin was added** for the seat-time refusal or the three-verdict message.
- The `ClampHalfHint = 50f` / `ClampBandHint = 2f` constants are a hand-copy of
  `HeroLocomotion`'s `PlayableHalf`. They shape a diagnostic sentence only - no behaviour keys off
  them - so a drift there degrades the hint rather than breaking a door. Still duplicated state
  (CLAUDE.md S15) and worth folding into WO-1094.

---

## 2026-09-09 pins (edit-only PINS lane) - the seat-time refusal and the three-verdict message, pinned

**New suite:** `Assets/Editor/Regression/BiomeDropGroundProbeRegression.cs`
**Tag / markers:** `[biome-drop-ground-probe]` -> `BIOME_DROP_GROUND_PROBE_OK` / `BIOME_DROP_GROUND_PROBE_FAIL`
**Registration line (hand-back to the lead - MUST land INSIDE the START/END fence of
`Assets/Editor/Regression/DataRegression.cs`, in the same gate as the file):**

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "biome-drop-ground-probe suite", () => { if (!DeNelle.Editor.Regression.BiomeDropGroundProbeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[biome-drop-ground-probe] " + r); });
```

**Not a duplicate:** `BiomeRoadDropClassificationRegression` (`[biome-drop-classification]`, WO-1604)
owns the CLASSIFIER contract - "a drop is the region its prompt names, or it does not exist". Nothing in
the tree asserted the SEAT-TIME navmesh probe before this file (`grep "SamplePosition\|ArrivalSampleRadius\|groundedPoint"`
over `Assets/Editor/Regression/*.cs`, run this session: no hit in any suite for this injector).

**Acceptance items pinned:** item 1 (an ungroundable drop refuses the door and says so loudly) -> cases
`[one-authority]`, `[fail-closed]`. Item 3 (the timeout message names which of the three things
happened) -> case `[three-verdicts]`. Item 2 (the door delivers the hero inside the settle radius) is a
RUNTIME fact; case `[one-point]` pins its precondition (seam target and arrival promise both receive the
grounded point) and the delivery itself is **not** claimed.

**RED-first mutations:**

| Case | Mutation that FAILS it |
|---|---|
| `[radii]` | either radius const removed/renamed/turned into a serialized field (FAIL, not skip); a non-positive radius; or inverting them so the seat probe searches TIGHTER than arrival is judged |
| `[one-authority]` | the seat probe given its own literal radius (e.g. `5f`) instead of `ArrivalSampleRadius` - choosing the point by one figure and judging it by another |
| `[fail-closed]` | deleting the `if (!NavMesh.SamplePosition(drop.Point, ...))` branch; downgrading its `FlowTrace.Fail`; dropping the player-facing `Notify(`; or logging the miss and seating the door anyway (no `return false`) |
| `[one-point]` | `seam.targetPosition` or `announce.PromisedPoint` re-pointed at the raw derived `drop.Point`, or `groundHit.position` never captured |
| `[three-verdicts]` | dropping `closestDrift` from the `VerifyArrival(...)` call, or collapsing `everReached` / `promisedOutsideClamp` / `sittingOnClampEdge` back to the flat "the warp did not happen" verdict that was FALSE on seq 4706 |

**No copied constants:** `ArrivalSampleRadius` / `ArrivalSettleRadius` are read by **reflection**
(`GetRawConstantValue`), and the case pins the RELATION (`sample >= settle`), never a number - a retune
moves the assertion with the code.

**PartialSkip handling - the fail-closed risk this RESULT flagged is DECLARED, not hidden.** Case
`[ground-clearance]` emits
`RegressionOutcome.PartialSkip("ground-clearance", ...)`: the drop Y is still `worldBounds.center.y`
(verified at source this session - `Assets/_Modules/Core/World/BiomeRoads.cs:420`, note the path is
under `Core/`, not `Village/`), and the seat probe searches the reflected radius, so a walkable surface
further below the bounds centre than that radius reaches makes the probe MISS and the door refuse. The
suite still counts green (it asserted five other cases) while the log names the hole. **Whether that is
true at any of the four live drops is NOT decidable headless** - it needs a loaded scene with a baked
navmesh. The `centre y=17` figure is cited from an EARLIER run
(`Builds/starter-settlement-proof-r4.log:19075`, quoted in `BiomeRoadDropClassificationRegression`'s
header) and was **NOT re-measured this session**. If the owner sees "The road to <X> is closed.", the
finding is "the probe radius is too small for the derived Y", not "the fix did not land".

**Unproven (CLAUDE.md S11B):** RED-first is established **by construction** - every token asserted was
verified present in the producer this session (51/51 token checks over all three pin files, re-run
after the final edits). **This lane
has no Unity and executed nothing**: no `COMPILE_GATE_OK`, no `REGRESSION_OK`, and until the
registration line lands, `RegressionMarkerRegression` RULE 2 reds the run on an unregistered
`Run(out string)` file. Brace-balanced (26/26), NUL-free, parens balanced outside strings/comments
(116/116).
