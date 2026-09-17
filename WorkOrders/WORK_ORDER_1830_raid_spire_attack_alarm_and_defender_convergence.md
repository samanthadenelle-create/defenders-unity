# WORK ORDER 1830 — Raid spire attack alarm + defender convergence

**Status:** IMPLEMENTED
**Result:** `WorkOrders/WORK_ORDER_1830_raid_spire_attack_alarm_and_defender_convergence.RESULT.md`
**Type:** NEW FEATURE (gameplay). Not a bugfix — nothing is broken; the behaviour asked for has never existed.
**Silo:** Combat/AI (CLAUDE.md §9 lane: code only, no scene files, no bake)
**Files:** `Assets/_Modules/Village/World/Camps/RaidSpire.cs`, `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs`, `Assets/_Modules/Village/Enemies/EnemyBrain.cs`, `Assets/Editor/Regression/RaidSpireAlarmRegression.cs` (new), `Assets/Editor/Regression/DataRegression.cs` (one registration line)

---

## 1. Owner ruling (verbatim, binding)

> "also we should make it when the player starts attacking the spire in a raid an alarm goes off and
> all the defenders start walking to the base to protect it"

and, when told the leash system already exists:

> "right now they just sit inside there leash range"

---

## 2. What is actually there today (read at source this session — no inference)

| Fact | Proof |
|---|---|
| The spire is a plain HP bucket; every damage source lands in one private method | `RaidSpire.ApplyDamage` — `Assets/_Modules/Village/World/Camps/RaidSpire.cs:293-308`, reached from `TakeDamage` (`:277`, the player/troop seam) and `ApplyContactDamage` (`:287`, the enemy/burn seam) |
| The spire already publishes an instance event, but only for its own death | `OnDestroyedEvent` — `RaidSpire.cs:113`, invoked in `Raze()` at `:323` |
| The live spire is reachable statically | `RaidSpire.Active` — `RaidSpire.cs:98`, set in `Awake` `:176`, cleared in `OnDestroy` `:184` |
| Garrison defenders are tracked in one list, and the spawner already holds each brain | `_garrison` (`RaidGarrisonSpawner.cs:89`), `Track` (`:477-482`); the brain locals are `brain` (`:353-354`) and `guardBrain` (`:427-428`) |
| Defenders are bound to a per-mob post with a 12-16 m wake ring and a 14-18 m chase cap | `BindDefendPost` — `RaidGarrisonSpawner.cs:528-550`, calling `EnemyBrain.SetDefendPost` (`EnemyBrain.cs:158-163`) |
| **Why they "just sit inside their leash range"**: the wake decision is purely per-mob and keyed on the hero's (or a troop's) distance from THAT mob's OWN anchor | `EnemyBrain.Update` `:1023-1027` — `wantsEngage = !ShouldLeashOut(_homeAnchor, _leashRadius, …)`, then the `_defendPost` troop top-up `RaiderInRadius(_homeAnchor, _leashRadius)` |
| A brain that is not engaged walks back to `_homeAnchor` and idles within ~2 m of it | `EnemyBrain.Update` `:1059-1078` ("RETURN-HOME"), arrival test `sqrMagnitude <= 4f` at `:1072` |
| No cross-unit broadcast of any kind exists | grepped `AlertAllDefenders`, `RaidAlarm`, `SpireAttack.*Alert`, `GarrisonAlert`, `SoundAlarm` across `Assets/_Modules` — zero hits |
| The raid garrison path adds **no** `EnemyBehaviorTree`, so the leash gate really is the branch that runs | `grep -n EnemyBehaviorTree Assets/_Modules/Village/Enemies/EnemyFactory.cs Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs` → rc=1 (no hits). The BT yield at `EnemyBrain.cs:970-974` returns before the leash gate, so this had to be confirmed, not assumed |
| Any new `EnemyBrain` instance field MUST be cleared in `ResetForPool` or an existing gate fails | `EnemyBrain.ResetForPool` `:1141-1189`; `EnemyPoolResetRegression` case 2 `[brain-latch-coverage]` fails on "EnemyBrain field(s) survive pooling" (`Assets/Editor/Regression/EnemyPoolResetRegression.cs:379-384`) |

---

## 3. Design

### 3.1 Trigger — first damage, not a threshold

`RaidSpire` gains `private bool _alarmRaised` and `public static event System.Action<RaidSpire> AlarmRaised`.
Raised from **`ApplyDamage` on the first hit of this spire's life**, before the `_hp <= 0` branch.

