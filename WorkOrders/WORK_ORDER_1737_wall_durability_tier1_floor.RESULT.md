# WORK ORDER 1737 — RESULT

**Outcome: the tier-1 durability floor is landed; the owner's hit-count ruling is met on the
single-attacker metric. The concurrency fork she raised is split out as WO-1738 and is UNRULED.**
**Date:** 2026-09-15. **Lane:** edit-only (no Unity, no gate, no bake, no git — by instruction).

---

## What was actually wrong

Not a missing HP field. `WallSegment.ToughnessFor` returned `Mathf.Pow(TierToughnessStep, t - 1)`,
which at tier 1 is **`step^0 == 1.0` for any step** — a base wall took every hit completely
unreduced, exactly as the device capture read (`toughDiv 1`). **No re-tune of the step could ever
have fixed it**; that is the trap this ticket exists to close, and it is written into the code so
the next seat cannot fall into it.

## The change

One new named constant, `WallSegment.BaseToughness`, anchoring the existing curve:
`ToughnessFor(t) = BaseToughness * Pow(TierToughnessStep, t-1)`.

**Tier RATIOS are unchanged** (tier 2 still exactly 1.6x tier 1, tier 3 still 2.56x), so every wall
upgrade buys precisely what it always bought. Only the floor moved.

**Deliberately NOT a larger `MaxHp`.** The 0-100 damage track is a contract with
`RepairTarget.DamageFraction` (`/100` literal), `RepairTarget.RepairFull` (`Repair(100f)`),
`StructureBurn` (hardcoded 100 for walls) and `RaidScoringRegression` (which fails the gate on
`Damage / 100` in scoring). Scaling the divisor changes durability while all four keep reading the
numbers they always read. The other two options are rejected with reasons in WO-1737 §3 — notably
`walls.json`, which **would not have reached the walls in her report at all** (`ApplyTierBlockerHeight`
early-returns without `PlacedStructure`, which raid walls never carry).

**Also deleted: a second toughness table.** `WallTierData.s_toughness` was a "read-only convenience
mirror" with **zero consumers** that would have started lying the moment the real divisor moved.
`WallTierData.ToughnessFor` now delegates to `WallSegment`. Same duplicated-state class CLAUDE.md
§2/§5/§8 each describe — the cure is deleting the copy, not maintaining a better one.

## Result on the ruled metric

