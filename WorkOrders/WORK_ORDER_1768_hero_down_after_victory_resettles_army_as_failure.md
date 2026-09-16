# WORK ORDER 1768 — Hero death during the down-beat rips away a won raid's victory screen; the "army settled as a failure" trace is a LIE

**Status:** IMPLEMENTED, NOT YET GATED — all three fixes landed in the working tree 2026-09-16 with PIN 4/5/6 added to `RaidExitParityRegression`; proven RED on the pre-fix tree (HEAD) and GREEN on the post-fix tree by a port of the pins' own logic. Lead holds the Unity gate; no commit from this lane. See `WORK_ORDER_1768_hero_down_after_victory_resettles_army_as_failure.RESULT.md`.
**Lane:** read-only RCA lane, 2026-09-16. No code edited, no commits, no Unity run. Every claim below is either a captured log line (file + absolute line number) or a source read taken this session (file:line). Unverified claims are listed in §8.
**Silo:** raid settle / hero death ordering (`HeroHealth.HandleDeath`, `RaidVictoryController`, `RaidDeployController`)
**Evidence:** `logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (377 MB), owner's Seeker, build **2026.09.16.371701**, raid `IronBastion` 13:24:27 → 13:26:06.

> ⚠ **THE FILENAME ENCODES THE REPORTED SYMPTOM AND THE RCA REFUTES IT.** The army was **NOT** re-settled
> as a failure. Both army-side writers were already latched and no-oped, and the capture says so. What is
> actually broken is (1) the victory screen being torn away 0.76 s after it opened, (2) the 2-star cap
> latching 1.75 s too late, and (3) a FlowTrace line that reports a settle that never happened. The
> filename is kept as assigned; read §1 before acting on the title.

---

## 1. What the capture proves, in order

All lines below are from `logcat_full.txt` at the absolute line numbers given. Timestamps are `09-16`.

### 1a. The hero died for real, 1.93 s BEFORE the spire fell — not 0.28 s after it

| line | time | captured line (abridged) |
|---|---|---|
| 3059439 | 13:26:03.395 | `[Flow:HeroHealth] TakeDamage id=-132142 scene='RaidBase_IronBastion' amount=8.3 hpBefore=3.7/108.0 (base=100) invuln=False` |
| 3059443 | 13:26:03.396 | `[HeroHealth] Hero defeated.` |
| 3059444 | 13:26:03.397 | `[Flow:Death]   lethal hit: cause=damage downSeconds=1.8 \| hero state: hp=0/108 pos=(-12.47, 1.57, -10.44) enemyOwnedScene=True \| ... \| lethalFrom=DefenseTower.FireAtParty` |
| 3059449-3059450 | 13:26:03.399 | `[Flow:HeroDeath] PlayDeathAnim ...` / `death freeze armed` |
| 3059639 | 13:26:04.892 | `[Flow:HeroDeath] death state watch: ... death clip played through and holds its final frame.` |

Three real `TakeDamage` ticks of 8.3 walked HP `19.8 → 11.8 → 3.7 → 0`, and `lethalFrom=DefenseTower.FireAtParty`
names the killer. **This is NOT the WO-1750 health-model artefact.** `WORK_ORDER_1750_...RESULT.md` §1
establishes `HeroHealth.cs:196` — `public bool IsAlive => _hp > 0f;` — i.e. there is no flag that can go
stale independently of HP, and the only `_hp` writer that also runs the death branch is `TakeDamage`
(`HeroHealth.cs:849`). The `lethalFrom=` field in the capture **is WO-1750's own instrumentation**, so the
build under test already carries that change. **WO-1750 is not implicated here.**

The event at **13:26:05.613** is not a second death — it is the **1.75 s down-beat elapsing**
(`HeroHealth.cs:1207` `yield return new WaitForSeconds(Mathf.Max(0.1f, _downSeconds))`, `_downSeconds = 1.75f`
at `HeroHealth.cs:101`). The raid was won *inside that window*.

### 1b. The raid concluded and settled while the hero lay dead

| line | time | captured line (abridged) |
|---|---|---|
| 3059719 | 05.330 | `[Flow:Raid] OBJECTIVE COMPLETE - RaidSpire 'RaidSpire' (config 'iron_bastion') RAZED. The raid is won.` |
| 3059720 | 05.330 | `[Flow:Raid] VICTORY — raid 'IronBastion' won (SPIRE RAZED). Running claim -> next-companion -> return.` |
| 3059731 | 05.337 | `[Flow:Raid] stars settled: 2 (earned=3 **heroDied=False cap=none** honor=2 clamped=min(3,2)) (cleared=True destruction=58 % elapsed=91s/180s underTime=True survival=100 % high=True @70 %).` |
| 3059757 | 05.346 | `[Flow:Raid] raid-end reconcile - deployed 10, survivors 10, wounded 0 (stars 2, recovery 2700s).` |
| 3059758 | 05.346 | `[Flow:Raid] veterancy: 2 star(s) - no ranks granted (3 stars required).` |
| 3059760 | 05.349 | `[Flow:Raid] army settled for the WIN (stars 2) and saved.` |
| 3059762 | 05.352 | `[Flow:Raid] VICTORY COUNT - raids won on this save: 1 ... Persisted.` |
| 3059792 | 05.398 | `[Flow:DeathTrace] SCREEN OPENED: EndState 'Victory!' by ...RaidVictoryController.ShowVictoryScreen` |
| 3059807-3059808 | 05.417 | `[Flow:EndState] 'Victory!' auto-dismiss armed at 30s WITH HOLD-ON-TOUCH (WO-1543)` / `Victory shown: spoils=4 action=return-home` |

### 1c. Then the down-beat elapsed and the death path ran over the top of it

| line | time | captured line (abridged) |
|---|---|---|
| — | 05.594 | `touch_boost: set_top_grp_aware` (the owner's finger lands on the victory screen) |
| 3059904 | 05.613 | `[Flow:EndState] 'Victory!' HELD by interaction - auto-return re-armed to the full 30s (re-arms so far: 1). The player who is reading is the player who is touching.` |
| 3059906 | 05.613 | `[Flow:Raid] HERO DOWN - the raid CONTINUES (WO-1526 ...). elapsed=91s/180s destruction=58 % **finalized=True**. ...` |
| 3059907 | 05.613 | `[Flow:Death] HandleDeath: down-beat elapsed -> **EVAC branch (GoCastle)**. Signal: enemyOwned=False raidInProgress=True **raidSettled=True** raidDeathEndsRaid=False -> chose=SETTLED raid (no session to respawn into).` |
| 3059908 | 05.613 | `[HeroHealth] Hero down in enemy territory — raid ends, retreating to home hub.` |
| 3059909 | 05.614 | `[Flow:Raid] hero death settle: **raid already finalized - loot was paid by the first exit.**` |
| 3059910 | 05.614 | `[Flow:Raid] **raid-end reconcile already ran for this raid - ignoring the duplicate call.**` |
| 3059911 | 05.614 | `[Flow:Raid] hero DOWN in an enemy-owned scene - army settled as a failure (0 stars); the troops still standing break and flee home, the fallen are wounded.` |
| 3059912 / 3059916 | 05.617 / 05.620 | `[Flow:Save] wrote signed save via LocalSaveProvider (len=14506).` |
| 3059913 / 3059914 | 05.617 | `[Flow:DeathTrace] HERO MOVED (pending scene route): SceneRouter.GoCastle() by HeroHealth.HandleDeath` / `GoCastle -> 'Main_Castle_Overworld'` |
| 3060078 | 06.180 | `[Flow:DeathTrace] SCREEN CLOSED: EndState 'Victory!' by EndStateView.OnDestroy (**torn down without firing**)` |

### 1d. WHAT WAS PERSISTED LAST: the WIN settle. The failure settle never happened.

**Proof, in order of strength:**

1. **The two army-side writers each announced their own no-op.** Line 3059909 is
   `RaidDeployController.SettlePartialLoot`'s `_scoring.Finalized` guard
   (`Assets/_Modules/Village/Troops/RaidDeployController.cs:1574-1578`). Line 3059910 is
   `RaidDeployController.ReconcileRaidEnd`'s `_reconciled` latch (`RaidDeployController.cs:1783-1789`),
   which **returns before `Army()` is even fetched**. Neither call touched army state.
2. **Post-settle reads show the army intact.** At **05.702** (line ~3059930) the raid-selection VM re-projected
   with `(garrison 9, **army 10**)` on every row, and at **05.712**
   `[Flow:HudKit] objective -> ... deployable=10, queued=0, required=10, ready=True` — **ten deployable, none
   wounded, none fled**, read *after* the "settled as a failure" line. Had the failure settle run,
   `ReconcileRaidEnd(0)` would have put the fallen on the 2700 s recovery timer and the deployable count would
   have dropped.
3. Corroboration only: the save length is unchanged across the boundary — `len=14506` at 05.349 (the WIN
   settle) and `len=14506` at 05.617/05.620 (after the "failure" settle).

**So the trace at 3059911 is FALSE.** It is emitted at `Assets/_Modules/Village/Hero/HeroHealth.cs:1367-1370`,
**unconditionally**, immediately after two `Guard.Try` calls whose bodies may have no-oped — nothing in that
block reads back whether either did anything:

```
Guard.Try("Raid", "settle partial loot on hero death", () => raidDeploy.SettlePartialLoot("hero death"));
Guard.Try("Raid", "settle army on hero death",         () => raidDeploy.ReconcileRaidEnd(0));
FlowTrace.Step("Raid",
    "hero DOWN in an enemy-owned scene - army settled as a failure (0 stars); " +
    "the troops still standing break and flee home, the fallen are wounded.");
