# WORK ORDER 1696 — The Echo wolf is standing inside the battle arena

**Status:** IMPLEMENTED 2026-09-10 — the post-battle reappearance is now gated on the hero being OFF the staged arena and is retried after the return warp, so the Echo can no longer be born 7 km from town.

**Silo:** Echo / Pets lifecycle (`EchoWorldPresence`) — no arena, wave, HUD or scene code touched.
**Owner-facing symptom:** "wolf is in the battle areena" (owner, 2026-09-10, Seeker build 363866).
**Lane:** ECHO-ARENA SME.

---

## 1. The evidence

**Frame:** `Builds/device-frames/2026-09-10_1520_owner_icons.png` — the owner's Seeker, inside the
battle arena. A white wolf Echo stands on the arena floor beside the hero and an Orcish Raider.

**Log:** `Builds/device-frames/2026-09-10_1520_logcat.txt` (732 325 lines).

### 1a. The Echo was BORN on the arena stage — it was not dragged there

The last play session in the capture opens its first battle *inside the arena*:

```
710554:[Flow:BattleArena] WarpHero REQUEST scene='Main_Castle_Overworld' from=(1.37, 0.07, 65.80) to=(5000.00, 0.00, 4991.00) intoArena=True battleInProgress=True.
...
714168:[Flow:Echo] battle RESOLVED (BattleLock true->false) -- evaluating the one reappearance.
714180:[Flow:Echo] echo REAPPEAR: 'Echo' returned at (4997.29, 0.08, 5002.89) after the battle (first battle resolved). bodies=1. This fires ONCE per session and never again.
715040:[Flow:BattleArena] WarpHero REQUEST scene='Main_Castle_Overworld' from=(4994.67, 0.08, 5003.83) to=(1.37, 0.07, 65.80) intoArena=False battleInProgress=False.
```

The reappearance fired **860 log lines and several seconds before the hero left the stage**, and
`(4997.29, 0.08, 5002.89)` is one metre from `BattleArena`'s `ArenaCentre (5000,0,5000)`. The
companion was seated on the stage and stayed there. Every later arena entry warps the hero back to
`(5000, 0, 4991)` — i.e. **back to the wolf**:

```
719600:[Flow:BattleArena] WarpHero REQUEST ... to=(5000.00, 0.00, 4991.00) intoArena=True ...
724767:[Flow:BattleArena] WarpHero REQUEST ... to=(5000.00, 0.00, 4991.00) intoArena=True ...
724917:[Flow:BattleArena] MARCH HERO-POS (post-warp): pos=(5000.00, 0.08, 4991.00) inArena=True ...
```

### 1b. It is a RACE, and only WINS lose it

| First battle of the session | Resolve line | Reappear seat | Verdict |
|---|---|---|---|
| session at :190379 (wave) | :194531 | `:194544 (1.40, 0.08, -7.11)` | town — correct |
| session at :380085 (wave) | :382896 | `:382908 (-0.73, 0.08, -35.04)` | town — correct |
| session at :709745 (**arena, WIN**) | :714168 | `:714180 (4997.29, 0.08, 5002.89)` | **ARENA — the defect** |

And the loss path proves the ordering is not fixed — here the warp home comes FIRST:

```
731794:[Flow:BattleArena] Resolve: LOSS in 14.3s -> 0 star(s), reward x1.00.
731866:[Flow:BattleArena] WarpHero REQUEST ... from=(5000.00, 0.08, 4991.00) to=(0.00, 0.08, -4.71) intoArena=False ...
731911:[Flow:Echo] battle RESOLVED (BattleLock true->false) -- evaluating the one reappearance.
```

`BattleArena` clears `BattleInProgress` when it resolves (`BattleArena.cs:2433`, and the code's own
note: *"Release the LOGIC gates last: BattleInProgress=false drops the BattleLock probe"*), while the
WIN path holds the hero on stage for the kill stream, the celebration and the fade. `EchoPresenceWatcher`
polls `BattleLock` every **0.5 s unscaled** (`EchoAutoDeployTrigger.cs`, `PollSeconds = 0.5f`), so on a
win it sees the edge while the hero is still at 5000. **Any fix that assumes an order is wrong.**

### 1c. Brief mechanism vs. captured data — the divergence, stated

The brief's hypothesis was: *"the gate-walk path despawns at the gate; a warp may skip it and the leash
then drags the Echo into the arena."* **The data does not support that**, and the WO is implemented
against the data (CLAUDE.md §11B / §12):

