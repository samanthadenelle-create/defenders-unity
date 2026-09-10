# WO-1414: START NEW inherits the away clock AND the ever-built ledger - a fresh town claims 8h of haul and then holds its production forever

**Status:** IMPLEMENTED - 6a5c7a36d on HEAD 2026-09-09 (was: IMPLEMENTED - 2026-09-07 uncommitted, awaiting gate); owner felt-test closes. Proof read at source 2026-09-09 (HEAD `eb879496f`): `git merge-base --is-ancestor 6a5c7a36d HEAD` exits 0; `git log -1` on BOTH `Assets/_Modules/Village/Harvest/OfflineHarvestService.cs` and `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs` returns `6a5c7a36d chore: checkpoint complete workspace and rebuild board` (Wed Sep 9 14:17:30 2026 -0500); the C/D mechanism is in the tree at `OfflineHarvestService.cs:1337-1363` (the symmetric deferral + `DismissIfOpen("mandatory tutorial chain live under the report (WO-1414 D)")`) and `TutorialFlow.cs:549-553` (`_modalExcludedSeconds`, the WO-1414 D reason-tag). See `WORK_ORDER_1414_new_game_inherits_the_away_clock_and_the_ever_built_ledger.RESULT.md`. Pinned by OfflineHarvestRegression case 6
[new-game-claims-nothing] (fresh-save fixture). PRIOR STATUS: READY TO IMPLEMENT - owner felt-test 2026-09-06 Fail (marked 2026-09-07T00:48:45, build 2026.09.07.358574). Bounced from Fixed. PRIOR STATUS: FIXED (A + C) - on the Seeker as build 2026.09.05.356620, installed 10:48, versionName confirmed on device. Felt-test: START NEW -> no welcome-back popup at all, and the founding dialogue runs to its end (no `STEP-STUCK :: founding_greet`). **B and evidence D are NOT fixed and need the two rulings below.** A and C gated (COMPILE_GATE_OK 10:32, REGRESSION_OK 383/383 10:36, commit 57d3437a2); building to the device now. ⚠ **The ticket's premise for B was WRONG and is corrected below** - `ResetToNewGame` DOES clear `everBuiltStructureIds` (`GameStateService.cs:1277`); the real route to the farm+lumbermill HELD lines is a Default Town founding, which marks `collector_lumbermill`/`collector_farm` ever-built while a `ResourceCollector` is attached only on PLACEMENT, so the baked scene twins are ledger ids with no collector. **That half needs an owner ruling and is NOT fixed.** Evidence D (the sub-minute re-fire) also needs a threshold. PRODUCTION BLOCKER, proven on the owner's device 2026-09-05 09:57-10:06 (build 2026.09.05.356468).

## Owner, verbatim (2026-09-05 10:0x, felt-test on the Seeker)
1. "on this screenshot new game getting this" (the welcome-back popup on a brand-new game)
2. "said 1 hour 56 min" (a second New Game, a different inherited window)
3. "screenshot closed but reports out resources"
4. "nothing new just bug fixes and stability now" / "see if we can push to production"

## Evidence (captured, not inferred)

### A. The away clock is inherited
`logs/f8-inbox/device/SM02G4061955851/break_01_error.png` (device, 09:57): on a NEW GAME the
welcome-back popup reads **"YOUR REALM WORKED FOR 8h 22m"** with `WOOD WAITING +11520`,
`IRON WAITING +6912`, `STONE WAITING +15000` and three "Storage nearly full - N will wait" lines.
8h22m is the wall time since the owner's PREVIOUS session (~01:40 -> 10:03), so the window was
measured from the old save's claim stamp. A second New Game reported **1h 56m**.

`ResetToNewGame` sets `LastHarvestClaimMs = 0` and its own comment says that means "reseed the
accrual clock on next load (no haul)" (`GameStateService.cs:1543-1545`). Something re-supplies a
non-zero anchor before the coordinator reads it. Candidates to prove, NOT yet proven: the cloud
load (`ServerLastSeenMs`, `GameStateService.cs:160-176` says it must NEVER be written into
`LastHarvestClaimMs` - so if it is, that is the bug), a queued offline payload replay (the device
log shows `[Sync] Queued offline payload (queue depth: 56)`), or a save round trip after the reset.
**Instrument the coordinator's window source first (WO-1036 clock line names the window; add the
ANCHOR and its provenance), read the device line, then fix that step.**

