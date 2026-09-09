# WORK ORDER 1603 - Retreat leaves the battle lock HELD by PursuitBattleProbe again (regression of WO-1337, closed on the owner's Pass this morning)

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:05, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 evening gate (COMPILE_GATE_OK Builds/cg-wave11.log, REGRESSION_OK 456/456 Builds/reg-wave11.log 14:19); reaches the Seeker with the next tester build; owner felt-test closes it. (instrumentation + the dead-hero pulse guard; the device pulser is named by the next capture) PRIOR STATUS: READY TO IMPLEMENT (instrument first) - minted 2026-09-07 (CLI) from F8 seq 4701/4702
**Silo / Lane:** Core/Combat - `Assets/_Modules/Core/Combat/PursuitBattleProbe.cs`, the retreat path (`BattleQuiescenceGate`, WO-1127/1308/1337 seams), the pursuit pulse owner (Enemy/EnemyBrain aggro tick)
**Type:** EXISTING system, REGRESSION
**Priority:** P1 - combat input stays suppressed and the HUD cannot return to town after a retreat

## Evidence (device, build 2026.09.07.359651, 18:27:33Z = 13:27 local, scene Main_Castle_Overworld)

seq 4701: `[Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (retreat) - battle-lock: still HELD after the battle
ended. HOLDER(S): PursuitBattleProbe.Probe (of 3 registered: PursuitBattleProbe.Probe,
BattleArena.<Awake>b__84_0, WaveManager.<OnEnable>b__116_0)`.
seq 4702: `battle-lock STILL HELD after the self-heal (retreat): [PursuitBattleProbe.Probe] (was
[PursuitBattleProbe.Probe]). A holder that survives a full session release is either a LIVE chase
re-pulsing every aggro tick, or an owner whose probe is latched true with no battle behind it.`
WO-1337 ("the pursuit pulse now dies with the body that stamped it") was validated Pass on 358574 at 00:50
today; this is the FIRST retreat on 359651 after the WO-1526 hero-death and WO-1595 raid AI changes
landed - both touched the troop/pursuit seams. Read those diffs first (2b3d8e9af, 70812668e).

## What to do

- Instrument: name the pulser - `FlowTrace.Step("Pursuit", "pulse from <owner> alive=<bool> scene=...")`
  at every PursuitBattleProbe stamp; at the retreat release log the holders with their last-pulse age.
- Reproduce headless: the arena retreat scenario in the AutoPilot fleet (WO-1337's own repro) at HEAD;
  read which owner keeps pulsing after retreat.
- Fix that owner (the pulse must die with the body OR with the retreat, whichever first); extend the
  WO-1337 regression with the retreat-after-hero-death and retreat-during-raid-AI shapes.

## Acceptance
- Headless retreat: BATTLE_QUIESCENCE_OK with zero holders; on device no F8 quiescence capture after a
  retreat. Owner felt-test closes.
