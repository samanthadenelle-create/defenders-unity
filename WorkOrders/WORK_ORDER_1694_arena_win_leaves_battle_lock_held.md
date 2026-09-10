# WORK ORDER 1694 — Arena win leaves the battle-lock held (a village wave lands 0.8 s after the victory)

**Status:** IMPLEMENTED 2026-09-10 — the active-scene exemption is now scoped to a scene-CHANGE hold, so the arena's hand-driven `TownSuspension.Suspend` actually stops the village wave clock instead of being silently voided.
**Silo:** Combat / World (TownSuspension seam — `DeNelle.Core`)
**Source:** F8 device captures seq **5009** + **5010**, owner's Seeker `SM02G4061955851`, build 363866, session 2026-09-10 15:20
**Raw evidence:** `Builds/device-frames/2026-09-10_1520_logcat.txt` (732k lines — grep it, never read it whole)
**Frame:** `Builds/device-frames/2026-09-10_1520_owner_icons.png`

---

## 1. The symptom the owner saw

She won a battle-arena fight and came home with combat input still suppressed and the HUD unable to
return to its town context. Two errors fired one after the other.

`logs/f8-inbox/capture-device-20260910-143752-seq5009.md`:

```
[Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (arena win) - 2 invariant(s) NOT restored after the battle:
  - battle-lock: still HELD after the battle ended. Combat input stays suppressed and the HUD cannot
    return to its town context. HOLDER(S): WaveManager.<OnEnable>b__129_0 (of 3 registered:
    PursuitBattleProbe.Probe, BattleArena.<Awake>b__84_0, WaveManager.<OnEnable>b__129_0).
    PURSUIT PULSES: none.
  - wave-phase: the wave loop is LATCHED at phase=Active ... phase=Active wave=1
    awaitingPlayerStart=False countdownRemaining=0.00s | liveEnemies=4 (+0 null slot(s) not yet
    pruned) apexBossAlive=False heart=present | heldSmartReinforcements=0 | lastPhaseTransition:
    Countdown -> Active at 'StartWave' (t=66.69s unscaled, frame 1719, 0.83s / 23 frames ago) ...
    townSuspension: IsSuspended=False reason='none' graceRemaining=0.00s held=False
    suspendedForThisManager=False | scene='Main_Castle_Overworld'
    activeScene='Main_Castle_Overworld' isCanonicalInstance=True liveWaveManagers=1
```

`logs/f8-inbox/capture-device-20260910-143753-seq5010.md`, 0.08 s later:

```
[Flow:Quiescence] battle-lock STILL HELD after the self-heal (arena win):
[WaveManager.<OnEnable>b__129_0] (was [WaveManager.<OnEnable>b__129_0]). ...
PURSUIT PULSES before the heal: none; after ...: none.
```

The frame `2026-09-10_1520_owner_icons.png` shows the arena floor, an `Orcish Raider 143/143` nameplate
with `LOCKING`, and `Flee` — and, **partly occluded behind that nameplate, the town HUD line
`Next Wave in 14m 46s`**. The village wave clock was on screen *inside the arena*. (That frame belongs
to a later arena of the same session — the countdown reads wave 2, not wave 1 — so it corroborates the
mechanism, it is not bound 1:1 to seq 5009.)

---

## 2. RCA — the framing is not the obvious one

**Nobody declared the win over four live enemies.** The arena's own release was clean, and the same
session proves the release path works: two retreats released with `holders=[none]`.

```
L714122  [Flow:Quiescence] BATTLE_SESSION_RELEASED (#1, arena win) - pursuit window cleared,
         5 owner unwind(s) run. battle-lock holders before=[none] after=[none], timeScale=1.00.
L720982  [Flow:Quiescence] BATTLE_SESSION_RELEASED (#2, retreat) ... before=[none] after=[none]
L731814  [Flow:Quiescence] BATTLE_SESSION_RELEASED (#3, retreat) ... before=[none] after=[none]
```

The four enemies are **a village wave that spawned 0.8 s AFTER the win**:

```
L714062  [Flow:BattleArena] Resolve: WIN in 14.0s -> 3 star(s), reward x1.50.
L714858  [Flow:Wave] TickCountdown: countdown hit 0 for wave=1 — calling StartWave (phase Countdown->Active)
L714859  [Flow:Wave] StartWave(1) -> phase=Active (spawning begins)
L714913  [Flow:Wave] wave 1: post-spawn live=4/8 released=4 across up to 1 side(s)
L715193  [BREAK] error: [Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (arena win) ...
```

The quiescence gate is **correct**: a live siege genuinely is combat, and the self-heal said so in
plain words rather than cancelling anything —

