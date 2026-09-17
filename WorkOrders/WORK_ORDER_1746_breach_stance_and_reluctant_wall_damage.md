# WORK ORDER 1746 — Breach is a persistent STANCE; ordinary troops hit walls at 10%

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:19:01, build 2026.09.17.373943). PRIOR STATUS: FIXED — gated by the lead 2026-09-15 (`COMPILE_GATE_OK` on fresh `Builds/cg1746b`, `REGRESSION_OK 534/534 suites` on fresh `Builds/reg1746`, brace bad=0 of 5, NUL 0); awaiting the owner's felt test. *(Lane's own line, kept for the record: IMPLEMENTED, NOT YET GATED — edit-only lane; no Unity, no gate, no bake, no git run here.)*

> ⚠ **SUPERSEDED IN PART 2026-09-15 (owner ruling, later the same day): the 10% reluctant wall-damage fallback is RETIRED** — *"the troops 100% ignore walls, unless explicitly told breach"*; siege (catapult) still auto-attacks walls. Implemented by `WorkOrders/WORK_ORDER_1752_troops_never_attack_walls_without_breach_and_defend_the_hero.md`. The Breach stance itself (persistent, auto-chaining, only AGGRO breaks it) is unchanged.
*(Board form: the exact `**Status:** <label>` shape with the colon OUTSIDE the bold. WO-1738 was
itself corrected from `**Status: RULED ...**` today after that shape produced
`BOARD_CHECK_FAIL 1 unlabeled` — `tools/board_build.py:248` only matches the exact form, and
`:187` buckets on the LEADING keyword.)*
**Minted:** 2026-09-15, pre-assigned by the lead (number blocks pre-allocated after two lanes
collided on independently-read banner numbers earlier today).
**Implements:** the owner's ruling on **WO-1738**, recorded verbatim in §1 below.
**Silo:** WO-1730's silo — `RaidAssaultAi.cs` / `TroopController.cs` (+ `TroopBreachOrder.cs`,
`RaidDeployController.cs`, `WallBreachOrderRegression.cs`).
**⛔ NOT THIS TICKET:** `WallSegment.cs`, `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`,
`HeroTargetIndicator.cs`, `PlayerAttackController.cs` (hero targeting was WO-1734). None were opened
for edit; `RaidBaseGenerator.cs` was read READ-ONLY, once, to confirm raid walls are `WallSegment`.

---

## 1. THE RULING RECORD (owner, 2026-09-15) — quoted verbatim from WO-1738

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

**This ruling also settles WO-1730 §3 Q2** (explicit Breach order vs. the new units-first rule):
Breach is a stance the player toggles; units-first is the default *outside* it. WO-1730 §3 Q2 and §5
Q2 are annotated accordingly.

---

## 2. WHAT SHIPPED, FILE BY FILE

| File | The one-line change |
|---|---|
| `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` | Added the named constant `ReluctantWallDamageMultiplier = 0.1f` + the pure `WallDamageMultiplier(preferStructures, breachStance)`, and gave `PreferUnit` / `PickBucket` a `breachStance` overload (old signatures kept as delegating overloads, stance=false). |
| `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` | Added the explicit stance: `StanceActive => _stanceArmed \|\| HasOrder`, `SetStanceArmed(bool)` (traced), and made the PUBLIC `Clear()` disarm it while the private `DropInternal` deliberately does not. |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | The Breach button arms/disarms the stance; the two exclusive-arm sites (rally arm, tile arm) drop the MODE flag while a standing order keeps the stance alive. |
| `Assets/_Modules/Village/Troops/TroopController.cs` | Reads `TroopBreachOrder.StanceActive` once per resolve, feeds it to `RallyHoldsMarch` / `PreferUnit` / `PickBucket`, applies the wall multiplier in `Attack()` **outside** the catalog-multiplier gate, and adds the new trace fields + the first-ever SWING line. |
| `Assets/Editor/Regression/WallBreachOrderRegression.cs` | Six new cases (7-12) pinning the ruling; already registered in `DataRegression.cs:1483`, so the suite count does not move. |

### The named constant and its home
`RaidAssaultAi.ReluctantWallDamageMultiplier = 0.1f` — beside `PeelHurtWindowSeconds` /
`PeelUnitLeashMeters`, which is already the raid-AI knob block. No `0.1f` is typed at any call site;
both readouts (the AI trace and `Attack()`) call the same pure `WallDamageMultiplier`, so the log
cannot disagree with the damage that lands. **A data/JSON home was looked for and deliberately not
used:** there is no raid-AI tunables JSON — every sibling knob (peel window, leash, formation
offsets, detour factor) is a const in this same class, and inventing a first JSON file for one value
would have created the duplicated state CLAUDE.md §2/§5/§16 each record as this repo's worst bug class.

