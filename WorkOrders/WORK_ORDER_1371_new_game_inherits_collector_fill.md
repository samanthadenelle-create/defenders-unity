# WORK ORDER 1371 - A NEW GAME inherits the previous save's collector fill: 14,089 free resources

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:45, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in f6540db88 (2026-09-04 12:47), on the Seeker in build 2026.09.05.355872; RCA re-verified 2026-09-04 (see the appended block). Awaiting owner felt-test: on build 355872+ tap START NEW, then read logcat for `ResetToNewGame: cleared N stale harvest PlayerPrefs key(s) across M collector id(s)` (GameStateService.cs:1587-1591) followed by `New Game: zeroed N live collector(s)` (ResourceCollector.cs:771-774); the first `collector status ->` after `ResetToNewGame: EXIT` must read `pending=0`. If `across 0 collector id(s)` appears, that is the residual hole (KnownCollectorIds on a pre-index device). The 16716 seen at 22:29 = 7500+5760+3456 (the three caps, identical to the pre-fix 07:42 figure) and `window 0s` is what every cold load reports - neither is evidence of a defect on the fixed build. The zero-fill vs seeded starting value was never asked - owner ruling awaited (one line: ResourceCollector.cs:784).
**Silo / Lane:** Economy / harvest collectors + `GameStateService.ResetToNewGame`
**Type:** EXISTING system, economy defect
**Minted:** 2026-09-04 (CLI), from her live session
**Severity:** ⛔ **P1.** A fresh character is handed several hours of production instantly. It
destroys the early economy, the FTUE pacing, and any balance felt-test done on a new game.

## THE REPORT

Owner: ***"how did i manage to acquire 3000 stone when this game is about 25 minutes old"*** /
***"for some reason i got tons on harvest"*** / ***"is harvest reset to zero on new character"***.

## THE PROVING LINES

Source: `logs/device/freeze-20260904-095249.log`. A new game began at **09:44:43**
(`[Flow:Difficulty] DynamicDifficulty reset for a new game`). **Eleven seconds later:**

```
09:44:45.228  [Flow:Harvest] collector status -> full=2/3 maxFill=100% pending=14089 (near-full band)
09:44:54.082  [Flow:Harvest]   collect building=farm       +7500 Food wallet
09:44:54.092  [Flow:Harvest]   collect building=lumbermill +5760 Wood wallet
09:44:54.099  [Flow:Harvest]   collect building=forge      +829  Iron wallet
09:44:54.113  [Flow:Harvest]   collect-all total-banked=14089
```

⚠ **`Food` IS the visible Stone balance** - `GameState.cs:59-61` records that WO-1163 reused the
legacy Food slot for Stone. So she was granted **7500 stone** into a **3000** cap: capped, and the
overflow silently discarded (which is what produced the WO-1370 modal she could not read).

### ⭐ PROOF IT WAS NOT EARNED - the honest rate, measured minutes later

```
09:45:44  pending=29     09:47:30  pending=87     09:49:19  pending=145
09:46:34  pending=58     09:48:26  pending=116    09:50:10  pending=174
```

**~29 per 50s = ~0.58/s.** At that rate 14,089 is **~6.7 HOURS** of accrual, on a game 11 seconds
old. The fill is inherited, not produced.

## THE CAUSE - `ResetToNewGame` resets the wallet but not the faucet

`Assets/_Modules/Core/State/GameStateService.cs` DOES reset the adjacent state, which is what makes
the omission clear rather than deliberate:

| line | reset |
|---|---|
| `:1156` | `s.Resources = ResourceBalance.Starter;` |
| `:1223` | `s.LastHarvestClaimMs = 0;` *(New Game -> reseed the accrual clock on next load (no haul))* |
| `:1249` | `s.SiloResources = 0;` *(ECHO_WORKFORCE_SPEC - empty silo on New Game)* |
| `:1259` | `s.EchoLanes = "harvest:1";` |
| — | ⛔ **collector fill / pending: NEVER TOUCHED** |

⭐ **`:1223`'s own comment says the intent out loud - "no haul" on a new game.** The accrual CLOCK is
reseeded and the SILO is emptied, but the per-building collector fill survives, so the haul arrives
anyway through a different door. **This is an omission in an otherwise careful reset, not a design
decision** - and `:1117` already warns about exactly this class: *"so a 'new' game inherited the..."*.

## THE WORK

