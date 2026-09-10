# WORK ORDER 1098 — an arena win leaves timeScale at 0.00 and the 'Harvest Result' modal open

**Status:** IMPLEMENTED - f4e4630e3 on HEAD 2026-09-09 (was READY); owner felt-test closes
PRIOR STATUS: READY TO IMPLEMENT
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1098 → 1099 in the same edit)
**Silo:** Combat / quiescence · UI panels
**Severity:** P0-felt — the world reads as frozen and the interact button is suppressed
**Source:** F8 capture seq=4973 (Editor, `Main_Castle_Overworld`, `t=73.00`)

---

## The capture — two invariants, both player-facing

```
[Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (arena win) - 2 invariant(s) NOT restored after the battle:
  - timeScale: the world clock is 0.00 (0% speed), not 1.00. The player will read this as frozen or
    unresponsive controls even though input is fine — this is the exact 2026-08-20 defect (a leaked
    hit-stop).
  - modal: a panel handle is STILL OPEN after the reward screen closed. The world interact button
    stays suppressed underneath and the back button targets a panel the player cannot see.
    HOLDER: 'Harvest Result' (its own IsOpen probe reports VISIBLE - a real panel is on screen).
```

`BattleQuiescenceGate.Arm` at `BattleQuiescenceGate.cs:334`.

**Felt effect:** you win an arena fight, return to town, and the game is frozen — the clock is stopped,
the interact button does nothing, and Back aims at a panel you cannot see. Input is fine; nothing
tells you that.

## ⛔ This is NOT WO-1093 — different holders entirely

WO-1093 (`_stung` latch → **battle-lock**) does not appear here: battle-lock is not among the two
failures. Do not merge these tickets; a fix for one leaves the other live.

## Finding 1 — timeScale 0.00, and the value matters

**WO-1297** is `FIXED — awaiting the owner's felt-verification`, landed as
`d2e0eb330 fix(clock): find and instrument the 0.04 hit-stop leak`. That fix names **0.04**. This
capture reads **0.00** — a full stop, not a slow-motion residue. Two readings:

- a **second leak site** that parks the clock at zero and was never covered by the 0.04 fix; or
- the 0.04 fix is incomplete and was never felt-verified, so its ticket has been sitting on an
  unproven pass.

Either way **WO-1297 must not be closed on this evidence**, and the RESULT here must say which it is.
Related prior art: memory `prove-with-data-including-owner-prose` records a `timeScale=0.28` reading
on 2026-09-03 that sat unread in a log while a frame-budget theory was argued — a third distinct
value. **Three different leaked values across three incidents says the owner of `timeScale` is not
singular.**

## Finding 2 — why is a harvest modal open around an arena battle at all?

The holder is `HarvestOverflowModal`, which registers as `"Harvest Result"`
(`Assets/_Modules/Core/UI/HarvestOverflowModal.cs:118`).

Note the rule already written into the codebase at
`Assets/_Modules/Village/Buildings/Progression/AutoHarvestService.cs:55`:

> *"Passive collection must never open the player-owned Harvest Result modal."*

So either that rule was violated (passive collection opened it), or the player opened it legitimately
and the battle started underneath it — the **same shape as WO-1090**, where the welcome-back modal
was already on screen when the tutorial began. Establish which before fixing; they need different
repairs.

The gate's probe is trustworthy here: it reports the panel's own `IsOpen` says **VISIBLE**, so this is
a real on-screen panel, not a stale handle.

## The pattern worth naming — three closed tickets, and the gate keeps finding new holders

| WO | Subject | Status |
|---|---|---|
| 1127 | battle-end quiescence gate | CLOSED 2026-08-27, owner pass |
| 1233 | battle lock survives the battle | (see board) |
| 1337 | retreat leaves pursuit lock **and modal open** | CLOSED 2026-09-06, owner pass |
| 1093 | `_stung` latch holds the battle-lock | open, this batch |
| **1098** | timeScale + Harvest Result modal | **this ticket** |

Every one of those fixes repaired **a named holder**. The gate is a detector and it keeps doing its
job; what it keeps detecting is that **restoration is not authoritative** — each new holder is a new
ticket. **Recommend the fix be posed as "who owns restoring timeScale and closing modals at battle
end, and why can anything else leave them dirty" rather than a fifth per-holder patch.** That is a
design question for the owner/CLI, not something to decide inside this ticket — but shipping a fifth
patch without asking it is how there comes to be a sixth.

## Do NOT

- Do not have the quiescence gate silently force `timeScale = 1` and close the panel as the fix. It
  already self-heals and reports; masking the leak upstream costs the next occurrence its evidence.
  Fix the leak, keep the gate loud.
- Do not touch `BattleQuiescenceGate`'s severity. Unlike the warmer's benign line (WO-1089 item 2),
  **every firing of this gate is a real defect.**

## Acceptance criteria

- [ ] The `timeScale` leak site is named at `file:line`, and it is stated whether it is WO-1297's
      0.04 path or a distinct one.
- [ ] It is established whether `HarvestOverflowModal` was opened by passive collection (violating
      `AutoHarvestService.cs:55`) or by the player before the fight.
- [ ] An arena win restores `timeScale == 1.0` and leaves no open panel — proven by a run, with the
      gate reporting no failed invariants.
- [ ] The design question above is put to the owner, and her answer recorded, before or with the fix.
- [ ] Brace balance; gate markers on a fresh log.
- [ ] Owner felt-verifies an arena win on device and closes.

## Unproven

- Whether either invariant reproduces on **device**. This capture is Editor-side.
- Whether the two failures share one cause (a single battle-end teardown path that bailed early,
  leaving both dirty) or are independent. **If a teardown threw partway, one exception would explain
  both** — worth checking the log around `t=73` for a swallowed throw before treating them as two.
