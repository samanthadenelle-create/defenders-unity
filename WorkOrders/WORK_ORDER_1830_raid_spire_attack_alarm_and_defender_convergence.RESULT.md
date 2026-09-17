# WO-1830 RESULT — Raid spire attack alarm + defender convergence

**Status:** IMPLEMENTED
**Verification:** code + oracle written; **the oracle has NOT been executed — no GO for a Unity run.** See §4.
**Date:** 2026-09-17
**Lane:** Combat/AI (§9). No scene file touched, no bake, no git run by this lane.

---

## 1. What shipped

| File | Change | Lines |
|---|---|---|
| `Assets/_Modules/Village/World/Camps/RaidSpire.cs` | `public static event Action<RaidSpire> AlarmRaised` (`:137`), `_alarmRaised` (`:139`), `IsAlarmRaised` (`:142`); raised from the ONE damage funnel on first damage behind `Guard.Try` | `:115-142` (event + doc), `:330-338` (raise inside `ApplyDamage`) |
| `Assets/_Modules/Village/Enemies/EnemyBrain.cs` | `RallyTo` (`:193`), `IsAlarmed` (`:205`), `HomeAnchor` (`:212`), pure `ComputeRallyPoint` (`:233-258`); `_alarmed` field (`:745-750`); cleared in `ResetForPool` (`:1277-1280`) | `:165-258`, `:745-750`, `:1277-1280` |
| `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs` | `_brains` (`:97`), `alarmRallyRing`=6 m (`:106`), `AlarmRaised` (`:109`), `TrackedBrainCount` (`:112`); subscribe `:124`, unsubscribe `:132`, `HandleSpireAlarm` `:147-160`, static `FanOutAlarm` `:173-189`; `Track(Enemy, EnemyBrain)` `:557-576` rallies a late spawn; call sites `:448`, `:515` | as listed |
| `Assets/Editor/Regression/RaidSpireAlarmRegression.cs` | NEW — 6-case oracle, marker `RAID_SPIRE_ALARM_OK` / `_FAIL` | whole file (569 lines) |
| `Assets/Editor/Regression/DataRegression.cs` | ONE registration line (`[raid-spire-alarm]`) | `:2120-2124` |

## 2. The design, in one paragraph

Attacking the spire raises **one static event on the first damage of that spire's life** (first hit, not a
% threshold — the ruling says *"starts attacking"*). `RaidGarrisonSpawner` subscribes once per raid and
fans the alarm out to the `EnemyBrain` list it already collected at spawn time, so there is **no
`FindObjectsByType`, on any cadence**. Each alarmed brain gets `RallyTo(point)`, which sets `_alarmed` and
**moves `_homeAnchor` onto a 6 m ring around the spire** — and that is the entire "Alarmed" state.