| Attacker | T1 before | **T1 after** | ruled |
|---|---|---|---|
| Battlemage (42, the owner's named probe) | 3 hits / 3.6 s | **10 hits / 16.2 s** | ~8-10 ✅ |
| Hero (52.5 @ 0.6 s) | 2 hits / 0.6 s | **8 hits / 4.2 s** | ~8-10 ✅ |
| Siege Catapult (48 x2.0) | 2 hits / 2.5 s | **5 hits / 10.0 s** | fastest, no one-shot ✅ |

Full nine-troop x six-tier table, before and after, in WO-1737 §4.

## What the owner must still read

⛔ **The ruled hit-count is a SINGLE-attacker measure and does not describe the real case.** With the
Breach order focusing the warband, concurrent time-to-kill on HEAD is **0.2-0.6 s** and after this
change **~1-3 s** — still a wall that evaporates. That fork is priced in **WO-1738** and is hers to
settle; ruling it also settles WO-1730 §3 Q2.

**Raid clock: cleared at this value** (one panel costs a warband 1-3 s against a 180 s clock). That
verdict is value-dependent and would **flip** under WO-1738 Branch A.

**Defender side: acceptable and probably desirable, pacing half UNPROVEN.** Gates carry their own
HP and are untouched; the ring is navmesh-carved so waves path to gates by default and only hit
walls opportunistically. Whether wave *pacing* still works needs a wave capture this lane may not
run — recorded as unproven, not ticked. Scoping the change to Hostile walls only was considered and
**recommended against**: the specific asymmetry would make the player's own wall 4x weaker than an
identical enemy wall.

**Side effect, flagged not changed:** `StructureBurn` does reach `WallSegment` (`:443-447`) with a
hardcoded `maxHp = 100`, so burn on a wall slows by the same factor (its ~25 s window becomes
~100 s). Town-side only (the sole igniter is `DragonBoss` fire), and only on a wall already below
50%. A consequence of the ruling, not a defect.

**Flagged, out of scope:** "siege is the designated wall-breaker" is **already false on HEAD** — the
hero's 87.5 structural DPS out-breaches the catapult's 38.4 by 2.3x. Ratios were preserved, so this
change neither caused nor altered it. Recorded in WO-1738 §6.

**Stale canon (CLAUDE.md §15):** `docs/RAID_BALANCE_AUDIT_2026-09-06.md` §A.1 lists slot costs of
2/3/4 for heavier troops; **every troop in `troops.json` is `slots: 1`** today. Not corrected here
(the audit is a dated point-in-time ledger, frozen by §15) but it materially understates the
concurrency ceiling, which is why WO-1738 says so at source.

## Verification

- `python tools/gate_brace.py` (the port of the gate's exact rule) on all seven `.cs`:
  **`GATE_BRACE_SUMMARY bad=0 of 7`**, exit 0.
- Raw brace counts, all balanced, **0 NUL bytes** in each: WallSegment 69/69, WallTierData 42/42,
  BuildEconomyRegression 580/580, WallDurabilityRegression 67/67, DataRegression 1220/1220,
  StructureBurnRegression 120/120, StructureFeedbackRegression 59/59.
- Board parse verified without touching the live tree (imported `classify_status` /
  `status_contradiction` from `tools/board_build.py`): **1737 → `Fixed`**, **1738 → `Blocked`**,
  contradiction `''` on both. (`AWAITING OWNER RULING` as a *leading* phrase parses as `Unlabeled`,
  which `docs/BOARD.md` defines as a defect — hence the `BLOCKED -` lead on 1738.)
- WO-number collision check: `ls WorkOrders/WORK_ORDER_173[6789]*` shows only 1736 (another lane)
  and my 1737/1738. No collision (memory `parallel-worktree-lanes-collide-on-wo-numbers`).
- **No bake is owed.** `BaseToughness` is a code constant; what raid scenes serialize is `_tier`,
  which is untouched. Do not schedule a re-bake for this.
- ⛔ **No Unity gate was run — this lane may not start Unity.** `COMPILE_GATE_OK` and
  `REGRESSION_OK <n>/<n>` are **OWED** and are the lead's to run on the combined tree.

## The regression

`Assets/Editor/Regression/WallDurabilityRegression.cs`, registered as `[wall-durability]` in
`DataRegression.RunAll`. Three cases: (1) the ruling driven **behaviourally** through a real
`WallSegment` at `SetTier(1)` with real 42-damage blows — not by recomputing the formula, because an
oracle that re-derives `amount / ToughnessFor` would pass even if `ApplyDamage` stopped calling the
divisor; (2) the floor reads the named constant and the tier spacing is pinned as **ratios** (so a
future re-tune never needs this file edited); (3) siege supremacy and no-one-shot measured off the
real `TroopCatalog`.

⛔ **RED PROOF IS ARGUED, NOT RUN** (CLAUDE.md §11B). Against the pre-WO-1737 tree case 1's wall
collapses at hit 3 (42 x 3 = 126 on a 100 track) so "still standing after 7" is false, and case 2 is
**compile-red** because `BaseToughness` did not exist. Case 3's no-one-shot half passed before and
still passes — it guards against overshooting downward. A seat with Unity should stash
`WallSegment.cs` and convert this into a captured RED line.

## ⭐ THE BLAST-RADIUS SWEEP — every suite that DRIVES damage into a WallSegment

Grepping `ToughnessFor` callers was **not** sufficient and nearly shipped a red gate: any edit-mode
suite that puts a damage amount calibrated to the 0-100 track into a real `WallSegment` now lands a
quarter of it. `grep -rn "AddComponent<WallSegment>" Assets/Editor Assets/Data/Tests Assets/_Modules`
returns 16 sites; each was read for the amount it drives and the assertion it makes.

**Only a literal in the 100-399 raw band can break** (anything >= a tier-1 wall's effective HP still
kills; anything that loops to a threshold still converges).

| Site | Amount driven | Verdict |
|---|---|---|
| `DestroyedStructureRegression:159, :189` | `ApplyContactDamage(100000f)` | ✅ still collapses |
| `RepairProbeRegression:208` | loops `0.5f` while `Damage < 99`, guard 4000 | ✅ converges in ~792 steps (its own comment already reasons about the divisor) |
| `RepairProbeRegression:296` | `ApplyContactDamage(100000f)` | ✅ still collapses |
| `StructureTargetableRegression:451, :453` | `1000f` on both damage seams | ✅ > tier-1 effective HP |
| `SurfaceImpactVfxRegression:228, :234` | `SetTier` only, no damage | ✅ unaffected |
| `BuildEconomyRegression:1539` | `SetTier` only (the clamp probe) | ✅ unaffected |
| `GridWallBuilder` / `PerimeterWallGenerator` / `RaidBaseGenerator` / `StructureFactory` | construction, no damage | ✅ unaffected |
| **`StructureBurnRegression:181`** | `ApplyContactDamage(0.05f)` | ⚠ **RE-POINTED** — see below |

**`StructureBurnRegression`'s scuff oracle was the one real hazard.** Its 0.05f probe is meant to
land "~0.05 points" and assert the wall becomes repair-eligible (`DamageFraction > 0.0001`). After
the change it landed a quarter of that and cleared the boundary by only a **25% margin** — and would
fall straight through it under WO-1738 Branch A, which prices floors up to 8x higher. The raw amount
now derives from `WallSegment.ToughnessFor(seg.Tier)`, so the **landed** amount stays 0.05 at any
floor and the oracle keeps testing the boundary its own failure message names.

**`StructureFeedbackRegression` case 2** computed `29f / (1.6f * 1.6f)` under a comment asserting
"= 11.33 points". It drives a **synthetic** fixture (it feeds the fraction it computes), so it would
have gone on **passing while describing a wall the game no longer has** — the §15 stale-canon failure
inside a green test. Now reads `29f / WallSegment.ToughnessFor(3)`.

**Player-felt check (the "my hits do nothing" risk).** `StructureHitReaction.MinDropFraction` is
`0.004f` (0.4% of the track) and it gates `_pendingDrop`, which **accumulates** — small hits coalesce,
they are never discarded. At tier 1 the weakest wave enemy still lands ~0.75% per hit, well clear. At
tier 3 a 4-damage acolyte lands 0.39%, so its tell fires every second hit rather than every hit — a
cadence change, not a silent one. `StructureBurnRegression`'s scuff oracle already pins that even a
0.05-point hit yields a non-zero tell ordinal.

## Two oracles re-pointed WITH the ruling

`BuildEconomyRegression.CheckWallTierCeiling` asserted tier 1 == x1 ("a base wall must take damage
unreduced") and the absolute legacy table `{1, 1.6, 2.56}`. Both would now pin **the exact defect the
owner reported**. Sub-check A now asserts tier 1 equals `WallSegment.BaseToughness`; sub-check B is
converted from absolutes to **ratios**, which keeps the original guard's teeth (a re-tuned step still
fails) and is immune to the floor's value. This is a ruling-driven re-point, not a weakening, and it
is written as such in the code comments.

## Files

| Path | |
|---|---|
| `Assets/_Modules/Village/Walls/WallSegment.cs` | new `BaseToughness`; curve anchored; tooltip + WO-1480 comment corrected |
| `Assets/_Modules/Village/Walls/WallTierData.cs` | duplicate `s_toughness` deleted; `ToughnessFor` delegates |
| `Assets/Editor/Regression/BuildEconomyRegression.cs` | two sub-checks re-pointed with the ruling |
| `Assets/Editor/Regression/StructureBurnRegression.cs` | scuff-oracle probe derived from `ToughnessFor` so the landed amount stays 0.05 |
| `Assets/Editor/Regression/StructureFeedbackRegression.cs` | case-2 expectation reads the real curve instead of a restated `1.6 * 1.6` |
| `Assets/Editor/Regression/WallDurabilityRegression.cs` | **NEW** |
| `Assets/Editor/Regression/DataRegression.cs` | `[wall-durability]` registered |
| `WorkOrders/WORK_ORDER_1737_wall_durability_tier1_floor.md` | this ticket, Status FIXED |
| `WorkOrders/WORK_ORDER_1738_wall_durability_concurrency_fork.md` | **NEW**, AWAITING OWNER RULING |
| `CLI_LANES_WO_NUMBERS.md` | minted 1737 + 1738, bumped to 1739 in the same edit |

⛔ **`BOARD.html` is NOT regenerated here** — `tools/board_build.py` rewrites status lines in the
live tree and this lane is edit-only. The lead regenerates and commits it with the work.
