# WO-1471: WORLD CLOCK FROZEN timeScale=0.00 fires 152 times on device in Main_Castle_Overworld

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT (instrument first))
**Silo:** `HeroOwner` / world-hold pause ownership. WO-988 covered only the HEADLESS variant and is DONE;
this is the on-device one.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1471 -> 1472 in the same edit).

## 1. EVIDENCE

Device log, 152 lines between 12:51:25.175 and 13:27:32.451:

```
WORLD CLOCK FROZEN timeScale=0.00   scene=Main_Castle_Overworld
```

Thirty-six minutes of the owner's session with the world clock stopped is a felt defect regardless of cause.

**ONE HOLDER IS ALREADY IDENTIFIED (audit batch four, same session).** The harvest-overflow modal takes the
BOUNDED handle for a PLAYER-PACED modal:

```
HarvestOverflowModal.cs:105   WorldHold.Acquire("harvest-overflow-result")   // default = BoundedBeat, 180 s ceiling
device ACQUIRE 12:51:25.157 -> RELEASE 12:53:06.089 = 101 s of WORLD CLOCK FROZEN in Main_Castle_Overworld
```

A modal the player dismisses at their own pace must take the PLAYER-OWNED hold, not the bounded beat handle
that exists for scripted moments. The player is NOT billed for the held time - `BuildTimerService.cs:188-189`
ticks on `unscaledTime` - so this is a felt/pacing defect, not an economy one. This is the WO-1360 follow-on.

The remaining 152 warnings are not all this modal; the rest of the holders are still unidentified.

## 2. FIX SHAPE

- `HarvestOverflowModal.cs:105` moves to `AcquirePlayerOwned`. Then SWEEP every player-paced modal for the
  same bounded default - the default is the trap, not this one call site.
- Make `WorldHold` log the OWNER on both ACQUIRE and RELEASE, and log the full holder set whenever the frozen
  warning fires, so the remaining holders name themselves. Read the trace before editing any other pause
  logic (CLAUDE.md sec.12).
- Then establish ONE pause owner per the WO-1353 law and remove any second claimant the trace names.

## 3. WHAT NOT TO DO
- Do not force `timeScale = 1` on a timer as the fix; that races the legitimate holder and hides the owner.

## 4. ACCEPTANCE
- [ ] `HarvestOverflowModal` uses `AcquirePlayerOwned`; the sweep result listed (every player-paced modal).
- [ ] Every remaining holder is NAMED from a captured trace line, quoted in the RESULT.
- [ ] One pause owner; a regression fails when a player-paced modal takes the bounded handle.
- [ ] `REGRESSION_OK n/n` on a fresh log.