**Why first hit and not a % threshold:** the owner's words are *"when the player **starts** attacking the
spire"*. A threshold would mean the first several swings are silent, which is the opposite of the ask. A
`bool` on the spire instance is also naturally once-per-raid without any reset bookkeeping — the spire is
a scene object and the scene is unloaded between raids.

`RestoreOwnedTownCondition` (`RaidSpire.cs:256-265`) writes `_hp` directly and deliberately does **not**
route through `ApplyDamage`, so owned-town restoration can never fire a phantom alarm.

### 3.2 Fan-out — one subscription per raid, no per-frame scan

`RaidGarrisonSpawner` subscribes to the static event in `Start` (before the `ActivateRoutine` yield) and
**unsubscribes in its existing `OnDestroy`** (`:105-109`). The unsubscribe is load-bearing: a static event
outlives the scene, so a leaked handler would fan out to destroyed brains on the next raid.

It keeps a `List<EnemyBrain> _brains` populated alongside `_garrison` in `Track`. `FindObjectsByType` is
never called.

**Stagger hole, closed:** guards spawn 1-2 per frame (`:251-259`), so the alarm can land mid-spawn. The
spawner stores `_alarmRaised` and `Track` rallies any brain registered *after* the alarm, so a late guard
is not born unalarmed.

The fan-out itself is a **pure static seam**, `RaidGarrisonSpawner.FanOutAlarm(IList<EnemyBrain>, Vector3
spirePos)` returning the count alerted, so the regression can drive it in EditMode without NavMesh,
catalogs or a scene config.

### 3.3 The "Alarmed" state — a re-anchor, which IS the convergence

`EnemyBrain` gains `private bool _alarmed`, `public bool IsAlarmed`, `public Vector3 HomeAnchor` and:

```
public void RallyTo(Vector3 rally)   // sets _alarmed = true and _homeAnchor = rally
```

`_leashRadius`, `_chaseLeashOverride`, `_defendPost`, `Role` and tactics are all left untouched.

**This is deliberately not a new `Update` branch, and that is the design claim to review.** The task
brief asked for an "Alarmed" override state; the honest finding is that the override already exists and
is already the convergence walk — `EnemyBrain.cs:1059-1078` makes an unengaged leashed brain *path to
`_homeAnchor`*. Moving the anchor to the spire therefore makes every alarmed defender walk to the base
using the NavMesh mover it already has (`_enemy.SetBrainTargetPosition`), with:

* **no second mover invented** — `Enemy.DriveNav` is the one mover, reached the same way as today;
* **no change to an unalarmed brain** — `_alarmed` is only ever read for the trace and the public getter;
  the leash maths is byte-identical for every village/overworld/dungeon enemy (`_leashRadius == 0`
  short-circuits at `:1025` exactly as before);
* **hero proximity no longer gates convergence** — the walk home happens *because* the hero is out of
  range, and the new home is the spire. When the hero *is* at the spire (i.e. attacking it), the brain is
  inside its wake ring of the new anchor and engages normally.
* **troop detection follows for free** — `RaiderInRadius` (`:223-234`) and
  `NearestTroopDistanceFromHome` (`:236-253`) both key off `_homeAnchor`, so an alarmed defender now also
  notices troops near the spire.

`_alarmed` is cleared in `ResetForPool` alongside the other latches, so `[brain-latch-coverage]` stays green.

### 3.4 Convergence target — a ring, not the spire point

`public static Vector3 EnemyBrain.ComputeRallyPoint(Vector3 spire, Vector3 oldHome, float ring, int index, int count)` — pure, testable:

* the arrival test is `sqrMagnitude <= 4f` (`:1072`), so 30 bodies sent to one point would shove forever;
  the rally is a point on a `ring`-metre circle around the spire;
* bearing = the defender's own `oldHome` direction from the spire, so each post keeps its side of the base
  and the convergence spreads naturally;
* degenerate direction (a defender already at the centre, e.g. the boss at `BossAnchor`) falls back to the
  index/count angle formula the spawner already uses for its ring seating (`RaidGarrisonSpawner.cs:383-385`);
* a defender already inside `ring` of the spire keeps its post (returns `oldHome`) — it is already
  defending the base and re-anchoring it would only shuffle it sideways.

