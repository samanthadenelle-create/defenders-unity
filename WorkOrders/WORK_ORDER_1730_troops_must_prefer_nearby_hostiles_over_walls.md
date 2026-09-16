# WORK ORDER 1730 — Raid troops must prefer NEARBY HOSTILES over walls, always; the wall is the last resort

**Status:** FIXED - awaiting gate 2026-09-15 (§3B: runtime ARRIVED rule ported from WO-1749 on captured routeGap last=4.7m)
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

> ## ✅ RULED by WO-1738 (owner, 2026-09-15). Q2 IS CLOSED — implemented in WO-1746.
> **Breach is a persistent STANCE, not a per-wall order.** Units-first is the default *outside* the
> stance; inside it the warband stays on the panel. **Owner-ruled explicitly 2026-09-15:** only
> AGGRO breaks the stance — an enemy that actually attacks them or closes to melee (the existing
> `Peel` trigger). A garrison standing 10 m away watching does **not** pull them off. She first
> answered "Any nearby hostile breaks the stance", then reversed within a minute to *"wait the other
> way — the way suggested"*; both answers are recorded verbatim in WO-1746 §4 so this is not
> re-litigated. ⚠ A paraphrase reading "units-first is the default INSIDE it" circulated on WO-1738's
> summary line and is RETIRED — it was the lead's wording, not the owner's. The two rulings do not in fact conflict, because
> the thing that breaks the stance is **aggro**, which is already a PHASE (`peelThreat` → `Peel`) and
> already outranks every bucket rule — so WO-1719's *"all together unless they have aggro"* survives
> verbatim while units-first governs everything else. The owner also kept ordinary troops off masonry
> by making them **reluctant rather than forbidden**: a blocked warband with no stance still takes the
> nearest blocking wall (so it never idles into a dead-end) at **10%** structural damage; siege and
> any troop under an armed stance stay at full. See
> `WorkOrders/WORK_ORDER_1738_wall_durability_concurrency_fork.md` for the ruling record and
> `WorkOrders/WORK_ORDER_1746_breach_stance_and_reluctant_wall_damage.md` for what shipped.
> ⚠ **This closes Q2 ONLY.** Q1 (what radius counts as "nearby") and Q3 (spire vs. nearby hostile
> after the breach) are UNRULED and this ticket stays open on them.

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

### ✅ THE CAPTURE HAS BEEN RUN — 2026-09-14 20:26, build `2026.09.15.370139`, Seeker `SM02G4061955851`

`logs/device/post-lane-a-370139/logcat_full.txt` (238,048 lines), pulled after the owner played a raid
on the Lane A build and confirmed *"i can now walk through destroyed walls"*.

**LANE A WORKED, AND IT IS MEASURED — wall probes only (`BREACH: structure 'Wall_`), 261 of them:**

| `holeNavmesh=` | Before Lane A (09-14 captures) | After Lane A | |
|---|---|---|---|
| `WALKABLE` | 11 | **166** | |
| `NOT-WALKABLE` | 645 | 95 | |
| **walkable rate** | **1.7 %** | **64 %** | the fix is real |

(16 further probes are `RaidSpire` deaths, excluded — the spire's own footprint reading NOT-WALKABLE is
expected and irrelevant. Counting them in would have overstated the result; they were separated
deliberately.)

**BUT RULING 2 IS *NOT* SATISFIED. This lands on the BAD branch of the table above:**

```
routeOpen=True  : 0        (out of 2,670 samples)
routeOpen=False : 2670
routeObj=PathPartial : 2470
routeObj=no-spire    : 200
```

`routeOpen=True` **still never occurs**, so `ResolvePhase` still never leaves `Breach`, so troops still
grind the wall after it is open — exactly the behaviour the owner reported. The phase machine is not at
fault; it is being handed `PathPartial` every single time.

**Therefore, per this ticket's own table: RCA the pathing gap FIRST. Do NOT add a rule.**

### The two residuals are probably ONE cause — treat them together

1. **95 of 261 breaches still read `NOT-WALKABLE`** (36 %).
2. **`NavMesh.CalculatePath` to the spire never returns `PathComplete`.**

A ring in which roughly a third of breached panels stay sealed is a ring an agent cannot path through,
which is sufficient to explain `PathPartial` forever. The leading hypothesis — **UNPROVEN, and it must
be measured, not assumed** — is the **78-segments-vs-60-clad-panels partition mismatch** (WO-1723 §11.4):
the carving obstacle is sized from the `WallSegment`'s own `BoxCollider`, so it clears only that
segment's narrow footprint, while the visible panel it belongs to is wider. Killing one segment opens
less than one panel's width of navmesh.

