# WORK ORDER 1714 - HeroLocomotion input frozen for 18+ seconds while the HUD context reads modal=True

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:19:01, build 2026.09.17.373943). PRIOR STATUS: FIXED - DialogueGateState seam (WO-795/combat/builder truces release immediately) + a 5s invisible-hold watchdog backstop + BreakCaptureHarness suppression whitelist bounded at 30s; COMPILE_GATE_OK 09:56, REGRESSION_OK 525/525 10:01 including new [dialogue-input-gate] 6/6; PO felt-verifies and closes
(PROVEN combat softlock: 35.5s frozen mid-wave, no timeout, F8 harness structurally blind to it.
Implementation lane 2026-09-14 shipped BOTH fix shapes — a Core truce seam
`DeNelle.Core.Dialogue.DialogueGateState` that releases the input gate the moment the HUD reports a
dialogue hidden, AND a bounded 5s no-visible-panel/no-known-truce backstop with a loud `FlowTrace.Fail`;
plus a 30s bound on `BreakCaptureHarness`'s suppression whitelist so this class can self-report. New
suite `DialogueInputGateRegression` (`DIALOGUE_INPUT_GATE_OK`, 6 cases), registered at
`DataRegression.cs:1236`. Edit-only: NOT compiled, NOT gated, NOT committed by the lane. See
`WorkOrders/WORK_ORDER_1714_hero_input_frozen_while_modal_flag_stuck.RESULT.md`.)
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from a live device pull while the owner was
felt-testing, immediately after she reported "something happens in combat where my movement freezes"

## 1. Owner report, verbatim

> "something happens in combat where my movement freezes" ... "i flagged it"

The lead pulled a live screencap and logcat instead of asking the owner to describe more or check
Unity herself (CLAUDE.md section 14, owner is never the bug detector).

## 2. What is PROVEN this session, captured directly off the device (not inferred)

- F8 capture seq=5090, `kind=flagged`, scene `Main_Castle_Overworld`, device utc
  `2026-09-14T14:05:44Z` - the owner pressed the on-screen FLAG button.
- Screenshot at that moment: `docs/handoffs/movement_freeze_modal_stuck.png` - hero standing still in
  town, a "The Night Market / FLAG" panel open top-left, Wave 1 counting down ("Next wave in 7s",
  Start Now button present, not yet started).
- Full logcat pulled live: `docs/handoffs/movement_freeze_logcat_2026-09-14.txt`. From local time
  09:06:19 through 09:06:37 (18+ continuous seconds, the window this pull happened to cover - the
  freeze may have started earlier, at or before the 09:05:44 flag press), **every single frame**
  repeats:
  - `[Flow:HeroOwner] ... ownerAgent=on-mesh scriptedMove=off velSelf=0.00 velRoot=0.00 ...
    inputSuppressed=True autoWalk=False ... pos=(-0.77, 0.08, -28.01)` - position and velocity
    completely static, `inputSuppressed=True` on every line.
  - `[Flow:HUD] context inputs: sceneCombat=False wave=False battleLock=False pursuit=False
    inVillage=True modal=True buildMode=False scene='Main_Castle_Overworld' -> Modal` - the HUD
    context classifier reads `modal=True` on every one of the same frames.
  - The instant `modal` flips to `False` (`-> Town`), in the SAME frame `inputSuppressed` flips to
    `False` and the hero's `animFeed` source changes from `velRoot(measured,suppressed)` to `velSelf`
    - movement resumes immediately.
- **Mechanism is proven, not guessed:** something sets the HUD's modal flag while the on-screen
  FLAG/Night-Market panel (or whatever else was open) is up, and for as long as it stays true,
  `HeroLocomotion` suppresses all movement input with **no visible timeout or escape** in this capture
  - it took at least 18 continuous seconds, possibly longer before the pull started, and only cleared
  when the modal state itself cleared.

## 3. What is NOT proven - instrument before fixing (CLAUDE.md section 12)

