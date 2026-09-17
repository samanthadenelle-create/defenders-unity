# WORK ORDER 1738 — Wall durability: the concurrency fork (tough walls vs. the Breach order)

> ## ⚠ SUPERSEDED IN PART 2026-09-16 — the "only AGGRO breaks the stance" half is REVERSED by a later owner ruling (WO-1764)
> The owner felt-tested the shipped behaviour on **Iron Bastion**, APK `2026.09.16.371701`, and ruled,
> **verbatim: *"Units first even inside Breach"*** — any **reachable** defender beats the wall, even
> under a Breach order; walls only when **no** defender is reachable.
>
> **So the clause this ticket's status line spent a paragraph RETIRING is the one that is now live.**
> That paragraph is left exactly as written (§15 — dated tickets are frozen, never rewritten): it is
> the record of a real 09-15 ruling, which a later 09-16 ruling reversed after the owner saw it in the
> game. It was not a paraphrase error the second time round.
>
> What is unchanged: Breach is still a **persistent, auto-chaining stance**; **siege** still takes
> walls on its own; **aggro** still peels (it always did, via `Peel`, before the stance is consulted);
> and with **no defender reachable** the armed stance is still what opens the panel. What changed is
> the *priority* inside the stance. Implemented by
> `WorkOrders/WORK_ORDER_1764_troops_still_prefer_walls_over_nearby_targets_bastion.md`; the ruling now
> lives in ONE guard, `RaidAssaultAi.StanceOutranksReachableUnit`, read at both stance gates, and
> `WallBreachOrderRegression` Case 8 was rewritten (not deleted) to pin the new reading in both
> directions.

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:18:59, build 2026.09.17.373943). PRIOR STATUS: FIXED via WO-1746 (gated 2026-09-15: `COMPILE_GATE_OK` cg1746b, `REGRESSION_OK 534/534` reg1746; awaiting the owner's felt test) — RULED 2026-09-15: BRANCH B + 10% reluctant fallback; Breach is a PERSISTENT STANCE that auto-chains. Implemented by `WorkOrders/WORK_ORDER_1746_breach_stance_and_reluctant_wall_damage.md` (RaidAssaultAi / TroopController silo); compiled and gated 2026-09-15, NOT yet felt-tested; this ticket closes when the owner felt-tests. ⚠ **CORRECTION 2026-09-15 — the summary clause "units-first is the default inside it" was the LEAD'S PARAPHRASE and it is WRONG; it is retired.** It contradicted this ticket's own ruling body. WO-1746 flagged the disagreement rather than picking silently, the owner ruled on it, and her FINAL answer is the ruling body as written: **the stance holds until the player toggles Breach OFF or a hostile pulls AGGRO** (the existing `Peel` trigger — actually attacked, or closed to melee). A nearby hostile merely standing and watching does NOT break it; units-first is the default **OUTSIDE** the stance. She first answered "Any nearby hostile breaks the stance" and reversed herself within a minute — both answers are recorded verbatim in WO-1746 §4 so this is not re-litigated. `WallBreachOrderRegression` Case 8 pins the ruled behaviour, and its red proof has been EXECUTED (WO-1746 §4A/§4B-RED — it caught a real bug: the stance needed TWO gates, not one). This ruling ALSO settles WO-1730 §3 Q2 (explicit Breach order vs units-first): Breach is a stance the player toggles, and units-first is the default **OUTSIDE** it — inside it only AGGRO breaks the warband off the wall.

> ⚠ **SUPERSEDED IN PART 2026-09-15 (owner ruling, later the same day): the 10% reluctant wall-damage fallback is RETIRED** — *"the troops 100% ignore walls, unless explicitly told breach"*; siege (catapult) still auto-attacks walls. Implemented by `WorkOrders/WORK_ORDER_1752_troops_never_attack_walls_without_breach_and_defend_the_hero.md`. The Breach stance itself (persistent, auto-chaining, only AGGRO breaks it) is unchanged.
*(Board note: an earlier edit wrote `**Status: RULED ...**` — a label the board builder does not recognize and a colon outside the bold — which produced `BOARD_CHECK_FAIL 1 unlabeled`. Corrected to the exact `**Status:** <label>` form; "ruled" is recorded in prose, never as a status.)*

> **RULING RECORD (owner, 2026-09-15, via decision prompt, after the DeepSeek packet
> `logs/debug/DEEPSEEK_PACKET_wall_durability_fork.md` and its verified answer):**
> 1. **Branch B, with a 10% reluctant fallback.** Keep WO-1737's shipped durability. Ordinary troops
>    do NOT auto-attack walls while any hostile unit or reachable non-wall objective exists. Siege, and
>    any troop under an active Breach stance, attack walls at full damage. A warband that is BLOCKED
>    (no route to the objective) with no Breach active attacks the nearest blocking wall at **10%**
>    structural damage, so it never idles into a dead-end — the hero-dead / no-catapult / no-Breach case
>    DeepSeek named. Verified arithmetic: owner's warband 2.6 s → ~26 s; 15 Legionnaires 1.0 s → ~10 s;
>    hero and catapult unaffected (hero 4.6 s T1). The raid clock is untouched. Branch A is rejected.
> 2. **Breach is a persistent stance that auto-chains.** One tap = "we are breaching"; when the ordered
>    wall falls the warband keeps opening walls (today's self-clear → most-damaged/nearest behaviour
>    is KEPT) until the player toggles Breach off or a hostile pulls aggro. No per-wall tap tax under
>    the 180 s clock. Walls stay meaningful because the FALLBACK is what is slow; Breach is the
>    deliberate ~10x speed-up.
>
> **Verified before ruling, by the lead, at source:** `EnemyBrain.cs` carries ZERO references to
> `RaidAssaultAi` / `TroopBreachOrder` — wave enemies run a separate brain, so Branch B cannot change
> how waves attack the player's own walls. DeepSeek's largest "unconsidered" item is therefore a
> non-issue, and the wave-defence pacing caveat in §4 applies to Branch A only.
>
> **Still open, deliberately (not ruled here):** Breach commitment/cooldown (DeepSeek: "if Breach is
> free and spammable, B only adds a tap") — the fallback's 10% is the first lever; add a cost only if
> telemetry shows Breach-always is the optimal play. And siege identity (the hero out-breaches the
> catapult 2.3x) stays its own ruling.

*(Prior status, kept for the record:)* BLOCKED - AWAITING OWNER RULING: no implementation until she picks a branch (see §5)
**Minted:** 2026-09-15, from the owner's own follow-up question during the WO-1737 wall-durability pass.
**Silo:** design decision first. Branch A is a one-constant change in WO-1737's silo
(`WallSegment.BaseToughness`); Branch B is a behaviour change in **WO-1730's silo**
(`RaidAssaultAi.cs` / `TroopController.cs`). ⛔ **The two branches land in different lanes** — do not
assign this until the branch is chosen.

---

## 1. WHY THIS EXISTS

WO-1737 landed the owner's ruling that a tier-1 wall should take ~8-10 hits from a 42-damage
battlemage. She then asked, in the same session:

> ***"but thats assuming that wall needs hit 10 times, right, but if i have 10 leveled troops
> attacking same wall will be faster?"***

She is right, and the answer is that the ruled metric is a **single-attacker** measure while the
real case is **concurrent** — and this game deliberately maximises concurrency on one panel. The
Breach order (WO-1719, her own design: *"all together unless they have aggro"*) exists to focus the
whole warband on a single wall, and `RaidAssaultAi.SelectFocusBreach` returns the tapped panel
outright for every troop.

**This ticket is minted rather than left as a paragraph inside WO-1737 deliberately.** The identical
failure is recorded one ticket earlier in this repo's own banner: WO-1506's RESULT said "this needs
a client ticket", that sentence sat in prose inside a closed ticket, the board never saw it, nobody
minted it, and eight days were lost. The fork gets a board row.

---

## 2. THE MEASUREMENT — full table in WO-1737 §5

Seconds to fell ONE tier-1 segment with the whole warband focused, at the value WO-1737 shipped:

| Warband | struct DPS | **on HEAD (before)** | **after WO-1737** |
|---|---|---|---|
| Owner's actual 10 (7 Footman + 3 Archer) | 156.5 | 0.6 s | 2.6 s |
| 11 troops @ ~30 dmg / 1.0 s | 330.0 | 0.3 s | 1.2 s |
| 15 Echo Legionnaires (best non-siege) | 420.0 | 0.2 s | 1.0 s |
| Hero alone | 87.5 | 1.1 s | 4.6 s |

**On HEAD the concurrent case is 0.2-0.6 s at every tier — her "single hit" report is exactly what
the data says. WO-1737 moves that to ~1-3 s, which is still a wall that evaporates.**

Army cap read at source: `ArmyStorage.DefaultMaxArmySize = 10`, `+5` from the only authored
`armyCapBonus` perk (`building-tiers.json:42`) → **10, or 15**. ⚠ An owner screenshot reads
`Troops 11/13`; **13 cannot be accounted for from the canonical data** — flagged unproven.
⚠ Every troop in `troops.json` is `slots: 1` today, so the audit's 2/3/4 slot costs are stale and
the concurrency ceiling is higher than that audit implies.

---

## 3. THE TENSION — this is a real design fork, not a tuning question

- **In CoC, walls are tough AND most troops path around them.** Only wall-breakers attack walls, so
  "the whole army on one wall" barely happens — that is what makes tough walls feel right there.
- **Here, the Breach order is built to do exactly that**, on her own WO-1719 ruling.

Those pull opposite ways, and the hit-count ruling only prices the first.

**WO-1730 does not dissolve it.** Units-first *delays* the concurrent case — but once the garrison
is dead every troop returns to the wall, so full-warband concurrency is the **end of every wall
fight**, not a rare edge. And **WO-1730 §3 Q2 is already this same fork, recorded and unruled**:
*"does the EXPLICIT breach order override the new rule? ... These two rulings can conflict and only
the owner can settle it."* **Ruling this ticket rules that one too.**

---

## 4. THE TWO BRANCHES, PRICED

### Branch A — tune for concurrent TTK

Raise the floor so a focused warband needs a meaningful stretch. Against the mid-game 330-DPS band
at tier 1, as a multiple of what WO-1737 shipped:

| Focused warband should need | floor vs shipped | battlemage solo, T1 | **hero solo, T1** |
|---|---|---|---|
| ~5 s | x4.1 | 40 hits / 70.2 s | 32 hits / 18.6 s |
| ~8 s | x6.6 | 63 hits / 111.6 s | 51 hits / 30.0 s |
| ~10 s | x8.3 | 79 hits / 140.4 s | 63 hits / 37.2 s |

- ✅ Cheap: one constant, WO-1737's silo, no behaviour change.
- ⛔ **Strands the solo player.** At the ~10 s row the hero needs ~37 s per tier-1 panel and
  **161 hits / 96 s at tier 3 — over half the 180 s raid clock for one wall.**
- ⛔ **The raid-clock verdict flips.** WO-1737 §6 clears the clock at the shipped value; that does
  **not** survive Branch A's upper rows. Retuning the clock would be a **separate ruling** — flagged,
  not assumed.
- ⛔ Makes the player's own town walls proportionally tankier against waves too (WO-1737 §8).

### Branch B — CoC-shaped

Keep WO-1737's floor; make **ordinary troops not attack walls at all** except siege, or under an
explicit Breach order. WO-1730's units-first rule is already most of the way there.

**The lever the data hands over: `troop-catapult` has `maxOwned: 1`.** "Siege only" is capped at ONE
catapult, so the default breach time falls straight out at the already-ruled floor:

| Default breach (1 catapult @ 38.4 struct DPS) | T1 | T2 | T3 |
|---|---|---|---|
| at WO-1737's shipped floor | **10.4 s** | 16.7 s | 26.7 s |

- ✅ Delivers the described feel **at the value she already ruled**, with the solo hero still opening
  a panel in 8 hits, and the raid clock untouched.
- ✅ Makes the Breach tap the deliberate "I want it now" choice — spending the warband's time on
  masonry becomes a player decision rather than the default.
- ⛔ A **behaviour** change on top of WO-1730, in **WO-1730's silo** (`RaidAssaultAi.PreferUnit`
  Breach branch, `AllowNonObjectiveStructure`). Must not run in the same agent as WO-1730.
- ⛔ **Dead-ends a warband with no siege and no Breach tap when the route is blocked**: today a
  Breach-phase troop falls through to the wall; under Branch B it falls through to nothing. **It
  makes the Breach tap a mandatory verb** — a UX commitment, not just a rule.

---

## 5. WHAT THE OWNER IS BEING ASKED

1. **Branch A or Branch B?** (Or a hybrid: Branch B's behaviour *plus* a smaller floor bump.)
2. If **A**: how many seconds should a full focused warband need on a tier-1 panel? The table above
   converts her answer straight into the constant.
3. If **B**: confirm the Breach tap becoming a **required** verb to open a wall without siege.
4. Either way this settles **WO-1730 §3 Q2** (does an explicit Breach order override units-first?).

---

## 6. FLAGGED SEPARATELY, NOT IN SCOPE

**"Siege is the designated wall-breaker" is already false today.** Structural DPS: catapult **38.4**,
best troop (echo legionnaire) 28.0 — a 1.37x lead; but the **HERO at 87.5 out-breaches the catapult
by 2.3x**. WO-1737 preserved every ratio, so it neither caused nor changed this. It needs its own
ruling and is not folded into either branch above.