### The behaviour, precisely
- **Reachable defender** (in attack range OR route-open) → the unit wins; the wall is not picked.
  This was already true before today; the suite now pins it.
- **Unreachable-only defender / blocked route, no stance, non-siege** → the wall is still *picked*
  (bucket 2) but swung at **0.10**. `AllowNonObjectiveStructure` is **unchanged** — returning `-1`
  here would have produced exactly the dead-end WO-1738 §4 prices as Branch B's real cost.
- **Stance armed** (Breach button on, or a standing order) → wall at **1.00**, and a calm defender
  does not pull the warband off it. **Aggro still peels** (`peelThreat` → `Peel` → bucket 0).
- **Siege** → **1.00 always**, identified by the catalog role (`_preferStructures` is set from
  `def.Role == "siege"`, `TroopController.cs:432`), never by a troop name.
- **Reluctance is scoped to `WallSegment` panels only.** Towers, the spire and other masonry keep
  full damage; the ruling says "the nearest blocking **wall**".

---

## 3. TWO THINGS THAT WOULD HAVE SHIPPED THE RULING BROKEN

Both were caught in review before the first line was written, and both are recorded in-code.

**(a) The multiplier would have applied to nobody.** `Attack()`'s existing multiplier block is gated
on `_preferStructures || _structureDamageMult != 1f || _unitDamageMult != 1f`. A Footman, an Archer
and an Echo Legionnaire have **all three at default**, so that block never runs for exactly the
ordinary troops the 10% is written for. Folding the reluctance inside it would have shipped a ruling
that changed only the catapult — while the new trace printed `mult=0.10` and looked correct. The
multiplier is applied outside the gate, and `WallBreachOrderRegression` Case 12 pins the ordering.

**(b) The auto-chain would have died on the first wall, whenever a rally was set.** `TroopController`
passed `TroopBreachOrder.HasOrder` as `RallyHoldsMarch`'s 4th argument. Trace it: the ordered panel
collapses → `Target` self-clears → `HasOrder` goes false → the rally march holds again →
`nearestOtherStruct` is nulled → Breach phase with no wall and no unit → `PickBucket` returns -1 →
**the warband walks back to the flag instead of opening the next panel.** WO-1719's own remarks
record that a rally is set "most of the time" mid-raid, so this is the common case, not an edge. The
argument is now the **stance**. This is a deliberate widening of WO-1719's rule ("an explicit ORDER
releases the march" → "the STANCE releases the march"); the implicit ring-farm suppression the owner
ruled for on 2026-09-12 is untouched, because a warband with Breach off and no order has
`StanceActive == false` and still marches without chewing masonry.

---

## 4. THE FLAGGED READING — RAISED, RULED, REVERSED WITHIN A MINUTE, AND SETTLED

**Both of the owner's answers are recorded here deliberately, so no future seat re-litigates this.**

**The flag.** WO-1738's *status line* summarised the ruling as *"units-first is the default inside
it"* (the lead's paraphrase), while the ruling *body* and WO-1719 both say the stance runs *"until
the player toggles Breach off or a hostile pulls **aggro**"*. Those are different rules: the first
lets any nearby acquirable hostile break the stance, the second only an actual attacker. This lane
implemented the two verbatim owner sources (the body) and **flagged it rather than picking
silently** — which is what produced the ruling below.

**The owner's first answer:** *"Any nearby hostile breaks the stance."* — units-first applies inside
the stance; the troops go fight a nearby acquirable hostile and return to the wall afterwards, with
the stated cost accepted (a garrison behind the wall can keep the warband off it).

**The owner's final answer, given within a minute, reversing herself:** *"wait the other way — the
way suggested."* — i.e. **the recommended reading: AGGRO ONLY.** While Breach is armed the warband
stays on the wall unless an enemy actually attacks them or closes to melee (the existing `Peel`
trigger). A garrison standing 10 m away watching does **not** pull them off.

**RULED: aggro-only. This is what shipped** — it is the reading this lane had already implemented,
so the predicate was never flipped. The `Peel` phase remains the one thing that breaks the stance,
which is also why WO-1719's *"all together unless they have aggro"* survives verbatim.