- There is **no arena-entry despawn to skip**. `EchoWorldPresence` has exactly three transitions
  (escort summon / vanish / one reappearance). The only despawn callers in the whole tree are
  `EchoAutoDeployTrigger.cs:333` (`NotifyEscortComplete`) and `PetDeployer` itself
  (`PetDeployer.cs:445/451/495/502`) — `grep -rn "DespawnAllEchoBodies\|DespawnEcho(" --include=*.cs Assets`.
- **Nothing in the arena knows pets exist**: `grep -n "Pet|pet|Echo" Assets/_Modules/Village/Arena/BattleArena.cs`
  returns one unrelated comment at `:608`.
- **The leash cannot teleport.** `PetHeroLeash.cs` has no warp/teleport/recall path
  (`grep -n "Teleport|Warp|recall|Snap"` returns two prose comments), and a `NavMeshAgent` does not
  cross 7 km of unbaked space. The Echo never travelled to the arena; it appeared there.

Once the reappearance is seated in town, the "stranded on the stage" consequence disappears with it —
which is why **no arena-entry despawn is added** (see §4, the open owner question).

### 1d. Is the Echo a combatant in the arena?

**No — it never attacks, and nothing in the tree targets it.** Proven:

```
23909:[Flow:PetCombat] pet combat gated OFF (ff.petcombat) — pet 'pet-ice-wolf' will not hunt/attack (harvest/companion only)
66637:[Flow:PetCombat] pet combat gated OFF (ff.petcombat) — pet 'pet-ice-wolf' will not hunt/attack (harvest/companion only)
```

`FeatureFlags.PetCombat` is `Get("petcombat", defaultOn: false)` (`Assets/_Modules/Core/FeatureFlags.cs:1141`),
and `Pet.Update` returns at that gate (`Assets/_Modules/Pets/Pet.cs:439-451`).

Not targetable by the hero or the reticle: `public sealed class Pet : MonoBehaviour`
(`Assets/_Modules/Pets/Pet.cs:53`) — it implements **no** `IDamageable`, and the reticle admits only
registry-backed damageables:

```
724852:[Flow:Reticle] [hostile-admit] ENEMY 'ArenaEnemy_orc-warlord_1' impl=DeNelle.Village.EnemyDamageable via TargetManager registry
730864:[Flow:Combat] hero MELEE hit 'ArenaEnemy_troll_0' (EnemyDamageable) under root '[BattleArena_Stage]' ...
```

Across all 732 325 lines there is **not one** damage, aggro, target-acquire or death line naming
`Echo`, `Pet_ice-wolf` or `pet-ice-wolf`.

**Named as UNPROVEN:** that an enemy can *never* damage it. `Pet` does carry `_hp` and a public
`TakeDamage` (`Pet.cs`), and absence in one capture is not a proof of impossibility. The cheap way to
close it: run an arena battle with `ff.petcombat` still OFF and grep for a `TakeDamage` trace on the
pet — no such trace exists today, so **that instrumentation would have to be added first**.

---

## 2. Git history of the seam

```
EchoAutoDeployTrigger.cs (EchoWorldPresence + EchoPresenceWatcher)
  6a5c7a36d chore: checkpoint complete workspace and rebuild board
  1ef5f6ad4 feat: WO-1374/1375/1378/1379/1380 - raid economy loop, victory settle, FTUE, Heartfire, Echo Guides
  4329e3d1c fix: keep Aldwin clear of hero spawn
  7fcb49a1b feat(echo): WO-1108 Lane B - the Echo walks you to the gate, then it is GONE   <-- introduced the watcher + the one reappearance
  8518ce0eb feat(backlog wave4+5a): ... companion+Echo (360) ...

PetDeployer.cs
  d19fe9d01 fix(art): Alduin's coat ...
  fae62b2ec fix(harvest): remove despawned Echo assignments
  b63bc7190 refactor(pets): WO-993 - retire the physical pet stack

Arena entry seam — BattleArena.cs
  26142d2f2 instrument(atmos): WO-1602 ...
  99b574392 fix(combat): retreat no longer leaves the battle locked (WO-1337)
  242fe4fb4 fix(arena): confirm stage warp before spawning battle   <-- the warp-in seam
```

`7fcb49a1b` shipped the reappearance with **no notion of where the hero is standing** — at that date
the arena warp and the Echo lifecycle had never met. Nothing has connected them since.

---

## 3. The fix (one owner, one file, no arena code touched)

`Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs`