⚠ **WO-1723 Lane B is already implementing the owner's Q1 ruling — the panel-matched (~4 m / 60-segment)
repartition — which is precisely the change that would close this.** So:

> **RE-MEASURE AFTER LANE B LANDS AND ITS RE-BAKE RUNS, BEFORE COSTING ANY WORK ON THIS TICKET.**
> Re-run the two greps above. If `routeOpen=True` starts appearing and the NOT-WALKABLE share collapses,
> ruling 2 is satisfied for free and this half of the ticket closes with a regression pin and no code.

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
- ~~**Q2 (§3):** does an explicit player Breach order still hold when a hostile comes near, or does the
  new always-units-first rule outrank the player's own tap?~~ **RULED by WO-1738 (owner, 2026-09-15),
  implemented in WO-1746 — see the ruling box in §3.** Breach is a persistent STANCE; units-first is
  the default outside it; only AGGRO (`peelThreat` → `Peel`) breaks it. Ordinary troops are reluctant
  on walls (10%), not forbidden, so no warband idles into a dead-end.
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

---

## 8. LANE VERIFICATION — 2026-09-15, read at source on `dev`, NO Unity run

⚠ **§2 of this ticket is a 2026-09-14 reading and the tree has moved past it.** Three commits landed
in this exact silo after this ticket was minted. Everything below was re-read at source today.

### 8.1 §1's ruling is ALREADY SATISFIED — by WO-1752, and MORE STRONGLY than this ticket asked

`d5b0221bb` (*"troops NEVER attack walls without Breach"*) implements the owner's 2026-09-15 felt-test
ruling *"we need to set it so that the troops 100% ignore walls, unless explicitly told breach"*. Read
at source:

- `RaidAssaultAi.MayTargetWall(preferStructures, breachStance)`
  (`Assets/_Modules/Village/Troops/RaidAssaultAi.cs:113-116`) — returns false for a non-siege troop
  with no stance.
- `PickBucket`'s `mayWall` clause (`RaidAssaultAi.cs:576-578`) consumes it, so the WALL BUCKET IS
  UNREACHABLE for that troop **in every phase**, not merely outranked by a nearby unit.
- The candidate's TYPE is fed in live: `bool otherStructIsWall = nearestOtherStruct is WallSegment;`
  (`TroopController.cs:1158`), passed as the 9th argument at `:1160-1162`. ⚠ Both numbers are
  POST-8.5: this lane's own instrumentation inserted ~25 lines above them today, so a pre-09-15
  citation of this seam is off by that much.

§2's premise — *"the live default in Breach phase IS the wall"* — is therefore **no longer true of this
tree**. The ticket's own §2 warning ("everything in this section is a dated reading") is what caught it.

**No code was written for §1. It needs none.**

### 8.2 §6's FIRST acceptance bullet is RE-POINTED, and the re-point is called out (as §6 requires)

