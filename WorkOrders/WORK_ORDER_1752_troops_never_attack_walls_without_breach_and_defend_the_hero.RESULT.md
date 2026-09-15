# WO-1752 RESULT — walls off without Breach; the warband defends the hero

**Outcome: IMPLEMENTED, NOT YET GATED.** Edit-only lane: no Unity, no compile gate, no regression
run, no bake, no build, no git operation. Everything below is either a file that was written or a
check that was actually executed. Where something was not proven it says so in those words
(CLAUDE.md §11B).

---

## Files changed (5) + 1 new

| # | File | What |
|---|---|---|
| 1 | `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` | `ReluctantWallDamageMultiplier` **deleted**; new `MayTargetWall` (`:113`), `WallDamageMultiplier` rewritten (`:141`), new `AdoptHeroAttacker` (`:172`), new knob `HeroAttackerRingMeters` (`:67`), `PickBucket` 9-arg overload with `otherStructIsWall` (`:547`), the `mayWall` wall clause (`:575`) and the blocked-warband stand-down (`:629-631`) |
| 2 | **NEW** `Assets/_Modules/Village/Troops/HeroAggroTarget.cs` | the "what is hitting the player right now" publisher |
| 3 | `Assets/_Modules/Village/Troops/TroopController.cs` | hero-attacker adoption (`:1075-1090`), `otherStructIsWall` fed to `PickBucket` (`:1133-1137`), `heroAttacker=` on the RETARGET line (`:710`), `mayWall=` / `otherStructIsWall=` on the AI line (`:1196-1197`) |
| 4 | `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` | **doc comment only** — a `<see cref="RaidAssaultAi.ReluctantWallDamageMultiplier"/>` at `:63` pointed at the member this ticket deletes and would have warned (CS1574). Repointed at `MayTargetWall` in the same breath as the deletion (§15). No code changed. |
| 5 | `Assets/Editor/Regression/WallBreachOrderRegression.cs` | cases 7 and 9 **amended** (they asserted the retired behaviour), case 8 strengthened, new cases 13–16 |

⚠ **`HeroAggroTarget.cs` has no `.meta` yet** — Unity mints it at the lead's gate. It is the first
new file in `Assets/_Modules/Village/Troops/` this session.

⚠ `TroopController.cs` was edited **in place**. WO-1748's uncommitted `ReportVisualVerdict`
(`:489`, `:511`) is present and untouched; nothing was restored from HEAD.

---

## 1. Ruling 1 — walls off without Breach (the 10% path DELETED, not zeroed)

**`ReluctantWallDamageMultiplier` is gone from the tree** — `grep` over `Assets/**/*.cs` returns
**0 code references**; the four remaining hits are all prose that records the retirement
(`RaidAssaultAi.cs` ×2, `TroopBreachOrder.cs`, the regression header). What replaced it:

* **`MayTargetWall(preferStructures, breachStance) => preferStructures || breachStance`** — one
  predicate, one home, consumed by both the selector and the multiplier.
* **The targeting is what changed.** `PickBucket`'s `mayWall` now carries
  `&& (!otherStructIsWall || MayTargetWall(...))`. That is the WO's own point — *"a zero multiplier
  still walks troops to walls"*.
* **`WallDamageMultiplier` returns 0.00, and that is a backstop, not the fix** — stated here because
  it reads like the thing the brief said not to do. Two concrete reasons it must also be zero:
  `TroopController._cachedFoe` is re-resolved on a 0.2 s timer, so between the player toggling
  Breach OFF and the next retarget a troop still legitimately holds a panel and must land **nothing**
  in that window; and WO-1752 §3 asks for the `wallDmgMult=0.00` token itself.

**⛔ THE GATE IS ON THE CANDIDATE'S TYPE, NOT ON THE BRANCH, AND THAT IS DELIBERATE.** Bucket 2 is
*"other masonry"*, not *"wall"* — towers and gates ride it, and WO-1746 §4B.3 records that the
owner's wall ruling was never about them. Two rejected alternatives, both of which look tidier and
are wrong:

* **Deleting the Breach wall branch** would also stop a stance-less warband hitting a **tower that is
  shooting it**. Case 13's second assertion pins that it still can.
