# WORK ORDER 1730 — Raid troops must prefer NEARBY HOSTILES over walls, always; the wall is the last resort

**Status: READY TO IMPLEMENT**
**Minted:** 2026-09-14, from a live owner ruling during on-device testing of build `2026.09.15.370139`
(the WO-1723 Lane A build, in which she confirmed *"i can now walk through destroyed walls"*).
**Silo:** raid troop AI targeting (`Assets/_Modules/Village/Troops/RaidAssaultAi.cs`,
`Assets/_Modules/Village/Troops/TroopController.cs`). ⛔ File-disjoint from WO-1723 Lane B, which owns
`RaidBaseDresser.cs` / `RaidBaseGenerator.cs` / `WallSegment.cs` — these two lanes must NOT run in the
same agent, but they CAN run in parallel.

---

## 1. THE RULING — owner, verbatim, 2026-09-14

> ***"second issue, should never default to target wall, should always default to aggresive targets
> nearby first asnd then only then wall"***

Read literally, and it is binding:
1. **A troop must NEVER default to a wall while a hostile unit is available nearby.**
2. **Nearby hostile units are the DEFAULT target.**
3. **The wall is the LAST resort** — only when there is no nearby hostile to fight.

This is a priority inversion of today's behaviour, not a tuning tweak. Do not implement it as a
threshold nudge.

## 2. WHAT THE CODE DOES TODAY (read at source 2026-09-14 — verify before editing)

Today a troop only prefers a unit over a structure in **Peel** phase, and `Peel` is entered only on a
narrow trigger:

- `RaidAssaultAi.ResolvePhase(peelThreat, routeToObjectiveOpen, objectiveInAttackRange)`
  (`RaidAssaultAi.cs:158-167`): `peelThreat` -> `Peel`; else route blocked -> `Breach`; else objective
  in range -> `Finish`; else `Push`.
- `peelThreat` is true **only** when the troop was hurt within `PeelHurtWindowSeconds` (2.5 s) **OR** a
  hostile is within `PeelUnitLeashMeters` (6 m, a fixed leash deliberately NOT tied to attack range)
  (`RaidAssaultAi.cs:42-46`; computed at `TroopController.cs:943-948`).
- `PreferUnit` returns true **only** in Peel (`RaidAssaultAi.cs:286-287`); in `Breach` it additionally
  requires `unitInAttackRange || routeToUnitOpen` (`:296-298`).
- `PickBucket` (`:317-369`): in Peel, bucket 0 (unit) always wins (`:348-350`). Otherwise the wall
  bucket (2) is reachable and a Breach-phase troop takes the wall.

**So the live default in Breach phase IS the wall** — exactly what the ruling forbids. A hostile
standing 7 m away, who has not yet hit this troop, does not trigger Peel and the troop keeps chewing
masonry.

⚠ **Everything in this section is a dated reading, not an authority. Open the files.** Line numbers in
this repo move (CLAUDE.md §8).

## 3. THE CHANGE

**Make "a nearby hostile unit outranks a wall" a standing rule, not a Peel-only exception.**

The cheapest correct seam is the one WO-1719 already established: put ONE gate in FRONT of the existing
rule rather than teaching the loop about priorities (the design note at `RaidAssaultAi.cs:230-241`
explains why that shape was chosen and it still applies). Concretely:

- In `PickBucket`, whenever a hostile UNIT is available and acquirable, it wins over the wall bucket —
  in **every** phase, not only Peel. The wall bucket is selected only when no such unit exists.
- `Peel` keeps its current meaning and its current priority over everything else; this change does not
  touch it.
- The **objective/spire** keeps its existing precedence in `Push`/`Finish` — the ruling is about
  "wall vs nearby enemy", and says nothing about demoting the spire. Do not change that without a
  ruling.

### ⛔ Two questions the implementing lane must NOT answer on its own

Both are recorded in §5 for the owner; ask before coding if she has not answered.

**Q1 — what is "nearby"?** Candidates, each defensible: the troop's own `attackRange`; the existing
6 m `PeelUnitLeashMeters`; the `_huntScanRadius` used by the acquisition sweep (16.4 m melee / 25.2 m
archer, measured in the 09-14 captures). These give very different play: `attackRange` means "only
fight what I can already hit"; the scan radius means "abandon the wall for anything I can see."

**Q2 — does the EXPLICIT breach order override the new rule?** The player taps Breach and names a wall.
If a hostile then wanders nearby, does the warband abandon the ordered wall? The WO-1719 ruling was
*"all together unless they have aggro"*, which suggests the order should hold — but this new ruling says
walls are always last. **These two rulings can conflict and only the owner can settle it.**

## 3B. SECOND RULING, SAME SILO — once the breach is OPEN, stop grinding the wall

Owner, verbatim, 2026-09-14, minutes after the ruling in §1:

> ***"3rd once the breach is through the troops should continue towards the spire or aggressive mobs
> not coninute to work down the wall"***

Same files, same lane, so it is recorded here rather than as a third ticket — but it is a SEPARATE
acceptance criterion.

### ⚠ THIS MAY ALREADY BE IMPLEMENTED AND SIMPLY UNREACHABLE UNTIL TODAY — CHECK BEFORE WRITING CODE

