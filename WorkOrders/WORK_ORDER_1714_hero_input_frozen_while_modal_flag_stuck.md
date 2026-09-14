# WORK ORDER 1714 - HeroLocomotion input frozen for 18+ seconds while the HUD context reads modal=True

**Status:** READY TO IMPLEMENT - owner live-device capture, proven mechanism, RCA lane assigned to find
the trigger
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