The bullet demands `bucket=0` whenever `has[unit=True,...,wall=True]` outside Peel. **That is not what
the shipped tree does in every case, and the difference is an owner ruling, not a defect.** Traced
through `PickBucket` for non-siege / no stance / Breach / wall present / hostile nearby but
`!unitInAttackRange && !routeToUnitOpen`: the wall is refused (`mayWall=false`), and the tail at
`RaidAssaultAi.cs:632-634` returns **-1 (the troop stands)** rather than 0 — because bucket 0 there
would hand back an UNREACHABLE foe, the steer WO-1438 proved freezes a troop on a navmesh edge. The
owner accepted that dead-end explicitly (WO-1752: *"a blocked warband with Breach OFF and no siege now
stands (or defends the hero)"*).

> **Re-pointed bullet:** *the wall bucket is never selected by a stance-less non-siege troop, in any
> phase; `bucket=0` holds whenever the nearby hostile is actually reachable.* The ruling this ticket
> exists to enforce — **the wall is never the default** — is satisfied either way.

**Q1 (§5) is MOOT for wall-vs-unit and needs no ruling.** It asked which radius makes a hostile "near
enough to outrank a wall". The wall is no longer a competitor at any radius, so there is nothing for
the radius to arbitrate. (It would only revive if the owner ever re-allows stance-less wall targeting.)

### 8.3 NO new regression was added — §1 is already pinned, three times over

Adding a fourth case would be duplicate coverage in a file another lane may be holding:
- `Assets/Editor/Regression/WallBreachOrderRegression.cs` — the `WallsOff` case (`:646-728`) asserts a
  stance-less non-siege troop picks **-1, not the wall**; that siege still takes it; that an armed
  stance still takes it; and that a TOWER on the same bucket stays targetable.
- Same file, Case 8 — pins that a calm in-range defender does not break an armed stance.
- `Assets/Editor/Regression/HeroUnitOverWallTargetingRegression.cs`.

### 8.4 §3B is NOT satisfied, its root cause has a landed fix, and ONE MEASUREMENT IS MISSING

`3b4b98834` (WO-1749) found the §3B blocker: **the spire was buried 1.5 m inside the KeepPlatform**, so
`spire.WorldPosition` — the exact point every troop paths to — sat inside solid geometry. Its own body
reports the identical symptom this ticket measured (`PathPartial` 1,650, `PathComplete` ZERO) and its
post-fix bake reports `RAID_NAV_REACH_OK scenes=5`.

⛔ **BUT THAT DOES NOT CLOSE §3B, AND THE REASON IS A SECOND DEFECT WO-1749 FIXED ONLY ON ITS OWN SIDE.**
WO-1749 retired its bake criterion in these words: *"PathComplete to an objective's CENTRE is
unsatisfiable for anything wide enough to carve its own footprint"*, replacing it with ARRIVED. **The
RUNTIME check never got that treatment.** `TroopController.RefreshRouteToObjective` still requires
`_routePath.status == NavMeshPathStatus.PathComplete` to `spire.WorldPosition` — the centre — at
`TroopController.cs:1289`, inside `RefreshRouteToObjective` (`:1271`), re-read today. If that criterion is unsatisfiable at runtime for the
same reason it was unsatisfiable in the probe, `routeOpen=True` stays at 0 even on a post-1749 build,
`ResolvePhase` never leaves `Breach`, and §3B's symptom survives its own root-cause fix.

**⚠ THAT IS A HYPOTHESIS, NOT A FINDING, AND IT IS HANDED BACK AS ONE (CLAUDE.md §11B/§12).** It is
measured for the editor probe and has NEVER been measured for a troop. No fix was written.

### 8.5 WHAT WAS WRITTEN: the measurement that settles it in one grep (§12)

`Assets/_Modules/Village/Troops/TroopController.cs` — **instrumentation only, zero behaviour change.**
A new `routeGap=[...]` token on the existing `[Flow:RaidAI]` line reports how far a REFUSED objective
route actually got: `last=<m from the spire> straight=<m> corners=<n>`. `_objectiveRouteStatus` is
left byte-identical, so the existing `routeObj=` / `routeOpen=` greps above keep working.

Read `last=` against the **4.30 m carve radius WO-1749 measured** (its probe legs arrive 2.00–2.17 m
from centre):

| Capture shows | Meaning | Action |
|---|---|---|
| `routeOpen=True` appears | 1749 closed it; §3B satisfied for free | pin with a regression, no behaviour change |
| `last=` a couple of metres | the troop REACHES the spire; the runtime PathComplete-to-centre criterion is the defect, in `TroopController.cs` | port WO-1749's ARRIVED rule to the runtime check — a costed, ready one-seam fix |
| `last=` tens of metres | the path dies at the WALL RING; the navmesh hole never opened | WO-1723 silo — per this ticket's §3B table, do NOT add a targeting rule |

**Command for the lead — HEADLESS, ~2 min, no device, no Play Mode.** The path was verified today:
`Assets/Editor/Regression/RaidAssaultTraceCapture.cs:73` calls `ForceAssaultRescanForTrace()`
(`TroopController.cs:1861`), which calls `NearestHostile()` (`:921`) — and `RefreshRouteToObjective`
is called from **inside** `NearestHostile`, so the batch run really does execute
`NavMesh.CalculatePath` against the baked scene and will print `routeGap=`.

```
powershell -File run-unity-method.ps1 -Method DeNelle.Editor.RaidAssaultTraceCapture.Run
# then, on the FRESH Unity log (UTF-16 — read it with PowerShell, per memory):
Select-String -Path <editor.log> -Pattern 'routeGap=\[[^\]]*\]' -AllMatches |
  % { $_.Matches } | % { $_.Value } | Group-Object | Sort Count -Desc | Select -First 10
```

⚠ **Two honest caveats on the headless run, neither of which blocks it:**
1. `RaidAssaultTraceCapture.ScenePath` is hardcoded to `RaidBase_raider_camp_small` (`:22`) — the ONE
   scene WO-1749 reports has **no KeepPlatform**, so its spire was never the buried one. That makes it
   the *clean* test of the carve-footprint question (no burial confound), but the lead should re-point
   `ScenePath` at `iron_bastion` for a second run to cover the platform scenes.
2. It is EditMode, so troops spawn without `Awake`. If the numbers look degenerate, fall back to the
   device capture below — but the route query itself needs only a transform and a baked navmesh.

**Command for the lead — DEVICE fallback** (any raid on a post-`3b4b98834` build):

```
adb logcat -d > logs/device/wo1730-routegap/logcat_full.txt
grep -o "routeOpen=[A-Za-z]*"        logs/device/wo1730-routegap/logcat_full.txt | sort | uniq -c
grep -o "routeGap=\[[^]]*\]"          logs/device/wo1730-routegap/logcat_full.txt | sort | uniq -c | sort -rn | head
```

⚠ `RaidNavBake`'s `RAID_NAV_REACH_OK` is an EDITOR probe and is **not** a substitute — the whole point
of 8.4 is that the probe and the runtime now ask different questions.

### 8.6 Reported, not fixed — outside this lane

`WorkOrders/WORK_ORDER_1723_...md` line 3 still reads **"LANE C RE-BAKE OUTSTANDING"**, but WO-1749
records re-baked scenes at `Builds/raidgen1749` / `Builds/raidbake1749b`. That status line may be
stale. Not this lane's file (WO-1723 Lane B is a named disjoint silo) — flagged for the lead.

### 8.7 Gates — SUPERSEDED by §9.5 (the capture came back and the fix was implemented)

- `python tools/gate_brace.py Assets/_Modules/Village/Troops/TroopController.cs` → `GATE_BRACE_SUMMARY bad=0 of 1`, exit 0
- raw brace one-liner → `258 / 258` balanced; NUL bytes → `0`
- ⛔ **No Unity run, no gate, no commit** — edit-only lane; `COMPILE_GATE_OK` is the lead's.

---

## 9. §3B IMPLEMENTED — the capture came back and it named the branch

⚠ §8 was written BEFORE the capture and handed §3B back as an explicit hypothesis. The lead ran it;
this section supersedes §8.4/8.5/8.7. §8.1–8.3 (the §1 half) still stand unchanged.

### 9.1 THE PROVING LINE

`Builds/wo1730-assault-trace.log` — 2026-09-15 21:46, `RaidAssaultTraceCapture` on
`RaidBase_raider_camp_small`. **Four `routeGap` tokens, all identical in shape:**

```
routeGap=[last=4.7 straight=48.1..55.1 corners=4]
```

Read against WO-1749's **measured 4.30 m carve radius**, that is §8.5's **middle row**: the troops
walked the full ~50 m and stopped **4.7 m from the spire's CENTRE** — on the carve edge. They ARRIVED.
The wall-ring explanation is ruled OUT by the same number: it would have read tens of metres.

**So the defect is the runtime criterion, in `TroopController`, exactly as §8.4 flagged — and §12's
gate is satisfied: this is captured data, not a static read.**

### 9.2 THE FIX — WO-1749's ARRIVED rule, ported to the runtime

`RefreshRouteToObjective` required `CalculatePath(... spire.WorldPosition ...) == PathComplete`. The
spire carves its own footprint out of the navmesh, so **its centre has no polygon and that criterion is
unsatisfiable** — which is why `routeOpen=True` never occurred in 2,670 samples. It is now: *PathComplete
**or** the path's last corner lands within the objective's arrival radius.*

- `RaidAssaultAi.ArrivalRadius(footprintRadius, agentRadius)` + `RaidAssaultAi.RouteArrived(...)` — new
  **pure** statics, so the rule is assertable with no scene (the shape every other rule in this file uses).
- `RaidAssaultAi.ArrivalSlackMeters = 0.5f` — **deliberately the same 0.5 m as
  `RaidKeepReachRegression.ArrivalSlack`** (`Assets/Editor/Regression/RaidKeepReachRegression.cs:136`).
  If the bake probe and the runtime used different slack, a bake could certify a scene the troops then
  refuse to path through — a green marker over a broken game (CLAUDE.md §8/§16). Pinned; see 9.4.
- **Both real terms are MEASURED, never hardcoded**, from the same places WO-1749's probe reads them:
  the footprint off the spire's own renderer bounds (`TroopController.ObjectiveFootprintRadius`, cached
  per spire instance-id — this sits in a 0.5 s-throttled per-troop loop) and the agent radius off the
  live agent, falling back to the baked NavMesh settings (`LiveAgentRadius`).

