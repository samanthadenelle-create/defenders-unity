# WORK ORDER 1768 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** implementation lane, 2026-09-16. Branch `dev`. No Unity gate run (the lead holds the lock), no commit.
**Scope honoured:** exactly the four files the WO names. `SmartMobileCamera.cs`, `TroopController.cs`,
`RaidAssaultAi.cs`, `TroopBreachOrder.cs`, `RaidGarrisonSpawner.cs`, `RaidBaseGenerator.cs`, the
`OwnedTown*` editor files, `DataRegression.cs` and the WO-number banner were **not touched**
(`git status` diff is the proof the lead can read at gate time).

---

## 1. Files changed

| file | what landed |
|---|---|
| `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` | `private bool _victoryScreenUp;` + `public bool VictoryOwnsTheReturn => _victoryScreenUp || _returning;`. The latch is set on the line **immediately after `EndStateView.Show(vm);`**, inside the existing `try`, so a build that threw never sets it and the existing `catch -> ReturnHome()` contract is untouched. |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | `public bool Reconciled => _reconciled;` beside the latch field. Property, not a `bool` return — **no existing call site changes**, and the latch keeps exactly one writer. |
| `Assets/_Modules/Village/Hero/HeroHealth.cs` | Fix 1 (victory yield + watchdog), Fix 2 (the cap latch moved into the synchronous death sequence), Fix 3 (both settle traces made truthful + the `Save()` made conditional), and two stale in-code citations corrected. |
| `Assets/Editor/Regression/RaidExitParityRegression.cs` | PIN 4 / PIN 5 / PIN 6, `VictRel` added to the read set, header ledger and the `RAID_EXIT_PARITY_OK` reason string extended. |

## 2. Fix 1 — the EVAC path yields to a victory screen that is UP

Inserted in `HandleDeath` **between** the `raidDeathExit` declaration and the existing
`if (enemyOwnedScene || raidDeathExit)` test:

- gate = `victory != null && victory.VictoryOwnsTheReturn` — the **new** latch, never `_handled`,
  never `_returning` alone (the WO's trap note; the reasoning is written at source on the accessor).
- **not a bare `yield break`.** A poll loop on `Time.realtimeSinceStartup` for **45 s**
  (`const float VictoryYieldWatchdogSeconds = 45f` — above the 30 s auto-dismiss observed at logcat
  3059807, plus grace) with `yield return null` per frame, because an end-state screen may zero
  `Time.timeScale` and a scaled wait would then never elapse.
- exits `yield break` + a `FlowTrace.Step` the moment `victory == null` **or** the active scene name
  changed — i.e. the return actually happened.
- on expiry emits a `FlowTrace.Warn` naming the capture-retry case
  (`ReturnHome`'s `if (!CanEnterCapturedTown()) return;`) and **falls through to the existing EVAC
  block unchanged**. A dead hero is never stranded on an enemy field (WO-1437 contract preserved).
- `Step`, not `Warn`, on the stand-down: `HeroDeathSeverityRegression` is a source-lint over
  `FlowTrace.Fail` prose only (read at source, `HeroDeathSeverityRegression.cs:150-173`), and a normal
  death must not raise an F8 severity. Only the abnormal watchdog expiry is a `Warn`.

## 3. Fix 2 — the cap latches at the lethal hit

`DeNelle.Village.RaidScoring.Instance?.NotifyHeroDied();` now sits in **`BeginDeathSequence`**,
synchronously, after the `[Flow:Death] lethal hit` dump and **before** `OnDeath?.Invoke()`,
`OnDied?.Invoke()` and `StartCoroutine(HandleDeath())`. The old call inside `HandleDeath`
(post-`WaitForSeconds`) is **deleted, not duplicated** — `RaidScoring.cs:616-620` says "There must
never be two latches for one death", and the comment there that claimed "LATCH FIRST, BRANCH SECOND"
now describes what the code actually does and cites the capture.

**Read at source this session before moving it:** `RaidScoring.NotifyHeroDied`
(`RaidScoring.cs:622-632`) does nothing but `if (_heroDied) return; _heroDied = true;` and one
`FlowTrace.Step`. No side effect, no honor-star snuff in the live body (the snuff is the unmerged
WO-1594 branch, and the COMPOSE NOTE is preserved verbatim in the new comment). Safe to run earlier
and on every death: `Instance` is null outside a raid and `?.` no-ops.

### `_isDead` writers enumerated (the WO §8 item the implementer was told to close)

- `true`: **one** writer — `BeginDeathSequence` (`HeroHealth.cs:960`, the first mutation, the latch).
- `false`: **two** writers — `Respawn` (`:1516`) and `RestoreToFull` (`:1912`).
- **Neither is reachable from the EVAC block.** The EVAC block does only: the signal `FlowTrace.Step`,
  a `Debug.Log`, `SettlePartialLoot`, `ReconcileRaidEnd(0)`, a `Step`, a `Save()`, `DeathTrace.Note`
  and `SceneRouter.GoCastle()`. So **yielding before it skips no cleanup.** The revive is scene-load
  driven: `SafeZoneRecovery.cs:141` calls `HeroHealth.Instance?.RestoreToFull()` — the
  `[Flow:SafeZone] SAFE-ZONE full recovery` line at logcat 3060359. The victory path's own
  `GoCastle`/`GoOwnedTown` reaches that same recovery, so the yield loses nothing.

## 4. Fix 3 — the failure-settle trace reports what happened

`HeroHealth` now reads `raidScorer.Finalized` and `raidDeploy.Reconciled` **before** the two
`Guard.Try` settles and `raidDeploy.Reconciled` again after, then:

- `reconcileRan == true` → the original **"army settled as a failure (0 stars); the troops still
  standing break and flee home, the fallen are wounded."** line, kept verbatim, plus the loot note.
- `reconcileRan == false` → **"hero DOWN after the raid had already settled - the army was NOT
  re-settled (reconcile was latched, so this call was a no-op) and nothing new was written..."**
- `Save()` is issued only when `reconcileRan || lootSettledHere`; otherwise a third `Step` says no Save
  was issued and why. **Both traces kept** — §12: the false one was made truthful, not stripped.
- Call **order and literals preserved** for PIN 1: `SettlePartialLoot(` at stripped offset 46322,
  `ReconcileRaidEnd(0)` at 46490 (measured, §6).

## 5. Regression — PIN 4/5/6 in `RaidExitParityRegression` (the existing suite, no new one)

- **PIN 4** — `Body()`-extracts `HandleDeath`, regex-locates the down-beat
  `WaitForSeconds(Mathf.Max(0.1f, _downSeconds)`, and requires the first
  `\.NotifyHeroDied\s*\(\s*\)\s*;` to sit at a **lower offset**. Matched **with parens and semicolon
  on purpose**: `StripLineComments` keeps string literals, so a symbol mentioned in a trace string
  can never turn this pin green with prose. Fails if either anchor is gone, rather than passing blind.
- **PIN 5** — requires `public bool Reconciled` in `RaidDeployController`; requires the
  `army settled as a failure` string to still exist; requires a regex
  `if\s*\([^)]*[Rr]econcile[^)]*\)` inside the **400 chars preceding it**; requires `.Reconciled`
  to be read somewhere in `HeroHealth`; requires the truthful twin's distinctive `NOT re-settled`.
- **PIN 6** — requires `public bool VictoryOwnsTheReturn` in `RaidVictoryController`; requires
  `_victoryScreenUp = true` to appear **after** `EndStateView.Show(`; requires `HeroHealth` to
  reference `VictoryOwnsTheReturn` at a **lower offset** than
  `if\s*\(\s*enemyOwnedScene\s*\|\|\s*raidDeathExit\s*\)`; and requires **both** a `yield break`
  **and** a `FlowTrace.Warn` in the gap between them — the Warn is the only lintable proof the
  fall-through is logged rather than a bare `yield break`.
- `VictRel = "_Modules/Village/World/Camps/RaidVictoryController.cs"` added to the read set (file
  confirmed present on disk).
- **`DataRegression` registration verified, not touched:** `Assets/Editor/Regression/DataRegression.cs:1474`
  already wraps `RaidExitParityRegression.Run` in a `Guard.Try` inside `RunAll`. Nothing to add.

## 6. Proofs (all run this session, outputs verbatim)

### 6a. Brace gate (the gate's own rule, `tools/gate_brace.py`) + NUL scan

Baseline, before any edit — `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0.
After all edits:

```
$ python tools/gate_brace.py Assets/_Modules/Village/Hero/HeroHealth.cs \
    Assets/_Modules/Village/World/Camps/RaidVictoryController.cs \
    Assets/_Modules/Village/Troops/RaidDeployController.cs \
    Assets/Editor/Regression/RaidExitParityRegression.cs
GATE_BRACE_SUMMARY bad=0 of 4
GATE_BRACE_EXIT=0

HeroHealth.cs NUL=0
RaidVictoryController.cs NUL=0
RaidDeployController.cs NUL=0
RaidExitParityRegression.cs NUL=0
```

No new `FlowTrace` string puts a `"` inside an interpolation hole — every new trace is plain `+`
concatenation, so CLAUDE.md §1's interpolated-string trap is not engaged.

### 6b. The pre-fix RED greps (captured BEFORE the edits — `scratchpad/wo1768_prefix_red.txt`)

```
--- HeroHealth: NotifyHeroDied vs the down-beat WaitForSeconds ---
1207:            yield return new WaitForSeconds(Mathf.Max(0.1f, _downSeconds));
1270:            if (raidScorer != null) raidScorer.NotifyHeroDied();      <- the ONLY real call, AFTER the wait
--- HeroHealth: VictoryOwnsTheReturn / .Reconciled count ---   0
--- RaidDeployController: any 'Reconciled' member ---          (no match, exit 1)
--- RaidVictoryController: _victoryScreenUp / VictoryOwnsTheReturn count --- 0
--- HeroHealth: 'NOT re-settled' narration ---                 0
```

### 6c. PIN 4/5/6 RED on HEAD, GREEN on the working tree

The pins' exact logic (comment-stripper, `Body()` brace matcher, every regex and offset comparison)
was ported to `scratchpad/pin456.py` and run against `git show HEAD:` copies and the working-tree
copies of the three runtime files:

```
=== PRE-FIX (HEAD) ===
FAILS(7):
  PIN4: latch AFTER the down-beat (notify=38027 wait=36893)
  PIN5: RaidDeployController has no public bool Reconciled
  PIN5: failure trace not inside a reconcile-ran test
  PIN5: HeroHealth never reads .Reconciled
  PIN5: no truthful 'NOT re-settled' twin
  PIN6: no public bool VictoryOwnsTheReturn
  PIN6: HeroHealth never consults VictoryOwnsTheReturn
=== POST-FIX (working tree) ===
ALL PINS 4/5/6 GREEN
```

Existing **PIN 1 still holds** on the post-fix tree (same stripper):
`SettlePartialLoot( idx = 46322`, `ReconcileRaidEnd(0) idx = 46490`, `order ok: True`.

### 6d. Cross-suite sweep — every OTHER source-lint that reads these three files

`grep -rln` over `Assets/Editor/Regression` + `Assets/Data/Tests` returned 30 suites naming one of the
three runtime files; every assertion that touches text this lane changed was re-run against the
post-fix tree with the same stripper (`scratchpad/crosssuite.py`):

```
GREEN RaidScoring:339-355  liveRaidContinues BEFORE the evac test  guard=39691 evac=45393
GREEN RaidScoring:362-370  flat span [liveRaidContinues..evac] contains NO banned token
                           span=5702 chars, hits=none
GREEN RaidScoring:371      that same span still contains NotifyHeroDown
GREEN RaidScoring:380      hero contains NotifyHeroDied
GREEN HeroDownInputRefusal:268  'raidScorer.NotifyHeroDied()' literal kept
GREEN HeroDownInputRefusal:273  'NotifyHeroDown()' kept
GREEN HeroDownInputRefusal:253  'lethalFrom=' kept
GREEN HeroDownInputRefusal:250  'ZERO HP WITH NO DEATH LATCH' kept
GREEN RaidExitParity PIN1  loot settles before the army reconcile  46699 < 46867
GREEN RaidExitParity PIN3  no bare catch in RaidDeployController
GREEN RaidTerminalState:478  vict still contains EndStateView.Show
RED COUNT: 0
```

### 6d-bis. ⛔ THE GATE CAUGHT THIS SWEEP BEING WRONG — `REGRESSION_FAIL 548/550`, 15:21

The first version of §6d **passed a check the real suite fails**, and the lesson is worth more than the
fix. The gate reported:

> `raid-scoring: [WO-1526] HeroHealth's live-raid death branch calls GoCastle. The raid CONTINUES on
> this branch...`

**Why my port disagreed with the oracle:** I extracted the live-raid branch as a **brace-matched body**
of `if (liveRaidContinues)`. `RaidScoringRegression.cs:362` does no such thing — it takes a **flat text
span**, `heroCode.Substring(iGuard, iEvac - iGuard)`, from the first `liveRaidContinues` to the first
`enemyOwnedScene || raidDeathExit`, and fails if that span *contains* `SettlePartialLoot`,
`ReconcileRaidEnd`, `GoCastle` or `Respawn(`. So:

1. the victory-yield block sits **inside** that span even though it is not the live-raid branch, and
2. the span is built with a **comment-only** stripper (`:534-563`), which is **not string-literal
   aware** — so a *trace string* reads to the lint exactly like a call.

The offending text was prose: `"...no Save, no GoCastle from here..."`. **No call was ever made** —
the block's whole point is that it routes nowhere. The fix is one word: `no scene route from here`.
`GoCastle` now appears in this region only inside `//` comments, which that lint does strip.

**Proof, with the corrected port (`scratchpad/crosssuite.py`, now carrying a port of
`RaidScoringRegression.StripComments` and the flat-span rule):**

```
(pre-reword file, the one the gate rejected)
RED   RaidScoring:362-370 flat span contains NO banned token  span=5483 chars, hits=['GoCastle']
RED COUNT: 1
(live working tree, reworded)
GREEN RaidScoring:362-370 flat span contains NO banned token  span=5702 chars, hits=none
RED COUNT: 0
```

**Nothing was weakened to get there.** The span still contains `NotifyHeroDown`
(`RaidScoring:371`), `liveRaidContinues` still precedes the evac test, PIN 4/5/6 are still
RED-on-HEAD / GREEN-on-tree (re-run below), PIN 1's order is intact, and the never-strand contract is
untouched: the watchdog still falls through to the unchanged EVAC block, which is where the real
`SceneRouter.GoCastle()` lives — **outside** the span, exactly as before this lane.

A comment at source (`HeroHealth.cs`, immediately above the STAND DOWN trace) now states this
constraint and names `RaidScoringRegression.cs:362`, so the next author does not re-learn it from a
gate failure. **The methodology failure this records: a hand-written port of an oracle is a CLAIM about
that oracle. I read the assertion's message but not its extraction, and a "GREEN" from my own port
proved only that my port agreed with itself.** The port now quotes the real logic and is kept beside
the RESULT for the next lane.

Re-run after the reword (unchanged verdicts):

```
PIN 4/5/6 on HEAD  -> FAILS(7)   (PIN4 latch AFTER the down-beat, notify=38027 wait=36893; PIN5 x4; PIN6 x2)
PIN 4/5/6 on tree  -> ALL PINS 4/5/6 GREEN
gate_brace (4 files) -> GATE_BRACE_SUMMARY bad=0 of 4, exit 0;  NUL=0 on all four
```

⛔ **ONE OF THESE WAS RED AND IS THE REASON THIS SWEEP HAPPENED.**
`HeroDownInputRefusalRegression.cs:268` pins the **literal text**
`raidScorer.NotifyHeroDied()` inside `HeroHealth.cs`. The first draft of Fix 2 wrote the moved call as
`DeNelle.Village.RaidScoring.Instance?.NotifyHeroDied();` — runtime-identical, and it would have turned
that suite RED at the lead's gate for a purely cosmetic reason. The call is therefore spelled
`var raidScorer = DeNelle.Village.RaidScoring.Instance; if (raidScorer != null) raidScorer.NotifyHeroDied();`
— the same two-statement form the retired call site used, with a comment at source naming the pin that
requires it. **No semantics were reshaped to satisfy an oracle**; only the spelling of a local.
(Side effect: it also keeps the Unity-overloaded `!= null` rather than CLR `?.`, exactly as before.)

`HeroDeathPinRebaseRegression` pins `EnterDeathFreeze` / `ExitDeathFreeze` / `RebaseDeathPin` / the
LateUpdate pin watchdog — none of which this lane touched.

### 6e. Canon sweep (§15)

`grep -rn "NotifyHeroDied"` over `docs/`, `KEY_FACTS.md`, `CANON_GROUND_TRUTH_*.md`,
`SESSION_CANON_LOADER.md`, `PIPELINE_STATE.md` → **no hits**. No load-bearing doc cites the latch's
location, so nothing is owed. The only citations of the old `:1270` call site live in
`WORK_ORDER_1750_*.RESULT.md` and `WORK_ORDER_1594_*.RESULT.md`, which §15 freezes as dated
point-in-time records — deliberately **not** rewritten. Two **in-code** citations that went stale the
moment the call moved were corrected in the same change: the `LATCH FIRST, BRANCH SECOND` block in
`HandleDeath` and the zero-HP recovery comment that named `:1270` by line number (that one now names
no line number at all, per CLAUDE.md's own rule about copied line references).

⚠ **What this proof is and is not.** It is a faithful port of the C# pin logic, run on the real
files — it proves the pins discriminate pre-fix from post-fix. It is **not** a Unity run: the
`RAID_EXIT_PARITY_OK` marker on a fresh log is the lead's gate, and nothing here substitutes for it.

## 7. What still needs a DEVICE capture (nothing here proves felt behaviour)

1. **A raid that concludes within 1.75 s of the hero's death** — the whole reachability window. The
   capture must show `stars settled: ... heroDied=True cap=2` (acceptance 2) and
   `HandleDeath: down-beat elapsed -> STAND DOWN, THE VICTORY SCREEN OWNS THE RETURN`.
2. **A victory screen that survives a touch** — `SCREEN OPENED: EndState 'Victory!'` with **no**
   `GoCastle`/`SCREEN CLOSED ... torn down without firing` in between, until the player taps
   Return to Castle or the 30 s guard fires (acceptance 1, and acceptance 6 is the owner's own read).
3. **The `NOT re-settled` line appearing instead of the failure line** on that same run
   (acceptance 3).
4. **A genuine hero-death loss on an UNSETTLED raid** — must still pay 0 stars, wound the fallen on
   the recovery timer and pay partial loot (acceptance 5). This is the branch where `reconcileRan`
   is **true**, so the original trace and the `Save()` both still fire.
5. **The watchdog path** (`VICTORY YIELD WATCHDOG EXPIRED`) is unexercised by any capture and cannot
   be reached without a refusing captured-town census. Unproven by construction; it is the belt.

## 8. Claims I could not prove / adjacent findings

- **No Unity compile, no gate, no PlayMode run.** The lead holds the lock; the edits are proven
  brace-balanced, NUL-free and pin-discriminating, **not proven to compile**. `CS` errors are the
  gate's job.
- **`RaidVictoryController._autoReturnSeconds`'s serialized value was still not read** (same as WO §8).
  The 45 s belt is anchored on the captured `auto-dismiss armed at 30s` line, and the comment at
  source says so rather than implying the field was consulted.
- **The same tear-down class exists on the non-victory exit and is NOT covered.** If the 180 s clock
  expires inside the down-beat, `DoRetreat -> ShowNonVictoryResult` puts an end state up and this
  fix's `VictoryOwnsTheReturn` says nothing about it — `RaidDeployController` has no equivalent
  "result screen is up" accessor. Out of WO-1768's scope and file list; flagged for the PO as a
  possible sibling ticket, **not** implemented here.
- **Whether any other reader of `RaidScoring.HeroDied` behaves differently now that the latch is
  1.75 s earlier** was not swept (WO §8 left it open). Moving the latch can only make
  `HeroDied` true *sooner*, and the two traced consumers (`Finalize`, `PresentationStars`) both want
  it true; a repo-wide sweep of `HeroDied` consumers was still not done.
- **Save-file byte equality** across the boundary remains unproven (WO §8), unchanged by this lane.

## 9. Housekeeping the lead needs to know before staging

- ⚠ **BOTH WO FILES ARE UNTRACKED (`??`).** The read-only RCA lane wrote
  `WorkOrders/WORK_ORDER_1768_hero_down_after_victory_resettles_army_as_failure.md` and never committed
  it, so `git add` must include **the ticket as well as this RESULT** — staging only the RESULT would
  commit a result for a ticket that is not in the tree, and `tools/board_build.py` reads the ticket.
- `git diff --stat`: `RaidExitParityRegression.cs +176`, `HeroHealth.cs +197/-16`,
  `RaidDeployController.cs +20`, `RaidVictoryController.cs +44`. No other tracked file touched by this
  lane (the rest of `git status` predates it).
- Git warns `LF will be replaced by CRLF` on `HeroHealth.cs` and `RaidExitParityRegression.cs` — the
  inserted lines are LF in two CRLF files. Harmless to the compiler and to both brace checks; noted so
  normalization at `git add` is not a surprise.

## 10. Hand-off

- Lead: gate the combined tree (`COMPILE_GATE_OK`, `RAID_EXIT_PARITY_OK`, `REGRESSION_OK <n>/<n>`),
  regenerate `BOARD.html`, commit by explicit path — the four files above and this WO pair.
- PO: felt-verify §7 items 1–3 and close.