The spawner snaps the result through its existing `SnapToNav` (`:552-557`). Ring default **6 m**.

### 3.5 Instrumentation (CLAUDE.md §12)

* **One alarm line**, in the spawner's handler: `FlowTrace.Step("Raid", "SPIRE ALARM config='…' — N defender(s) alerted, converging on the spire at …")`.
* **One per-brain line**, inside `RallyTo`: `FlowTrace.Once("EnemyAggro", "alarm-rally-<instanceId>", …)`.
  `Once`, not `Throttle`, because it is a one-shot per body — and it must be a *new* key: the existing
  `Once("leash-home-{id}")` at `:1074` has already fired for any brain that ever walked home, so reusing
  that key would print nothing.
* Nothing is stripped; instrumentation is permanent (CLAUDE.md §12, the never-strip ruling).

---

## 4. Owner decision points (defaults picked, not blocking)

| # | Fork | Default shipped | Why |
|---|---|---|---|
| D1 | Re-leash after the spire stops taking damage for N seconds? | **No — the alarm is permanent for the raid.** | "all the defenders start walking to the base to protect it" reads as a commitment, and a defender that drifts back to a far post 10 s later re-creates the exact complaint. Reversible: clear `_alarmed` + restore the old anchor. |
| D2 | Do archers/casters converge differently from melee? | **No — same rally ring for every role.** | Tactics are untouched, so a Kiter still holds its 10 m standoff *once engaged* (`KiterTactics`, `EnemyBrain.cs:371-387`). Role-specific rally rings would be a second seating system next to `GarrisonSlot_*`. |
| D3 | All raid tiers, or scale with difficulty? | **All tiers, unscaled.** | The alarm is a readability/fairness beat, not a difficulty knob; `RaidDifficultyTunables` already owns difficulty and adding a fourth axis there is out of lane. |
| D4 | Does the boss also converge? | **No special case — it goes through the same rule as every other post.** | If `BossSpawn` sits within the 6 m ring, §3.4's hold-post clause keeps the boss on the keep with no code for it; if it sits outside, the boss converges like any other defender. ⚠ **Which one it is was NOT read this session** — `RaidGarrisonSpawner.cs:331-333` only shows `transform.Find("BossSpawn")` with a fallback to `transform.position`; the marker's distance from the spire is a BAKE fact per scene. Not asserting it either way. |
| D5 | Should troop damage (not just the hero) raise the alarm? | **Yes — any damage raises it.** | `IDamageable.TakeDamage(float, DamageElement)` (`RaidSpire.cs:277`) carries no attacker identity, so the distinction is not available at this seam without inventing one. |
| D6 | Any audio sting on the alarm? | **None in this WO.** | Deliberately out of scope: picking a cue is an owner creative call, and the Audio lane is a different silo (§9). The `FlowTrace.Step` is the hook a later WO attaches a `SfxId` to. |

---

## 5. Acceptance criteria

1. Hitting the spire once raises the alarm **exactly once per raid**; further hits do not re-raise it.
2. Every brain the spawner has tracked receives the rally, including guards spawned *after* the alarm.
3. An alarmed brain's `HomeAnchor` moves onto (or toward) the spire; an unalarmed brain's does not.
4. A defender that was already inside the rally ring keeps its post.
5. `_alarmed` is cleared by `EnemyBrain.ResetForPool` (pooled body does not inherit an alarm).
6. Unalarmed leash behaviour is unchanged — `ShouldLeashOut` / `ShouldHoldChase` / `ShouldWake` are not edited.
7. One alarm `FlowTrace` line with the defender count; one `Once` line per converging brain.
8. `RaidSpireAlarmRegression` passes and is registered in `DataRegression.RunAll`.
9. `tools/gate_brace.py` clean + zero NUL bytes on every touched `.cs`.

## 6. What NOT to touch

* No `.unity` scene file, no bake — this is behaviour, not layout.
* Do not edit `ShouldLeashOut`, `ShouldHoldChase`, `ShouldWake`, `ConfineToArea` or `HeroDistanceFromHome`
  (pure helpers pinned by `AggroLeashRegression` / `RepChaseLeashRegression`).
* Do not add a second mover — `Enemy.SetBrainTargetPosition` / `DriveNav` stays the only one.
* Do not call `FindObjectsByType` on any per-frame path.
* `CLI_LANES_WO_NUMBERS.md` is already bumped for 1830 — do not touch it.
