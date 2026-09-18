# WORK ORDER 1736 — a wave ends and the HUD never returns to the peaceful dock (external player "Sminer", Wave 146)

**Status:** DONE - committed 6d2ae71cc, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW

⭐ **THE HOLDER IS NAMED, FROM A CAPTURED DEVICE WINDOW, AND FIXED. See §14 (2026-09-17).**
It is **not** a `BattleLock` probe. It is **`HudContextEvaluator.IsWaveActive()`'s Countdown branch**:
an **endless wave parked awaiting the player's DEFEND press** holds `CountdownRemaining` at **0.0**, and
the imminent test `0.0 <= 5f` is **TRUE**, so a wave that is not counting down at all read as
*permanently imminent* and pinned `HudContext.Battle`. Captured on the owner's Seeker for **34m30s /
186 consecutive samples** with `battleLock=False pursuit=False sceneCombat=False` — **the sole holder.**
⚠ **This CORRECTS §2.3 and §2.4, which ranked both wave-sourced inputs OUT.** §9.0's build question is
now **moot for the RCA** (it stands only for the ship decision): the defect is live at HEAD.

⚠ **THE BOARD'S DRIFT FLAG ON THIS TICKET IS A FALSE POSITIVE — checked at source 2026-09-17.** The
named files were all touched after the mint date, but by **other tickets**: `hud-areas.json` by
`29e0cc7fd` (WO-1824/1825), `PostureSignals.cs` by `732b4584a` (WO-1802), `HudContextEvaluator.cs` and
`EndStateView.cs` by `1fac4dde3` (WO-1705), `WaveManager.cs` by `bd9ddfb45` (WO-1773) and `72c573ed3`
(WO-1163), `EndStateVM.cs` by `74d5f1e64` (WO-1810), `HudModelProducers.cs` by `72c573ed3`. The **only**
WO-1736 commit on any of them is `dbe6b9544` (§12's arm). **This ticket is NOT stale and NOT already
resolved.**

**Minted:** 2026-09-15 — banner bumped 1736 → 1737 in the SAME edit (`CLI_LANES_WO_NUMBERS.md`)
**Silo:** HUD posture / wave loop quiescence
**Severity:** P0-felt — the game is unplayable between waves; the only recovery the player found is quitting the app
**Distribution:** ⚠ **Google Play closed-tester track** (see §0)

---

## 0. ⚠ READ THIS FIRST — THIS IS THE PROJECT'S FIRST EXTERNAL PLAYER BUG REPORT

This did not come from the owner, a test seat, or an F8 capture. It came from a **real external
player**, "Sminer", who reached the owner on **Discord** to ask whether the fault was his.

It is not his fault.

Three consequences that change how this ticket is handled:

1. **A fix must reach the Play closed-tester track**, not just `dev`. A local exe or a Seeker
   sideload does not reach him. The ship chain is CLAUDE.md §16 (`tools\r2-ship.ps1`, marker-judged)
   plus a Play upload — a new version code (memory `dapp-store-version-codes-are-burned-on-upload`).
2. **We cannot reproduce his state.** He is at **Wave 146**; see §6 for what that costs us and
   whether any dev path can fast-forward the wave counter.
3. **He should get an answer.** The owner may want to tell him it is a known bug, that it is not his
   doing, and that there is no in-game way out today. That reply is the owner's to send, not ours.

---

## 1. THE REPORT — verbatim

Player **Sminer**, Discord, 8:15 AM, quoted exactly as written:

> *"Wave 146 but every time a Wave is to end the Fight Modus are working again.*
> *After the Wave it Change not the Skill Screen to the build/Hero/Harvest Screen.*
> *I must Log Out and by the next Log in click to Upgrade...*
> *Sometimes after that it Change to normal Screen and sometimes not.*
> *Is this my fail? Can i Change the Screen Form Fight to normal Modus in another way?"*

He is a **German speaker writing in English**. "Modus" = mode; "Fight Modus" = combat mode; "by the
next Log in" = on the next login. Read as:

> **When a wave ends, the HUD stays in combat mode instead of returning to the peaceful town HUD
> (build / hero / harvest). His only recovery is logging out and back in, and even that only works
> sometimes.**

**From his screenshot** (the owner's relay — treat as the owner's reading, not a pixel we measured):
hero **Thrain Lv 45**, **Wave 146**, `Echoes 6/6`, `Heartfire 3/3 (raids)`, and a **"Start Wave"**
button visible on screen.

---

## 2. THE RCA — every claim cited, read at source 2026-09-15 on `dev`

### 2.1 What is supposed to put the peaceful dock back

The bottom bar the player touches is the **adaptive peaceful dock** (CLAUDE.md §7), and which
widgets exist at all is **data**, not an `if`. `HudKitController.ApplyPosture`
(`Assets/_Modules/HUD/Kit/HudKitController.cs:5319`) activates whatever the posture's occupancy row
in `Assets/Resources/Data/Canonical/hud-areas.json` names.

Posture comes from one pure function,
`PostureEvaluator.Derive` (`Assets/_Modules/HUD/Kit/PostureEvaluator.cs:129-145`), over four inputs
gathered in `Evaluate` (`:86-100`).

### 2.2 THE DISCRIMINATOR — his screenshot rules out half the candidates

Read out of `hud-areas.json` (parsed 2026-09-15):

| posture | `actionBar` | `status` | queue chips / resource chips |
|---|---|---|---|
| `calm(town)` | **`peacefulDock`** | `compass`, **`waveBlock`** | `queueStatusChip`, `collectorsChip`, `defenseReportChip`, `resourceChipsCollapsed` |
| `hostile(prebattle)` | **`combatDock`** | `compass`, **`waveBlock`** | *(none — `actionRail` is empty)* |
| `hostile(activebattle)` | **`combatDock`** | `compass`, **`waveBlock`** | *(none — `actionRail` is empty)* |
| `hostile(postbattle)` | **— the row is EMPTY —** | — | — |
| `modal` | **— the row is EMPTY —** | — | — |

**`hostile(postbattle)` and `modal` occupy NOTHING.** A player in either sees no dock, no wave block,
no heart status — a bare screen. Sminer sees a **Start Wave button** and his **Heartfire / Echoes**
chips. So he is **not** in postbattle and **not** in modal.

He is in **`hostile(prebattle)` or `hostile(activebattle)`**: `waveBlock` survives (his Start Wave
button), and `actionBar` holds the **`combatDock`** — the "Skill Screen" he is describing — in place
of the `peacefulDock` that carries build / hero / harvest. **His words map onto the occupancy table
exactly.**

### 2.3 Which input is latched

`Derive` reaches those two postures through:

- `hostile(activebattle)` ← `context == HudContext.Battle` ← `combat`, computed at
  `HudContextEvaluator.cs:100-106` as
  `sceneCombat || IsWaveActive() || BattleLock.IsInBattle() || PostureSignals.PursuitActive`.
- `hostile(prebattle)` ← `pursuitActive || manualLock` (`PostureEvaluator.cs:138`).

Taking them one at a time:

- **`PursuitActive` — EXCLUDED as a permanent latch (PROVEN).** Pursuit is pulse-based and
  self-expiring: `PursuitTtl = 1.5f` (`PostureSignals.cs:36`) and both `PursuitActive` (`:139-146`)
  and `PursuitCount` `Prune()` as they read (`:148-158`). It cannot hold a posture for minutes.
- **`manualLock` — EXCLUDED as a permanent latch (PROVEN).** It reads `hm.Target.Locked`
  (`PostureEvaluator.cs:94`), and `TargetProducer.Poll` clears the model the moment the target is
  null, dead, or not an `Enemy`: `if (_hadTarget) { … Model.Target.Clear(); }`
  (`HudModelProducers.cs:471`). Wave enemies die; the lock clears with them.
- **`sceneCombat` — EXCLUDED (PROVEN).** `HubScenes.SceneDeclaresCombat(sceneName)` is
  `IsRaid(sceneName) || IsTownPractice(sceneName)` — one line, `HubScenes.cs:200`. The home hub is
  neither, and he is in the hub (he can reach Upgrade). Constant false for his whole session.
- **`IsWaveActive()` — EXCLUDED BY HIS OWN SCREENSHOT (PROVEN). See §2.4.**
- **`IsWaveActive()` and `BattleLock.IsInBattle()` — BOTH REDUCE TO THE SAME STATE FIELD.**
  `IsWaveActive()` returns true when `wm.Phase == WavePhase.Active`
  (`HudContextEvaluator.cs:176-190`). The wave's own battle-lock probe, registered at
  `WaveManager.cs:766`, is the lambda built at `:765`:

  ```
  _waveBattleProbe = () => isActiveAndEnabled && Instance == this && _phase == WavePhase.Active;
  ```

  **One field — `WaveManager._phase` — feeds both combat inputs.** Neither has a TTL, a ceiling, or
  a watchdog. **But see §2.4: his screenshot excludes it.**

### 2.4 ⛔ THE "START WAVE" BUTTON EXCLUDES THE WAVE-PHASE LATCH — READ THIS BEFORE RANKING

> ## ⛔ SUPERSEDED 2026-09-17 — THIS SECTION'S CONCLUSION IS WRONG. READ §14 FIRST.
> The reasoning below excludes **both** wave-sourced inputs from a visible Start Wave button. A captured
> device window (§14.2) shows the button's phase — `Countdown` — reporting **Battle** anyway, because
> `IsWaveActive()` has a **second branch** this section never reads. The button and the combat dock
> appear **together**; §2.4 calls that impossible and it is in fact the signature. Kept unrewritten
> because the mis-step is instructive: the citation in §2.3 described one branch of a two-branch method.

An earlier draft of this RCA ranked `_phase` latched at `Active` as the top candidate, reasoning that
`waveBlock` is occupied in the hostile postures so his Start Wave button was consistent with it.
**That was wrong, and his screenshot is what disproves it.**

Occupancy of `waveBlock` is not the same as the button being *offered*. `StartWaveHudBridge.Push`
(`Assets/_Modules/Village/Waves/StartWaveHudBridge.cs:139-151`) decides that, and it is a plain
switch on the phase:

```
case WavePhase.Countdown:
case WavePhase.Idle:
case WavePhase.Complete:   available = true;  break;
default: // Active, Breached, Defeated
                           available = false; break;
```

The file's own header says it in words at `:14-15` — *"VISIBLE between waves (Countdown / Idle /
Complete), HIDDEN while a wave is Active."*