⚠ **A flip instruction for the first answer reached this lane out of order, after the cancellation
that superseded it.** It was not acted on; the cancellation states explicitly *"DO NOT FLIP THE
PREDICATE"* and identifies the first answer as the reversed one. If that ordering is wrong, the flip
is small and bounded: delete `if (breachStance) return false;` in `RaidAssaultAi.PreferUnit` **and**
the `!breachStance &&` guard on the in-attack-range shortcut in `PickBucket`'s Breach branch (both,
not either — see §4A), then invert Case 8's first expectation.

---

## 4A. ⛔ THE RED PROOF FOUND A REAL BUG — THE STANCE WAS BROKEN AND READING HAD NOT SHOWN IT

**This is the finding of the session.** The Case 8 red proof was executed rather than reasoned, and
the very first run — against the code as handed back — came up **RED**:

```
-- Case_BreachStance_HoldsTheWarbandOnMasonry (WallBreachOrderRegression case 8)
   FAIL  stance armed + calm reachable defender -> bucket 2 (the wall); got 0
   PASS  aggro'd troop under an armed stance -> bucket 0 (the unit); got 0
CASE8_RED failures=1
```

**Cause: `PreferUnit` is not the only place a unit can beat the wall in Breach.** `PickBucket`'s
Breach branch carries its own separate shortcut, `if (hasUnit && unitInAttackRange) return 0;`
(`RaidAssaultAi.cs:474` before the fix). Teaching `PreferUnit` about the stance and stopping there
left the stance **silently broken for exactly the case the ruling is about**: a calm defender already
inside attack range still stole the warband off the ordered panel. Siege never reaches that line
(the `preferStructures` branch returns first), which is why the bug was invisible to reading —
it looked like the stance already behaved like siege, and it did not.

**Fix:** `if (!breachStance && hasUnit && unitInAttackRange) return 0;` — the stance now behaves
like siege at that gate too, which is the ruling.

Had this shipped on the reasoned note alone, the suite would have gone red at the lead's gate at
best, and at worst the guard would have been "corrected" to match the broken code. **A guard nobody
has watched fail is not a guard.**

---

## 4B-RED. THE EXECUTED RED PROOF — method and observations

`tools/gate_brace.py` aside, this lane cannot run Unity. The pure statics under test
(`PreferUnit`, `PickBucket`, `AllowNonObjectiveStructure`, `WallDamageMultiplier`) reference **no
UnityEngine type**, so they were run directly: a scratch script **mechanically SLICES those method
bodies out of the shipped `RaidAssaultAi.cs` by brace-matching** and emits a console harness
carrying Case 8's two assertions verbatim. Nothing was retyped — a hand-copied predicate would only
prove that the copy behaves, which is the same "decoration" failure this proof exists to close.
Harness: `<scratchpad>/extract.py` + `harness/Program.cs` (regenerated from source on every run).

| Run | Mutation | Observed |
|---|---|---|
| 1 | none (code as handed back) | **`CASE8_RED failures=1`** — `got 0`, expected 2. **The bug above.** |
| 2 | after the `!breachStance` fix | `CASE8_GREEN` — both assertions PASS |
| 3 | delete `if (breachStance) return false;` from `PreferUnit` | **`CASE8_RED failures=1`** (`got 0`) |
| 4 | delete the `!breachStance &&` guard in `PickBucket` | **`CASE8_RED failures=1`** (`got 0`) |
| 5 | restored | `CASE8_GREEN` |

Runs 3 and 4 prove Case 8 is sensitive to **both** stance gates independently — neither is
decoration, and removing either alone reddens the case. The aggro assertion stayed `PASS` throughout,
which is the control: it shows the case is not simply failing for everything.

⚠ **Still not proven:** this executed the *pure statics only*. It is not a compile of the Unity
assemblies, not a run of the real `WallBreachOrderRegression` (its source-text cases 11/12 and the
`TroopBreachOrder` static cases 10 cannot run here), and not `REGRESSION_OK`. Cases 7, 9, 10, 11 and
12 remain **reasoned, not executed**. The lead's gate is still the first full execution.

---

## 4C. THE ORIGINAL FLAG NOTE (superseded by §4 above, kept for the record)

WO-1738's **status line** summarises the ruling as *"units-first is the default inside it"*, which
would mean a reachable defender still outranks the wall even under an armed stance. The ruling
**body** and WO-1719 both say the stance runs *"until the player toggles Breach off or a hostile
pulls **aggro**"* — and aggro is `peelThreat`, i.e. the `Peel` phase.

