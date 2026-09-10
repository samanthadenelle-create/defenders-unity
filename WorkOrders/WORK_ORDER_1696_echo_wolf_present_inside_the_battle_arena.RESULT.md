# WO-1696 RESULT — the Echo was BORN on the arena stage, not dragged into it

**Date:** 2026-09-10 · **Lane:** ECHO-ARENA SME · **Status:** IMPLEMENTED (not gated, not committed —
no Unity run was permitted in-lane).

---

## 1. Answer to the two questions asked

**Q: on an arena warp does `EchoWorldPresence` ever despawn the Echo?**
**No — and there is no despawn to skip.** The owner has exactly three transitions and only one
despawn caller outside `PetDeployer` itself:

- `Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs:333` — `NotifyEscortComplete` →
  `PetDeployer.DespawnAllEchoBodies(reason)`.
- `Assets/_Modules/Pets/PetDeployer.cs:445 / :451 / :495 / :502` — the verb itself.
  (`grep -rn "DespawnAllEchoBodies\|DespawnEcho(" --include=*.cs Assets`, run 2026-09-10.)

**The leash-drag mechanism in the brief is DISPROVEN, not merely unconfirmed.** `BattleArena.cs`
contains no pet code (`grep -n "Pet|pet|Echo"` → one unrelated comment at `:608`), `PetHeroLeash.cs`
has no teleport/warp/recall path, and the Echo's own trace shows it *appearing* at the arena rather
than arriving there:

```
714168:[Flow:Echo] battle RESOLVED (BattleLock true->false) -- evaluating the one reappearance.
714180:[Flow:Echo] echo REAPPEAR: 'Echo' returned at (4997.29, 0.08, 5002.89) after the battle (first battle resolved). bodies=1.
715040:[Flow:BattleArena] WarpHero REQUEST scene='Main_Castle_Overworld' from=(4994.67, 0.08, 5003.83) to=(1.37, 0.07, 65.80) intoArena=False battleInProgress=False.
```
(`Builds/device-frames/2026-09-10_1520_logcat.txt`)

The first battle of that session was an arena **win** (warp-in `:710554`, `intoArena=True`).
`BattleArena` drops `BattleInProgress` at Resolve (`Assets/_Modules/Village/Arena/BattleArena.cs:2433`)
while the win path holds the hero on stage for the kill stream + celebration + fade;
`EchoPresenceWatcher` polls that edge every 0.5 s, so the reappearance fired with the hero still at
`(5000, 0.08, 4991)` and `ResolveReturnPosition` seated the companion beside him. It then stayed at
5000 for the rest of the session, and every later arena entry warped the hero back to it
(`:719600`, `:724767`, `:724917 inArena=True`).

The loss path inverts the order (`Resolve: LOSS :731794` → warp home `:731866` → `battle RESOLVED
:731911`), which is why the same session also shows healthy town returns — **an ordering-based fix
would be wrong; the gate must be positional.**

**Q: is the Echo a combatant in the arena?**
**No.** `[Flow:PetCombat] pet combat gated OFF (ff.petcombat) — pet 'pet-ice-wolf' will not
hunt/attack (harvest/companion only)` at logcat `:23909` and `:66637`; the flag is
`Assets/_Modules/Core/FeatureFlags.cs:1141` (`defaultOn: false`) and `Pet.Update` returns at that gate
(`Assets/_Modules/Pets/Pet.cs:439-451`). It is not targetable either: `public sealed class Pet :
MonoBehaviour` (`Assets/_Modules/Pets/Pet.cs:53`) implements no `IDamageable`, and the arena reticle
admits only registry damageables (`:724852 [hostile-admit] ... impl=DeNelle.Village.EnemyDamageable
via TargetManager registry`). No damage/aggro/target/death line anywhere in 732 325 lines names
`Echo`, `Pet_ice-wolf` or `pet-ice-wolf`.

---

## 2. UNPROVEN — named, not glossed

1. **"An enemy can never damage the Echo."** Not proven. `Pet` carries `_hp` and a public
   `TakeDamage`; no Village code was found calling it on a pet, but absence in one capture is not
   impossibility. Closing it needs a `FlowTrace` on `Pet.TakeDamage` (none exists today) and one
   arena battle.
2. **The fix has NOT been run.** No Unity was fired in-lane (per the brief): `COMPILE_GATE_OK`,
   `REGRESSION_OK` and `ECHO_WORLD_PRESENCE_OK/_FAIL` are all **unmeasured**. The claim that the new
   regression case is RED on HEAD is reasoned from the HEAD code path (no gate existed, so
   `TryReappearAfterBattle` returns true and seats a body at ~5000) — it has not been observed. The
   reasoning's one load-bearing assumption is checkable at source: case (a) already summons at
   `Vector3.zero` in a headless scene with no baked NavMesh and the suite passes on HEAD, so
   `PetDeployer.SummonAt` does not require a NavMesh hit — which is why the ~5000 seat likewise lands
   a body on HEAD and (b2) goes red rather than skipping. (`ResolveReturnPosition`'s
   `NavMesh.SamplePosition` miss is a `FlowTrace.Warn` + unsnapped seat, not a refusal.)
3. **A CS1628 was caught in review, not by a gate.** The first draft captured the `out Vector3 heroPos`
   parameter inside the `Guard.Try` lambda — illegal in C# and invisible to `gate_brace.py`, so it
   would have surfaced only as a withheld `COMPILE_GATE_OK` on the lead's run. Fixed by copying to a
   plain local before the lambda (`IsHeroOnArenaStage`, with the reason recorded in-code). Worth
   stating plainly: a brace/NUL pass is **not** a compile proof, and this lane could not produce one.