```
L715204  [Flow:Wave] wave 1: the forced tick left the phase at Active - live=4 apexBossAlive=False
         heldReinforcements=0. A GENUINE siege is still running, so the battle-lock is correctly still
         held and nothing is cancelled. Retreating from an overworld encounter must never end a village siege.
```

### Why was the village clock running during an arena fight?

The arena **is** supposed to pause the town, and `BattleArena` drives that by hand precisely because
the arena has no scene change (`Assets/_Modules/Village/Arena/BattleArena.cs:500-516`, its own comment
spells it out; the call itself is `:516`). The call fired:

```
L710344  [Flow:TownSuspend] town SUSPENDED (arena battle staged at ArenaCentre (hero 7km away,
         player active)). Held: wave countdown, enemy spawns, structure damage, heart damage. ...
```

And the countdown ticked **every second, straight through the fight**:

```
L710750  [Flow:HUD] Countdown wave 1/0 live 0/0 cd19.8
L713672  [Flow:HUD] Countdown wave 1/0 live 0/0 cd8.0
L713989  ... cd7.0     L714261  ... cd5.9     L714486  ... cd3.8     L714689  ... cd2.0     L714808  ... cd0.8
```

`grep -c "HELD at phase"` over all 732 000 lines returns **0**. The `SuspendAndResume` hold
(`Assets/_Modules/Village/Waves/WaveManager.cs:1307-1310`) never engaged once, all session.

**The same-session control settles it.** The dungeon window is a real scene change:

```
L398282  [Flow:TownSuspend] town SUSPENDED (player active in 'dg_hollow_roads' ...)
L405463  [Flow:TownSuspend] town RESUMED (active scene 'Main_Castle_Overworld' is a hub ...)
```

Between those two lines there is **not one countdown tick**. Same build, same session, same
`WaveManager`. Scene change → the pause works. No scene change → the pause is a no-op.

### Root cause

`TownSuspension.SuspendedFor` — the exemption as it stood at HEAD `0b942d0be`
(the method now sits at `Assets/_Modules/Core/TownSuspension.cs:204-236`):

```csharp
// The player is standing here - never hold it still.
if (scene.handle == SceneManager.GetActiveScene().handle) return false;
```

That exemption answers exactly one question — *"did the player LEAVE this scene?"* — and it was applied
**unconditionally**. The arena stages 7 km away in the **same** scene, so `WaveManager` (living in that
active scene) was exempted and the hand-driven `Suspend` was voided **one method away from the call
site that exists solely to prevent this defect**. `BattleArena.cs:508-512` even names the original
incident: *"a village wave cleared 2.7 s after an arena victory and stranded the player, because the
wave clock ran the whole fight."* The return grace was added then; the grace was never reached, because
the freeze it grants a grace on never engaged.

Classic duplicated-intent drift: `TownSuspension.cs:362-365` states *"The ARENA is NOT covered by this
hook ... BattleArena drives it explicitly"*, and `BattleArena.cs:500-506` states the mirror image — both
correct, and the gate between them honoured neither.

---

## 3. Git history of the files involved

| File | Last commits |
|---|---|
| `Assets/_Modules/Core/TownSuspension.cs` | `bb3293a3b` *WO-1017 — the active scene is a FLOOR, not a peer flag*; `0fe787803` *suspend the town while the player is away*. **Untouched since 2026-08-10.** |
| `Assets/_Modules/Village/Arena/BattleArena.cs` | `26142d2f2`, `d6511b8e5`, `99b574392` *retreat no longer leaves the battle locked (WO-1337)*, `a2e0095f2` *BATTLE_QUIESCENCE_FAIL was a FALSE POSITIVE — the gate judged a different battle*, `242fe4fb4` |
| `Assets/_Modules/Village/Waves/WaveManager.cs` | `f4e4630e3`, `d6511b8e5`, `addb40de2` *WaveManager never registered a battle-session unwind (WO-1308)*, `88e72ea8d`, `486cd7b17` |
| `Assets/Editor/Regression/TownSuspendSceneFloorRegression.cs` | `42e7519ad`, `bb3293a3b` |

Note the two prior quiescence tickets on `BattleArena.cs` (`99b574392` WO-1337, `a2e0095f2`) both fixed
**release-side** causes. This one is not release-side at all — the release was clean.

---

## 4. The fix

`Assets/_Modules/Core/TownSuspension.cs`

The active-scene exemption is now honoured **only for a hold a scene CHANGE created** — the FLOOR:

