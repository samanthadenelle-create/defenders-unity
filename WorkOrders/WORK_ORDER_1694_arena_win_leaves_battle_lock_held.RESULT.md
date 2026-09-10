# WORK ORDER 1694 — RESULT

**Status:** IMPLEMENTED 2026-09-10 (ARENA-LOCK SME lane, worktree `agent-a9eeaae7251d2c8c4`, branch `dev` ff-merged to `0b942d0be`)
**Not gated, not committed, not pushed** — per the brief. No Unity was run.

---

## What was wrong (evidence, with file:line and log:line)

The arena win did **not** declare a win over live enemies. Its release was clean:
`Builds/device-frames/2026-09-10_1520_logcat.txt` **L714122**
`BATTLE_SESSION_RELEASED (#1, arena win) ... holders before=[none] after=[none]`, matching the same
session's two retreats (**L720982**, **L731814**).

The four enemies were a **village wave that started 0.8 s after the win** (**L714858/L714859**
`TickCountdown: countdown hit 0 for wave=1 — calling StartWave`, **L714913** `post-spawn live=4/8`),
which correctly re-raised the battle-lock and failed the gate (**L715193**).

The village clock should have been frozen. `BattleArena.cs:516` drove the pause by hand
(**L710344** `town SUSPENDED (arena battle staged at ArenaCentre ...)`) — and the countdown ticked
anyway, every second, **L710750** `cd19.8` through **L714808** `cd0.8`. `grep -c "HELD at phase"` over
all 732 000 lines of that logcat = **0**: `WaveManager.cs:1307-1310`'s hold never engaged, all session.
Same-session control: the dungeon window **L398282–L405463** (a real scene change) contains **zero**
countdown ticks.

**Root cause:** `TownSuspension.SuspendedFor`'s active-scene exemption
(`if (scene.handle == SceneManager.GetActiveScene().handle) return false;`) was unconditional. It
answers "did the player LEAVE this scene?" — and the arena stages 7 km away in the **same** scene, so
`WaveManager` (in the active scene) was exempted and the hand-driven `Suspend` was voided one method
away from the call site that exists to prevent exactly this (`BattleArena.cs:508-512` names the
original incident by name).

## What was changed

| File | Change |
|---|---|
| `Assets/_Modules/Core/TownSuspension.cs` | The active-scene exemption is now honoured only for a **FLOOR** hold (`if (_floorReason != null) return false;`, `:216`). A floorless, hand-driven hold covers the active scene too, with a 10 s-`Throttle`d `[Flow:TownSuspend]` line at the decision. `Suspend` now names the hold's **coverage** (`FLOORED by '<scene>'` vs `FLOORLESS`). Header + `SuspendedFor` doc carry the RCA with the logcat line numbers. |
| `Assets/Editor/Regression/ArenaInSceneSuspensionRegression.cs` | **NEW.** Markers `ARENA_INSCENE_SUSPEND_OK` / `ARENA_INSCENE_SUSPEND_FAIL`. Six cases (below). |
| `WorkOrders/WORK_ORDER_1694_arena_win_leaves_battle_lock_held.md` | **NEW.** Full RCA, quoted evidence, git log of the files involved. Status flipped to IMPLEMENTED. |
| `docs/MASTER_CATALOG/village-enemies-world.md` | Canon updated in the same change (CLAUDE.md §15) — the wave-loop bullet that said the `SuspendedFor` freeze "is deliberate and self-clears" now records that it never engaged for an arena at all until this ticket. Points at source + the regression; carries no live values. |

`_floorReason` has exactly one writer (`ApplySceneBaseline`), so the new test cannot drift out of sync
with the floor.

**Blast radius, enumerated at source, not assumed.** `SuspendedFor` has four production callers:
`WaveManager.cs` (:893, :1014, :1285, :2600, :2636, :2856, :2872, :2874), `StructureBurn.cs:210`,
`RegionMobSpawner.cs:165`, `TownActivityProbe.cs` (:136, :171). All four are town-side; **nothing
arena-side consults this gate** — arena bodies are `Enemy` instances built by `EnemyFactory` and are
not callers — so the change cannot freeze anything the player is fighting. `TownActivityProbe.Note`
counts a leak only when `!inActive && !gated`, and its `FlowTrace.Fail` requires `!IsSuspended`, so no
new probe failures are manufactured.