**The honest finding, stated plainly because the brief asked for a new state:** the override already
existed. `EnemyBrain.Update`'s RETURN-HOME arm (`EnemyBrain.cs:1162-1181` after this change — `bool
leashedOut = !holdChase` at `:1162`, the walk-home `SetBrainTargetPosition` at `:1179`) already paths
an unengaged leashed brain to `_homeAnchor` through `Enemy.SetBrainTargetPosition` — the one mover. So
re-anchoring the home **is** the convergence walk, it uses the NavMesh mover already there, and it is why
convergence is not gated on hero proximity: the walk home happens *because* the hero is out of range, and
home is now the base. `_leashRadius`, `_chaseLeashOverride`, `_defendPost`, `Role` and tactics are
untouched, and `_alarmed` is read only by `IsAlarmed` and the trace — so **an unalarmed brain's leash
arithmetic is byte-identical to before** (a `_leashRadius == 0` village enemy still short-circuits at the
same line). Two behaviours follow for free: `RaiderInRadius` and `NearestTroopDistanceFromHome` both
measure from `_homeAnchor`, so an alarmed defender also notices deployed troops near the spire.

A **ring**, not the spire point, because the return-home arrival test is `sqrMagnitude <= 4f` (~2 m) —
thirty bodies sent to one point would shove forever. Bearing = the defender's own old post direction, so
each post keeps its side; a defender already inside the ring holds its post. ⚠ That hold-post rule is
what *would* keep the boss on the keep with no special case — but whether `BossSpawn` actually sits inside
6 m of the spire is a per-scene BAKE fact and was **not read this session**, so it is not asserted (see
D4). If it sits outside, the boss converges like any other post, which is also acceptable.

## 3. Instrumentation (§12, permanent)

* `FlowTrace.Step(Sys, "SPIRE UNDER ATTACK - …took its first damage… Raising the garrison alarm.")` — `RaidSpire.cs:335`
* `FlowTrace.Step("Raid", "SPIRE ALARM config='…' - N defender(s) alerted of M tracked (K alive), converging on the base at … (rally ring 6m)")` — `RaidGarrisonSpawner.cs:156-159` — **the one alarm line, with the count**
* `FlowTrace.Once("EnemyAggro", "alarm-rally-<instanceId>", …)` — `EnemyBrain.cs:199` — one per converging brain. A **new** `Once` key deliberately: the existing `Once("leash-home-<id>")` has already fired for any brain that ever walked home, so reusing it would print nothing.
* Late-spawn line in `Track` (`RaidGarrisonSpawner.cs:573-575`) so the stagger top-up is visible, not silent.

## 4. ⛔ What is NOT proven, and what would prove it

**Nothing here has been executed.** This lane was told not to run Unity without GO, and
`Get-Process Unity` returned `NO_UNITY_RUNNING` (checked, per the instruction to check regardless).
So, in §11B terms:

* **PROVEN this session** — every fact in the WO's evidence table was read at source; brace balance is
  clean by BOTH counters (`python tools/gate_brace.py …` → `GATE_BRACE_SUMMARY bad=0 of 5`, exit 0; and
  the raw CLAUDE.md §1 count, 40/40, 112/112, 166/166, 51/51, 1243/1243); zero NUL bytes in all five
  files; `DeNelle.EditorRegression.asmdef` references both `DeNelle.Core` and `DeNelle.Village`, so the
  oracle's types resolve; the raid garrison path adds no `EnemyBehaviorTree` (grep rc=1), which is what
  makes the leash gate the branch that actually runs.
* **NOT PROVEN** — that it compiles (`COMPILE_GATE_OK`), and that `RAID_SPIRE_ALARM_OK` passes. Both need
  one batchmode run once the lead gives GO:
  * `run-unity-method DeNelle.Editor.CompileGate.Run`
  * `run-unity-method DeNelle.Editor.Regression.RaidSpireAlarmRegression.RunAll` (standalone marker), then
    `DeNelle.Editor.DataRegression.RunAll` for `REGRESSION_OK <n>/<n> suites` on a fresh log.
  * `DeNelle.Editor.Regression.EnemyPoolResetRegression.RunAll` — **run this one too**: a new `EnemyBrain`
    instance field is exactly the shape its `[brain-latch-coverage]` case fails on, and `_alarmed` is
    cleared, so it should stay green. If it does not, that suite is the authority, not this file.
  * **Not proven and not provable from the editor:** that the convergence *feels* right — how long 20
    defenders take to arrive, whether 6 m is the right ring, whether the alarm needs an audio sting
    (decision point D6). That is an owner felt-test on the device (§13: PO closes).

## 5. Owner decision points still open

D1 permanent alarm (no re-leash) · D2 same rally for every role · D3 all tiers unscaled · D4 boss has no
special case (the inside-ring rule decides; the BossSpawn distance is an unread bake fact) · D5 hero OR
troop first hit alarms — the `IDamageable` seam carries no attacker identity, so the distinction is not
available there · D6 no audio sting yet.

**One tightening the lead/owner may want, not taken unilaterally:** `HandleSpireAlarm` does not scope to
its own scene (`if (spire.gameObject.scene != gameObject.scene) return;`). Raid bases load **additively**,
and the owned-town spire is also a `RaidSpire` (`RaidSpire.cs:252-254` returns `Friendly` there). Today it
is harmless — the `IsEnemy` early-out (`RaidGarrisonSpawner.cs:139-143`) leaves `_brains` empty for a
non-enemy config, so a stray alarm fans out to zero defenders — but the one-line scene guard is what makes
"once per raid" literally true rather than true by luck. Defaults are shipped; all six are named in §4 of the WO with the reasoning, and
each is a one-line change if the owner rules otherwise.
