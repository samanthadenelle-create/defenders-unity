# WORK ORDER 1737 — Walls fall to a single hit: the tier-1 durability floor

**Status: FIXED — the hit-count ruling is landed, awaiting the owner's felt test**

> ⛔ **§7 (the concurrency fork) is NOT part of this ticket's status. It is minted separately as
> WO-1738 so the board carries a row for it.** Landing this one does not settle it — read §7 before
> judging the feel, because the value shipped here satisfies her hit-count ruling and does **not**
> fix the concurrent case.

**Minted:** 2026-09-15, from a live owner ruling during on-device testing.
**Silo:** wall durability (`Assets/_Modules/Village/Walls/WallSegment.cs`, `WallTierData.cs`) +
the two oracles that read the curve. ⛔ **File-disjoint from WO-1730**, which owns
`RaidAssaultAi.cs` / `TroopController.cs` — this ticket deliberately changes **no targeting
behaviour**, and §7 Branch B cannot be implemented from this lane.

---

## 1. THE RULINGS — owner, verbatim

> ***"walls fall with a single hit, the HP should be strong enough even at lowest level that it
> takes some damage to get a wall down. Think of CoC."***

She then ruled two decisions in the same session:

1. **A TIER-1 wall should take ~8-10 hits from a mid-tier attacker (the battlemage, 42 damage).**
   Today it takes 3. That is roughly a 3.5-4x increase in effective wall durability at the bottom tier.
2. **Siege KEEPS its 2x structure-damage multiplier** and stays the designated wall-breaker. After
   the change it must still breach clearly faster than anything else — but it must NOT one-shot.

And, on reading the first draft of the table below:

> ***"but thats assuming that wall needs hit 10 times, right, but if i have 10 leveled troops
> attacking same wall will be faster?"***

She is right, and §7 exists because of that question. It is **not settled**.

---

## 2. THE PROVEN CAUSE — it is not a missing HP field

From a real device capture:

```
[Flow:WallSegment] WallSegment 'Wall_Outer_SS_17' took 42 raw -> 42 effective
  (attack, tier 1, toughDiv 1, bulwark 0 %, Hostile) -> damage 42/100 (58 % standing).
```

`toughDiv 1` is the whole bug. `WallSegment.ToughnessFor` read (2026-09-15, at source):

```csharp
return Mathf.Pow(TierToughnessStep, t - 1);   // step = 1.6f
```

⛔ **At tier 1 that is `step^0 == 1.0` for ANY value of the step.** A base wall took its hit
completely unreduced, and **no re-tune of `TierToughnessStep` could ever have fixed it** — raising
the step only widens the spacing *between* tiers while leaving the bottom rung exactly as
paper-thin as reported. Any ticket that proposes "just raise the step" is proposing a no-op.

Incidental, and it defuses a false alarm for the next reader: **`_SS_` / `_SN_` / `_SE_` in a raid
wall's name is the SIDE, not the tier.** `Wall_Outer_SS_17` is the south side of the outer ring.
Its `tier 1` is correct — the Forsaken Camp authors `wallTier: Wood`, and `WallTier.Wood == 1`
(`WallTierData.cs:32`). The raid tier ladder is genuinely in play (Iron == 2, ReinforcedSteel == 3),
so a single floor change scales every camp coherently.

---

## 3. THE MECHANISM, AND THE THREE OPTIONS REJECTED

**Landed:** one new named constant, `WallSegment.BaseToughness`, folded into the existing curve:

```csharp
return BaseToughness * Mathf.Pow(TierToughnessStep, t - 1);
```

Effective HP measured in RAW incoming damage = `MaxHp * ToughnessFor(tier)`.
**Read the constants; this document deliberately does not restate the product** (CLAUDE.md §8 — a
number copied into a doc is the bug, not the value inside it).

### Rejected — raise `MaxHp` off the hardcoded 100

The 0-100 track is a **contract** with at least four systems, every one read at source today:

| Consumer | What it does with the 100 |
|---|---|
| `RepairTarget.DamageFraction` (`RepairTarget.cs:141`) | `Mathf.Clamp01(_wall.Damage / 100f)` — a literal |
| `RepairTarget.RepairFull` (`:236`) | `_wall.Repair(100f)` — commented "the damage track is 0..100 by contract" |
| `StructureBurn.TryResolve` (`StructureBurn.cs:443-447`) | resolves a `WallSegment` and sizes its tick off a **hardcoded** `maxHp = 100f` |
| `RaidScoringRegression` (`:225-227`) | **FAILS the gate** if raid scoring is ever written against `Damage / 100` instead of `HpFraction` |