**Sminer can see a Start Wave button. Therefore his `WaveManager.Phase` is NOT `Active`. Therefore
`IsWaveActive()` is false, and the wave's own battle-lock probe (`WaveManager.cs:765`, which tests
the same `_phase == WavePhase.Active`) is false too.** Both wave-sourced combat inputs are
**EXCLUDED**.

⚠ One honest caveat: the button's availability is also `&= tutorialComplete` (`:151`), and the label
reads `"Start Now"` during a real countdown vs `"Start Wave"` otherwise (`HudKitController.cs:4201`).
The owner relayed the caption as **"Start Wave"**, which is the Idle/Complete/long-countdown wording.
If a re-read of his screenshot shows **"Start Now"** the conclusion is unchanged (still not `Active`);
only a screenshot with **no wave button at all** would re-open the wave-phase candidate.

### 2.5 The project has ALREADY diagnosed the wave-phase latch once

`WaveManager` carries a purpose-built dump for a stuck `Active` phase, added by **WO-1308**:

- `CheckWavePhaseQuiescence` (`WaveManager.cs:831-853`) — returns a report **only** when a
  `WaveManager` is holding the battle lock through `WavePhase.Active`.
- `DescribeLatchedWavePhase` (`:859-899`) — prints the whole state in one line, opening with:

  > *"the wave loop is LATCHED at phase=Active, which is the whole reason the battle-lock is still
  > held … The question is whether a live wave is really behind it."*

  It names `phase`, `wave`, `awaitingPlayerStart`, `countdownRemaining`, live-enemy count, null
  count, apex-boss liveness, both scene names, the last `SetPhase` transition (with site, frame and
  age) and the last time `Update` reached the phase switch.