### 9.3 TOKEN COMPATIBILITY — kept, and the fix is observable

- `routeOpen=` / `routeObj=` unchanged in shape. A route that arrives without `PathComplete` reports
  **`PathPartial-arrived`**, which is PREFIX-compatible: the `grep -o "routeObj=PathPartial"` in §8.5
  still matches it, while a reader can see *which* rule opened the route. Writing `PathComplete` there
  would have been a lie the next capture could not catch.
- `routeGap=` is now emitted on the **OPEN** branch too (it was failure-only), so the fix is observable
  rather than asserted — the next capture should show `routeOpen=True` alongside `last=4.7`.
- Per CLAUDE.md §12 the instrumentation **stays in permanently**; it is not "cleanup" to remove later.

### 9.4 REGRESSION — `Assets/Editor/Regression/ObjectiveRouteArrivalRegression.cs` (new, 5 cases)

Registered with **ONE** line in `DataRegression.cs:1506`, placed immediately after the
`wall-breach-order` suite (`:1505`) — well clear of the other lanes' hunks at ~:1780 / ~:1918.
Marker `OBJECTIVE_ROUTE_ARRIVAL_OK`, log tag `[objective-route-arrival]`.

| Case | Pins | Red proof |
|---|---|---|
| `ArrivedAtCarveEdge_IsOpen` | the **captured** 4.7 m vs 4.30 m carve radius reads OPEN | revert `RouteArrived` to `return false` |
| `StoppedAtWallRing_IsNotOpen` | **10 m** is NOT arrived — the ring is still sealed | make `RouteArrived` return true |
| `NoCorners_IsNeverArrived` | the -1 sentinel never opens a route on a failed query | return 0 instead of -1 |
| `SlackMatchesBakeProbe` | runtime slack **== 0.5 m == the bake probe's** | change `ArrivalSlackMeters` |
| `ArrivalRadiusIsMeasuredNotHardcoded` | the radius scales with BOTH footprint and agent radius | fix the radius to a constant |