1. **Find where collector fill actually persists** and reset it in `ResetToNewGame`. ⚠ It is NOT an
   obvious `GameState` field - the grep for `collectorstate|pendingHarvest|collectorFill` finds only
   `CollectorStackView.cs`, so **establish the real storage before writing the reset**; it may be
   derived from a per-building timestamp, in which case the fix is to reseed that stamp (the same
   shape as `LastHarvestClaimMs`).
2. ⛔ **AUDIT `ResetToNewGame` FOR SIBLING OMISSIONS.** Two independent "new game inherits X" bugs are
   now on the record (this one, and the `:1117` note). **Enumerate every accrual/timer/fill source in
   the economy and prove each is either reset or deliberately carried.** A list, in the RESULT file.
3. **Decide the correct new-game state and say so**: zero fill, or fill seeded to the same starting
   point a founding town gets. ⚠ If that is a design question, it is the OWNER'S - ask, do not pick.

## ACCEPTANCE

- [ ] ⛔ **Proven from a capture**: `ResetToNewGame` -> the first `collector status` line reads
      `pending=0` (or the ruled starting value), NOT a carried figure. Quote before and after.
- [ ] ⛔ **Proven RED first** - reproduce the 14,089 inheritance, then show it gone (WO-1138).
- [ ] The full `ResetToNewGame` audit list from item 2 is in the RESULT file, each entry marked
      reset / deliberately carried, with a reason.
- [ ] An oracle fails if a fresh `ResetToNewGame` state carries any non-zero accrual. ⚠ This shipped
      to the production candidate with `REGRESSION_OK 358/358` green.

## WHAT NOT TO TOUCH

- ⛔ Not the collector RATE or the storage caps. 0.58/s and the 3000 cap are the WO-837 stockpile
      design working; the defect is the inherited starting fill.
- ⛔ Not the Food-slot-is-Stone mapping (WO-1163/WO-1212). It is deliberate and load-bearing.
- ⛔ Not the overflow-discard behaviour - that is WO-1370's copy problem, not a logic change.

## RELATED

- **WO-1370** - the unreadable HARVEST RESULT modal. **This ticket is why she saw it**: the inherited
  7500 stone overflowed a 3000 cap. Fixing 1371 removes the trigger; 1370 still needs doing.

---
## RCA re-verified 2026-09-04 (QA read-only pass)
**Verdict:** SUPERSEDED
**Evidence:**
- Commit `f6540db88 2026-09-04 12:47` (ancestor of HEAD) body: "WO-1371 - a new game inherited 14,089 resources because collector fill lives in PlayerPrefs OUTSIDE the save envelope. ClearHarvestPrefs added; ResourceBuildingState.ResetAll() finally has a caller - its first ever ... The 13+ other out-of-envelope stores the RCA found are recorded as ledger rows with reasons rather than silently fixed." That answers work items 1 and 2 of this WO.
- `Assets/_Modules/Core/State/GameStateService.cs:1296` `ClearHarvestPrefs(); // WO-1371`; `:1443-1465` the ONE authority for the three collector PlayerPrefs prefixes (`:1452 CollectorPendingPrefPrefix = "dotr.collector.pending."`, `:1453 ...hp.`, `:1454 ...lastaccrual.`) plus the building-id index. `Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs:22-26` now aliases `GameStateService.CollectorPendingPrefPrefix` instead of declaring its own; `:697`, `:731` "the LIVE half of the New Game reset".
- The line numbers this WO cites (`:1117`, `:1156`, `:1223`, `:1249`, `:1259`) have all moved (`:1156` is now the StartingBudget string, `:1259` is `WavesCompleted = 0`) - the file was touched by `f6540db88`, `6979fb961`, `1ef5f6ad4` after the mint.
- Oracle: `Assets/Editor/Regression/NewGamePrefStoreSweepRegression.cs` (`:12` cites this WO, `:43` "RED against the pre-WO-1371 tree", `:52` markers `NEWGAME_PREF_SWEEP_OK/FAIL`), registered `DataRegression.cs:667` beside `reset-full-clear` (`:663-664`, the GameState-FIELDS axis).
- This WO's Status line was never flipped (`git log -1 -- <WO>` = `f850e5ed6`, the mint).
- The capture-based acceptance (first `collector status` after reset reads `pending=0`) is NOT met: every log under `logs/device/` is the 09-04 morning pre-fix pull. The headless side IS green: `Builds/regression.log` at its 22:31 state read `:113715 REGRESSION_OK 377/377 suites` (a 22:42 rewrite began after that read).
**What changed since the RCA:** the fix, the audit ledger and the oracle all landed in `f6540db88`; the storage question (item 1) is answered - PlayerPrefs, outside the save envelope.
**Ready for a lane?** no - implemented; remaining acceptance is a post-fix device capture quoting `pending=0` after `ResetToNewGame`, then owner felt-verify. Files a lane would touch: this WO (Status line).
**Pins/rulings needed:** item 3's design question (zero fill vs seeded founding fill) - the commit chose CLEAR (zero); the owner has not been asked on record. Confirm or re-rule.

