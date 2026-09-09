# WORK ORDER 1600 - "JEWELER DISCOVERED" fires on the TITLE screen after START NEW, its body overflows the plate, and its button lands on the title's CLOSE

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:47, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 evening gate (COMPILE_GATE_OK Builds/cg-wave11.log, REGRESSION_OK 456/456 Builds/reg-wave11.log 14:19); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT (instrument first) - minted 2026-09-07 (CLI) from the owner's reset screenshot
**Silo / Lane:** Village/Crafting FTUE - `Assets/_Modules/Village/Crafting/JewelerDiscoveryFtue.cs` (TryPresent ~:55-80), `JewelerProgression` (IsUnlocked / Completed - the flags that must reset), the New Game reset path (`ResetToNewGame`, `ResetToNewGameFullClearRegression`)
**Type:** EXISTING system, RESET LEAK + LAYOUT
**Priority:** P1 - first thing a player sees after START NEW

## Evidence

Frame `Logs/device/seeker-shots/Screenshot_20260907-132324.png` (Seeker, build 2026.09.07.359651, 13:23):
the TITLE screen (CONTINUE / START NEW / PLAY INTRO visible behind) with the compact "JEWELER DISCOVERED"
modal over it; the body text starts ABOVE the plate's top edge ("You recovered a rare rough stone" spills
over the frame), and "OPEN CRAFTING: JEWELER" sits on top of the title dialog's CLOSE. Log: 13:23:08
process start, 13:23:45 `[Flow:Onboarding] OnStartNew: routing to the HeroSelect carousel` - the modal was
up BEFORE start new resolved. Code read: `TryPresent` gates on `JewelerProgression.IsUnlocked &&
!Completed` and only excludes scenes whose name contains "Dungeon" - nothing excludes the Title - and
`body.overflowMode = TextOverflowModes.Overflow` on a fixed 0.34-0.91 band is what spills.

## What to do

- Instrument: `FlowTrace.Step("JewelerFtue", "TryPresent scene=... unlocked=... completed=... source=<what set IsUnlocked>")`.
- Find what carried `IsUnlocked` across START NEW (a PlayerPrefs key, `VillageInventory.HasEverAcquired`,
  or a save field the reset does not clear) and make the reset clear it - through the one reset path,
  pinned by `ResetToNewGameFullClearRegression`.
- Present only in a HUB scene with a live hero (never Title / HeroSelect / Dungeon); size the body to the
  copy (preferred height, no Overflow) with the verb clear of any other panel's CLOSE.
- Capture entry + regression: modal absent on Title; body inside the plate at 2670x1200.

## Acceptance
- START NEW on the Seeker shows no Jeweler modal; the modal appears once, in town, when the first stone is
  earned, fully inside its plate. Owner felt-test closes.