GameStateService.Instance?.Save();
```

---

## 2. THE DEAD STEP (§12) — three of them, ranked felt → scoring → trace

### DEAD STEP 1 (player-felt, ranks first) — the EVAC branch does not yield to a victory screen it can see

`HeroHealth.HandleDeath`'s EVAC branch (`HeroHealth.cs:1321-1376`) is entered when
`enemyOwnedScene || raidDeathExit`, where `raidDeathExit = raidInProgress && (raidSettled || RaidDeathEndsRaid)`
(`:1318-1319`). Its own comment at `:1315-1317` states the assumption:

> *"A SETTLED raid always goes home ...: the loot is paid, the camp is claimed and the clock is stopped, so
> there is nothing left to fight on inside."*

That assumption is **true for the raid and false for the UI**. `RaidVictoryController` owns the return
(`_returning` at `:74`, set in `ReturnHome` at `:994`) and WO-1543 gave the victory screen a 30 s
hold-on-touch window. The death path knows the raid is settled (`raidSettled=True` on the captured Signal
line) and **routes home anyway**, calling `SceneRouter.GoCastle()` at `HeroHealth.cs:1375` while the screen is
up and being touched.

**Captured consequence:** screen opened 05.398 → player touched it 05.594, re-arming the full 30 s → `GoCastle`
05.617 → `SCREEN CLOSED ... torn down without firing` 06.180. **The victory screen was visible for 0.78 s.**
`spoils=4` was never read, the `Return to Castle` CTA never fired ("torn down without firing"), and WO-1543's
hold-on-touch was defeated by a code path that never heard of it.

### DEAD STEP 2 (scoring) — `NotifyHeroDied()` latches 1.75 s AFTER the death

`HeroHealth.cs:1264-1270` carries the comment **"LATCH FIRST, BRANCH SECOND"** and calls
`raidScorer.NotifyHeroDied()` at `:1270` — but `:1270` is **inside `HandleDeath()`, after the
`yield return new WaitForSeconds(_downSeconds)` at `:1207`**. The latch therefore lands 1.75 s after the
lethal hit, and the "on EVERY path, before any branch is chosen" guarantee the comment claims holds only
relative to the *branch*, not to the *death*.

**Captured consequence:** `stars settled: 2 (earned=3 **heroDied=False cap=none** honor=2 clamped=min(3,2))`
at 05.337 — 1.94 s after `Hero defeated`. `RaidScoring.Finalize` ran `ApplyHeroDeathCap(earnedStars, _heroDied)`
(`RaidScoring.cs:1783`; `ApplyHeroDeathCap` at `:709-710`) with `_heroDied == false`, so **`cap=none`** — the
WO-1526 2-star cap (`RaidScoring.HeroDeathStarCap = 2`, `:127`) was **not applied to a raid in which the hero
had fallen.**

It landed on 2 stars **only by luck**: `honor=2` independently clamped `earned=3` to 2. Had honor read 3, this
capture would have paid **3 stars + veterancy ranks** to a player who died — `veterancy: 2 star(s) - no ranks
granted (3 stars required)` at 3059758 shows 3 stars is exactly the veterancy gate. That is the WO-1526 owner
ruling ("cap the result at 2 stars if the hero dies... without turning the hero into a giant red
self-destruct button") **inverted**, and the window is any raid that concludes within 1.75 s of the hero's death.

### DEAD STEP 3 (instrumentation, §12) — a `FlowTrace.Step` that reports work that did not happen

`HeroHealth.cs:1367-1370`, detailed in §1d. The **loot** path is guarded and says so
(`RaidDeployController.cs:1574-1578`); the **reconcile** path is *also* guarded and also says so
(`:1783-1789`) — **the brief's premise that "the ARMY path apparently is not guarded" is wrong.** What is
unguarded is the *narration*. This is the §12 failure class: a trace asserting a step that did not run turns
a clean capture into a false lead, and it cost this RCA lane its first hour. `Guard.Try` swallowing nothing
does not make the following `Step` true.

---

## 3. What the player experienced

- **A won raid whose victory screen was snatched away in under a second**, mid-touch, and was replaced by a
  hub load. No spoils read, no CTA press, no "Return to Castle" of their own volition.
- **The army was NOT harmed** (§1d): 10/10 survivors, 0 wounded, no recovery timer applied, no veterancy
  change, loot paid once. **No save repair is needed for this session.**
- **The raid paid 2 stars** — the correct number here, but reached without the hero-death cap (§2 DEAD STEP 2),
  so the same sequence on a raid with honor=3 over-pays stars and grants veterancy it should not.

---

## 4. Fix spec

### Fix 1 — the EVAC branch yields to a handled victory (player-felt, do this one first)

> ⛔ **DO NOT GATE THIS ON `_handled`.** `_handled` latches at `RaidVictoryController.cs:225`, the **first
> statement of `HandleVictory`** — before `Finalize` (`:300`), before the army reconcile (`:798`) and before
> `ShowVictoryScreen` (`:883`). A throw anywhere in between leaves `_handled == true`, `_returning == false`
> and **no screen on screen**; a death path that yields on `_handled` would leave a dead hero on an
> enemy-owned field with nothing routing home — re-creating the exact WO-1437 stranding the EVAC branch
> exists to prevent. `_returning` alone is equally wrong: it is set only in `ReturnHome` (`:994`), i.e. *after*
> the screen is dismissed — in this capture it was **false** for the whole window that needs protecting.
> **The gate must be "the victory screen is actually up."**

- **`Assets/_Modules/Village/World/Camps/RaidVictoryController.cs`** — add one private latch set immediately
  after the successful `EndStateView.Show(vm);` at `:930` (inside the existing `try`, so a build that threw
  never sets it), and expose it read-only:
  `private bool _victoryScreenUp;` → set true after `:930`; `public bool VictoryOwnsTheReturn => _victoryScreenUp || _returning;`
  Once `Show` has returned, a route home is guaranteed from two independent places: the screen's
  `Return to Castle` primary action (wired to `ReturnHome` in `EndStateVM.FromRaidVictory`, `:902`) and its
  `AutoDismissSeconds = _autoReturnSeconds` anti-soft-lock guard — and `ShowVictoryScreen`'s own `catch`
  (`:937-943`) already calls `ReturnHome()` directly if the build threw. That is the never-strand contract,
  read at source this session.
- **`Assets/_Modules/Village/Hero/HeroHealth.cs`**, in `HandleDeath` **before** the
  `if (enemyOwnedScene || raidDeathExit)` at `:1321`: if a `RaidVictoryController` exists and
  `VictoryOwnsTheReturn` is true, emit a `FlowTrace.Step` naming the yield (the victory screen owns the
  return; the death path stands down) and **stand down** — **no loot settle, no reconcile, no `Save()`, no
  `GoCastle()`**.
- **ARM A STRANDING BELT, DO NOT JUST `yield break`.** One residual hole is real and provable at source:
  `ReturnHome` opens with `if (!CanEnterCapturedTown()) return;` (`:993`), and `CanEnterCapturedTown`
  (`:1007-1012`) **refuses and toasts** when `_captureRequired` and the census commit fails — so a route
  home is not unconditional even after the screen is up. So the death path yields *with a watchdog*: instead
  of a bare `yield break`, wait `WaitForSecondsRealtime(45f)` and, if the active scene is still the raid
  scene and no return is in flight, fall through to the existing EVAC block and log why
  (`FlowTrace.Warn`, naming the capture-retry case). 45 s is chosen as comfortably above the 30 s
  auto-dismiss window observed in this capture (`auto-dismiss armed at 30s`, logcat 3059807) —
  ⚠ the serialized `_autoReturnSeconds` field value was **not** read at source; if the implementer prefers,
  derive the wait from it plus a 10 s grace rather than hardcoding 45.
- **This is WO-1543's existing default, not a new ruling:** "any interaction re-arms the full window; the
  guard still fires for a player who does nothing." Nothing here needs an owner decision.

### Fix 2 — latch the death on the scorer at the death, not after the down-beat

- **`Assets/_Modules/Village/Hero/HeroHealth.cs`** — move the `RaidScoring.Instance?.NotifyHeroDied()` call
  from `:1270` (post-`WaitForSeconds`) to the **synchronous death sequence**, i.e. beside the `_isDead` set in
  `BeginDeathSequence` (`~:876` / `~:923-1039` — WO-1750's post-edit numbering, not re-counted this session — where the `[Flow:Death] lethal hit` dump is emitted), or at
  minimum to the **top of `HandleDeath` before the `yield` at `:1207`**. `NotifyHeroDied` is already idempotent
  (`RaidScoring.cs:624`: `if (_heroDied) return;`), so leaving the existing call site in place is harmless —
  but do **not** add a second *latch*; there must remain exactly one, per the COMPOSE NOTE at `RaidScoring.cs:616`.
- Update the "LATCH FIRST, BRANCH SECOND" comment at `HeroHealth.cs:1264-1270` to say what it now actually
  guarantees (latched at the lethal hit, before any raid conclusion can settle), and cite this capture.

### Fix 3 — the failure-settle trace must report what happened

- **`Assets/_Modules/Village/Troops/RaidDeployController.cs`** — make the reconcile answerable:
  either `public bool Reconciled => _reconciled;` or change `ReconcileRaidEnd` to return `bool` (true = it
  settled, false = latched no-op). Prefer the property — it does not change any existing call site.
- **`Assets/_Modules/Village/Hero/HeroHealth.cs:1367-1370`** — make the `FlowTrace.Step` conditional on the
  reconcile having actually run, and emit the opposite line when it did not, e.g. *"hero DOWN after the raid
  had already settled — the army was NOT re-settled (reconcile was latched); the win's 10 survivors stand."*
  Same for `SettlePartialLoot`. **Keep both traces (§12: never strip instrumentation)** — make them truthful.
- The `GameStateService.Instance?.Save()` at `:1372` should not run when nothing was settled; if kept, its
  trace must not imply a write of new army state.

### What NOT to touch

- `Assets/_Modules/Village/World/SmartMobileCamera.cs`
- `Assets/_Modules/Village/Troops/TroopController.cs`, `RaidAssaultAi.cs`, `TroopBreachOrder.cs`
- `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs`

**None of the three fixes needs any of those files.** Fix 1 touches `RaidVictoryController.cs` (accessor only)
and `HeroHealth.cs`; Fix 2 touches `HeroHealth.cs`; Fix 3 touches `RaidDeployController.cs` and `HeroHealth.cs`.
Also do not change `RaidScoring.HeroDeathStarCap`, the honor ladder, `ComputeStars`, or the veterancy gate —
this WO is about **when** the cap is latched, never **what** it is.

---

## 5. Regression — extend the existing suite, do not add a new one

**Suite: `Assets/Editor/Regression/RaidExitParityRegression.cs`** — marker `RAID_EXIT_PARITY_OK` /
`RAID_EXIT_PARITY_FAIL`, registered in `DataRegression.RunAll`, source-lint (edit mode, no PlayMode). It is
already the authority on hero-death exit ordering (PIN 1 asserts hero death calls `SettlePartialLoot`
**before** the army reconcile, in retreat's order) and it **already reads `HeroHealth.cs`** via
`HeroRel = "_Modules/Village/Hero/HeroHealth.cs"` (`:50`) — so all three pins land with no new file wiring.

Add, with these labels:

- **PIN 4 — THE CAP LATCHES AT THE DEATH, NOT AFTER THE DOWN-BEAT.** In `HeroHealth.cs`, a
  `NotifyHeroDied()` call must appear **before** the `WaitForSeconds(` in the death sequence (compare
  character offsets of the first `NotifyHeroDied` against the `WaitForSeconds` inside `HandleDeath`). Fails
  with the captured line as the reason: `stars settled: 2 (earned=3 heroDied=False cap=none ...)` at
  13:26:05.337, hero dead since 13:26:03.396.
- **PIN 5 — THE FAILURE-SETTLE TRACE IS CONDITIONAL.** The `"army settled as a failure"` string in
  `HeroHealth.cs` must sit inside a branch that tests the reconcile actually ran (the emitted text or the
  enclosing block must reference `Reconciled` / the bool return). A `FlowTrace.Step` that follows the two
  `Guard.Try` calls at the same brace depth, with no such test, FAILS.
- **PIN 6 — THE EVAC BRANCH YIELDS TO A HANDLED VICTORY.** `HeroHealth.cs` must reference
  `VictoryOwnsTheReturn` (or the equivalent accessor) with a `yield break` **textually before** the
  `if (enemyOwnedScene || raidDeathExit)` test, and `RaidVictoryController.cs` must still declare that public
  accessor. Cite `SCREEN CLOSED: EndState 'Victory!' by EndStateView.OnDestroy (torn down without firing)`
  (logcat line 3060078, 0.78 s after `SCREEN OPENED`) as the cost.

Existing PIN 1 must keep passing: hero death still funnels loot through `SettlePartialLoot` before the
reconcile **on the branch where it still settles** (an unsettled raid that the hero's death does end).

---

## 6. Acceptance criteria

1. A raid whose spire falls while the hero is inside the down-beat window keeps its victory screen until the
   player dismisses it or WO-1543's 30 s guard fires — proved by a capture showing `SCREEN OPENED: EndState
   'Victory!'` with **no** `GoCastle` / `SCREEN CLOSED ... torn down without firing` in between.
2. `stars settled:` on any raid in which the hero fell reads `heroDied=True cap=2`, regardless of how close the
   death was to the conclusion.
3. The `hero DOWN ... army settled as a failure` line **never** appears on a run where
   `raid-end reconcile already ran for this raid` also appears.
4. `RAID_EXIT_PARITY_OK` on a fresh log, with PIN 4/5/6 present, plus `REGRESSION_OK <n>/<n> suites`.
5. No change to the army outcome of a genuine hero-death loss (unsettled raid): 0 stars, standing troops home,
   fallen wounded on the recovery timer, partial loot paid.
6. **PO felt-verify:** the owner wins a raid with the hero down and reads the victory screen at her own pace.

---

## 7. Why this sequence is reachable at all (for the implementer)

The down-beat is 1.75 s of scaled `WaitForSeconds` during which the hero is dead, the army is alive, and the
raid is still running by design — that *is* WO-1526. Two things then run against a state neither was written
for: the victory path settles a raid whose scorer has not been told the hero died (DEAD STEP 2), and the death
path finishes its coroutine into a world where the raid is already over and its end screen is on top
(DEAD STEP 1). `RaidScoring.HeroDied`'s own doc calls the second call a *"death/settle race"*
(`RaidScoring.cs:614`) — this capture is that race losing.

Note that `HeroHealth.cs:1318-1319`'s `raidDeathExit` and the WO-1437 comment at `:1327-1332` were written to stop a
player being **stranded** inside a won raid. Fix 1 must not restore that: the yield hands the return to
`RaidVictoryController`, which already has its own 30 s guard and its own `GoCastle`. If the victory
controller is gone, missing or its return is not in flight, the death path must keep evacuating exactly as it
does now.

---

## 8. Unverified / not proven this session

- **Save-file byte equality.** Only the logged *length* (`14506`) was compared across the boundary; the bytes
  were not diffed. The no-op proof rests on the two latch traces and on `army 10` / `deployable=10` read after
  the failure line, not on the length.
- **Why `honor=2`.** `clamped=min(3,2)` shows honor clamped the result; which honor tier produced the 2 was not
  traced to its inputs. Immaterial to the fix, material to the luck: this raid was saved from over-paying by
  honor, not by the cap.
- **No PlayMode/headless reproduction was run** (read-only lane; the lead holds the Unity lock). The sequence is
  established entirely from the device capture plus source reads.
- **Whether any other code path reads `RaidScoring.HeroDied` during the down-beat window** and would also see
  `false`. Only `Finalize` (`:1783`) and `PresentationStars` (`:595`) were traced; a repo-wide sweep of
  `HeroDied` consumers was not done.
- **Whether the owner has ever hit the honor=3 variant** (3 stars + veterancy after dying). The over-pay is
  proven *reachable* from `cap=none`, not proven to have *happened*.
- **`RaidVictoryController` accessor naming** — `VictoryOwnsTheReturn` / `_victoryScreenUp` are this lane's
  proposals, not existing symbols; today the class holds only the private `_handled` (`:73`) and `_returning`
  (`:74`).
- **NOT SWEPT: what clears `_isDead` and re-enables locomotion on the hub load.** Fix 1 changes *when*
  `GoCastle` fires, not *what* fires it — both paths go through `SceneRouter.GoCastle` → hub load →
  `[Flow:SafeZone] SAFE-ZONE full recovery (Main_Castle_Overworld): hero HP+MP restored to full`
  (logcat 3060359, 06.380). The read of `HeroHealth.cs:1321-1376` shows the EVAC branch does nothing but the
  two settles, the `DeathTrace.Note` and `GoCastle`, so the recovery looks scene-load-driven — but the writers
  of `_isDead` were **not** enumerated this session. The implementer must confirm that yielding out of
  `HandleDeath` before the EVAC block does not skip a cleanup step that only that block reaches.
- **The serialized value of `RaidVictoryController._autoReturnSeconds`** was not read; the 30 s figure comes
  from the captured `auto-dismiss armed at 30s` line, not from the field.

---

## 9. Hand-off

- Lane flips this `**Status:**` line and writes `WORK_ORDER_1768_hero_down_after_victory_resettles_army_as_failure.RESULT.md`.
- Lead gates (`COMPILE_GATE_OK`, `RAID_EXIT_PARITY_OK`, `REGRESSION_OK <n>/<n>`), regenerates `BOARD.html`, commits by explicit path.
- PO felt-verifies criterion 6 and closes.