---
## Diagnosis 2026-09-04 (read-only)

Scope: the 22:29 Seeker welcome-back popup (`collectorsPending=16716 across 3 collector(s)`, `window 0s`)
against the tree at HEAD `d1fd1f6e0`. No code touched, no Unity, no adb. Every line below was read at
source or measured this session; anything not provable from here is named as such.

### 0. The 22:29 capture is NOT on disk - and what its one quoted line already proves

- `grep collectorsPending logs/device/*.log logs/f8-inbox/**` = **zero hits**. The newest inbox capture is
  seq4680 (09:36); the device bridge dir has nothing after 08-31; the newest device log is `enemy-color.log`
  (14:13). **The 22:29 popup exists only in the owner's screenshot / the CLI's terminal - not in any file
  this agent can read.** The line is quoted from the task, not from a capture.
- **The string `collectorsPending=` did not exist in any commit before 22:44** (`git log -S"AttachPendingCollectors"`
  = `d1fd1f6e0 22:44`, first and only). So the build that printed it at 22:29 was built from tonight's
  **working tree**, which sits on `f6540db88 12:47` - **the WO-1371 fix was in that build.** Corroborating:
  `Builds/Android/DefendersOfTheRealm.apk` (mtime 22:18) carries `bundleVersion 2026.09.05.355872` (read out of its
  `assets/bin/Data/globalgamemanagers`), newer than HEAD's committed `2026.09.04.355307`
  (`ProjectSettings.asset:148`, last committed 13:23). *Unprovable from here:* that this APK is the one installed
  on the Seeker at 22:29 (adb is the only proof; not permitted for this agent).
- **16716 = 7500 + 5760 + 3456** - farm cap + lumbermill cap + forge cap, all three AT CAP. It is the
  **identical** figure the pre-fix build logged at `07:42:40.655` (`freeze-20260904-095249.log:379210`
  `collector status -> full=3/3 maxFill=100% pending=16716`). The same PlayerPrefs, still full.
- **`window 0s` is NOT evidence of a new game.** On every cold load the RESUME claim runs first and seeds or
  consumes the clock, so the cold-load claim measures ~0 s: `07:42:41.149 Claim #1 (resume): FRESH clock ... seed
  to now` -> `07:42:41.185 Claim #2 (cold-load): ONE delta = 0s` (`:379542`, `:379602`). That sequence occurred
  with NO reset in that session (the reset came 3 s later, `:384407`).

**Two readings, and the one grep that separates them (CLI, on the device logcat, between the popup and the
previous `ResetToNewGame: ENTER`):**
- **(A) No "Start New" was pressed on the fixed build.** The prefs still hold the pre-fix fill and a cold load
  reports it honestly through `AttachPendingCollectors` (`OfflineHarvestService.cs:444-484`, which sums
  `ResourceCollectorRegistry.All` `PendingAmount`). The fix has simply not been exercised on the device yet.
  Signature: **no** `[Flow:Save] ResetToNewGame: cleared N stale harvest PlayerPrefs key(s)` line in the session.
- **(B) "Start New" WAS pressed on the fixed build and 16716 came back** = a residual hole. Signature: the
  `cleared ... across M collector id(s)` line (`GameStateService.cs:1587-1591`) IS present - read `M`. If it says
  `across 0 collector id(s)`, `KnownCollectorIds()` (`:1500-1529`) found nothing: the owner's device predates the
  `dotr.collector.ids` index, so it depends on the catalog union (`:1516-1527`), and `CatalogRegistry.OfType` is a
  **plain dictionary lookup, not lazy** (`CatalogRegistry.cs:93-96`). On the pre-fix device boot the catalog
  registered at `07:42:37.45` (`:373952`), 7 s before the Title reset at `07:42:44.9`, so this is unlikely - but
  the count line settles it, a theory does not. Then look for `[Flow:Harvest] New Game: zeroed N live
  collector(s)` (`ResourceCollector.cs:771-774`) and per-collector `New Game: collector '<id>' pending X -> 0`
  (`:790-792`).