```csharp
if (scene.handle == SceneManager.GetActiveScene().handle)
{
    if (_floorReason != null) return false;      // floored: the player really did leave
    FlowTrace.Throttle("TownSuspend", "floorless-hold-covers-active-scene", 10f, ...);
    return true;                                  // floorless: the player is elsewhere INSIDE this scene
}
```

- `_floorReason` is written by exactly one method (`ApplySceneBaseline`), so the test cannot drift.
- The trace is `Throttle`d at 10 s because this runs from `WaveManager.Update`; a per-frame line here
  would evict the boot window out of the device logcat ring (`FlowTrace.cs:293-300`).
- `Suspend` now also names the **coverage** of the hold it is raising (`FLOORED by '<scene>'` vs
  `FLOORLESS`). Before this, a working suspension and a no-op suspension printed the identical
  `town SUSPENDED` line — which is why a hold that did nothing sat in the log looking like one that did.

**Blast radius, enumerated at source.** `TownSuspension.SuspendedFor` has exactly four production
callers: `WaveManager.cs` (:893, :1014, :1285, :2600, :2636, :2856, :2872, :2874), `StructureBurn.cs:210`,
`RegionMobSpawner.cs:165` and `TownActivityProbe.cs` (:136, :171). All four are town-side; **nothing
arena-side consults this gate** (arena bodies are `Enemy` instances built by `EnemyFactory` and are not
callers), so no arena system can be frozen by the change. `TownActivityProbe.Note` counts a leak only
when `!inActive && !gated`, and its `FlowTrace.Fail` branch requires `!IsSuspended` — so the change
manufactures no new probe failures.

---

## 5. Regression

`Assets/Editor/Regression/ArenaInSceneSuspensionRegression.cs` — markers
`ARENA_INSCENE_SUSPEND_OK` / `ARENA_INSCENE_SUSPEND_FAIL`.

| Case | Proves |
|---|---|
| `arena-hold-covers-town` | **the fix.** The shipped literal `Suspend("arena battle staged at ArenaCentre (hero 7km away, player active)")` with no floor ⇒ a town object in the ACTIVE scene reports `SuspendedFor=true`. **RED before the fix.** |
| `floor-carve-out` | **the over-fix guard.** With a floor (`Dungeon_HealersCottage`), an active-scene object is still exempt — the dungeon's own enemies must never freeze. |
| `grace-covers-town` | after `Resume("arena battle resolved")` the return grace holds active-scene town objects. Pre-fix that grace was decorative for every in-hub hold. |
| `ddol-still-held` | the other half of the rule; needs no scene. |
| `callers-intact` | `BattleArena` still drives the `Suspend`/`Resume` pair and `WaveManager` still consults the gate per tick — both behaviour cases pass on a tree where either call was deleted. |
| `floor-scoped-at-source` | the exemption is floor-scoped and the `FLOORLESS` trace survives — **the batch fallback** (see below). |

**Honest limit, stated in the file header:** cases 1–3 need a probe object in a **named** scene.
`SuspendedFor` treats an unnamed scene as DontDestroyOnLoad (`TownSuspension.cs:210`) *before* the handle
test, so in a batch run with only an Untitled scene open they log a loud stand-down and skip; the summary
line says which mode ran. `floor-scoped-at-source` is what keeps the suite RED on a reverted tree in that
case. A source pin is not coverage on its own — it is the fallback under the behavioural cases, never
instead of them.

**Registration line for the committer** (`DataRegression.cs` is lane-fenced — the lead adds this):

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "arena-inscene-suspend suite", () => { if (!DeNelle.Editor.Regression.ArenaInSceneSuspensionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[arena-inscene-suspend] " + r); });
```

---

## 6. Residual — named, not fixed

`BattleArena.Resume` fires at **Resolve** (`BattleArena.cs:2723`), not at the hero's arrival home. In
this capture the warp followed ~1.5 s later (`L714116` resume → `L715040` `WarpHero REQUEST` →
`L715054` `FADE IN: home arrival`), comfortably inside the 3.5 s grace. But the victory summary is
**deferred until Continue** (`L714124`: *"home return is DEFERRED until Continue (watchdog armed)"*), so
a player who sits on that summary longer than the grace with a low countdown can still have a wave start
while they are 7 km away. **I have not proven this happens** — no capture in this session shows it — and
moving the `Resume` risks leaking a permanent freeze, which is strictly worse. Recorded for a ruling.

## 7. Do NOT touch

HUD dock / ability-face code (WO-1695) · `EchoWorldPresence` / `PetDeployer` (WO-1696) · any `.unity` ·
`Assets/Editor/Regression/DataRegression.cs` · `CLI_LANES_WO_NUMBERS.md`.