* **Filtering the wall out of the candidate set upstream** (in `TroopController`) would promote the
  **tower behind it** to bucket 2 — and structures carry **no reachability filter** (only units do).
  That is WO-1438's navmesh-edge freeze, reintroduced by a cleanup. The wall stays the candidate, so
  it still prints as `has[wall=True]`, and is **refused** at the selector.

`SelectFocusBreach` was **not** changed, for the same reason: it only decides *which* panel would be
the candidate; the refusal is one layer down. Naming it because the WO listed it.

---

## 2. ⛔ THE RED PROOF WAS EXECUTED AND IT CAUGHT A REAL DEFECT I HAD SHIPPED

**This is the finding of the lane.** Run 1 against the code as first written came up **RED**:

```
   FAIL C7 unreachable foe now takes NO wall: want -1, got 0
WO1752_RED failures=1
```

**Cause.** `PickBucket`'s Breach tail ends `return hasUnit ? 0 : -1;` — the old *"nothing else,
engage any unit rather than idle"* fallback. It was harmless while the wall bucket absorbed the
blocked warband. The moment walls were refused it became the **dominant path** for exactly the
troops this ticket is about, and it returns bucket 0 for a foe that is **UNREACHABLE** — the steer
WO-1438 proved freezes a troop on a navmesh edge, *because the thing making it unreachable is the
wall that was just refused*. I would have traded "chews a wall at 10%" for "walks into a navmesh
edge and stands there twitching", and the owner's felt-test would have been the detector. **Reading
the code did not show this; running it did** (memory `never-inference-fix`).

**Fix** (`RaidAssaultAi.cs:629-631`): the blocked-warband tail stands down instead —

```csharp
bool wallRefused = hasOtherStruct && otherStructIsWall
    && !MayTargetWall(preferStructures, breachStance);
if (wallRefused && !unitInAttackRange && !routeToUnitOpen) return -1;
```

Scoped to `wallRefused`, so with no wall in the picture the tail keeps its pre-WO-1752 answer
exactly. This is also the owner's own specification — *"a blocked warband with Breach OFF and no
siege now **stands** (or defends the hero)"*.

### Method (so the proof is auditable, not asserted)

The statics under test reference no `UnityEngine` type. A scratch script **mechanically slices the
method bodies out of the shipped `RaidAssaultAi.cs` by brace-matching** and emits a `net8.0` console
harness carrying the new cases' assertions. Nothing was hand-copied — a retyped predicate only
proves the copy behaves. `dotnet 10.0.300` is on this machine. Harness:
`<scratchpad>/rp/extract.py` + `rp/harness/`, regenerated from source on every run.

| Run | Mutation | Observed |
|---|---|---|
| 1 | none (code as first written) | **`WO1752_RED failures=1`** — the bug above |
| 2 | after the `wallRefused` fix | **`WO1752_GREEN`** (24 assertions) |
| 3 | drop the `!otherStructIsWall \|\| MayTargetWall(...)` clause on `mayWall` | **RED ×2** — case 13 and amended case 7 both fail (`got 2`) |
| 4 | drop the `wallRefused` stand-down | **RED ×1** — amended case 7 (`got 0`) |
| 5 | `WallDamageMultiplier` back to `0.1f` | **RED ×1** — amended case 9 (`got 0.1`) |
| 6 | `AdoptHeroAttacker` always false | **RED ×1** — case 15 |
| 7 | restored | **`WO1752_GREEN`**; source verified **byte-identical** to the pre-mutation copy (33 932 bytes both) |

Runs 3–6 prove each new guard is load-bearing rather than decoration. Three controls held green
throughout: the **siege** assertions, **Case 8** (the stance vs a calm defender, and aggro still
peeling), and the **7-arg `PickBucket`** shapes that `RaidAssaultAiRegression` uses.

---

## 3. Ruling 3 — defend the hero

### The seam the WO asked me to find DOES NOT EXIST. That is a finding, not a gap I papered over.

