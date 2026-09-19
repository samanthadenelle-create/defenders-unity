# WO-1881 — founding_walk (and founding_stores) eject players via the 120s watchdog

**Status:** IMPLEMENTED 2026-09-19 — diagnosed (55/58 walk drops at idle 120.0s, autoAdvanced, coachBeats=3; stores 14/14 at 300.0s; not network/wallet). Walk rescue: if hero moved ≥8m when watchdog fires, complete as played (WO-962 radius/watchdog untouched). AFK still drops. founding_stores 300s placement is a follow-up.

**Owner, verbatim:** "Ticket B — founding_walk and founding_stores are ejecting players via the 120s watchdog" / "Open Ticket B, pull the three diagnostic items listed, and fix founding_walk before you do anything else for growth."

## Problem

30d funnel: founding_walk 99 entered / 33 completed / **33 dropped** / 0 skip / median idle **120s**. founding_stores 35 / 11 / 9 / 0 / idle **300s**. Skip is authored `true` on both rows (`tutorial-steps.json`) and **nobody taps it**. The watchdog auto-advances as `tutorial_step_drop`. This is the onboarding cliff, upstream of wave 1.

Walk completion is `hero.reached:guide_gate` (latched nearest gate, WO-962). WO-962 forbids widening ReachedRadius or the watchdog. Do not "fix" by hiding the wait.

## Diagnose (required before the edit)

1. Raw `tutorial_step_drop` properties for founding_walk 30d: is `secondsIdle` exactly the watchdog threshold?
2. `playtest_break` kind=`possible_softlock` for those sessions: scene, message.
3. Is the step gated on network, wallet, animation, or lost input?

Then pick: timeout+try again / skippable-on-animation (not this step) / auto-advance on first valid input / fix an underlying error.

## Acceptance

founding_walk completion from entered **>80%**. Fewer than **10 drops / 30d** on that step (forward window after ship). Watch `?view=funnel`.

## Not in scope

WO-1879, WO-1880. Widening the watchdog. Authoring new player-facing copy without owner words.