### B. The ever-built ledger is inherited
Device logcat 10:04:55 -> 10:06:25, every 10 s, on the same fresh town:
```
[Flow:Harvest] 'farm' is in the ever-built ledger but NO ResourceCollector is registered - 13 Food HELD (not lost) this tick
[Flow:Harvest] 'lumbermill' is in the ever-built ledger but NO ResourceCollector is registered - 10 Wood HELD (not lost) this tick
[Flow:Harvest] collector status -> full=0/1 maxFill=0% pending=12
```
`everBuiltStructureIds` (save v36, WO-834) survived the reset, so the harvest tick pays a building
that does not exist in the new town. The resources are HELD forever - the player watches a town
that says it is producing and never banks anything. This is the owner's "reports out resources".

### C. The popup blocks the tutorial, and the tutorial then dies
`logs/f8-inbox/capture-device-20260905-095701-seq4681.md`:
`[Flow:Tutorial] SKIP_TOP_HIT_BLOCKED top=ObsidianPanel path=WelcomeBackUI/ObsidianPanel`
(the welcome-back panel is over the SKIP TUTORIAL button - visible ghosted behind the panel in the
screenshot), then `capture-device-20260905-100245-seq4682.md`:
`[Flow:Tutorial] STEP-STUCK :: founding_greet - no 'dialogue.ended:tut_founding_greet' after 120s
... RESCUED via watchdog and recorded as SKIPPED - the step was NOT completed`.
So the first-run tutorial is silently skipped on every new game that shows this popup.

### D. The popup fires again on a sub-minute resume, and it proves B on screen
`logs/f8-inbox/device/live-0905-1015.png` (live capture 10:15, owner mid-session): the welcome-back
popup is up AGAIN reading **"YOUR REALM WORKED FOR under 1m"** with a single row `IRON WAITING +36`.
Two findings in one frame:
- The popup raises for a window the player did not experience as being away. It should not open
  below a threshold (the owner: "screenshot closed but reports out resources" - she closes it and it
  comes back). One rule, on the window, in the coordinator.
- **Only IRON is listed.** Wood and Food are missing because their collectors are the two the harvest
  tick is HOLDING (`farm`, `lumbermill`), so B is visible to the player as a popup that quietly
  stopped reporting two of three resources.

## The shape (this is the FIFTH instance)
`GameStateService.cs:1543-1547` already records the pattern by name: WO-860 equip, WO-1019 hot-swap
bar, WO-1220 talents, WO-1371 collector prefs - **state that lives outside the save envelope, or
survives in memory, and that `ResetToNewGame` has never heard of.** A and B are instances five and
six. The fix must follow that file's own two-half shape: the PERSISTED half in `ResetToNewGame`,
the LIVE half on the `NewGameStarted` event.

## Fix shape
1. **Instrument first (CLAUDE.md s12).** The offline coordinator prints its window; make it print
   the ANCHOR and where the anchor came from (`state`, `server`, `queued-payload`, `reseed`), once.
   Read that line off the device before changing the window arithmetic.
2. **A:** whatever supplies the non-zero anchor on a fresh save must not. On a new game the first
   claim window is ZERO and no popup is shown at all. A brand-new town has nothing to collect.
3. **B:** `ResetToNewGame` clears `everBuiltStructureIds` (persisted half) and the live harvest
   tick's ledger view is rebuilt on `NewGameStarted` (live half), so a fresh town holds nothing.
   Never leave a HELD interval pointing at a building the town does not have.
4. **C:** the welcome-back popup must not open while a tutorial step is awaiting a dialogue - and,
   independent of that, the tutorial SKIP control must sit above any popup that can cover it
   (a raycast the panel cannot block, or the popup defers until the step ends).

## Acceptance
- [ ] Headless: START NEW -> the offline coordinator's first window is 0 s and no welcome-back is
      raised; `everBuiltStructureIds` is empty; no `is in the ever-built ledger but NO
      ResourceCollector` line fires in the first 60 s. RED first: name the mutation.
- [ ] `BaseLayoutRoundTripRegression` / `OfflineHarvestRegression` / `CollectorIncomeRegression`
      / the tutorial-step suites stay green.
- [ ] Device: START NEW on the next build -> no popup, no HELD lines, the founding dialogue
      completes (no `STEP-STUCK :: founding_greet`).

## Not in scope
The welcome-back copy itself (WO-1392 shipped it), the harvest maths, the tutorial content.