That is precisely the pair the lead asked for (arrival-radius+tolerance = open; 10 m = not open), plus
the three guards that stop the fix from being undone by a plausible-looking tidy-up.

### 9.5 Files touched + gates

| File | Change |
|---|---|
| `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` | pure `ArrivalRadius` / `RouteArrived` / `ArrivalSlackMeters` |
| `Assets/_Modules/Village/Troops/TroopController.cs` | ARRIVED rule in `RefreshRouteToObjective`; `LastCornerDistance` / `ObjectiveFootprintRadius` / `LiveAgentRadius`; `routeGap` on every branch |
| `Assets/Editor/Regression/ObjectiveRouteArrivalRegression.cs` | **new** suite (5 cases) |
| `Assets/Editor/Regression/DataRegression.cs` | **one** registration line at `:1506` |

- `python tools/gate_brace.py` on all four → `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0
- raw brace one-liner → 263/263, 34/34, 28/28, 1229/1229; **NUL bytes = 0** on all four
- `tasklist | findstr /i Unity.exe` returned **clear** immediately before every write under `Assets/`
- ⛔ **No Unity run, no gate, no commit** — edit-only lane. `COMPILE_GATE_OK` + `REGRESSION_OK` are the lead's.

### 9.6 ⚠ STILL UNPROVEN — state it, do not assume it

- **`iron_bastion` (and every KeepPlatform scene) was NOT captured.** `RaidAssaultTraceCapture.ScenePath`
  is hardcoded to `RaidBase_raider_camp_small` (`:22`), the one scene WO-1749 reports has **no
  KeepPlatform** — so the capture proves the carve-edge case on a ground-seated spire only. A platform
  scene adds the lift WO-1749 fixed on top of it, and **nothing here proves the ARRIVED rule clears that
  combination.** The lead is running the second capture; the `ScenePath` change is deliberately left for
  it and was not made blind.
- **No device capture on the fixed runtime yet.** The expected reading is `routeOpen=True` with
  `routeObj=PathPartial-arrived` and `last=` a few metres. Until that log exists, "troops leave the wall
  for the spire after a breach" is **implemented and unit-pinned, not observed**.
- **§3B's acceptance is the owner's felt test**, not this lane's (CLAUDE.md §13: the CLI does not close).

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
