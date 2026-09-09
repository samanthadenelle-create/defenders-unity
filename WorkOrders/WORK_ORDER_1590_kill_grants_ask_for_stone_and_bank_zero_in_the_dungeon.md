# WORK ORDER 1590 - Every dungeon kill asks for 8 Stone and banks 0: the material grant shortfalls on Stone while Wood and Iron land in full

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:52, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT (instrument first) - minted 2026-09-07 (CLI) from the owner's device log
**Silo / Lane:** Village/Enemies reward grant - `Assets/_Modules/Village/Enemies/Enemy.cs:3515` (the SHORTFALL warn) and the economy sink it calls; `EconomyService` / the Stone lane (WO-1416 "the quarry pays stone - one producer three answers")
**Type:** EXISTING system, DEFECT (a promised grant not paid)
**Priority:** P2

## Evidence (device log, Seeker, `dg_sunken_vault`, 2026-09-07 09:35:59)

```
[Flow:Reward] KILL GRANT id=hollow-rogue baseXp=17 baseGold=7 ... | WO-1216 mult=1.10 floor=6 cap=40 baseWood=8 baseIron=8 baseStone=8 rolledWood...
[Flow:Reward] KILL GRANT SHORTFALL (materials) id=hollow-rogue askedWood=8 bankedWood=8 askedIron=8 bankedIron=8 askedStone=8 bankedStone=0 - a material grant did not land in full (missing EconomyService/GameState, or ...
```

Wood 8/8, Iron 8/8, Stone 0/8 on the same grant. The warn's own guess ("missing EconomyService/GameState")
cannot be right for one resource out of three; the Stone lane specifically refuses or is capped/absent.
Candidates only: the Stone storage cap is 0 without a Quarry/stockpile (WO-837 stockpiles cap capacity),
the grant writes to a retired `Food` key that migrated to Stone (WO-1416), or the dungeon economy sink
has no Stone column. The trace decides. Read the warn's full text (cut here at the log width) first.

## What to do

- **Instrument first:** at the Stone bank call log the cap, the balance before/after and the return
  reason; reproduce headless (a kill grant against a fresh save with and without a Quarry).
- Fix the cause the data names. If it is the cap (no Stone container on a fresh town), that is the
  WO-837 ruling working and the fix is the MESSAGE: the toast/warn must say "Stone full" or "no Stone
  store", not "grant did not land"; and the kill toast must not promise Stone it cannot bank.
- Pin: a regression that a grant against a capped/absent Stone store reports the cap reason, and that an
  uncapped store banks 8/8.

## Not to touch
- Base reward numbers (WO-1216 mult/floor/cap), the Quarry's production.

## Acceptance
- Device log after the fix: no SHORTFALL on Stone with a Stone store present; with none, a named cap
  reason and a toast that matches what banked.
- Regression green, REGRESSION_OK n/n on a fresh log.