1. **`EchoWorldPresence.TryReappearAfterBattle` — a positional off-stage gate.** After the existing
   `BattleLock` gate, the new `IsHeroOnArenaStage(out pos)` reads the ONE authority on arena geometry,
   `BattleArena.IsArenaPosition` (`Assets/_Modules/Village/Arena/BattleArena.cs:102` — a pure public
   static predicate, same assembly, **not** battle-lock code, so WO-1694 is untouched). When the hero
   is on the stage the reappearance is **deferred with a `FlowTrace.Warn`** and the once-per-session
   guard is **NOT consumed** — the same contract the existing null-summon branch already documents.
   The 7 km offset and its radius are never copied here; the predicate is read.
2. **`EchoPresenceWatcher` — the deferred landing.** A `_reappearPending` bool is set when a resolve
   leaves the return owed, and every 0.5 s poll retries `TryReappearAfterBattle` while out of battle,
   clearing on success (with its own trace). Positional, not ordering-based, so it covers the win
   path and the loss path identically.
3. **The once-per-session rule is unchanged.** This is ONE transition, deferred — not a second
   appearance owner, not a second spawner, not a new beat.

New FlowTrace at the seam (`[Flow:Echo]`, permanent per §12): `echo REAPPEAR DEFERRED (...)`,
`reappearance still OWED after this resolve`, `deferred reappearance LANDED`.

---

## 4. Deliberately NOT done — one owner question

**No arena-entry despawn / stow was added.** The brief asked for one, but the capture shows the Echo
never enters the arena by travelling — it was seated there. With the reappearance seated in town the
hero warps to the arena alone and the companion waits at home, which needs no new transition.

Adding one would need an owner ruling, because both shapes invent a beat the owner has not ruled on:

- **stow-only** — the companion is despawned on arena entry and never restored: gone for the rest of
  the session after the first arena visit.
- **stow + restore** — a new "the Echo returns after every arena battle" beat, which sits directly
  against the once-per-session rule this WO was told to protect.

**Recommendation:** none needed — the Echo stays in town, the hero warps alone.

---

## 5. Regression — RED on HEAD, in an EXISTING registered suite

`Assets/Editor/Regression/EchoWorldPresenceRegression.cs` (marker `ECHO_WORLD_PRESENCE_OK/_FAIL`,
already registered at `Assets/Editor/Regression/DataRegression.cs:1406` as the *echo-world-presence
suite*). **Nothing for the lead to register — DataRegression.cs is untouched.**

- **`[lifecycle]` (b2), live:** **every** `Player`-tagged object is moved to `(5000, 0.08, 4991)`
  between the vanish and the reappearance; `TryReappearAfterBattle` must return **false**, leave
  `LiveBodyCount == 0`, leave `ReappearedThisSession == false` and keep `AwaitingBattleReappear ==
  true`. All are restored in a `finally` and the original case (c) then runs unchanged, proving the
  owed return still lands. **On HEAD the first call returns true and seats a body at ~5000 → RED**
  (and (c) then also trips).
  ⚠ **Round 1 of this case was wrong and chain 49 caught it.** It moved only the suite's own
  stand-in `heroGo`, which the production code never reads — `EchoWorldPresence` resolves its hero
  with `FindWithTag("Player")` and in a loaded scene got a different object
  (`Builds/wave10h-reg1`: `hero=(0.00, 0.93, 0.00)`, reappear seated at `(1.40, 0.03, -2.40)`), so
  the case failed on the FIXED tree while proving nothing about the gate. It now stages the whole
  tagged set and self-proves the seat through `IsArenaPosition` before asserting, taking a named
  skip if the arena geometry ever moves.
- **`[arena-stage]`, source-lint:** the appearance owner must contain `IsArenaPosition`, must NOT
  contain a literal arena coordinate (no re-derived geometry), and the watcher must keep
  `_reappearPending`.

---

## 6. Files

- `Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs` — the gate + the retry + header canon.
- `Assets/Editor/Regression/EchoWorldPresenceRegression.cs` — case 7 + the live (b2) assertions.

**Not touched:** any `.unity`, `WaveManager`, `BattleArena` (read-only reference to a public
predicate), HUD dock/faces, `DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`.

**Gates run in-lane:** `python tools/gate_brace.py <both files>` → `GATE_BRACE_SUMMARY bad=0 of 2`,
exit 0; raw braces 90/90 and 65/65; NUL scan clean on both (byte-level). **No Unity run, no commit,
no push** — the lead gates and commits.

⚠ **The lead must re-copy the regression file into the shared checkout before re-running.** Chain 49
ran a tree carrying round 1 of the case; the corrected case lives in this worktree.
