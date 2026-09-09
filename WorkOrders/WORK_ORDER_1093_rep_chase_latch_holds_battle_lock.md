# WORK ORDER 1093 — `_stung` is a one-way latch, so a rep-chase re-stamps a pursuit one frame after ClearPursuits and the battle-lock never releases

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1089 → 1095 in the same edit)
**Silo:** Combat / AI
**Severity:** P1 — felt softlock: the player wins an arena fight and town stays in combat state
**Source:** F8 captures seq=4767 and seq=4768

> ⚠ **A UI-SEAT AGENT ALREADY WROTE A CANDIDATE FIX** into
> `Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs`. It reported itself **complete**
> (brace balance 264/264, 0 NUL bytes) — unlike the other four tickets in this batch, which were
> stopped mid-edit. **It is still an untrusted UI-seat edit and CLI decides whether to keep, review
> or discard it.** The spec below stands on its own so the ticket can be implemented from scratch.
> See `WO_1089_1094_WORKING_TREE_NOTE.md`.

---

## Symptom

seq=4767: `[Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (arena win) - 1 invariant(s) NOT restored after
the battle: battle-lock: still HELD after the battle ended`

seq=4768, after the self-heal: `battle-lock STILL HELD after the self-heal (arena win):
[PursuitBattleProbe.Probe] ... PURSUIT PULSES before the heal: key=-307854
owner='OverworldEncounterSpawner/rep-chase' age=0.00s; after (re-stamped within one frame of a full
ClearPursuits): key=-307854 owner='OverworldEncounterSpawner/rep-chase' age=0.00s.`

## Second occurrence, 2026-09-09 11:20 (seq=4803–4804) — it is the CLASS, not one rogue rep

Reproduced on the device in a different session, with a **different pursuit key**:

| Occurrence | `t=` | Pursuit key | Owner tag |
|---|---|---|---|
| seq=4767/4768 | 1175.96 | `-307854` | `OverworldEncounterSpawner/rep-chase` |
| seq=4803/4804 | 370.49 | `-195128` | `OverworldEncounterSpawner/rep-chase` |

Different rep body, different point in the session, identical failure and identical re-stamp
"within one frame of a full ClearPursuits". **So this is the latch's class behaviour, not one
mis-initialised instance** — which is what the fix spec assumes, and now has two samples behind it.

seq=4803 also enumerates the holder set, which is worth having on the ticket: `HOLDER(S):
PursuitBattleProbe.Probe (of 3 registered: PursuitBattleProbe.Probe, BattleArena.<Awake>b__84_0,
WaveManager.<OnEnable>b__116_0)`. The other two released correctly; only the pursuit probe held.

### Add a distance to this trace — it is the one field that would close the open question

Neither capture carries the chase distance, which is exactly why "far-latch vs near-but-blocked"
remains unproven below. When implementing, **include the current hero distance in the
BATTLE_QUIESCENCE_FAIL pursuit-pulse line** (and in the self-heal line). It is nearly free, and it
turns the next occurrence into a self-diagnosing capture: a ~7 km distance proves the far-latch
variant, a small one proves a blocked chase and points at a reachability fix instead of a leash.

## Root cause — the chase has no distance test and no way to stop

- `_stung` is declared at `OverworldEncounterSpawner.cs:988` (pre-edit numbering), written `true` at
  `:1216`, and **cleared nowhere.** A grep for `_stung` returns only the declaration, `IsPursuing`
  (`:993`), `QuietIfNotPursuing` (`:1052`), the aggro test (`:1214`/`:1216`) and the chase branch
  (`:1232`).
- The chase branch `:1232 if (_stung)` gates both `SetBrainTargetPosition` and the pursuit stamp at
  `:1242-1243` **on that latch alone — no distance, no liveness.** A rep that ever saw the hero
  stamps `ReportPursuit(..., "OverworldEncounterSpawner/rep-chase")` every frame, at any range, for
  the rest of the session.

## The ordering that turns the latch into the captured failure

`BattleArena.cs:2708-2713` — `RepEngageWatcher.ResumeAll()` runs, and its own comment supplies the
distance: *"while the masked return fades, the hero is still at the far arena, so a resumed rep reads
a ~7km distance and cannot aggro until the warp lands."* True of **aggro**. Never true of the
**chase**, which has no distance test to be far away from.

Then `BattleArena.cs:2729` `QuietNonPursuersOnBattleEnd()` **preserves** the latched rep, because
`QuietIfNotPursuing` (`:1052`) returns false on `_stung`. Then `BattleArena.cs:2753-2754`
`BattleSessionEnd.Release("arena win")` → `PostureSignals.ClearPursuits()`
(`BattleSessionEnd.cs:200`). The preserved rep's next `Update` re-stamps.
`PursuitBattleProbe` returns `PostureSignals.PursuitActive` verbatim, so the lock never releases.