Plus `StructureFeedbackRegression` case 2 asserts `WallSegment.MaxHp == 100`. Changing the track
means touching all of it and buys nothing the divisor does not.

### Rejected — change the exponent base

Mathematically cannot move tier 1 (§2). This is the trap, not the fix.

### Rejected — author per-tier wall HP in `walls.json`

**It would not reach the walls in the owner's report.** `walls.json`'s tier table is the *player
perimeter* ladder (`level` 0..3, `heartDamageMultiplier` / `targetHeight`), and the only code that
reads a tier out of it — `ApplyTierBlockerHeight` — **early-returns unless the segment carries
`PlacedStructure`** (`WallSegment.cs:201`), which raid walls never do. A new field there would be
read by nothing on a raid wall. Canonical JSON is also binary-edit-only here with a
`StreamingAssets` mirror to keep in sync — cost with no benefit.

### Also landed: a SECOND toughness table is deleted

`WallTierData.s_toughness` was `{ 1f, 1f, 1.6f, 2.56f }`, described in its own comment as a
"read-only convenience" mirror of WallSegment's curve. **It had zero consumers** (its own
`ToughnessFor` had no callers), and the moment the real divisor moved it would have gone on
reporting the old numbers to any UI that ever read it. Deleted, and `WallTierData.ToughnessFor`
now delegates to `WallSegment.ToughnessFor`. Same duplicated-state class as CLAUDE.md §2's stale
WO block and §5's retired dependency table — **the cure is deleting the copy, never a better copy.**

---

## 4. BEFORE / AFTER — SINGLE ATTACKER (the ruled metric)

Hits to fell one wall segment, and the seconds that takes at the unit's authored attack interval
(first hit at t=0, so TTK = (hits-1) x interval). Damage = `attackDamage x structureDamageMult`,
read from `troops.json`. Hero = `PlayerAttackController._baseDamage` 52.5 at `_attackCooldown`
0.6 s — **note: the real hero figure is `_baseDamage * weaponMult` (`:756`); weaponMult 1.0 is
assumed here, so a weapon makes the hero faster than shown.**

| Attacker | dmg/hit | interval | **T1 before** | **T1 after** | **T3 before** | **T3 after** |
|---|---|---|---|---|---|---|
| **Hero** | 52.5 | 0.6 s | 2 / 0.6 s | **8 / 4.2 s** | 5 / 2.4 s | 20 / 11.4 s |
| **Battlemage** (the ruled probe) | 42 | 1.8 s | 3 / 3.6 s | **10 / 16.2 s** | 7 / 10.8 s | 25 / 43.2 s |
| **Siege Catapult** (48 x2.0) | 96 | 2.5 s | 2 / 2.5 s | **5 / 10.0 s** | 3 / 5.0 s | 11 / 25.0 s |
| Archer | 29 | 1.2 s | 4 / 3.6 s | 14 / 15.6 s | 9 / 9.6 s | 36 / 42.0 s |
| Echo Legionnaire | 28 | 1.0 s | 4 / 3.0 s | 15 / 14.0 s | 10 / 9.0 s | 37 / 36.0 s |
| Outrider | 18 | 0.9 s | 6 / 4.5 s | 23 / 19.8 s | 15 / 12.6 s | 57 / 50.4 s |
| Spearman | 16 | 1.1 s | 7 / 6.6 s | 25 / 26.4 s | 17 / 17.6 s | 65 / 70.4 s |
| Footman | 12 | 1.0 s | 9 / 8.0 s | 34 / 33.0 s | 22 / 21.0 s | 86 / 85.0 s |
| Shieldguard | 10 | 1.3 s | 10 / 11.7 s | 40 / 50.7 s | 26 / 32.5 s | 103 / 132.6 s |
| Field Cleric | 18 | 2.4 s | 6 / 12.0 s | 23 / 52.8 s | 15 / 33.6 s | 57 / 134.4 s |

**Both rulings are met on this metric.** Battlemage 10 (inside "8-10"); hero 8 (inside); siege 5
hits, never a one-shot, and fastest of any single attacker.

⚠ **Siege supremacy is thinner than the phrase suggests, and it is thin on HEAD too.** Structural
DPS: catapult **38.4**, echo legionnaire 28.0, archer 24.2, battlemage 23.3. The catapult leads the
best troop by 1.37x. **And the HERO, at 87.5 structural DPS, out-breaches the catapult by 2.3x.**
"Siege is the designated wall-breaker" is **already false today** and this change does not alter
that — the ratios are untouched. Flagged, not fixed: it needs its own ruling.