**This implements the two verbatim owner sources:** under an armed stance a *calm* reachable
defender does not peel the warband off the panel; an *aggro'd* troop keeps its fight exactly as
WO-1719 shipped. `WallBreachOrderRegression` Case 8 pins it so a flip cannot happen silently.
**RULED by the owner on 2026-09-15 — see §4; this reading stands.**
⚠ The "deleting one line" estimate written here originally was **WRONG**, and §4A is why: there are
**two** stance gates, not one. Corrected rather than deleted, because the wrong estimate is the
evidence for why the red proof was worth executing.

---

## 4B. THREE CONSEQUENCES OF THE STANCE, NAMED RATHER THAN DISCOVERED LATER

1. **Arming Breach with a rally set and no tap yet now releases the rally march immediately.** The
   stance arms on the button, before any wall tap, and `RallyHoldsMarch` reads the stance — so a
   blocked warband starts working the automatic most-damaged panel at full damage the moment the
   player declares "we are breaching". Under WO-1719 arming alone did nothing until a tap landed.
   This is deliberate (it is what "one tap = we are breaching" means) but it is a behaviour change
   the owner will feel, so it is written down rather than left to a felt-test surprise.
2. **One sequence approximates "toggle Breach OFF" with "left Breach mode":** arm Breach → tap a
   wall → arm Rally or a troop tile (mode off, standing order deliberately kept) → that wall falls.
   `_stanceArmed` is false and `HasOrder` goes false, so the stance drops there and the auto-chain
   ends. The Breach button reads "Breach" (off) at that moment, so it is visually consistent — but
   it is the one path where the chain stops without the player pressing the toggle.
3. **Towers, gates and the spire are untouched by the reluctance.** Verified read-only:
   `RaidBaseDresser.cs` contains **no** `AddComponent<WallSegment>` (grep, 0 hits), so `foe is
   WallSegment` cannot leak the 10% onto dressed props; the only attachment site is
   `RaidBaseGenerator.cs:1986`, on wall segments.

---

## 4C. CANON UPDATED IN THE SAME BREATH (CLAUDE.md §15)

`docs/handoffs/RAID_SYSTEMS_REFERENCE_2026-09-14.md` **Part 2** carried the pre-ruling model —
§2.3 "the explicit player breach ORDER" and §2.5's `hasExplicitBreachOrder` rally argument. It is a
dated point-in-time reference, so it got a **`⚠ STALE 2026-09-15` banner naming exactly what moved
and pointing at this WO — the body was NOT rewritten.** §2.1, §2.2, §2.4 (aggro still wins), §2.6
and §2.7 were checked and are current.

---

## 5. INSTRUMENTATION (CLAUDE.md §12 — added, never stripped)

Both lines are throttled at 1 Hz per troop. `Attack()` runs several times a second per troop; an
unthrottled line there would flood the logcat ring and evict the boot window the capture exists for
(memory `logcat-ring-buffer-destroys-evidence`).

**`[Flow:RaidAI]` `raid-ai-phase`** — three new fields, greppable:

```
breachStance=True blocked=False wallDmgMult=1.00 siege=False
```

**`[Flow:TroopAI]` the SWING line — this method emitted NOTHING before today.** The gap is named in
WO-1723 §5 and WO-1730 §4 and was never closed: the AI trace proved what a troop *intended* to hit,
and nothing proved a blow ever landed or at what multiplier.

```
id=<n> SWING target='Wall_Outer_SS_11' kind=wall dmg=4.2 mult=0.10 base=42.0 structMult=1.00 siege=False breachStance=False
```

`kind=` is `wall` / `struct` / `unit`. `mult=` is the reluctance factor; `structMult=` is the
separate catalog factor (siege 2.0) — printed apart on purpose so neither can be mistaken for the
other. Grep `SWING target=` with `mult=0.10` to see reluctance live; `mult=1.00 kind=wall` is either
siege or an armed stance, and the same line says which.

**`[Flow:RaidAI]` `BREACH STANCE ARMED` / `BREACH STANCE disarmed`** — the stance transition, so the
persistent-stance concept is explicit in the log, not inferred from the order's own lines.

---

## 6. REGRESSION

`Assets/Editor/Regression/WallBreachOrderRegression.cs`, cases 7-12 — **extended, not a new suite**
(same subject; a second file would have split one rule across two markers). Already registered at
`DataRegression.cs:1483`, so **the suite count does not move**.

