# WO-1562: winning never says what it unlocked, and a cleared camp is indistinguishable from one you never fought

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Priority:** P1 — this is the return leg of the raid loop, and it has no memory.
**Silo:** `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` +
`Assets/_Modules/Village/Hero/RaidSelectionScreen.cs` + `RaidSelectionVM.cs`.
**All three files are CLEAN in the working tree as of 2026-09-06 21:50** — this lane is file-disjoint
from WO-1561 and can run beside it.
**Parent:** WO-1534 §A5 + §A6. **Source:** read-only review 2026-09-06 (CLI seat), re-read at source.
**Minted** from the banner (`CLI_LANES_WO_NUMBERS.md`, renumbered to the banner's hundred-and-second-pass reconciliation, 2026-09-06 22:12).

---

## PART 1 — the victory screen never reports ladder progress

### Evidence

`RaidVictoryController.ResolveUnlockLine(victories)` (`:814-821`) **returns `null` unconditionally.** Its
own trace says why:

> *"UNLOCK LINE: victories={victories}; no ladder gate is wired into this seam yet, so the victory screen
> announces no unlock. The count is persisted and correct — only the announcement is unowned (section-4
> thresholds belong to the ladder lane, not to this file)."*

⛔ **That lane is WO-1375, and it is CLOSED (2026-09-06). Its RESULT does not claim this seam.** So the
announcement is not deferred — it is **orphaned**. Nobody owns it and nothing is coming.

Meanwhile the loop advertises the ladder at both ends and stays silent in the middle:
- the grid shows escalation-lock sentences (`Logs/device/screens/seeker-357453-raids.png`);
- the win **is** counted (`RaidVictoryController.cs:344-361`);
- `PostRaidBeatTokens.cs:130-147` shows only the **first-raid tutorial dialogue** ever speaks the ladder.
  Every subsequent victory says nothing.

### What to do

A victory that crosses a ladder threshold announces it, in words, on the screen that already exists. Feed
`ResolveUnlockLine` from the same authority the grid's lock sentences read, so there is **one** ladder
fact and not a second copy of the thresholds.

⛔ **Keep the trace.** `:810-812` states the reason it exists: *"Traced, so the absence is OBSERVABLE: a
player crossing a threshold and seeing no line must be distinguishable in a capture from a player who
crossed nothing."* A victory crossing nothing must still stay silent, and the trace must still tell the
two apart. Never strip FlowTrace (CLAUDE.md §12).

---

## PART 2 — a cleared camp reads identically to one you have never fought

### Evidence

`RaidClaimService.MarkClaimed` persists the win (`RaidVictoryController.cs:685`). But grepping
`RaidSelectionVM.cs` and `RaidSelectionScreen.cs` for `RaidClaimService|IsClaimed|Cleared` returns
**comments only** — `RaidSelectionVM.cs:50, :52, :73`. **There is no functional read.** The row's bottom
band falls through to `RewardHint` -> `"- x1 Loot"` (`RaidSelectionScreen.cs:998`, `:1167-1174`).

So the return leg of the loop has no memory of what the player just did, and nothing warns that a repeat
clear pays a fraction — **which WO-1461 sets at 60%.** The player discovers that after committing.

**Not covered by WO-1461**, which puts repeat-clear economics on the **deploy card** (*"the deploy card
quotes what will BANK and what will CACHE"*) and never touches the grid row. **A player choosing among
four camps chooses on the GRID**, one screen before the deploy card speaks.

⚠ **Not proven:** whether any camp was actually claimed on the save behind `seeker-357453-raids.png`. The
PNG corroborates; the source read is the proof. Do not cite the frame as evidence of the defect.

### What to do

1. A cleared camp is marked as cleared on the grid row, read from `RaidClaimService`. ⛔ **Never a second
   claim predicate** — the WO-1521 lesson (`PlayerDeckWorkspace.cs:719-723`: *"ONE rule, TWO surfaces...
   the drift is the actual defect"*).
2. The row states the repeat-clear rate, or points at it, **before** the deploy card does.
3. ⛔ **Do not re-author the multiplier.** WO-1461 owns the economics; this is a DISCLOSURE only, and it
   must read whatever number that ticket lands.

⚠ **Card real estate is contested.** WO-1402 already resized this card (`CardHeightPx` 142 -> 178, clock
28 -> 22 pt) and WO-1534 §A6's sibling note flags that **every row currently reads the identical
`Clock: 3:00`** — a band that differentiates nothing across all four camps while difficulty, walls,
defenders and spoils all vary. **If you need a band, take that one** — but that is a judgement call, so
record it in the RESULT rather than doing it silently.

---

## ACCEPTANCE

1. A victory crossing a ladder threshold announces it in words on the existing screen; one crossing
   nothing stays silent, and the trace still distinguishes the two in a capture.
2. The ladder thresholds are read from one authority, not re-listed in `RaidVictoryController`.
3. A cleared camp is visibly cleared on the grid, read from `RaidClaimService`.
4. The repeat-clear rate is disclosed on the grid, reading WO-1461's number, not a new one.
5. Oracles for both halves, each **proven RED before green** and both runs recorded.
6. Fresh captures of the victory screen (with an unlock line) and the grid (with a cleared camp) attached
   to the RESULT.
7. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on **fresh** logs — judged by the marker, never the exit code.

## WHAT NOT TO TOUCH

- Loot amounts, the Raid Cache, repeat multipliers — **WO-1461**. Disclosure only here.
- The grid's clipping, scrollbar and camp-count caption — **WO-1442, CLOSED**.
- Heartfire, and any second "when may you raid" gate — **WO-1379** / `HeartfireRegression` PIN F.
- The `LOCKED - needs Army N` word — **WO-1542**, blocked on an owner ruling. Leave it exactly as it is.
- The victory screen's auto-return timer — **WO-1543**, blocked on an owner ruling.