- **This specific capture is NOT during active combat.** `sceneCombat=False` and `wave=False` on every
  line in the captured window; Wave 1 had not started. The owner described the symptom as happening
  "in combat" - this capture proves the FREEZE MECHANISM (modal-gated input suppression with no
  failsafe), not that it fired during a wave. Whether the same mechanism can trigger `modal=True` to
  stick during `sceneCombat=True` is the open, load-bearing question - if a similar panel can pop or
  fail to clear mid-wave, this is a real combat softlock risk, not just a town-UI annoyance.
- What exactly set `modal=True` at the start of this episode - the FLAG button's own UI (a confirmation
  toast/panel), the "Night Market" storefront label visible in the same screenshot, or something else
  entirely. Do not assume it was the FLAG press itself; the F8 harness's on-screen debug button is not
  necessarily part of normal play, so confirm the trigger is something a normal player can also open
  before treating this as a general-audience bug (as opposed to an F8-harness-specific one).
- Whether `HeroLocomotion`/the input-suppression system has ANY timeout, watchdog, or player-facing
  escape (a "tap to dismiss and resume" affordance) once modal has been stuck for an unreasonable
  duration - if not, that is the core defect regardless of what set the flag.
- Four "STEP-STUCK" tutorial-watchdog captures fired ~14 minutes earlier in the same session (seq
  5081-5084, `founding_defense`/`founding_timers`/`founding_defend`/`founding_win`, each reporting
  "excluded (builder/frozen/modal) 0s of which modal 0s" in its own accounting) - these explicitly
  report ZERO modal-exclusion time, so they are NOT proven to be the same defect. Treat as a separate,
  already self-resolving watchdog behavior (each was "RESCUED via watchdog and recorded as SKIPPED")
  unless the RCA finds a connection.

## 4. Acceptance criteria

- [ ] RCA lane finds and cites the exact code path that sets the HUD context's `modal=True` in this
      episode (which panel, which flag/state variable).
- [ ] RCA lane proves or disproves whether the same class of stuck-modal can occur with
      `sceneCombat=True` - if a headless/code-path check is feasible, do it; otherwise say exactly what
      device repro would prove it.
- [ ] RCA lane checks whether `HeroLocomotion`'s input suppression has any existing timeout/escape and,
      if not, states that plainly as the root defect to fix regardless of the specific trigger found.