| Brief | Case | What it pins |
|---|---|---|
| (a) | 7 | reachable defender → bucket 0, not the wall; unreachable-only → wall (the anti-dead-end clause, stated so it cannot read as a miss) |
| (b) | 8 | stance armed → wall bucket at 1.00; aggro still peels |
| (c) | 9 | blocked + no stance + non-siege → **0.10** |
| (d) | 9 | siege → 1.00 with the stance either on or off |
| (e) | 10 | ordered wall dies → order self-clears, **stance survives**, auto rule picks the next panel, still at 1.00; the player's cancel does disarm |
| — | 11 | the STANCE (not the order) releases the rally march; ring-farm suppression intact |
| — | 12 | source-text: the multiplier is called, sits OUTSIDE the catalog gate, is scoped to `WallSegment`, the rally arg is the stance, the SWING trace exists, and the HUD arms the stance |

All six run inside a `try/finally` that clears the order and disarms the stance — `TroopBreachOrder`
is static, and leaked state would land as a failure in the next suite's file.

**Case 8's red proof was EXECUTED — see §4A and §4B-RED. It found a real bug.** Cases 7, 9, 10, 11
and 12 remain **reasoned, not executed** (they need Unity, the `TroopBreachOrder` static, or
source-text reads); each carries its own "delete X → this case fails" note in the suite header. The
lead's gate run is the first full execution.

---

## 7. SUITES THAT MOVED

**None changed verdict.** Every pre-existing caller of `PreferUnit` / `PickBucket`
(`RaidAssaultAiRegression`, `TroopTargetPreferenceRegression`, `WallBreachOrderRegression` cases 1-6)
uses the **old signatures, kept as delegating overloads with `breachStance: false`**, so they compile
and pass untouched. `AllowNonObjectiveStructure` is unchanged, so
`RaidAssaultAiRegression.Case_AllowNonObjectiveWiredIntoPickBucket` is unaffected.

Specifically checked: `WallBreachOrderRegression` case 4's `sameTroopNoAggro` assertion — the one
that pins "the same troop without aggro should take the ordered wall". Its fixture unit is
**unreachable** (`unitInAttackRange: false`, `routeToUnitOpen: false`), so it resolves the wall under
the new rule for the same reason as before. **It did not need to move and was not touched.**

---

## 8. WHAT REMAINS UNPROVEN (this lane cannot close these)

1. **No gate, no compile, no regression run.** Brace + NUL are clean (§9); that is not a compile.
   Case 8's predicates were executed as pure statics (§4B-RED); the Unity assemblies were not built.
2. **Cases 7, 9, 10, 11, 12 are reasoned, not executed** (§6).
3. **Felt pace is the owner's call.** The arithmetic in the ruling record is WO-1738's, not
   re-measured here. Whether ~26 s at 10% *feels* like a wall worth having is a felt-test verdict.
4. **§4's reading is a judgement call**, awaiting one owner word.
5. **Wave enemies attacking the player's own town walls are untouched** — WO-1738 verified at source
   that `EnemyBrain.cs` holds zero references to `RaidAssaultAi` / `TroopBreachOrder`. Not re-verified
   this session; taken from that ticket's record and named as such.
6. **`logs/debug/DEEPSEEK_PACKET_wall_durability_fork.md` was NOT opened by this lane.** Every number
   quoted here comes from WO-1738's own ruling record, which already carries the packet's verified
   arithmetic. Stated because the brief listed the packet as a read-first document; the ruling record
   was treated as the authority and the packet was not independently checked.

---

## 10. UNCOMMITTED WORK IN THE SAME FOLDER THAT IS *NOT* MINE

`git diff` warns on `Assets/_Modules/Village/Troops/RaidHudController.cs`, `TroopCatalog.cs` and
`TroopDef.cs` — other lanes' uncommitted changes sharing this directory. **This lane did not open or
edit any of them.** The lead must stage by explicit path (CLAUDE.md §11: never `git add -A`); the
five files in §2 are the whole of this ticket.

---

## 9. FILE QUALITY GATE

`python tools/gate_brace.py` (the port of `CompileGate.BraceBalanced`'s exact rule):
**`GATE_BRACE_SUMMARY bad=0 of 5`, exit 0.**

| File | raw `{` / `}` | NUL bytes |
|---|---|---|
| `RaidAssaultAi.cs` | 29 / 29 | 0 |
| `TroopBreachOrder.cs` | 19 / 19 | 0 |
| `TroopController.cs` | 237 / 237 | 0 |
| `RaidDeployController.cs` | 268 / 268 | 0 |
| `WallBreachOrderRegression.cs` | 85 / 85 | 0 |

Every added **string literal** is ASCII (verified by regex over the added diff lines); the non-ASCII
that remains is pre-existing comment typography. All files written with Write/Edit on the Windows
path — no bash redirect, no heredoc (CLAUDE.md §0).