## Owner ruling (2026-09-09) — leash at 26 m

The owner was asked directly, because the fix touches her standing design ruling (the file header at
`:10-11`: a rep chases at +5% hero speed *"so a too-tough mob can't be outrun — the danger-gradient
stake"*). She chose **`DeaggroRange = 26f`**, with hysteresis above `AggroRange = 14f`. A dash or
blink that opens a big gap may now shake a chaser off; that is accepted.

## Fix spec

1. **`ChaseBrokeOff(heroAlive, d, out why)`** — the distance test the chase never had:
   `d > DeaggroRange (26f)`, plus **hero DOWN**, matching the predicate and reasoning of the WO-1603
   guard on the sibling stamp site (`Enemy.cs:1732-1752`). A null `HeroHealth` counts as ALIVE
   (headless/test scenes), matching BattleArena's own arbitration.
2. **`Deaggro(why)`** — clear `_stung`; revoke **only this body's own key**
   (`PostureSignals.RevokePursuit` — never `ClearPursuits`, never `BattleLock`, per WO-1337); and
   reverse what the sting turned on (combat presentation off, threat cue destroyed, repath reset so
   the rep visibly resettles).
3. **`heroAlive` must also gate the aggro test.** Without it, a downed hero inside `AggroRange`
   de-aggros and re-stings every frame — danger-sting spam, a Step per frame, and the pulse stamped
   over her corpse anyway.
4. Place the check **after** the `BattleArena.AnyBattleInProgress` return, so the arena's own warp can
   never un-sting a rep mid-fight.
5. **FlowTrace:** a `Step` in `Deaggro` naming the rep, the reason, the revoked key and the owner tag;
   plus a throttled warn for the one shape a leash cannot judge — a chase *inside* the leash that
   closes no ground (navmesh island / unreachable hero). Measure **time since last progress**, not
   time since sting (which would include the frozen battle window), and re-baseline on a large jump so
   a post-warp chase that *is* closing is not reported as blocked.

## Deliberately NOT doing

- **No wall-clock give-up timer.** That would overturn the owner's "+5% speed, can't be outrun"
  ruling to fix a signalling bug.
- **No distance test inside `QuietIfNotPursuing`.** That sweep runs at `BattleArena.cs:2729` while the
  hero is still ~7 km away; a distance test there would quiet every rep on the map. The owner's
  preserve rule stays untouched — what changes is that `IsPursuing` becomes honest.
- **Do not widen `PursuitBattleProbe`.** Fix the owner, not the probe.

## Missing coverage — part of this ticket

Nothing pins this. `EveryPursuitProducerNamesItselfAtSource`
(`Assets/Editor/Regression/BattleQuiescenceRegression.cs:2082`) pins the sibling WO-1603 guard in
`Enemy.cs` by source-string. Add, in the same file and the same style:

1. a source pin that `OverworldEncounterSpawner.cs` still gates the chase stamp on a `ChaseBrokeOff(`
   call, citing seq4768 in the failure message; and
2. **a behavioural case** — a source lint cannot prove the latch is broken. Stamp a pursuit under the
   `OverworldEncounterSpawner/rep-chase` tag, run a full `ClearPursuits()`, and assert a chaser that
   is far (past the leash) or whose hero is down does **not** re-stamp: `PursuitActive` stays false
   and the lock releases. If the spawner cannot be driven in an editor test, assert at the
   `PostureSignals` / `PursuitBattleProbe` seam and record which form was used and why.

The WO-1603 wiring lint (`BattleQuiescenceRegression.cs:2037`) must stay green — both
`PostureSignals.ReportPursuit` and the literal tag `OverworldEncounterSpawner/rep-chase` must remain
in the file.

## Acceptance criteria — the runtime confirm, in order, on the next arena win

- [ ] `[Flow:HudKit] pursuit cleared (posture -> peaceful); dropped: ...owner='OverworldEncounterSpawner/rep-chase'...`
- [ ] within 1–2 frames, `[Flow:Encounter] rep '<name>' de-aggro (hero lost the leash: d=~7000m > deaggro=26.0m ...)`
- [ ] `[Flow:HudKit] pursuit revoked (key=..., owner=OverworldEncounterSpawner/rep-chase ...)`
- [ ] **no** `BATTLE_QUIESCENCE_FAIL`
- [ ] both regression cases present and passing; brace balance; gate markers on a fresh log
- [ ] Owner felt-verifies an arena win returning to town and closes

If instead the throttled chase-stall warn appears next to a fail, the holder is the blocked-chase
variant and the fix needed is a reachability test, not a leash.

## Unproven

Whether rep `-307854` was the far-latch case (hero at the arena during the deferred return) or a
near-but-blocked chase. The capture carries no distance, and `t=1175.96` cannot be placed inside or
after the Continue-deferred window. **The far-latch mechanism is proven; which variant fired is not.**