---

## 5. ⛔ TIME-TO-KILL UNDER CONCURRENCY — THE NUMBER THAT DECIDES THE FEEL

The owner's question is the right one. Seconds to fell **one** segment with the whole warband
focused (the Breach order, `TroopBreachOrder` / `RaidAssaultAi.SelectFocusBreach`, exists to do
exactly this: *"all together unless they have aggro"*).

**Army cap, read at source:** `ArmyStorage.DefaultMaxArmySize = 10`, plus `armyCapBonus` from perks.
The only authored bonus in canonical data is `barracks-expanded-capacity`, **+5**
(`building-tiers.json:42`) — so the cap is **10, or 15 with the perk**.
⚠ **An owner screenshot reads `Troops 11/13`. 13 is not 10 and not 15, and I cannot account for it
from the data** — there is no second `armyCapBonus` row in any canonical file. Flagged as unproven;
15 is used below as the worst case I can source.

⚠ **Also stale:** `docs/RAID_BALANCE_AUDIT_2026-09-06.md` §A.1 lists slot costs of 2/3/4 for the
heavier troops. **Every troop in `troops.json` is `slots: 1` today.** So a 15-cap army is 15 *units*
of any composition — which makes the concurrency ceiling markedly higher than that audit implies.

| Warband (all focused on one panel) | struct DPS | **T1 before** | **T1 after** | **T3 after** |
|---|---|---|---|---|
| Owner's actual 10 (7 Footman + 3 Archer) | 156.5 | **0.6 s** | 2.6 s | 6.5 s |
| 10 Archers (cap 10, best day-one) | 241.7 | **0.4 s** | 1.7 s | 4.2 s |
| 11 troops @ ~30 dmg / 1.0 s | 330.0 | **0.3 s** | 1.2 s | 3.1 s |
| 15 Archers (cap 15) | 362.5 | **0.3 s** | 1.1 s | 2.8 s |
| 15 Echo Legionnaires (best non-siege) | 420.0 | **0.2 s** | 1.0 s | 2.4 s |
| 14 Legionnaires + 1 Catapult | 430.4 | **0.2 s** | 0.9 s | 2.4 s |
| ...the same at Barracks T6 (+38% dmg) | 594.0 | **0.2 s** | 0.7 s | 1.7 s |
| **Hero alone** | 87.5 | **1.1 s** | 4.6 s | 11.7 s |

### ⭐ THE FINDING, AND IT EXPLAINS THE REPORT COMPLETELY

**On HEAD, concurrent TTK is 0.2-0.6 seconds at every tier, and the hero alone fells a tier-1 wall
in 2 hits / 0.6 s.** The owner's *"walls fall with a single hit"* is not an exaggeration and needed
no re-derivation — it is what the data says, from both the solo and the concurrent seat.

**And the shipped value does not fix the concurrent case.** 0.9-2.6 s is still a wall that
evaporates. The single-attacker ruling and the concurrent reality are two different targets.

*Method caveat:* the sum-of-DPS is **exact** for ranged bands — archer 14 m, battlemage 16 m,
catapult 26 m all reach one ~4 m panel — and an **upper bound** for melee-heavy bands, where
footman range 2.5 m physically limits how many bodies touch one panel. The worst cases above
(archers, legionnaires) are real; the owner's own 7-Footman row is overstated.

### WO-1730 interaction — it delays the concurrent case, it does not remove it

`WORK_ORDER_1730` rules that troops prefer nearby hostile UNITS over walls in every phase. That
**reduces how often a full warband is free to chew masonry while the garrison lives**. But:

- **Once the garrison is dead, every troop returns to the wall.** Full-warband concurrency is
  therefore the *end of every wall fight*, not a rare edge — so the worst-case row above is reached
  routinely, just later.
- I **cannot pin how much time units-first buys**: it depends on garrison positioning and on the
  unruled "what is nearby" (WO-1730 §3 Q1 offers attack range / 6 m / a 16-25 m scan radius, which
  "give very different play"). Stated as a variable, not estimated.
- ⛔ **WO-1730 §3 Q2 is the same fork as §7 below, already recorded and unruled:** *"does the
  EXPLICIT breach order override the new rule? ... These two rulings can conflict and only the
  owner can settle it."*

---

## 6. THE RAID CLOCK — no risk at the shipped value

The clock is **180 s** (`RaidScoring.DefaultClockSeconds`, `ArenaMode.RaidTimeoutSeconds`). The
`1:40` in the owner's screenshot is time **remaining** mid-raid, not the budget.

