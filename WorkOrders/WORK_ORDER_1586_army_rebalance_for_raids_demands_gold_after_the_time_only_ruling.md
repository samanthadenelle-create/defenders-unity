# WORK ORDER 1586 - Rebalancing the army for a raid says "need gold" for every change; the ruling is gold ONLY skips the wait

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:53, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the owner's report
**Silo / Lane:** Village/Troops muster - `Assets/_Modules/Village/Troops/ArmyMusterService.cs`, `ArmyMusterVM.cs`, `ArmyMusterPanel.cs`, `BarracksService.cs`; suite `TrainingCostsTimeOnlyRegression`
**Type:** EXISTING system, RULING VIOLATION (WO-1387, closed on the owner's Pass 2026-09-07T00:49)
**Priority:** P1 - blocks the raid loop for a player who has upgraded troops

## Owner, verbatim (2026-09-07 morning, Seeker 2026.09.07.359076)

> "i couldnt seem to rebalance my army for the raids. Now that I upgraded troops, I should be able to
> change out troops but everytime showed as need gold. But we agreed the one need for gold was if you
> didnt want to wait on troops to train"

## The ruling this must honour (WO-1387, verbatim head)

"training and troop upgrades cost TIME only - gold is spent only to skip the clock". Training charges
nothing ("Train one: 45s . Ready"); HIRE REINFORCEMENTS is the gold skip; pinned by
`TrainingCostsTimeOnlyRegression`.

## What the code says today (read 2026-09-07, `ArmyMusterService.cs`)

- `:187` `p.Cost.Gold += def.CostGold * row.Count;` - the muster PROJECTION sums a per-unit gold cost.
- `:198` `p.Affordable = state != null && state.Resources.Coins >= p.Cost.Gold;` - and the plan is
  "affordable" only if the coin balance covers it. So a composition that swaps units into the army is
  priced in gold and the panel reads "need gold" whenever coins are short - the exact thing WO-1387 retired
  for training. `ArmyMusterVM.cs:93` exposes `GoldBalance` for that face.
- Whether the SWAP path (replace trained troops of one kind with another when the player already owns the
  upgraded kind) goes through this projection, or through `BarracksService.EnqueueTraining` (which is
  time-only), is NOT proven - instrument it.

## What to do

- **Instrument first:** `FlowTrace.Step("ArmyMuster", ...)` at plan projection (per row: def id, count,
  CostGold, whether the unit is already OWNED vs must be TRAINED), at `Affordable` evaluation (coins vs
  Cost.Gold) and at the panel's reason string. Reproduce headless: a save with upgraded troops, swap one
  kind for another, read the trace. Name the line that turns a swap into a gold demand.
- Then make the muster obey WO-1387: composing/rebalancing the army from troops the player OWNS costs
  nothing; troops that must be TRAINED to fill the plan cost TIME (queue) and the plan says so
  ("2 Archers: 1m30s"); gold appears ONLY on the skip verb (finish now / hire reinforcements), priced there
  and nowhere else. `Affordable` becomes "fits the cap and the queue", not "coins >= gold".
- Extend `TrainingCostsTimeOnlyRegression` (or add a case beside it) to pin: a swap between owned units
  projects Cost.Gold == 0 and Affordable == true with zero coins; the skip verb is the only gold price.
- Keep `def.CostGold` in the data if the skip price derives from it - do not delete a live catalog field;
  re-point the reader.

## Not to touch
- The raid deploy screen and the loadout bank (`ArmyLoadoutService`, WO-934) beyond what the projection
  feeds them.

## Acceptance
- Headless: with 0 coins and an upgraded roster, rebalancing between owned kinds is accepted and shows no
  gold; training new kinds shows time; the skip verb shows gold.
- Regression green, REGRESSION_OK n/n on a fresh log. Owner felt-test on the Seeker closes.