- [ ] Fix (separate lane once cause is proven) ensures modal-gated input suppression cannot exceed a
      bounded duration without a player-facing way out (either the panel auto-closes, or input
      suppression itself times out and warns loudly per CLAUDE.md section 12's no-silent-failure rule).
- [ ] Headless or capture-based regression proving a stuck modal cannot freeze movement indefinitely.

## 5. What NOT to touch

- Do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` -
  unrelated, already shipped today.
- Do not conflate this with the WO-1705 owned-town queue-naming finding or WO-1713's Crystal Mine
  rotation bug - separate tickets, separate evidence.

## RCA 2026-09-14 (read-only lane)

Read-only lane, branch `dev` @ `23ee34129`. No `.cs`/`.unity`/scene edits, no gate, no commit. Every
claim below is either PROVEN with a file:line or a log:line read this session, or marked NOT PROVEN.

### 0. Headline - the ticket's central attribution is wrong, and the real finding is worse

**`modal=True` is a CORRELATE, not the cause. `HeroLocomotion.InputSuppressed` never reads
`PanelManager` at all.** PROVEN: `grep -rn "InputSuppressed" Assets --include=*.cs` returns exactly
five assignments to the property, all in `Assets/_Modules/Village/Hero/HeroLocomotion.cs` -
`:936` (OnDestroy clear), `:953`/`:961` (HookDialogueGate reconcile), `:971` (`OnDialogueStarted`),
`:978` (`OnDialogueEnded`). The property is declared at `HeroLocomotion.cs:315` and its only writers
are the two `DeNelle.Core.Dialogue.DialogueService.Started` / `.Ended` handlers subscribed at
`HeroLocomotion.cs:945-946`. There is no `PanelManager` reference in that file's suppression path.

**The freeze IS a dialogue-suppression freeze, and it PROVABLY occurred DURING ACTIVE COMBAT.**

### 1. The captured sequence, line by line (`docs/handoffs/movement_freeze_logcat_2026-09-14.txt`)

| log line | local time | event |
|---|---|---|
| `:1198` | 09:06:01.318 | `inputSuppressed=False`, `animFeed=velSelf` - hero free |
| `:1488` | 09:06:02.354 | `[Flow:Dialogue] Play 'tut_ctx_talents'.` |
| `:1489` | 09:06:02.354 | `[Flow:Dialogue] opened during combat -- starting hidden at current line; resumes after combat.` |
| `:1519` | 09:06:02.403 | `inputSuppressed=True`, `animFeed=velRoot(measured,suppressed)` - **freeze begins** |
| `:1526` | 09:06:02.417 | `context inputs: ... wave=True battleLock=True ... modal=False -> Battle` |
| `:1526-3001` | 09:06:02 - 09:06:12 | **11.3 s of `inputSuppressed=True` with `wave=True battleLock=True` and `modal=False`** - a live wave, nothing on screen |
| `:3183-3186` | 09:06:13.69 | combat ends -> `restored after combat`; `PanelManager: 'Dialogue' opened` -> `modal` becomes True |
| `:3224` | 09:06:13.809 | `Dialogue suppressed - modal open ('EndState' swapped in, WO-795 truce) -- panel off, VM stays open, Ended NOT fired.` |
| `:3250` | 09:06:13.850 | `context Battle -> Modal` - the ticket's window starts HERE |
| `:3627-3632` | 09:06:19.8 | `EndState` closed; `Dialogue restored`; `'Dialogue' opened and verified visible` |
| `:4755` | 09:06:37.301 | last `inputSuppressed=True` |
| `:4819` | 09:06:37.855 | `PanelManager: 'Dialogue' closed` |
| `:4836` | 09:06:38.020 | `context Modal -> Town`; `:4874` `inputSuppressed=False` - freeze ends |

**Total continuous suppression: 09:06:02.403 -> 09:06:37.9 = ~35.5 s**, not 18 s. PROVEN.
Segmented honestly: **11.3 s combat-hidden with nothing visible (the defect)**; ~6 s while the
`EndState` wave-summary modal was legitimately up; ~18 s with the dialogue panel restored and
"verified visible" (`log:3632`) - whether the player could actually advance it in that last stretch
is **NOT PROVEN** (no screenshot covers it).

**CORRECTION to ticket section 3, lines 43-44** ("`sceneCombat=False` and `wave=False` on every line;
Wave 1 had not started"): that is **false for the full log** - it describes only the 09:06:19+ tail.
The log starts at 09:05:56 with `wave=True battleLock=True` (`log:3`) and the wave was live and
enemies pursuing (`pursuit=True`, `log:1193`) when the freeze started. The load-bearing open question
in section 3 - "whether the same mechanism can trigger during `sceneCombat=True`" - is **ANSWERED AND
PROVEN in the affirmative by this very capture** (this build reaches combat via `wave`/`battleLock`,
not `sceneCombat`, which is the raid-scene predicate - `HudContextEvaluator.cs:99-108`).

### 2. Root defect (PROVEN from source): the dialogue truces hide the panel but never release the input gate

`Assets/_Modules/HUD/DialogueView.cs` has THREE "truces" that hide a live dialogue without ever
closing it, because closing would fire `Ended` and falsely complete a dialogue-gated tutorial step:
- builder truce (WO-702) - `DialogueView.cs:121-132`, `TickBuilderTruce` `:564+`
- **combat truce** - `TickCombatTruce` `DialogueView.cs:538-561`, seeded at open `:163-165`
- **modal truce (WO-795)** - `TickModalTruce` `:594-618`, arbiter path `OnArbiterClose` `:630-657`

All three explicitly log `Ended NOT fired` (`:552`, `:582`, `:612`, `:657`). **`Ended` is the ONLY
thing that clears `HeroLocomotion.InputSuppressed`** (`HeroLocomotion.cs:975-979`). So for the entire
duration a dialogue is hidden by any truce, the hero is frozen with **no visible cause and no
affordance**: `DialogueView.Update` (`:517-534`) early-returns on `HiddenForBuilder` (`:521`),
`_hiddenForCombat` (`:522`) and `_hiddenForModal` (`:524`), so even the any-key advance that would end
the conversation is disabled.

The freeze is total, not partial - the same static hard-gates every input surface:
`HeroAbilityInput.cs:51`, `PlayerAttackController.cs:297` and `:456`, `Tower.cs:1327`,
`BuildModeController.cs:772`.

**The builder truce already learned this lesson and got a bypass seam - the other two never did.**
`BuildModeState.DialogueHiddenForBuilder` (`Assets/_Modules/Core/BuildModeState.cs:38`, written by
`DialogueViewModel.cs:88`) exists exactly so `BuildModeController.cs:772` can ignore a suppression
raised by an invisible dialogue. `grep -rn "DialogueHiddenFor" Assets --include=*.cs` finds **no
`...ForCombat` / `...ForModal` analogue**, and no path that restores locomotion. That is the fix shape.

**Generalised from source, not from this capture:** `DialogueView.OnArbiterClose` (`:630-647`)
converts WaveManager's combat `PanelManager.CloseAll` into a truce rather than a close. So **any
dialogue open when a wave starts leaves the player frozen and defenceless for the whole wave.**

### 3. Timeout / watchdog / escape hatch: NONE. And both watchdogs are blind to it by design.

PROVEN: no timer, no elapsed-time field, and no periodic re-check exists on `InputSuppressed` - the
five assignments at `HeroLocomotion.cs:936/953/961/971/978` are the complete set. The 2-second check at
`AutoPilotDriver.cs:4066-4070` is an **EditMode/AutoPilot test assertion**, not a runtime escape.

Worse - the game's only softlock detector explicitly treats this exact state as *progress*:
`Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs:402-407` resets the stall timer whenever
`IsHeroInputSuppressed()` is true, and `:411-416` resets it again whenever `PanelManager.AnyOpen`.
**Both halves of this freeze are individually whitelisted**, so it can never self-report. That is why
the owner had to flag it by hand - exactly the failure CLAUDE.md section 14 exists to prevent.

**This is the core defect regardless of trigger, as ticket section 3 anticipated.**

### 4. The screenshot: NOT the freeze, and the debug harness is RULED OUT as the trigger

`docs/handoffs/movement_freeze_modal_stuck.png` (opened this session) shows: hero at the Heart, "Wave 1
/ Next wave in 7s / Start Now", a `FLAGGED` toast centre-screen, and top-left a **"THE NIGHT MARKET"
card with a "FLAG" chip beneath it**. No dialogue panel, no `EndState` panel, no modal of any kind is
visible.

- **Timing:** F8 capture utc `14:05:44Z`; the device clock is UTC-5 (inferred from the log's local
  times and the 7 s countdown matching `wave=True` at 09:05:56, `log:3`) => **09:05:44 local, ~12 s
  before the log begins and ~18 s before the captured freeze started at 09:06:02.403.** The screenshot
  and the freeze are **different moments**. The episode the owner actually flagged is NOT in this
  capture and its mechanism is **NOT PROVEN** - though it is the same build, the same scene and the
  same pre-wave-1 window, so the truce mechanism is the leading candidate.
- **Neither visible element is a modal.** "THE NIGHT MARKET" and the "FLAG" chip are adjacent HUD
  **band elements**, named side by side in `Assets/Editor/Regression/HudLabelFitRegression.cs:2086-2111`
  ("the Night Market card ... is not larger than the FLAG chip"). `Assets/_Modules/Core/Dev/FlagCaptureButton.cs`
  contains **zero** `PanelManager` references (grepped) - the F8 chip registers no panel and opens no
  UI of its own; its only acknowledgement is the timed `FLAGGED` GUI label at
  `BreakCaptureHarness.cs:755`. **The debug harness is not the trigger. This is a genuine
  player-facing defect.**
- The modal that actually held the arbiter during the ticket's window was `'EndState'` (the wave-end
  summary, `Assets/_Modules/Village/UI/EndState/EndStateView.cs`) and then `'Dialogue'` itself
  (`log:3226`, `log:3632`) - both normal player-facing panels.

### 5. Secondary finding (do NOT fix here): a contextual tutorial fires mid-wave with no combat gate

`tut_ctx_talents` is a contextual FTUE one-shot
(`Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json:211`) driven by `TutorialFlow.TickContextual`
(`Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs:2724`, ticked every frame in every phase from
`:757`). Scanning `:2724-2900` finds **no `BattleLock` / `IsInBattle` / wave check** before it plays -
its only dialogue-state guard is `DialogueService.IsRunning` (`:2757`). The capture proves it fired at
`wave=True battleLock=True` (`log:1488` vs `log:1526`). That is what put a dialogue on the stack
mid-wave in the first place. Worth its own ticket; it is the trigger, not the defect.

### 6. Question 6 - the four STEP-STUCK captures: DIFFERENT modal signal, so the "0s" proves nothing

**NOT the same signal. PROVEN.** `TutorialFlow.TickStepClock` reads
`bool modal = DeNelle.Village.UI.WelcomeBackPopup.IsOpen;` (`TutorialFlow.cs:2470`) - deliberately
narrow, and the in-code comment at `:2463-2469` says so explicitly ("NARROW ON PURPOSE: the
welcome-back report specifically, NOT 'any PanelManager modal'"). The exclusion set is
`builder || frozen || modal` (`:2472`) where `builder = BuildModeState.IsActive` (`:2461`) and
`frozen = WorldClockFrozen` (`:2462`). **`HeroLocomotion.InputSuppressed` is excluded from nothing.**

Consequences, both worth recording:
1. The ticket's reasoning in section 3 ("each reporting `modal 0s` ... so they are NOT proven to be the
   same defect") is **invalid** - that counter reads `WelcomeBackPopup` only and would read `0s` during
   this freeze too. Same cause remains **NOT PROVEN**, but it is **not ruled out**.
2. **The tutorial watchdog is structurally blind to this class of freeze, and actively charges the
   player for it.** The four stuck steps (`founding_defense` / `founding_timers` / `founding_defend` /
   `founding_win`) are all combat beats - precisely where the combat truce bites. A player frozen by a
   hidden dialogue accumulates full watchdog time and is rescued-and-SKIPPED through content they were
   never able to act in. Related, separate, not fixed here.

### 7. What is NOT proven / what would close it

- The **09:05:44 flagged episode itself** - not in the capture. NOT PROVEN.
- Whether the player could advance the restored dialogue in the final ~18 s. NOT PROVEN.
- The hero's `pos=(-0.77, 0.08, -28.01)` is byte-identical even through the 5 *unsuppressed* seconds
  09:05:57-09:06:01 (`log:197`, `log:1198`) with `velSelf=0.00`. Observed, unexplained, not theorised
  about here - it may simply be that she was not touching the stick. Flagging it so a fix lane does not
  read "position static" as proof of suppression.
- **Repro that would prove the combat case end-to-end on device** (the capture already proves it, this
  is for a regression): start Wave 1, and while `BattleLock.IsInBattle()` is true trigger any dialogue
  (the fastest deterministic route is the `tut_ctx_talents` contextual beat - gain a talent point
  mid-wave). Expected on today's build: `[Flow:Dialogue] opened during combat -- starting hidden`,
  followed by `inputSuppressed=True` with `wave=True battleLock=True modal=False` and a completely
  frozen, defenceless hero with **nothing on screen** until the wave ends.
- **Headless regression shape** (for the fix lane): assert that `HeroLocomotion.InputSuppressed` cannot
  remain true while `DialogueView.IsShowing` is false - i.e. a hidden dialogue must release the input
  gate, by the same law `BuildModeState.DialogueHiddenForBuilder` already applies at
  `BuildModeController.cs:772`.

**Verdict: genuine player-facing combat softlock, proven in-capture. Not a debug-harness artifact.**
Root defect = dialogue truces suppress input without releasing the gate; aggravating = no timeout and
both watchdogs (`BreakCaptureHarness.cs:402-416`, `TutorialFlow.cs:2470`) are disarmed by the very
flags that define the state.