`HeroHealth.cs` was read **read-only** (it belongs to WO-1750's lane). There is no "last attacker of
the hero" publisher anywhere in the damage pipeline:

* `HeroHealth.TakeDamage(float)` (`:666`) takes an **amount and no attacker**.
* The contact tick attributes nothing outward: it scans for adjacent `Enemy` components **itself**,
  sums their `ContactDamage`, and keeps the buffer **private** (`:364-405`, `_attackerBuf`). The
  nearest thing to a publisher is `_lastDamageSourceWorld` — a **position**, private, used only to
  pick a death-direction bucket.
* `NoteDamageSource(Vector3)` (`:664`) is the inbound seam and is also a bare position.
* `HeroCombatEngagement` (Core/Combat) is a ref-counted set of opaque `object` tokens feeding
  `BattleLock`; it cannot hand back an `IDamageable` to target and is scoped to hero-only duelists.

So **no second damage listener was added** (the WO forbids one) and **HeroHealth.cs was not
touched**. `HeroAggroTarget` derives the signal from two public facts instead: the hero's HP going
**down** (`HeroHealth.Hp`) and the nearest live hostile body to her.

### What is measured vs. inferred

* **MEASURED:** "her HP dropped" — polled, not subscribed. `OnHealthChanged` fires from eleven sites
  (`Heal`, `RegenTick`, `RestoreToFull`, `Respawn`, gear re-sync …) so a subscriber would have had
  to diff the value anyway, and `Instance` is replaced across scene loads (`:246`/`:279`) so the
  subscription would need re-arming. Polling a public float every 0.35 s has no lifetime to get
  wrong.
* **INFERRED:** "…and *this* enemy did it" — nearest hostile within `HeroAttackerRingMeters` (8 m).
  Tight for the melee case the WO is about; **wrong for a tower or a ranged attacker** — see open
  question 2. The unattributed case is **traced, not swallowed**
  (`FlowTrace.Throttle "hero-aggro-unattributed"`).

`EnemyDamageable` is the scan type because every `Enemy` carries it by `RequireComponent`
(`EnemyFactory.cs:159-161`). The scan is **not** widened to every `IDamageable`: sending the warband
at a hostile *structure* beside her would reintroduce the very bug this ticket closes.

### WO-1746 §4 is NOT re-litigated — and the guarantee is arithmetic, not a promise

`peelThreat` is **deliberately not recomputed** after adoption. Aggro (the Peel phase) stays the one
thing that breaks an armed Breach stance. It could not fire anyway: adoption only happens when the
troop has **no unit in its own sweep** (`AdoptHeroAttacker`), so the adopted body is by construction
outside the acquire radius — **12 m** for the shieldguard in the WO's own evidence — while
`PeelUnitLeashMeters` is **6 m**. The omission is the guard; the arithmetic is the proof. Case 15's
fourth assertion pins that an armed stance still resolves the wall with an adopted unit present and
reachable, and mutation run 6 shows it is live.

The hurt window reuses `RaidAssaultAi.PeelHurtWindowSeconds` (2.5 s) rather than minting a second
number; case 16 fails if a future seat mints one.

---

## 4. Trace (§12 — added, never stripped)

* `RETARGET` line (`TroopController.cs:710`) gains **`heroAttacker='<name(Type)>'`** / `'<none>'`.
* AI line (`:1196-1197`) gains **`mayWall=`** and **`otherStructIsWall=`**. `has[wall=True]` alone
  can no longer distinguish a **refused** panel from no panel — that distinction is the whole
  device-side proof of ruling 1.
* **`wallDmgMult=` now prints `0.00`** for non-siege without stance (same `WallDamageMultiplier`
  call the `SWING` line and `Attack()` use — one source, three readouts). **A `0.10` token on a
  fresh log is now proof the build predates this ticket.**
* `HeroAggroTarget` emits `FlowTrace.Once` when there is no live `HeroHealth.Instance` and
  `FlowTrace.Throttle` (5 s) when the hero lost HP with no hostile body in the ring.

---

## 5. Checks actually executed

* `python tools/gate_brace.py` over all 5 changed files + the new one → **`GATE_BRACE_SUMMARY bad=0
  of 5`, exit 0** (and `bad=0 of 1` for the new file after its line endings were normalised).
* **NUL scan: 0 bytes** in every file. Raw brace counts balanced: 32/32, 13/13, 252/252, 19/19,
  92/92.
* Line endings: `HeroAggroTarget.cs` written LF, converted to **CRLF** to match its neighbours.
* The sliced red proof above — 7 runs, 24 assertions, 4 mutations, source restored byte-identical.
* Read **read-only** to avoid a silent no-op: `HeroHealth.cs` (the missing seam),
  `HeroCombatEngagement.cs`, `EnemyDamageable.cs`, `EnemyFactory.cs`, `RaidAssaultAiRegression.cs`.

## 6. NOT proven here

* **No compile.** The harness compiles *sliced pure statics* under `net8.0`, **not** the Unity
  assemblies. `HeroAggroTarget.cs`, the `TroopController` edits and the regression file have **never
  been through a C# compiler**.
* **No `COMPILE_GATE_OK`, no `REGRESSION_OK`, no `WALL_BREACH_ORDER_OK`.** Cases 16's source-text
  assertions and the `TroopBreachOrder` static cases cannot run here.
* **Nothing was run in a scene.** `HeroAggroTarget`'s scan, the adoption's effect on real steering,
  and the trace tokens are **unverified on device**. The acceptance criterion *"headless raid: zero
  `wallDmgMult=0.10` tokens"* is unmet — no headless run was made.
* **A false-positive window in the HP poll is NOT ruled out.** `HeroHealth.SyncGearHp()` runs every
  frame and can **clamp `_hp` downward** on a gear change; the poll reads any decrease as "she was
  hurt". Un-equipping an HP item could therefore arm a 2.5 s defend-the-hero window with nobody
  attacking her. Not observed, not excluded — it needs a scene to test and the fix (if it is one)
  would be in `HeroHealth.cs`, another lane's file.
* **The 8 m ring is a felt number with no data behind it.** `HeroHealth`'s real contact ring is a
  private `const float EngageRadius = 1.5f` (`HeroHealth.cs:45`) — too tight to use here (an
  attacker stepping back half a metre between swings would flicker the answer every scan), and that
  file belongs to another lane so it was not widened. 8 m is reasoned, not measured. The owner's
  felt-test is its only real verdict.

---

## 7. Owner questions (neither guessed at)

1. **A hero-attacker with a CLOSED route loses to the objective.** The WO says defending the hero
   *"out-ranks walls and objectives"*. As implemented, the adopted attacker is a normal unit
   candidate and therefore still passes through `PreferUnit`'s **reachability** filter — so if the
   route to it is blocked, the spire still wins. I did **not** override that, because bypassing
   reachability is precisely WO-1438's navmesh-edge freeze. In practice the hero is usually inside
   the base with the warband, so the route is usually open. **Is the reachability filter acceptable
   here, or does "out-ranks objectives" mean steer at her attacker regardless?**
2. **A TOWER hitting the hero is not covered.** `DefenseTower` / `ArcaneTower` / `Tower` all damage
   `HeroHealth` directly, and the publisher only adopts hostile **units**. If she is being shot from
   a tower the warband will take the nearest body to her instead, or — with nothing near her — stand.
   The case is traced (`hero-aggro-unattributed`) rather than silently ignored. **Should towers
   shooting the hero become adoptable targets too?**
3. **A FELT CHANGE ON A PATH THE WO DID NOT MENTION — named so it is not triaged as a bug.**
   In the **Peel** phase, a troop hurt by a **tower** with no unit in its leash used to resolve
   bucket **-1** (`PickBucket`'s Peel branch: *"Hurt by a tower with no unit in leash — do NOT
   resume wall-ring farming"*). Now, if the hero is being hit at the same moment, that troop adopts
   her attacker and **walks off while it is being shot**. It follows directly from ruling 3 ("the
   warband defends the player before anything else") and is probably what she wants, but it is a
   behaviour change she will feel and it was not in the WO.
4. **The dead-end is now real and she asked for it.** A blocked, stance-less, siege-less warband
   with nothing to defend now **stands still** (`PickBucket` → -1). Recorded so the first felt-test
   report of "my troops are doing nothing" is read as the ruling working, not as a new bug.

## 8. For the lead

* **Not edited, reported instead:** `Assets/Editor/Regression/RaidAssaultAiRegression.cs` also drives
  `PickBucket` with a wall candidate (`:100`, `:115`, `:132`, `:220-231`). It uses the **7-arg**
  overload, so `otherStructIsWall` defaults false and **every one of its cases still passes** —
  proven in the harness (three shapes, run 7). No change needed; flagged so a red there is not a
  surprise.
* `WallBreachOrderRegression` is already registered in `DataRegression.cs`; **the suite count does
  not move.**
* WO-1746 §1 and WO-1738's matching clause still assert the retired 10% fallback — the lead's step
  per this WO's own acceptance line.