**WO-1308 is `CLOSED 2026-09-06 — owner felt-test PASS**
(`WorkOrders/WORK_ORDER_1308_retreat_leaves_battle_lock_held_by_wave_phase.md:3`). Its subject was
*"retreat leaves battle lock held by wave phase"*, and its fix — per that Status line — *"registers
its own `BattleSessionEnd` unwind and drives one `TickActiveWave` at battle end."*

⚠ That fix is keyed to `BattleSessionEnd` — a RAID/battle-session boundary, not a town wave ending.
Per §2.4 the wave-phase latch is **not** Sminer's defect, so WO-1308 is **context, not a suspect**:
it is the precedent proving this codebase produces undecaying combat-input latches, and it is where
the diagnostic dump came from. Do not re-open it on this evidence.

### 2.6 ⛔ THE COVERAGE GAP, AND IT IS ONE GREP

`BattleQuiescenceGate.Arm` — the detector that catches precisely this class of defect, and the thing
that produced WO-1098's capture — **has exactly ONE caller in the entire `_Modules` tree**:

```
grep -rn "BattleQuiescenceGate.Arm" --include=*.cs Assets/_Modules/
Assets/_Modules/Village/Arena/BattleArena.cs:2814
```

**It is armed on an ARENA battle end and NOWHERE ELSE. A town wave ending arms nothing.** So
`CheckWavePhaseQuiescence` — the WO-1308 dump written specifically to diagnose a latched wave phase —
**is registered and never fired on the very boundary it was written for.** This is why a defect this
loud reached us through Discord instead of through F8. **PROVEN.**

---

## 3. RANKED CANDIDATES

> ## ⛔ SUPERSEDED 2026-09-17 — THE HOLDER WAS NONE OF THESE. READ §14.
> Every candidate below is a `BattleLock` probe. The captured window (§14.2) reads
> `battleLock=False pursuit=False sceneCombat=False` for the whole 34-minute latch, so **the entire
> ranking is excluded by measurement** and #1 ("holder UNNAMED") is closed. The real holder is the
> `wave` input. Kept unrewritten per CLAUDE.md §15 — and because §3's own warning that *"the holder
> cannot be named from here"* was correct about the evidence gap while being wrong about where to look.

> ⚠ **THE HOLDER CANNOT BE NAMED FROM HERE, AND THAT IS THE FINDING.** Every combat input with a
> documented self-clearing mechanism is excluded below **by source**. What remains is a `BattleLock`
> probe holding true with no decay — and the single line that would name which one
> (`BattleLock.DescribeHolders()`) is only printed by a gate that **a town wave end never arms**
> (§2.6). We are not guessing between candidates; we have proven that the evidence needed to choose
> between them **is not being collected**. Arming the gate is therefore the fix's first half, not an
> afterthought.

### #1 — a latched `BattleLock` probe, **holder UNNAMED** — **UNPROVEN, and the only surviving fit**

Posture `hostile(activebattle)` via `combat → HudContext.Battle`. It is what is left once §2.4 and
the exclusions below have run, and its profile matches the logout asymmetry (§5): `BattleLock._probes`
is `private static readonly List<Func<bool>>` (`BattleLock.cs:42`) — **process state that survives a
scene reload**, which is exactly what "sometimes a re-login fixes it, sometimes not" requires.

Production registrants, all eight:
`WaveManager.cs:766` (excluded, §2.4) · `PursuitBattleProbe.cs:55` (excluded below) ·
`HeroCombatEngagement.cs:64` (excluded below) · `ATBCombatManager.cs:115` · `ArenaMode.cs:132` ·
`BattleArena.cs:334` · `OwnedTownPracticeController.cs:59` · `TutorialWaveSpawner.cs:198`.
Plus the second, separate battle-lock claim `Enemy.OnDisable` releases at `Enemy.cs:1097+` (WO-1337).

**The remaining five are arena / ATB / town-practice / tutorial owners.** None should be live for a
player 146 waves into town — which makes a stale registration from an earlier session-phase (an
arena visit, the tutorial) the shape to look for. **NOT PROVEN; do not fix one on suspicion.**

**The ONE line that settles it:** `BattleLock.DescribeHolders()` (`BattleLock.cs:85`), which names
the holding delegate. **It is called only on a quiescence FAIL, which never fires on a wave end
(§2.6).** Arming that gate is acceptance criterion 1 precisely because it converts this UNPROVEN into
a named holder on the very next occurrence.

### #2 — `PursuitBattleProbe` holding the lock — **EXCLUDED (PROVEN)**

`PursuitBattleProbe.Probe` (`Assets/_Modules/Core/Combat/PursuitBattleProbe.cs:59-61`) is a pure
read-through: `bool active = PostureSignals.PursuitActive;` … `return active;`. It inherits the
1.5 s TTL and cannot latch. Its header says so at `:20-21` ("natural hysteresis … never flickers").
It is a *messenger*, and WO-1603's comment at `:64-70` records that it was once mis-reported as the
holder for exactly this reason — **do not repeat that mistake.**

### #3 — `HeroCombatEngagement` token never released — **EXCLUDED for a town wave enemy (PROVEN)**

A `HashSet<object>` static (`HeroCombatEngagement.cs:54`) whose `PruneDead` only drops *destroyed*
Unity objects (`:91`), so a **pooled** enemy (wave enemies come from `EnemyPool.Get`,
`WaveManager.cs:703`) could not be pruned — a promising mechanism. **It is excluded twice over:**

1. `Enemy.UpdateHeroCombatEngagement` (`Enemy.cs:1300-1310`) gates the token on `_heart == null` and
   says why in-line at `:1302-1303`: *"Only heart-less duelists (the OutpostEnemyGroupSpawner hollows
   and arena orcs) can ever hold this lock — **a heart-siege wave enemy or a plain overworld roamer
   never does**."* Sminer's wave enemies siege the heart.
2. `Enemy.OnDisable` (`:1082-1095`) releases the token on every exit and names pool release
   explicitly: *"OnDisable runs before OnDestroy and on pool release — covers all exits."*

Keep it excluded unless a capture shows `engagedCount>0` in town.

### #4 — `WaveManager._phase` latched at `Active` — **EXCLUDED by his screenshot (PROVEN, see §2.4)**

Ranked #1 in the first draft of this RCA. The Start Wave button he can see is pushed only for
`Countdown / Idle / Complete` (`StartWaveHudBridge.cs:139-151`), so the phase is not `Active`.
Recorded rather than deleted so the next reader does not re-derive it: it is the most *plausible*
candidate and it is wrong.

### #5 — `HudPostureReset.OnCombatEnded()` is unreachable while the latch is held — **PROVEN, and it is an AMPLIFIER not the cause**

`HudContextEvaluator.cs:135`:
```
if (_pushedOnce && _combat && !combat) HudPostureReset.OnCombatEnded();
```
The reset fires **only on the falling edge of `combat`**. Any latched combat input means `combat`
never falls, so the reset **never runs**. And what it would have done
(`HudPostureReset.cs:32-51`) is clear pursuits, release the target lock and clear the HUD target —
**it does not clear `BattleLock`, does not touch `WaveManager._phase`, and closes no panel.** So even
if it did run it could not break candidate #1 (a latched `BattleLock` probe). **The recovery path is structurally incapable of
recovering from the failure it is positioned to catch.** Fix the holder; do not "fix" this by
loosening the edge test.

### #6 — `PostureSignals.EndStateVisible` latched true — **EXCLUDED by his screenshot; REAL BUG ANYWAY, LOG IT SEPARATELY**

Ruled out as *his* symptom by §2.2: `hostile(postbattle)` occupies nothing and he sees widgets.

**But the latch is real and reachable, and it should not be lost.** `EndStateView.OnDestroy`
(`EndStateView.cs:2908-2933`) releases the world hold **unconditionally** (`:2921`) but restores the
posture **only inside `if (_open == this)`** (`:2923-2932`). `Show` destroys the previous view and
nulls the static at `:108-109`, then does ~80 lines of canvas/panel construction before re-pointing
`_open = view` at `:192`. `Destroy` is deferred to end of frame, so **if anything between `:109` and
`:192` throws, the old view's `OnDestroy` runs with `_open == null`, `SetEndState(false)` is skipped,
and `EndStateVisible` is latched true with no end-state on screen** — a bare HUD and no way out.
The `EndStateView.Show(...)` call at `WaveCelebrationManager.cs:443` is **not** wrapped in `Guard`.

Honest constraint: that latch **self-heals on the next completed wave clear** (the next `Show` sets
`_open = view`, and its `OnDestroy` then runs the restore), so it predicts a one-wave stuck window,
not Sminer's every-wave symptom. It is a separate, smaller ticket — see §8.

### #7 — `PrimaryGate` blocks the anti-softlock dismiss — **EXCLUDED (PROVEN)**

`FirePrimary` returns early on a false gate *without* setting `_fired` and *without* destroying
(`EndStateView.cs:2698`), and the non-hold branch of `AutoDismissAfter` is one-shot (`:2775-2777`) —
so a gate that reads false would strand the modal forever.

**It cannot happen: `PrimaryGate` is DECLARED (`EndStateVM.cs:113`) and READ (`EndStateView.cs:2698`)
and ASSIGNED NOWHERE in the tree.** Verified: `grep -n "PrimaryGate" Assets/_Modules/Village/UI/EndState/*.cs`
returns exactly those two lines. Excluded.

### #8 — the auto-dismiss timer stalls at `timeScale 0` — **EXCLUDED (PROVEN)**

`AutoDismissAfter` is unscaled on both branches: `WaitForSecondsRealtime(window)` (`:2775`) and
`waited += Time.unscaledDeltaTime` (`:2792`), with the reason written in-line at `:2790-2791`. A
frozen clock cannot strand it.

---

## 4. THE NEIGHBOURS WE WERE ASKED TO CHECK

### F8 seq 5092 — related mechanism, **NOT** evidence of this defect

`logs/f8-inbox/capture-device-20260914-092335-seq5092.md` (device `SM02G4061955851`, the **owner's**
device, not Sminer's):

> `[Flow:WaveCelebration] WAVE-CLEAR SLOW-MO LEAK RECOVERED: our 0.28 dip ran 0.36s past its deadline
> and its ease-back never completed. The world-clock hold has been released; live holds now
> [wave-results], timeScale 0.00.`

Stack: `WaveCelebrationManager:SweepDeadline()` (`WaveManager`-adjacent, `WaveCelebrationManager.cs:303`).

**Do not over-read it.** `[wave-results]` is the hold the wave-clear end-state legitimately takes:
`EndStateVM.FromWaveClear` sets `HoldWorld = true` (`EndStateVM.cs:749`) and `EndStateView.Show`
acquires `WorldHold.AcquirePlayerOwned("wave-results", …)` (`EndStateView.cs:211-212`). A
player-owned hold at scale 0 **while the results modal is up** is the designed state, not a fault.
5092 shows the celebration's own `fx:wave-clear-dip` leaking past its deadline and being recovered —
a real defect in its own right, already self-reported — and it confirms the wave-end path is
contended. It does **not** show a stuck HUD, and it is from the wrong device.

### WO-1098 — same family, different holder, and its own table says so

`WorkOrders/WORK_ORDER_1098_arena_win_leaves_timescale_zero_and_harvest_modal_open.md` —
**Status: IMPLEMENTED `f4e4630e3` on HEAD 2026-09-09**. Its capture was
`BATTLE_QUIESCENCE_FAIL (arena win)` with two unrestored invariants (`timeScale 0.00`, a still-open
`Harvest Result` panel). Its own finding is the one that matters here:

> *"Every one of those fixes repaired **a named holder** … what it keeps detecting is that
> **restoration is not authoritative** — each new holder is a new [ticket]."*

**Sminer's report is that thesis arriving from outside the building.** But 1098's specific holders are
excluded for him: a stuck panel handle would give `HudContext.Modal` → posture `modal` → the **empty**
occupancy row (§2.2), and he has widgets. And a stuck `timeScale` would freeze the world, which he
does not report — he keeps clearing waves.

**Which of the three is it? §3 #1 — a latched `BattleLock` probe. NOT a modal, NOT `timeScale`, NOT
the wave phase. That much is settled by the occupancy table (§2.2) and the Start Wave button (§2.4);
WHICH PROBE is NOT PROVEN, and §2.6 explains why it cannot be from here.**

---

## 5. THE LOGOUT/LOGIN ASYMMETRY, AND "SOMETIMES"

**The mechanism class is PROVEN; the specific holder is NOT.**

**And this is a second, independent reason the surviving candidate is a `BattleLock` probe and not
the wave phase.**

- `BattleLock._probes` is `private static readonly List<Func<bool>>` (`BattleLock.cs:42`) —
  **process state. It survives a scene reload.** `PostureSignals` is likewise a `static class`
  (`PostureSignals.cs:34`) whose header says in as many words *"A Core static cannot go stale."*
- `WaveManager._phase`, by contrast, is **instance state on a scene-baked object** (`:424`). Only the
  `Instance` *pointer* is static (`:737`); the phase it points at is not. A logout that reloads the
  scene therefore yields a fresh manager in `Idle` — so **if the wave phase were the holder, a
  re-login would fix it EVERY time.** He says it only sometimes does. That is evidence *for* a static
  holder and *against* the wave phase — converging with §2.4, which excluded it on different grounds.

So the asymmetry reduces to **what a "Log Out" actually tears down**:

- if it **reloads a scene** → the statics survive → the latch survives → *"and sometimes not"*;
- if it **kills the process** → the statics are gone → *"sometimes after that it Change to normal
  Screen"*.

⛔ **NOT PROVEN, and it is a required step:** we did not find the player-facing Log Out button. A grep
for `LogOut|SignOut|Log Out|Sign Out` across `Assets/_Modules/` returns only
`FirebaseAuthService.SignOut` (`:231`, `:359`) — the auth call, not the UI route or whatever scene
transition follows it. **The implementer must find that button and read what it does before claiming
this explanation.** If it reloads the hub scene, `HudPostureReset.OnHubLoaded` (`HudPostureReset.cs:27`)
runs — and per §3 #3 it clears pursuits and the target lock and **nothing else**, which is consistent
with a re-login that does not always help.

A second, independent contributor to *"sometimes"*: his workaround is *"click to Upgrade"* — that
opens a `PanelManager` panel, which drives `HudContext.Modal` and forces a posture transition on
close. Whether that re-evaluation can break the latch is **NOT PROVEN** and depends entirely on which
holder is live. Do not build a fix around it.

**What would prove all of this in one read:** the `[Flow:HUD] context inputs: …` line
(`HudContextEvaluator.cs:133`) captured **before** a logout and **after** the next login.

---

## 6. WAVE 146 — is the wave count implicated?

**No — not in the HUD path. PROVEN by reading, with two caveats worth writing down.**

- `WaveCelebrationManager.Significance01(146)` (`:369-374`): `146 % 7 == 6`, so it falls to
  `Mathf.Clamp01(145f / 12f)` = **1.0**. Maximum celebration, and `AutoDismissSeconds = 8f`
  (`EndStateVM.cs:747`). No overflow, no degradation — the function clamps.
- Nothing in `PostureEvaluator`, `HudContextEvaluator`, `PostureSignals` or `hud-areas.json` reads a
  wave number at all. The pursuit ring is a fixed 12 slots (`PostureSignals.cs:41`) and over-capacity
  is handled by an explicit early return (`:96`), not by corruption.

**Caveat 1 — he is in the ENDLESS-CYCLE branch, which is not everyday code.** Wave 146 is past
`_schedule.MaxWaveId`, so his waves are sourced by `cycleStart + (waveId - max - 1) % cycleLen`
(`WaveManager.cs:1787-1792`), with a second ceiling check at `:3473` (`cleared > MaxWaveId`).
**Nobody plays here.** It is not implicated in the HUD latch, but it is the least-exercised wave code
in the game and the implementer should read those two sites once while in the file.

**Caveat 2 — accumulation, flagged not proven:** `_enemyBestSqr` and `_enemyStuckTime`
(`WaveManager.cs:401-402`) are `Dictionary<Enemy, …>` keyed on a Unity object. If entries are not
removed on despawn they grow for the life of the session. **Not measured, not implicated in this
defect — noted so it is not lost.**

---

## 7. ACCEPTANCE CRITERIA — every one requires a CAPTURED LINE

⛔ **"It feels fixed" closes nothing here.** We cannot felt-test his save, and the owner's own device
has cleared waves for weeks without hitting this hard enough to report it.

1. **The wave-end boundary ARMS the quiescence gate.** After the fix,
   `grep -rn "BattleQuiescenceGate.Arm" --include=*.cs Assets/_Modules/` returns the wave-clear site
   **in addition to** `BattleArena.cs:2814`. Arming it is what makes the next occurrence
   self-diagnosing instead of arriving via Discord.
2. **A fresh headless/device log shows the gate PASSING on a town wave end** — the quiescence marker
   on a wave clear, with `wave-phase` among the checks that ran. A log with no gate line is a FAIL,
   not an unknown (CLAUDE.md §11B).
3. **A fresh log shows `combat` FALLING after a wave clear:**
   `[Flow:HUD] context inputs: … wave=False battleLock=False pursuit=False … -> Town`
   within a few seconds of the clear.
4. **A fresh log shows the posture returning:** `[Flow:HudKit] posture hostile(activebattle)->calm(town)`
   (`PostureEvaluator.cs:78-80`), followed by
   `[Flow:HudKit] occupancy applied: posture calm(town)` (`HudKitController.cs:5371`).
5. **A negative test that FAILS before the fix.** Force `WaveManager._phase` to stay `Active` past a
   clear (or register a never-releasing `BattleLock` probe) and assert the gate REPORTS it, naming the
   holder via `BattleLock.DescribeHolders()` (`BattleLock.cs:85`) and/or
   `DescribeLatchedWavePhase` (`WaveManager.cs:859`). A guard that cannot fail proves nothing.
6. **A regression pinning the occupancy discriminator** — that `hostile(postbattle)` and `modal`
   occupy nothing while `hostile(*battle)` carries `combatDock` + `waveBlock`. §2.2 is one of only two
   reason this ticket could be ranked at all, and it lives in a JSON file nothing currently pins.
7. **Ship proof for the tester track:** `R2_PARITY_OK` on a fresh log (CLAUDE.md §16) plus the Play
   upload's version code recorded in the `.RESULT.md`.

### Grep list for a device log (exact strings)

```
[Flow:HUD] context inputs:
[Flow:HudKit] posture
[Flow:HudKit] occupancy applied: posture
[Flow:HudKit] pursuit cleared (posture -> peaceful)
[Flow:HudKit] posture reset (
[Flow:HudKit] end-state SHOWN
[Flow:HudKit] end-state dismissed
the wave loop is LATCHED at phase=Active
BATTLE_QUIESCENCE_FAIL
WAVE-CLEAR SLOW-MO LEAK
[Flow:EndState]
```

---

## 8. NOT IN SCOPE

- **The `EndStateVisible` / `_open == this` latch (§3 #4).** Real, reachable, excluded as Sminer's
  symptom by the occupancy table. **Mint it as its own ticket** — the repair is a `try/finally`
  around `EndStateView.Show` `:109-192` that restores `_open`, and/or making the `OnDestroy` posture
  restore unconditional like the world-hold release beside it. Do not fold it in here; a combined fix
  cannot be proven by a single capture.
- **The `fx:wave-clear-dip` ease-back leak** that F8 seq 5092 self-reports. Same wave-end
  neighbourhood, different invariant (`timeScale`), already recovering itself.
- **`WaveManager._enemyBestSqr` / `_enemyStuckTime` growth** (§6 caveat 2) — unmeasured.
- **Any rework of the peaceful dock's contents.** The dock is not broken; it is never asked for.
  Nothing in this ticket touches `BuildAdaptivePeacefulDock`, `HudDockSlotLayout` or the face set —
  and CLAUDE.md §7 forbids reasoning about the shipped bar from `HudActionBarModel` anyway.
- **Loosening the `_pushedOnce && _combat && !combat` edge test** (§3 #3). That converts a latch into
  a flicker and hides the holder.
- **Answering Sminer.** The owner's call, the owner's words.

---

## 9. REPRODUCTION — the honest obstacle

### 9.0 ⛔ DO THIS BEFORE ANY CODE — WHICH BUILD IS HE PLAYING?

**The cheapest discriminator in this whole ticket, and it is currently unanswered.**

Three tickets in this exact family landed very recently: **WO-1308** closed `2026-09-06`; **WO-1093**
(`_stung` → battle-lock) and **WO-1098** are both marked `IMPLEMENTED … on HEAD 2026-09-09`
(`6a5c7a36d` and `f4e4630e3`). Sminer is on a **Play closed-tester build of unknown vintage**.

**If his build was cut before 2026-09-09, this defect may already be fixed at HEAD and never shipped
to the track.** Writing a fix first would then be inventing a second repair for a solved bug — and
"READY TO IMPLEMENT" on such a ticket is wrong (CLAUDE.md §11B).

Two things to record in the `.RESULT.md` before anything else:

1. **His build's version / version code**, asked for directly (the in-game build stamp, or the Play
   console's tester-track version).
2. **Which commit the CURRENT Play tester track was cut from** — ours to look up, not his.

If the build postdates `09-09`, the ticket proceeds as written. If it predates it, the ticket becomes
a **SHIP** task (§0.1 + acceptance criterion 7) and the RCA below stands as the coverage argument for
arming the gate anyway.

### 9.1 Reproduction — the honest obstacle

**We cannot reproduce his state, and no dev path to it was found.**

A search of the wave path turned up **no cheat, console command or debug entry point that sets the
wave counter**. Wave progression is driven through `SetPhase` / `EnterCountdown`
(`WaveManager.cs:463`, `:1697`, `:1723`) with no external setter surfaced. The AutoPilot fleet
drives waves by *playing* them (`TriggerWave`), which at ~146 waves is not a practical path.

Options, in the order they should be tried:

1. **Ask Sminer for a log.** A Play tester build + `adb logcat` is the cheapest complete answer and
   would settle §3 in one read. Whether that build carries the F8 device bridge is **NOT PROVEN** —
   check before asking him to press anything.
2. **Ask for his save.** §10 says we may not have it.
3. **Synthesise the latch** rather than the wave count — acceptance criterion 5 does exactly this and
   needs no Wave-146 save. **This is the path that does not depend on him.**

A fix shipped without 1, 2 or 3 is a guess (CLAUDE.md §11B).

---

## 10. ⚠ SEPARATE FLAGGED CONCERN — IS HIS SAVE REACHING THE BACKEND AT ALL?

**NOT THIS WORK ORDER'S SCOPE. Recorded here so it is not lost; it needs its own ticket.**

Reported alongside this bug: **the maximum hero level across all 63 saves in the database is 20.**
Sminer is at **Thrain Lv 45**, Wave 146, Echoes 6/6.

Either his saves are not reaching the Neon backend (memory `firebase-auth-neon-architecture`:
Firebase email login, `/api/game/save` → Neon), or they are landing somewhere the query did not look.
**Both readings are serious and neither is proven here.** If it is the first, our only external player
has no cloud save — and every telemetry-based judgement about the tester cohort is being made on
data that does not include the one person actually playing.

**Neither number was verified by this lane** — both are relayed. Verify the DB query and the save
path before acting.

---

## 11. FILES READ (all absolute under the repo root, opened 2026-09-15 on `dev`)

- `Assets/_Modules/HUD/Kit/PostureEvaluator.cs` — `Evaluate` `:86-100`, `Derive` `:129-145`
- `Assets/_Modules/HUD/Kit/HudKitController.cs` — `ApplyPosture` `:5319-5371`, Start Wave `:1805-1808`
- `Assets/Resources/Data/Canonical/hud-areas.json` — the occupancy table (§2.2)
- `Assets/_Modules/Core/HudModel/PostureSignals.cs` — pursuit ring `:36-178`, `SetEndState` `:200-206`
- `Assets/_Modules/Village/HUD/HudContextEvaluator.cs` — `Poll` `:87-152`, `IsWaveActive` `:176-190`
- `Assets/_Modules/Village/HUD/HudPostureReset.cs` — whole file (53 lines)
- `Assets/_Modules/Village/Waves/WaveManager.cs` — probe `:741-766`, quiescence `:820-899`, endless cycle `:1787-1792`
- `Assets/_Modules/Village/Waves/WaveCelebrationManager.cs` — `Significance01` `:369`, `WaveClearRoutine` `:378-451`
- `Assets/_Modules/Village/UI/EndState/EndStateView.cs` — `Show` `:78-217`, `FirePrimary` `:2695-2708`, `AutoDismissAfter` `:2769-2808`, `OnDestroy` `:2908-2933`
- `Assets/_Modules/Village/UI/EndState/EndStateVM.cs` — `PrimaryGate` `:113`, `FromWaveClear` `:733-750`
- `Assets/_Modules/Core/Combat/BattleLock.cs` — `:42`, `:61`, `:85`, `:137`
- `Assets/_Modules/Village/HUD/HudModelProducers.cs` — `TargetProducer.Poll` `:461-471`
- `logs/f8-inbox/capture-device-20260914-092335-seq5092.md`
- `WorkOrders/WORK_ORDER_1098_arena_win_leaves_timescale_zero_and_harvest_modal_open.md`
- `WorkOrders/WORK_ORDER_1308_retreat_leaves_battle_lock_held_by_wave_phase.md` (Status line only)

**Nothing in this ticket was run.** No Unity, no gate, no commit — read-only lane. Every posture,
occupancy and line number above was read at source this session; the two relayed numbers in §10 and
the screenshot contents in §1 were not, and are labelled as relayed.

---

## 12. INSTRUMENTATION LANDED 2026-09-15 — THE GATE IS NOW ARMED ON THE WAVE-END BOUNDARY

⛔ **STATUS DELIBERATELY NOT FLIPPED. THE BUG IS NOT FIXED — IT IS INSTRUMENTED.** The holder is
still UNNAMED (§3 #1). This lane closed §2.6's coverage gap ONLY, so the next occurrence arrives with
data instead of as a guess. No fix was attempted; CLAUDE.md §12 does not permit one yet.

Proceeding under §9.0's own last clause — *"the RCA below stands as the coverage argument for arming
the gate anyway"*. **His build's vintage is still UNANSWERED** and §9.0 remains blocking for any fix.

### 12.1 The seam armed — ONE call site, 5 lines of code

`Assets/_Modules/Village/Waves/WaveCelebrationManager.cs:500` — inside `WaveClearRoutine`, in the
**`else`** branch, immediately after `EndStateView.Show(EndStateVM.FromWaveClear(waveNumber))`:

```
DeNelle.Core.Diagnostics.Guard.Try("WaveCelebration", "arm town wave-end quiescence gate",
    () => StartCoroutine(DeNelle.Core.Combat.BattleQuiescenceGate.Arm(
        () => DeNelle.Village.UI.EndStateView.IsShowing, "town wave clear")));
```

**Acceptance criterion 1 is MET** — verified by running §7's own grep after the edit:

```
grep -rn "BattleQuiescenceGate.Arm" --include=*.cs Assets/_Modules/
Assets/_Modules/Village/Arena/BattleArena.cs:2814
Assets/_Modules/Village/Waves/WaveCelebrationManager.cs:500
```

**`DescribeHolders()` IS in the dump path — confirmed at source, not assumed.** The gate's own
`Evaluate` prints it inside the battle-lock finding at `BattleQuiescenceGate.cs:214`
(`HOLDER(S): {BattleLock.DescribeHolders()} (of {BattleLock.ProbeCount} registered: …)`), and again
at `:371` after the self-heal. WO-1308's `wave-phase` probe rides the same block as module probe
`4..n` (`:262-272`), so `DescribeLatchedWavePhase` now prints on this boundary too.

### 12.2 WHY `WaveClearRoutine` AND NOT `CompleteWave()` — this choice is load-bearing

`Arm` judges with `Evaluate(rewardScreenOpen: false)`, so it must not settle while a reward screen is
legitimately up. At `CompleteWave()`+0 (`WaveManager.cs:3459`) the banner has **not shown yet** — the
routine yields through its VFX bursts first (`WaveCelebrationManager.cs:404-414`) — so a bare
`IsShowing` probe reads FALSE, the 0.75 s settle runs **through the slow-mo dip**, and the gate emits
a **false `timeScale` FAIL on every clean wave**. That is precisely the firehose the gate's own header
warns is "the fastest way to teach everyone to ignore this gate". `EndStateView.Show` sets `_open`
synchronously (`EndStateView.cs:53`), so armed *after* it the probe is exact and needs none of
`BattleArena`'s pending-or-showing pair.

Armed inside the `else` on purpose: the suppressed branch means an arena battle owns the screen and
`BattleArena.cs:2814` already arms the gate for that end.

### 12.3 Exact strings the next device log will contain

```
BATTLE_QUIESCENCE_OK (town wave clear)
BATTLE_QUIESCENCE_FAIL (town wave clear)
BATTLE_QUIESCENCE_SUPERSEDED (town wave clear)
HOLDER(S):
PURSUIT PULSES:
wave-phase:
the wave loop is LATCHED at phase=Active
battle-lock SELF-HEALED after
battle-lock STILL HELD after the self-heal
```

**⭐ THE ONE LINE THE OWNER SHOULD LOOK FOR IN SMINER'S NEXT REPORT:** `HOLDER(S):` inside a
`BATTLE_QUIESCENCE_FAIL (town wave clear)` block. **That names the latched probe** — the single fact
§3 could not establish from source, and the thing that converts #1 from UNPROVEN into a fix.

### 12.4 Flood proof

`Arm` writes no per-frame log — its wait loops only `yield return null`. A **clean** wave costs exactly
**one** `FlowTrace.Step` (`BATTLE_QUIESCENCE_OK`), ~9 s after the clear (the banner's 8 s
`AutoDismissSeconds`, `EndStateVM.cs:747`, plus the 0.75 s `SettleSeconds`). Only a genuine latch is
loud: one `Fail` block plus at most two heal lines. Memory `logcat-ring-buffer-destroys-evidence`.

The per-second context line (§5) was **not duplicated**: `HudContextEvaluator.cs:133` is
`FlowTrace.Throttle("HUD","ctx-eval",1f,…)` and runs continuously, so the `combat` transition is
captured within one second of the clear on its own. No edge line was added.

### 12.5 ⚠ TWO RISKS THE LEAD MUST WEIGH BEFORE THE AAB — stated, not buried

1. **Arming `Arm()` connects WO-1233b's SELF-HEAL to the wave boundary.** On a **FAIL only**, the gate
   re-drives `BattleSessionEnd.Release` (`BattleQuiescenceGate.cs:349-355`) and may close a *ghost*
   panel handle. It no longer writes `Time.timeScale` (WO-1353, `:440`). This is pre-existing shipped
   behaviour of the gate the ticket asked us to reuse, and it always **reports first** — the `Fail`
   line with `DescribeHolders()` is emitted before any heal, so evidence is never lost. But it is a
   behaviour change on a clean-wave-end path, and "instrumentation only" is the brief. **If the lead
   judges that unacceptable for an external tester, delete the 5-line call — nothing else depends on it.**
2. **`BattleSessionEnd.Epoch` is bumped by `Begin`, whose ONLY caller is `BattleArena.cs:489`
   (an arena encounter staging) — NOT by a town wave going Active.** So the gate's SUPERSEDED guard
   does not cover a player tapping Start Now inside the 0.75 s settle.
   ⚠ **A CORRECTION TO THIS LANE'S OWN FIRST DRAFT, recorded per CLAUDE.md §11B.** This bullet
   originally read *"bumped only by `Release` (`BattleSessionEnd.cs:136`)"*. That was **hearsay**: the
   grep behind it was case-sensitive on `Epoch` and matched the property (`:132`) and a **doc-comment
   fragment** (`:136`) — it could not have matched `s_epoch++`. Re-read at source: the single
   increment is `BattleSessionEnd.cs:143`, inside **`Begin`** (`:142-146`); `Release` only *prints*
   `s_epoch` at `:210`. The conclusion is unchanged — a town wave start bumps nothing — but it was
   reached from the wrong line, which is exactly the copied-state failure §2 and §5 describe.
   Assessed and rated **small**: `StartWave` spawns **synchronously** in the same method
   that sets `Active` (`WaveManager.cs:1988` then `SpawnSmartComposedWave` at `:2026`), so the forced
   `TickActiveWave` sees live enemies or `_heldSmartReinforcements > 0` and **declines** at the clear
   test (`:3224`) — exactly as `ReconcileLatchedWavePhase`'s own comment at `:960` claims. **Not
   guarded here** (a guard is a condition edit, which this lane is forbidden); recorded so it is not lost.

### 12.6 ⭐ PROOF THE GATE IS NOT WITHDRAWN ON EVERY CLEAN WAVE

**The failure mode that would have made this whole lane inert**, and it is not hypothetical: `Arm`
captures `armedEpoch` synchronously at arm time and withdraws with `BATTLE_QUIESCENCE_SUPERSEDED` if
`BattleSessionEnd.Epoch` changes at any poll over the ~9 s that follow. Had anything on the town
wave-clear cascade begun a battle session, **every wave would emit SUPERSEDED and the gate would never
evaluate** — a marker that looks like coverage and is not.

**Checked, not assumed.** `grep -rn "BattleSessionEnd.Begin(" --include=*.cs Assets/_Modules/` returns
**exactly one** call site — `Assets/_Modules/Village/Arena/BattleArena.cs:489` ("encounter staged").
`grep -rn "BattleSessionEnd.Release(" --include=*.cs Assets/_Modules/` returns **three**, none of them
on the town wave path: `BattleQuiescenceGate.cs:356` (the gate's own self-heal), `BattleArena.cs:2442`
(abandoned) and `BattleArena.cs:2754` (arena win / retreat). The fourth grep hit
(`OverworldEncounterSpawner.cs:1020`) is a comment.

**Verdict: the epoch is untouched by a town wave clear, so the gate ARMS AND EVALUATES — it is not
withdrawn.** (The same fact is why risk 2 above is not covered by SUPERSEDED.)

### 12.7 Does a Play-tester build even emit these lines?

**The markers are live in a release build — `FlowTrace.Enabled` defaults `true` in EVERY build**
(`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:46`, and its header says so in words at `:19-21`:
*"NEVER STRIP THE CALLS … and Enabled DEFAULTS TRUE in every build"*). There is **no `[Conditional]`
attribute and no `#if DEVELOPMENT_BUILD` guard anywhere in the file** (grep returned nothing), and
every `FlowTrace.Enabled = false` write in the tree is under `Assets/Editor/` (regression suites
restoring their own prior value). No runtime `FlowTrace.Apply(` caller exists.

⛔ **What is therefore PROVEN vs NOT.** Proven: the lines will be *emitted* by his build. **NOT
proven: that we can ever *retrieve* them from his device** — that is §9.1 option 1's open question
(whether the tester track carries the F8 device bridge, and whether he can run `adb logcat`), and it
is unchanged by this lane. The instrumentation is a precondition for an answer, not the answer.

### 12.8 What this lane did NOT do

No fix, no new gate, no second dump mechanism, no condition edited, no wave-loop logic touched, no
instrumentation stripped. No Unity, no gate run, no bake, no git. Brace-verified only:
`python tools/gate_brace.py` → `GATE_BRACE_SUMMARY bad=0 of 1` (exit 0); raw count 49/49; 0 NUL bytes;
the added block is ASCII-only.

---

## 13. REGRESSION COVERAGE LANDED 2026-09-17 — §12'S ARM AND §2.2'S DISCRIMINATOR ARE NOW PINNED

⛔ **STILL NOT A FIX.** This lane wrote **no gameplay code, edited no condition, touched no wave
logic and stripped no instrumentation.** It added regression cases only, to the two suites that
already own these subjects. No new suite, no new marker, no `DataRegression.cs` edit — **the suite
count is unchanged.**

### 13.1 Why regressions and not a fix

§12 armed the gate but nothing pinned that it stays armed: `WiringIsPresent`
(`Assets/Editor/Regression/BattleQuiescenceRegression.cs`) linted **only** `BattleArena`, so the
town wave-end arm — the entire deliverable of `dbe6b9544` — could have been deleted or moved with
every gate green. That is the same everything-green-world-broken shape §2.6 describes, one layer up.

### 13.2 `Assets/Editor/Regression/BattleQuiescenceRegression.cs` — two new source-lints

Registered in `Run()` after `TheGateObservesAndDoesNotWriteTheClock`:

- **`TownWaveEndArmsTheGate`** — on `WaveCelebrationManager.cs`: `BattleQuiescenceGate.Arm` is
  present; its reward-screen probe is `EndStateView.IsShowing`; it sits **AFTER**
  `EndStateVM.FromWaveClear` (§12.2 records that ordering as load-bearing — armed before the banner
  shows, the probe reads FALSE, the settle runs through the slow-mo dip, and **every clean wave**
  emits a false `timeScale` FAIL); and it is wrapped in `Guard.Try`.
  ⚠ The `Guard.Try` rule uses a **bounded 300-char window before the arm**, not a file-wide
  `Contains`: the file already guards its deadline sweep at `:344`, so a bare token rule would stay
  GREEN against an unguarded arm — the exact trap WO-1603's producer lint records.
- **`WavePhaseProbeIsRegistered`** — on `WaveManager.cs` (**read-only**, no edit): the module probe is
  still registered and still routes to `DescribeLatchedWavePhase`. This is what makes acceptance
  criterion 2's *"`wave-phase` among the checks that ran"* provable rather than hoped for.

Both are source-lints for the stated reason the sibling cases are: a live wave loop cannot be driven
inside a synchronous editor batch, and `DeNelle.Village` is not referenced from `DeNelle.Editor`'s
runtime side here.

### 13.3 `Assets/Editor/Regression/HudActionBarRegression.cs` — acceptance criterion 6

New case **`CheckPostureOccupancyDiscriminator`** (plus two private helpers, `PostureRow` and
`FirstOccupiedWidget`), run over **both** `hud-areas.json` dual copies:

- `hostile(postbattle)` and `modal` **occupy NOTHING**;
- `hostile(prebattle)` and `hostile(activebattle)` mount **`combatDock`**, never `peacefulDock`, and
  keep **`waveBlock`**;
- `calm(town)` mounts **`peacefulDock`** and never `combatDock`.

⚠ `FirstOccupiedWidget` reads **what is actually occupied** rather than matching a token list: an
area added with an **empty** widget list is still an empty occupancy, and a fixed token list would
miss a future area name. **Proven red, not assumed:** two mutations were run against the live JSON —
giving `modal` an `actionBar`/`combatDock` row reports `combatDock`, and giving `hostile(postbattle)`
an **empty area followed by** an occupied one reports `peacefulDock` (the case a naive
`Contains("actionBar")` rule would pass).

⛔ **No face count, face list or order is asserted** — CLAUDE.md §7 gives that to
`CheckMeasuredPeacefulDock`, measured out of the built tree. This case asserts only **which dock**
each posture row mounts.

### 13.4 Acceptance criteria — honest state

| AC | State |
|---|---|
| 1 — wave end arms the gate | **MET** (§12, `dbe6b9544`) and now **pinned** by `TownWaveEndArmsTheGate` |
| 2 — gate PASSES on a town wave end, `wave-phase` among the checks | **NOT MET** — needs a fresh headless/device log. The probe's presence is now pinned; the passing marker is the lead's run. |
| 3 — `combat` FALLS after a clear | **NOT MET** — fresh log required |
| 4 — posture returns to `calm(town)` | **NOT MET** — fresh log required |
| 5 — negative test that FAILS before the fix | **ALREADY SATISFIED, CITED NOT DUPLICATED.** Its second form — a never-releasing `BattleLock` probe must make the gate NAME its holder via `DescribeHolders()` — is `HolderIsNamedInTheFinding` in `BattleQuiescenceRegression.cs`, which asserts both directions. Its first form (*"fails before the fix"*) is **unsatisfiable while there is no fix**; a second copy of that assertion is a second answer waiting to disagree with the first. |
| 6 — regression pinning the occupancy discriminator | **MET** (§13.3) |
| 7 — `R2_PARITY_OK` + Play version code | **NOT MET** — ship task, lead's, and still gated on §9.0 |

### 13.5 What this lane did NOT do

No Unity process of any kind — **no batchmode, no `CompileGate`, no `DataRegression`, no bake, no
AutoPilot, no git**. Multiple lanes are open and one seat fires Unity. Verified locally only:
`python tools/gate_brace.py` on both touched files → `GATE_BRACE_SUMMARY bad=0 of 2` (exit 0); raw
brace counts 183/183 and 83/83; **0 NUL bytes** in either file.

---

## 14. ⭐ THE HOLDER, NAMED FROM A CAPTURED DEVICE WINDOW — AND FIXED (2026-09-17)

**This section supersedes the ranking in §3 and corrects §2.3 and §2.4.** Nothing here is inferred;
every line below was read out of a device logcat pulled while the owner was playing.

### 14.1 How it was captured

The owner reported live, first-hand: *"after some levels I can't get back to peaceful mode"* — the same
symptom Sminer reported from the Play tester track. Read-only `adb logcat -d` against her Seeker
(**SM02G4061955851**) while she played, no device interaction. Raw pull: 911,386 lines; the
`[Flow:*]` slice is 8,069 lines spanning `09-17 10:19:22` → `13:41:02`.

### 14.2 THE PROVING LINES

Endless **wave 20** cleared and the loop parked:

```
13:05:01 [Flow:Wave] reinforcement drain wave 20: COMPLETE - all 13 held enemy(s) released (source roster 21).
13:05:21 [Flow:Wave] wave 20 payout RECORDED for the clear banner: wood=0 iron=40 food=88 (x1.8 scale)
13:05:21 [Flow:Wave] EnterCountdown(waveId=21) phaseBefore=Active forceSpawn=False
13:05:21 [Flow:Wave] endless wave 21: def=4 countScale=x1.05 awaiting player start
```

The field was **empty**, the drain **COMPLETE**, the phase **Countdown**. Then, from `13:05:21` to
`13:39:51` — **34 minutes 30 seconds, 186 consecutive throttled samples, zero exceptions** — the device
logged, every single second:

```
[Flow:HUD] countdown IMMINENT (0.0s <= 5s) -> counts as Battle
[Flow:HUD] context inputs: sceneCombat=False wave=True battleLock=False pursuit=False
           inVillage=True modal=False buildMode=False scene='Main_Castle_Overworld' -> Battle
```

**`IMMINENT` count in the window: 186. `long-gap` count: 0.** The latch broke only when she pressed
DEFEND herself at `13:39:52` (`ForceBeginNextWave (PLAYER 'Defend!' path) phase=Countdown`).

### 14.3 ⛔ THE HOLDER — AND WHY §3 COULD NOT SEE IT

`battleLock=False`, `pursuit=False`, `sceneCombat=False` for the **entire** window. **`wave` was the
sole holder**, so every candidate in §3 — all of which are `BattleLock` probes — is excluded by this
capture. §3 #1 (*"a latched `BattleLock` probe, holder UNNAMED"*) is **WRONG, and is now closed.**

The mechanism, read at source:

- `HudContextEvaluator.IsWaveActive()` has **TWO** branches. The first is `Phase == Active`. The second
  (`HudContextEvaluator.cs`, the `Countdown` branch) returns `CountdownRemaining <= ImminentThreshold`
  (`5f`) — the owner's 2026-07-08 *"an imminent countdown counts as Battle"* ruling.
- **Endless mode parks in phase `Countdown` with `_countdownRemaining` held at `0f`** and
  `_awaitingPlayerStart` set, waiting for DEFEND (`WaveManager.TryArmEndlessWave`; the design is stated
  in `WaveManager.cs:518-525`).
- So the imminent test evaluates **`0.0f <= 5f` = TRUE, forever.** The test cannot tell *"5 seconds
  until the wave starts"* from *"no countdown is running at all."*

⚠ **§2.3's citation was the hearsay that mis-ranked this ticket.** It recorded *"`IsWaveActive()`
returns true when `wm.Phase == WavePhase.Active`"* — true of the **first branch only**. §2.4 then built
on it: Sminer's visible START WAVE button proves the phase is `Countdown`, therefore `IsWaveActive()` is
false, therefore **both** wave-sourced inputs are excluded. **The second step does not follow.**
`StartWaveHudBridge` offers the button *in* `Countdown`, and the Countdown branch reports Battle *in*
`Countdown` — so the button and the combat dock appear **together**, which is Sminer's screenshot
exactly. §2.4 called that combination impossible; it is the signature.

It also explains §5's *"sometimes a re-login fixes it"* without needing a static: a fresh manager in
`Idle` reads false, one restored into an awaiting-start `Countdown` **re-latches**.

### 14.4 THE FIX — one precondition, five copies of it

`WaveManager.IsAwaitingPlayerStart` already exists as a `public` read-only seam documented *"Read-only
seam for HUD/bot producers"* (`WaveManager.cs:570-575`), and it is the only thing that can distinguish
the two cases. Each consumer now requires a countdown that is **actually running**:

| file | what it drives | proven? |
|---|---|---|
| `Assets/_Modules/Village/HUD/HudContextEvaluator.cs` | the HUD posture — `combatDock` vs `peacefulDock` | **PROVEN by §14.2** |
| `Assets/_Modules/Village/HUD/HudModelProducers.cs` | the wave model's published `imminent` flag | same predicate, fixed by inspection |
| `Assets/_Modules/Village/Hero/HeroLocomotion.cs` | the hero's braced combat idle | same predicate — this is the *stance* half of "can't get back to peaceful mode" |
| `Assets/_Modules/Village/NPCs/AmbientNPC.cs` | every townsfolk NPC's combat behaviour | same predicate, fixed by inspection |
| `Assets/_Modules/Village/Audio/BattleMusicManager.cs` | battle music over a peaceful town | same predicate; its own header already records the seq-2251 defect of this shape |

⛔ **`WaveManager.cs` WAS NOT EDITED** (WO-1835 holds that file). The seam it already exposes was read,
nothing more. ⛔ **The threshold was NOT touched** — a real 4.9 s countdown must still read as Battle;
raising or deleting it would trade this latch for a wave that arrives with no warning.

`HudContextEvaluator` also now traces the parked case distinctly, so the next reader sees it named:
`countdown PARKED awaiting the player's DEFEND press (0.0s, endless mode) -> gated OUT of Battle`.

### 14.5 Regression

`ParkedCountdownIsNotImminent` in `Assets/Editor/Regression/BattleQuiescenceRegression.cs` pins **all
five** consumers plus the seam: each must read its threshold **and** `IsAwaitingPlayerStart`. Pinned as
an invariant rather than as one fix because the predicate is copied five times — a guard restored in one
file and lost in another is the duplicated-state failure CLAUDE.md §2/§5/§16 each describe.
`WaveManager.cs:518-525` documents the parking design and names the HUD consumers it believed were safe:
it lists the DEFEND button and the wave-timer label, and **misses all five of these.**

### 14.6 Acceptance criteria — revised

AC-2/3/4 are now **testable and expected to pass**, and AC-3 is the direct oracle: after a clear the
context line must read `wave=False`. **The lead's headless/device run is still required** — no Unity
process was run by this lane. AC-5's *"fails before the fix"* is now satisfiable for real: revert any one
row in §14.4 and `ParkedCountdownIsNotImminent` names that file.

⚠ **AC-7 unchanged and still open**: Sminer needs this on the **Play closed-tester track**, and §9.0's
build-vintage question survives **for the ship decision only** — the RCA no longer depends on it, since
the defect is live at HEAD and was captured on the owner's own current build.