WO-1723 Lane B means **one** breached panel is enough (*"i can now walk through destroyed walls"*).
At the shipped value that costs a warband **1-3 s** at tier 1 and **2-3 s** at tier 3; even the
solo hero pays 4.6 s / 11.7 s, and a lone catapult 10.0 s / 25.0 s. Against 180 s, and against the
audit's 90-140 s median-clear target, **this change does not endanger the clock.**

⛔ **That verdict is value-dependent and would flip under §7 Branch A** — see the numbers there.

---

## 7. ⛔ THE OPEN OWNER RULING — the design tension, with both branches priced

**This is hers to settle. This lane deliberately does not pick.**

The tension is real and structural:

- **In CoC, walls are tough AND most troops path around them.** Only wall-breakers attack walls, so
  "the whole army on one wall" barely happens — which is what makes tough walls feel right there.
- **Here, the Breach order is built to do exactly that.** WO-1719 shipped it on her own ruling, and
  `SelectFocusBreach` returns the tapped panel outright for every troop.

Those two designs pull opposite ways, and the hit-count ruling only prices the first.

### Branch A — tune for concurrent TTK

Set the floor so a full focused warband still needs a meaningful stretch. Using the mid-game
330-DPS band at tier 1, and expressed as a multiple of the **shipped** value:

| She wants a focused warband to need... | floor vs shipped | battlemage solo, T1 | **hero solo, T1** |
|---|---|---|---|
| ~5 s | x4.1 | 40 hits / 70.2 s | 32 hits / 18.6 s |
| ~8 s | x6.6 | 63 hits / 111.6 s | 51 hits / 30.0 s |
| ~10 s | x8.3 | 79 hits / 140.4 s | 63 hits / 37.2 s |

**Cost, stated plainly: Branch A strands the solo player.** At the ~10 s row a hero with no army
needs ~37 s of uninterrupted swinging per tier-1 panel, and at tier 3 **161 hits — 96 s, over half
the raid clock, for one wall.** §6's "no risk" verdict does **not** survive Branch A at the upper
rows; the clock would need its own ruling, which this lane will not touch.

### Branch B — CoC-shaped

Keep the floor at the shipped (hit-count-ruled) value, and make **ordinary troops not attack walls
at all** except when they are siege or under an explicit Breach order. WO-1730's units-first rule
is already most of the way there.

**There is a strong lever here that the data hands over: `troop-catapult` has `maxOwned: 1`.** So
"siege only" is capped at ONE catapult, and the default breach time falls straight out:

| Default breach (siege only, 1 catapult @ 38.4 DPS) | T1 | T2 | T3 |
|---|---|---|---|
| at the shipped floor | **10.4 s** | 16.7 s | 26.7 s |

That is squarely the feel she described, at the value she already ruled, with the solo hero still
able to open a panel in 8 hits. The Breach tap remains the "I want it now" override, and spending
the whole warband's time on masonry becomes a deliberate player choice.

**Costs of Branch B, stated:**
- It is a **behaviour** change on top of her existing WO-1730 ruling, in **WO-1730's silo**
  (`RaidAssaultAi.PreferUnit` Breach branch `:295-298`, `AllowNonObjectiveStructure` `:306-310`).
  ⛔ **Not implementable from this lane** — WO-1730's own text says the two must not run in one agent.
- It **dead-ends a warband with no siege and no Breach tap when the route is blocked**: today a
  Breach-phase troop falls through to the wall; under Branch B it would fall through to nothing.
  **It makes the Breach tap a mandatory verb**, which is a UX commitment, not just a rule.
- It settles WO-1730 §3 Q2 by implication (the order must override), so ruling one rules both.

---

## 8. THE DEFENDER SIDE — the same walls are the player's own

`Faction` is derived from `SceneOwnership`, so the player's Elarion perimeter is `Friendly` and
additionally gets the BULWARK talent reduction (up to -50%, `WallSegment.cs:361-366`). At the
shipped floor, a tier-1 wood wall against the town-wave roster:

| Wave enemy | dmg / interval | hits before | hits after | after, **with BULWARK -50%** |
|---|---|---|---|---|
| Orc Berserker | 10 / 1.2 s | 10 (10.8 s) | 40 (46.8 s) | 80 (94.8 s) |
| Orc Raider | 12 / 1.3 s | 9 (10.4 s) | 34 (42.9 s) | 67 (85.8 s) |
| Hollow Warrior | 10 / 1.3 s | 10 (11.7 s) | 40 (50.7 s) | 80 (102.7 s) |
| Hollow Brute | 24 / 1.8 s | 5 (7.2 s) | 17 (28.8 s) | 34 (59.4 s) |