### 1. Where the fill lives, and whether the reset clears it

- **Storage = PlayerPrefs, OUTSIDE the `dotr-save` envelope**, three keys per building id:
  `dotr.collector.pending.<id>` / `dotr.collector.hp.<id>` / `dotr.collector.lastaccrual.<id>`
  (`GameStateService.cs:1452-1454`, aliased at `ResourceCollector.cs:26-27,37`). Written by
  `ResourceCollector.SaveState` (`:691-704`, on every Accrue / OnDisable / catch-up), read by `LoadState`
  (`:674-689`) from `Awake` (`:167-171`) and again in `Configure` (`:343`). The fill is not derived from a
  stamp: `_pending` is stored directly (`:676`, `:693`); the stamp (`:683-688`) only drives the away top-up in
  `CatchUpAway` (`:234-283`), where an absent stamp seeds to now and back-fills nothing (`:240-247`).
  The CAP is a fourth key, `dotr.resbuilding.level.<id>` (`ResourceBuildingState.cs:54`).
- **At HEAD, `ResetToNewGame` DOES clear it - two halves:**
  - prefs half: `ClearHarvestPrefs()` called at `GameStateService.cs:1296`, body `:1567-1592` - deletes the three
    prefixes + level key for every id in `KnownCollectorIds()`, then the index.
  - live half: `NewGameStarted` raised `:1309-1324`; `ResourceCollector.OnNewGameStarted` (`:757-775`, installed
    BeforeSceneLoad `:747-755`) calls `ResetForNewGame` (`:780-793`: `_pending = 0`, HP full, stamp = now,
    `SaveState`) on every collector **including inactive parked DDOL fallbacks**, then
    `ResourceBuildingState.ResetAll()` (`:276-285`, which also reaches `TechTree.ResetAll`). Order is right:
    delete first, then the live write-back of zeros.
  - Other `NewGameStarted +=` subscribers, for the audit: `HeroProgression.cs:139`, `WisdomCurrencyService.cs:78`,
    `SkillSystem.cs:80` (each unsubscribes).
- **The defect the WO describes is real and proven in the pre-fix build `2026.09.04.354315`** (the only build in
  any log on disk; `:373721`, `:834248`, `:1049051`): after `ResetToNewGame: ENTER` at `09:33:36.458`
  (`:852089`) the town read `collector status -> full=0/0 pending=0` (`:852700`), and **7.5 s later**
  `register id=farm pending=7500/7500` (`:863735`, 09:33:43.996), then `full=2/3 pending=14041` at 09:37:59
  (`:969646`). No `cleared ... harvest` line follows either `ENTER` in that log - the build did not have the fix.
- **Correction to the WO's proving lines:** the "new game at 09:44:43" is NOT a `ResetToNewGame`. The only
  `ENTER` lines in the log are 07:42:44 (pid 22805) and 09:33:36 (pid 28972). `09:44:43 DynamicDifficulty reset for
  a new game` is `WaveManager.EnsureDifficultySessionReset` (`WaveManager.cs:3286-3292`), a once-per-process
  latch - pid 30184 launched at 09:43:44. The inheritance is proven; the timestamp framing was wrong.

### 2. Is the stamp keyed by something that survives a new game?

Yes: **by building id** (`farm` / `lumbermill` / `forge`, `ResourceBuildingProgression.cs:173-175,223`) - a
stable identity across saves, new games, and the Default-Town template. That is exactly why the fill outlived
`s.LastHarvestClaimMs = 0` (`GameStateService.cs:1232`) and `s.SiloResources = 0` (`:1258`).
`NewGamePrefStoreSweepRegression` sweeps `GameStateService.NewGamePrefStores` (`:1642-1723`); **the collector keys
ARE in the list** as `ClearedByReset` rows: pending `:1647`, hp `:1650`, lastaccrual `:1653`, level `:1656`, the
ids index `:1659`. Proven green tonight: `Builds/regression.log` (22:44:03)
`[newgame-pref-sweep] NEW GAME PREF SWEEP OK ... [notes: 16 KNOWN GAP(s) ...]` and `REGRESSION_OK 377/377 suites`.
The 16 `NotYetCleared` rows (`:1680-1722`) - including the second harvest clock `dotr-harvest-last-active`
(`:1714`) - remain inherited by design of that pass and are each a candidate ticket.

