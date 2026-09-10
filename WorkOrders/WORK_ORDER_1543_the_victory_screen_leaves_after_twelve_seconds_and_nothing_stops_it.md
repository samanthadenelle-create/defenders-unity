# WO-1543: the victory screen routes home after 12 s and no touch stops it — hold on touch, longer guard

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes — **owner ruling 2026-09-06: "Hold on touch, longer guard."**
**Priority:** P1
**Silo:** `Assets/_Modules/Village/UI/EndState/EndStateView.cs` + `EndStateVM.cs` +
`Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` (`_autoReturnSeconds` only).
**All three CLEAN** in the working tree as of 2026-09-06 21:50.
⚠ **COORDINATE WITH WO-1561** — that ticket adds a retreat/timeout end state through the same
`EndStateView` and was told to give it a guard and then follow whatever this ticket settles. **The rule
this lands must apply to BOTH screens.** If the two run concurrently, one seat takes both.
**Parent:** WO-1534 §A4. **Source:** read-only review 2026-09-06 (CLI seat), re-read at source.
**Minted** from the banner (`CLI_LANES_WO_NUMBERS.md`, renumbered to the banner's hundred-and-second-pass reconciliation, 2026-09-06 22:12).

---

## 1. EVIDENCE

`RaidVictoryController.cs:61` — `_autoReturnSeconds = 12f` -> `EndStateVM.cs:416` `AutoDismissSeconds` ->
`EndStateView.cs:989-990` `StartCoroutine(AutoDismissAfter(...))` -> `:2628-2632`
`WaitForSecondsRealtime(...)` then `FirePrimary()`.

**There is no cancel.** `EndStateView.cs:2771` states outright that the file contains **no
`StopAllCoroutines` / `StopCoroutine`**. No tap, no drag, no scroll stops the timer.

In those 12 seconds the player is meant to read: the star result, **up to five spoils rows**, a
companion-join line, and — when the bank overflowed — *"Some of the reward could not be paid out"*
(`RaidVictoryController.cs:782-784`). **That last one is precisely the message a player must not miss**,
and it is on the screen that leaves by itself.

## 2. ⚖ THIS WAS A DELIBERATE CHOICE, NOT A BUG — DO NOT DELETE THE GUARD

`RaidVictoryController.cs:753` calls `AutoDismissSeconds` **"the anti-soft-lock guard"**, and it exists
because an end state that never dismisses can strand a player with no route home. The codebase already
knows how to opt out — `EndStateVM.cs:379` sets `AutoDismissSeconds = 0f` with *"deliberate: no
softlock-guard here — Retry must be chosen."*

The defect was never "there is a timer". It is that **the timer cannot tell a reading player from an
absent one.**

## 3. THE RULING

> **Owner, 2026-09-06: "Hold on touch, longer guard."**

So:
1. **The guard STAYS.** A player who has walked away is still returned home. ⛔ Never remove it, and never
   set `AutoDismissSeconds = 0f` on a raid end state.
2. **Raise the duration.** 12 s does not read a five-row spoils screen plus a caveat line.
3. **A touch holds it.** Interaction cancels or restarts the countdown — the player who is reading is the
   player who is touching.

**WWCD** (memory `design-tiebreaker-what-would-coc-do`): Clash of Clans' battle-result screen waits
indefinitely. The guard is this game's concession to not stranding anyone; touch is what makes it fair.

⚠ **Cancel vs. restart is yours to choose — pick deliberately and record it.** *Cancel* means one stray
tap pins the screen open forever, which re-opens the softlock the guard exists to prevent. *Restart on
each interaction* keeps the backstop alive while giving a reading player unlimited time. **Restart is the
safer reading of "hold on touch"** — but say which you implemented and why in the RESULT.

## 4. IMPLEMENTATION NOTES

- The timer is a coroutine started at `EndStateView.cs:989-990`. Holding it needs a handle to stop or
  re-arm — note `:2771` records that **nothing in this file has ever stopped a coroutine**, so this is a
  new capability in that class, not a re-use. Add it carefully and traced.
- ⛔ **`EndStateView` serves more than raids** (arena, dungeon, death states — `EndStateVM.cs:329`, `:352`,
  `:379` all carry different dismiss values). **Do not change the shared default.** Make the hold explicit
  for the raid templates, or opt-in per template, so no other end state's timing moves silently.
- `:2634-2645` (`OnSceneLoaded`) tears the panel down **without** firing the primary when the world moves
  underneath it, and records why the silence there was expensive. Your hold must not resurrect that class:
  a held screen whose scene changes must still tear down cleanly and still say so in the log.
- Add `FlowTrace` on the hold/re-arm path (CLAUDE.md §12). **Never strip existing FlowTrace.**

## 5. ACCEPTANCE

1. The victory screen does **not** route home while the player is interacting with it.
2. The anti-softlock guard still exists and still fires for a player who does nothing. ⛔ **Do not delete
   it.**
3. The bank-overflow caveat is legible for as long as the player wants it.
4. No other end state's dismiss timing changes. Prove it — name the templates checked.
5. Cancel-vs-restart is chosen deliberately and the reasoning is recorded in the RESULT.
6. An oracle covers **both** halves — the guard still fires with no input, **and** input holds it.
   ⛔ **Prove it RED before green** and record both runs (memory
   `prove-the-success-path-not-just-the-refusal`: a failure-only acceptance once shipped a guard that
   aborted every good run while exiting 0).
7. A **fresh** capture of the victory screen. ⛔ There is no post-raid result PNG anywhere in the repo today.
8. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on **fresh** logs, judged by the marker, never the exit code.

## 6. WHAT NOT TO TOUCH

- The **content** of the victory screen — stars, spoils rows, the companion line. This ticket changes
  **when it leaves**, not what it says.
- The unlock/ladder line on that same screen — **WO-1562**.
- Loot amounts and the overflow caveat's wording — **WO-1461**.
- The non-victory exits' end state — **WO-1561** (but the timing rule this ticket lands applies there too).
- Hero-death settlement — **WO-1526**.