**Verdict: acceptable, and probably desirable — but the pacing half is NOT proven.** What I can
prove: `Gate` carries its own HP and is untouched; the wall ring is navmesh-carved
(`WallNavObstacleInstaller`, and `Collapse()` disables the obstacles to hand the mesh back), so
agents **path to the gates by default** and only hit walls opportunistically via
`Enemy.ProbeForStructure`'s forward spherecast. Tougher walls therefore funnel waves to gates,
which is the CoC-shaped outcome. What I **cannot** prove from here: whether wave *pacing* still
works, because that needs a wave capture I am not permitted to run. **Recorded as unproven.**

**On scoping the change to Hostile walls only** — the seam exists and is one line (`Faction` is
already read in `ApplyDamage`). **I recommend against it.** An identical wall behaving differently
depending on who owns it is the confusing outcome, and the specific asymmetry would be backwards
from the player's seat: *your* wall would be 4x weaker than the enemy's identical wall.

### Side effect, closed as instructed: StructureBurn on walls

`StructureBurn.TryResolve` **does** resolve a `WallSegment` (`:443-447`), with `maxHp` hardcoded to
100, and ticks through `ApplyContactDamage` — which is now divided by the new floor. **Burn on a
wall therefore slows by the same factor**: its authored "50% to 0 in ~25 s" window becomes ~100 s.
Scope is narrow — the only igniter is `DragonBoss` fire (`DragonBoss.cs:1017`), and burn only
ignites at/below 50% HP, i.e. a wall already half down. **Town-side only, not raids. Flagged, not
changed** — it is a consequence of the ruling, not a defect, and re-tuning it is a separate call.

---

## 9. FILES CHANGED

| File | Change |
|---|---|
| `Assets/_Modules/Village/Walls/WallSegment.cs` | new `public const float BaseToughness`; `ToughnessFor` anchored on it; tooltip + WO-1480 comment corrected |
| `Assets/_Modules/Village/Walls/WallTierData.cs` | duplicate `s_toughness` table **deleted**; `ToughnessFor` delegates to `WallSegment` |
| `Assets/Editor/Regression/BuildEconomyRegression.cs` | `CheckWallTierCeiling` A: tier 1 must equal `BaseToughness` (was: must equal 1); B: legacy `{1,1.6,2.56}` re-pointed from **absolutes to RATIOS** |
| `Assets/Editor/Regression/WallDurabilityRegression.cs` | **NEW** — pins the ruling behaviourally |
| `Assets/Editor/Regression/DataRegression.cs` | registers `[wall-durability]` |
| `CLI_LANES_WO_NUMBERS.md` | minted 1737, bumped 1737 -> 1738 in the same edit |

**The two oracle edits are a ruling-driven re-point, not a weakening.** The old assertion *"a base
wall must take damage unreduced (x1)"* was a deliberate prior design statement and the owner
overruled it; asserting it would now pin the exact defect she reported. The ratio form keeps the
original guard's teeth — a re-tuned step still fails — and is immune to the floor's value, so it
never needs editing again if she re-tunes durability.

## 10. THE REGRESSION

`WallDurabilityRegression` — `[wall-durability]`, registered in `DataRegression.RunAll`.

1. **Behavioural, through the real component.** A real `WallSegment` at `SetTier(1)` takes real
   42-damage blows through the real `IDamageable` seam until it falls; the count must land inside
   the ruled 8-10 band. Asserted by **driving** damage, not by recomputing the formula — an oracle
   that re-derives `amount / ToughnessFor` would pass even if `ApplyDamage` stopped calling the
   divisor.
2. **The floor is the named constant; the tier spacing is untouched** (ratios, so a future re-tune
   does not need this file edited).
3. **Siege, off the real catalog:** the catapult's per-hit structural damage must be the max over
   every troop in `troops.json`, and strictly less than a tier-1 wall's effective HP.

⛔ **RED PROOF — ARGUED, NOT RUN.** This lane is edit-only and may not start Unity, so the failing
run was not executed. Against the pre-WO-1737 tree: case 1's wall collapses at hit 3 (42 x 3 = 126
on a 100 track), so "still standing after 7" is false; case 2 is **compile-red** (`BaseToughness`
did not exist). Case 3's no-one-shot half passed before and still passes — it guards against the
fix overshooting downward, and is not a restatement of the bug. A seat with Unity should stash
`WallSegment.cs` and convert this argument into a captured RED line (CLAUDE.md §11B).