The behaviour she is asking for is what the existing phase machine already claims to do:
- `ResolvePhase` returns `Breach` **only** when `!routeToObjectiveOpen` (`RaidAssaultAi.cs:158-167`).
  Once a route to the spire exists, the phase becomes `Push` or `Finish`.
- In `Push`/`Finish`, `AllowNonObjectiveStructure` returns **false** for non-siege troops
  (`RaidAssaultAi.cs:311-315`), and `PickBucket` then makes the wall bucket unreachable (`:337-344`) —
  i.e. troops already refuse to keep chewing a wall once a route is open.

**So why did it never happen?** Because a route never opened. Measured across every 2026-09-14 device
capture, **`routeOpen=True` appears 0 times in 2,517 `[Flow:RaidAI]` lines**, with
`routeObj=PathPartial` on 98%+ — which is precisely the WO-1723 root cause: the visible wall was baked
into the navmesh, so destroying a segment never produced a traversable hole, so `NavMesh.CalculatePath`
to the spire never returned `PathComplete`, so the phase never left `Breach`.

**WO-1723 Lane A (landed `8b88a5053`, owner-confirmed on device: *"i can now walk through destroyed
walls"*) removes that cause.** The FIRST thing this lane must do is capture a raid on build
`2026.09.15.370139` or later and grep for `routeOpen=True`.

| What the capture shows | What it means | What to do |
|---|---|---|
| `routeOpen=True` appears, and troops leave the wall for the spire/mobs after a breach | The ruling is **already satisfied** by Lane A | Write a REGRESSION that pins it, change no behaviour, and say so in the RESULT |
| `routeOpen=True` still never appears | The hole opens for the hero but not for `NavMesh.CalculatePath` | RCA that first — do NOT "fix" the phase machine to paper over a pathing gap |
| `routeOpen=True` appears but troops keep hitting the wall anyway | A real defect in `AllowNonObjectiveStructure` / `PickBucket` | Fix THAT, cite the line |

⛔ **Do not implement a new "stop attacking walls after a breach" rule before running that capture.**
Adding a second mechanism on top of a working one is how this system got two wall hierarchies in the
first place (WO-1723). §12: static reading locates, captured data concludes.

### Interaction with §1's ruling
§1 says nearby hostiles outrank walls always. §3B says after a breach, head for the spire **or**
hostiles. Both agree the wall is last. Where they could conflict is spire-vs-hostile once through the
breach — she named both in one breath (*"the spire or aggressive mobs"*) without ordering them.
Today `Push`/`Finish` prefers the objective. **Leave that as-is** unless §5 Q3 is ruled.

## 4. INSTRUMENT FIRST AND AFTER (CLAUDE.md §12)

The decision line already exists and is the oracle for this ticket
(`TroopController.cs:1040-1050`):
```
[Flow:RaidAI] id=... job=... phase=... in[peel=, routeOpen=, objInRange=] routeObj=...
              bucket=... preferUnit=... has[unit=,obj=,wall=]
```
Capture it **before and after**. The proving read is `has[unit=True,...,wall=True]` paired with
`bucket=` — today that reads `bucket=2` (wall) outside Peel; after the fix it must read `bucket=0`
(unit) whenever `has[unit=True]`.

⚠ **`TroopController.Attack` (`:1420-1560`) emits NO FlowTrace at all** — there is no line saying
"troop X swung at Y". WO-1723 §5 already asks for one throttled `SWING` line; if that has not landed
when this lane runs, add it here, because "the troop retargeted but never actually swung" is otherwise
unprovable.

## 5. OPEN OWNER RULINGS

- **Q1 (§3):** what radius counts as "nearby" — attack range, the 6 m peel leash, or the full scan radius?
- **Q2 (§3):** does an explicit player Breach order still hold when a hostile comes near, or does the
  new always-units-first rule outrank the player's own tap?
- **Q3 (§3B):** once through a breach, does the SPIRE or a nearby HOSTILE win? She named both
  (*"the spire or aggressive mobs"*) without ordering them. Today the objective wins in `Push`/`Finish`.

## 6. ACCEPTANCE CRITERIA

- With a hostile unit nearby and an intact wall present, a captured `[Flow:RaidAI]` line reads
  `has[unit=True,...,wall=True]` with `bucket=0` — in a NON-Peel phase. This is the ticket.
- No troop is observed attacking a wall while an acquirable hostile stands within the ruled "nearby"
  radius.
- Peel behaviour is unchanged (an aggro'd troop still keeps its current fight).
- The spire's precedence in `Push`/`Finish` is unchanged.
- `WallBreachOrderRegression.cs` and any suite pinning `SelectFocusBreach` / `PickBucket` still pass,
  or are re-pointed WITH the ruling and the re-point is called out explicitly.
- Brace + NUL gate on every `.cs`, plus `python tools/gate_brace.py`.
- WO Status flipped and `.RESULT.md` written in the same hand-back (CLAUDE.md §11).
- Owner felt-verifies and CLOSES; the CLI does not close it (CLAUDE.md §13).

## 7. NOT IN SCOPE

- The destroyed-wall VISUAL — that is WO-1723 Lane B, a different silo and a different file set.
- Wall HP, tier toughness, troop damage numbers.
- The frozen-troop watchdog (WO-1723 §7 Q3, still open and deliberately unasked).

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