4. **`GameObject.FindWithTag("Player")` is unordered** if a scene holds more than one Player-tagged
   object during the headless suite. The new gate inherits that from the existing
   `ResolveReturnPosition`; it is not a new risk, but it is not eliminated either.

---

## 2b. Round 2 — the suite ran for real and the ORACLE was wrong, not the gate

Chain 49 (`Builds/wave10h-reg1`, 2026-09-10 15:06) ran the suite on the tree that already carries the
fix and reported **5 failures** — all four `[arena-stage]` assertions plus `[lifecycle] the Echo did
NOT reappear`. The run's own trace names the cause in two lines:

```
[Flow:Echo] escort staging moved from overlapping anchor (0.00, 0.00, 0.00) to navmesh-safe (0.00, 0.04, 3.25); hero=(0.00, 0.93, 0.00).
[Flow:Echo] echo REAPPEAR: 'Echo' returned at (1.40, 0.03, -2.40) after the battle (oracle: battle resolved while the hero was still on the arena stage). bodies=1.
```

The oracle's stand-in `heroGo` is created at the origin with **y = 0**. The hero the production code
resolved has **y = 0.93** — a different, scene-loaded `Player`-tagged object. `EchoWorldPresence`
resolves its hero with `GameObject.FindWithTag("Player")`, so **the case moved a transform that
neither the gate nor `ResolveReturnPosition` ever reads**: setting `heroGo` to `(5000, 0.08, 4991)`
changed nothing, the gate correctly saw a hero in town, the return seated at `(1.40, 0.03, -2.40)`
around the origin hero, and the guard was consumed — which is why (c) then reported no reappearance.

**The production gate was never exercised.** It compiled and ran (so the CS1628 fix is confirmed
good), but it was asked about the wrong hero. Nothing about the fix changed this round.

**Oracle fix (regression file only):** stage **every** `Player`-tagged object via
`GameObject.FindGameObjectsWithTag("Player")` — not the one `FindWithTag` happens to return, whose
choice among several is unspecified and would be the same unproven assumption in a new costume — then
restore every one of them in a `finally` before (c) runs. The case is now **self-proving**: it asks
`BattleArena.IsArenaPosition(staged)` first and takes a **named skip** if the arena centre/radius ever
moves, rather than passing on a stale coordinate; and the failure text now reports the **measured**
seat instead of the hardcoded one it asserted last round (that hardcoded string was itself the §11B
pattern — a claim written before it was measured).

**Still unproven, and it is the same one:** the gate's live refusal has not been observed. The next
run is the first that can produce it — RED on HEAD, GREEN on the fix, both by the same mechanism.

## 3. What changed

**`Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs`**
- `EchoWorldPresence.TryReappearAfterBattle`: new positional off-stage gate via
  `IsHeroOnArenaStage(out pos)` → `BattleArena.IsArenaPosition`
  (`Assets/_Modules/Village/Arena/BattleArena.cs:102`, a pure public static predicate — read, never
  re-derived; no battle-lock code touched, WO-1694 respected). Defers with `FlowTrace.Warn` and
  **does not consume** the once-per-session guard.
- `EchoPresenceWatcher`: `_reappearPending` retry so the owed return lands after the home warp, with
  its own `[Flow:Echo]` lines (`reappearance still OWED...`, `deferred reappearance LANDED`).
- File header: the three-transition block now records the arena warp and why the beat is deferred
  rather than duplicated (§15, canon in the same breath).

**`Assets/Editor/Regression/EchoWorldPresenceRegression.cs`** — an EXISTING registered suite
(`Assets/Editor/Regression/DataRegression.cs:1406`), so **there is nothing for the lead to register**
and `DataRegression.cs` was not touched.
- `[lifecycle]` (b2): drives a resolve with the hero at `(5000, 0.08, 4991)` and asserts the return is
  refused, no body is seated, the guard is unconsumed and the return is still owed; then restores the
  hero to the origin so the original once-only case runs unchanged.
- New `[arena-stage]` source-lint (rule 7): the owner must read `IsArenaPosition`, must carry no
  literal arena coordinate, and the watcher must keep `_reappearPending`.

**Deliberately NOT done:** no arena-entry despawn/stow. Both shapes invent an unruled beat (stow-only
= companion gone for the session; stow+restore = a per-arena-battle return, against the once rule).
Recommendation: none is needed once the return seats in town. Owner question recorded in the WO §4.

---

## 4. In-lane gates

```
python tools/gate_brace.py Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs Assets/Editor/Regression/EchoWorldPresenceRegression.cs
GATE_BRACE_SUMMARY bad=0 of 2        (exit 0)
```
Raw brace counts: `EchoAutoDeployTrigger.cs` 90/90, `EchoWorldPresenceRegression.cs` 65/65.
Byte-level NUL scan: **NUL-CLEAN** on both (37 790 B / 41 243 B).
Line endings: both files are **CRLF throughout** (`file` reports "with CRLF line terminators", not a
mixed CRLF/LF file) — no §0 garble signature.
No Unity run, no commit, no push.

---

## 5. Paths

- WO: `WorkOrders/WORK_ORDER_1696_echo_wolf_present_inside_the_battle_arena.md`
- RESULT: `WorkOrders/WORK_ORDER_1696_echo_wolf_present_inside_the_battle_arena.RESULT.md`
- Code: `Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs`
- Regression: `Assets/Editor/Regression/EchoWorldPresenceRegression.cs`
- Evidence: `Builds/device-frames/2026-09-10_1520_logcat.txt`,
  `Builds/device-frames/2026-09-10_1520_owner_icons.png`