**Two gaps in what the oracle proves (observations, not proven defects):**
- Case 3 registers its probe via `RegisterCollectorId(ProbeId)` BEFORE clearing (`NewGamePrefStoreSweepRegression.cs:206`),
  so it proves the **index** path only - never the **catalog-union** path that a pre-index device (the owner's
  Seeker) relies on. Reading (B) above is the un-oracled path.
- Case 5 is a **source-text grep** (`:236-252`), not behaviour: no oracle drives a live `ResourceCollector`
  through the reset and reads `PendingAmount`. Case 6 reports, never fails.
- Minor: `ResetForNewGame` WRITES the stamp = now (`ResourceCollector.cs:787`) while the ledger row says
  "deleted, not stamped" (`GameStateService.cs:1653-1655`). Both back-fill nothing; the two statements disagree.

### 3. Headless reproduction the CLI can run

Extend `Assets/Editor/Regression/NewGamePrefStoreSweepRegression.cs` with **Case 7 `[live-collector-zero]`**
(`DeNelle.EditorRegression.asmdef` already references `DeNelle.Village`, so `ResourceCollector` is reachable;
`SiegeLossStakesRegression.cs:510-527` is the fixture pattern, `:800-811` the `SetPrivate` helper):

1. Snapshot + delete `dotr.collector.{pending,hp,lastaccrual}.farm`, `dotr.resbuilding.level.farm`, and
   **`dotr.collector.ids`** (deleting the index is what simulates the owner's pre-index device and forces the
   catalog-union path).
2. `new GameObject` + `AddComponent<ResourceCollector>()`, `Configure(ResourceBuildingProgression.FarmId)`
   (edit mode: Awake/OnEnable do not run; Configure loads + registers).
3. Seed the fill both ways: `SetPrivate(c, "_pending", 14089.0)` AND `PlayerPrefs.SetFloat("dotr.collector.pending.farm", 14089f)`;
   `PlayerPrefs.SetInt("dotr.resbuilding.level.farm", 5)`.
4. Reflection-invoke `GameStateService.ClearHarvestPrefs` (private static), then
   `ResourceCollector.OnNewGameStarted` (private static; the `RuntimeInitializeOnLoadMethod` hook at `:747` does
   not run in edit mode, so invoking the handler directly is the honest edit-mode equivalent of the event).
5. Assert `c.PendingAmount == 0`, `PlayerPrefs.GetFloat("dotr.collector.pending.farm", -1f) is 0 or -1`,
   `ResourceBuildingState.GetLevel(FarmId) == 1`. Restore the snapshot in `finally`.
6. RED proof (WO-1138): temporarily comment the `ClearHarvestPrefs();` call at `GameStateService.cs:1296` AND the
   `ResourceBuildingState.ResetAll()` call at `ResourceCollector.cs:769` - the case must go RED on step 5; the
   existing Case 2 already goes RED on the first of those alone.

Full-flow (device-shaped) variant: seed the three prefs, drive Title -> Start New headlessly (`run-defenders`
AutoPilot), and assert the first `[Flow:Harvest] collector status ->` after `ResetToNewGame: EXIT` reads
`pending=0` - the WO's own acceptance line. That is also the felt-verify grep for the owner's next capture.

### 4. Fix location (stated, not applied)

- **Already at HEAD** (`f6540db88`, 12:47): `GameStateService.cs:1296` + `:1567-1592`, `ResourceCollector.cs:747-793`.
  This WO's `**Status:**` line still reads `READY TO IMPLEMENT` and there is no RESULT file - the board is derived
  from that line, so it is misreporting; the CLI flips it in the same commit as the RESULT.
- **If reading (B) is proven**, the seams are the same two methods: `KnownCollectorIds()` (`:1500-1529`) for an
  id the sweep did not find, `OnNewGameStarted` (`:757-775`) for a live instance it did not reach.
- **The zero-vs-seeded value is the OWNER's ruling, not picked here.** The number would go in exactly one line:
  `ResourceCollector.cs:784` `_pending = 0.0;` (the live half); the prefs half needs nothing because an absent key
  reads as 0 (`:676`) - a seeded value would also reword the ledger `Why` at `GameStateService.cs:1647-1649`. The
  founding-budget precedent for "one authoritative home" is `StartingBudget` (`:1184-1194`).