## Regression

`Assets/Editor/Regression/ArenaInSceneSuspensionRegression.cs`

| Case | Asserts |
|---|---|
| `arena-hold-covers-town` | **RED on HEAD.** The shipped literal `Suspend("arena battle staged at ArenaCentre (hero 7km away, player active)")` with no floor ⇒ a town object in the ACTIVE scene reports `SuspendedFor=true`. |
| `floor-carve-out` | Over-fix guard: with a floor (`Dungeon_HealersCottage`), an active-scene object is still exempt. |
| `grace-covers-town` | **RED on HEAD.** After `Resume("arena battle resolved")` the return grace holds active-scene town objects. |
| `ddol-still-held` | Null/unowned owner still held under a floorless hold. Needs no scene. |
| `callers-intact` | `BattleArena` still drives the `Suspend`/`Resume` pair; `WaveManager` still consults the gate per tick. |
| `floor-scoped-at-source` | **RED on HEAD.** The exemption is floor-scoped and the `FLOORLESS` trace exists at source. |

**Registration line for the lead** (`DataRegression.cs` deliberately untouched):

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "arena-inscene-suspend suite", () => { if (!DeNelle.Editor.Regression.ArenaInSceneSuspensionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[arena-inscene-suspend] " + r); });
```

## Gate checks run here

- `python tools/gate_brace.py Assets/_Modules/Core/TownSuspension.cs Assets/Editor/Regression/ArenaInSceneSuspensionRegression.cs` → `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0.
- NUL scan (byte count of `\x00`) on both `.cs` → **0** in each. Raw braces balanced 35/35 and 43/43.

## What was NOT done, and what is UNPROVEN — stated rather than ticked

- **Not compiled, not gated, not committed, not pushed.** No Unity ran in this lane, so
  `COMPILE_GATE_OK` / `REGRESSION_OK` have **not** been observed and I am not claiming them. The new
  `.cs` has **no `.meta`** yet — Unity generates it on the lead's first import; the lead must stage it.
- **The new suite has never executed.** Its RED-on-HEAD claim is by construction (three cases assert
  post-fix behaviour or post-fix source shape), not by an observed run. First run belongs to the gate.
- **⚠ HONEST LIMIT in the suite itself, written into its header:** cases 1–3 need a probe GameObject in
  a **named** scene. `SuspendedFor` treats an unnamed scene as DontDestroyOnLoad
  (`TownSuspension.cs:210`) *before* the handle test, so in a batch run with only an Untitled scene
  open they log a loud stand-down and skip; the suite's summary line says which mode ran. I could not
  determine, without running Unity, whether the fleet's batch context has a named scene open —
  `floor-scoped-at-source` is the fallback that keeps the suite RED on a reverted tree either way.
  **If the lead's first run reports the stand-down, that is a finding to close, not noise.**
- **Residual, named not fixed:** `BattleArena` calls `Resume` at **Resolve** (`:2723`), not on the
  hero's arrival home. Here the warp followed ~1.5 s later (**L714116** → **L715040** `WarpHero
  REQUEST` → **L715054** `FADE IN: home arrival`), well inside the 3.5 s grace. But the victory summary
  is **deferred until Continue** (**L714124**), so a player sitting on that summary longer than the
  grace with a low countdown could still have a wave start while 7 km away. **I have not proven this
  occurs** — nothing in this session shows it — and moving `Resume` risks leaking a permanent town
  freeze, which is strictly worse. Left for a ruling.
- **UNEXERCISED PATH, named not claimed:** entering the arena **mid-siege** (the wave already `Active`
  with live village enemies). With this fix the freeze now genuinely holds `TickActiveWave` for that
  case too, which is the stated `SuspendAndResume` policy — and on that path the post-win quiescence
  gate would still FAIL, correctly, because a real siege is still running. Nothing in this capture
  exercised it, so I am not claiming it behaves well; I am recording that it is untested.
- **Not touched, per the brief:** HUD dock / ability-face code (WO-1695), `EchoWorldPresence` /
  `PetDeployer` (WO-1696), any `.unity`, `Assets/Editor/Regression/DataRegression.cs`,
  `CLI_LANES_WO_NUMBERS.md`.
- The F8 captures seq 5009 / 5010 were **not acked** — acking is the lead's call after triage closes.
