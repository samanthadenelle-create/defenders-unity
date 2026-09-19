# WO-1883 — Town overflow stays at the building until you Collect

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/cg-wave1876.log`) + `REGRESSION_OK` (`Builds/r-wave1876c.log`). FIXED after tester APK.

**Owner, verbatim (2026-09-19):** "also in town when you have more resources than you can store I think we only leave them there short term, otherwise there is no reason to need to come back and collect them"

## What is true now (read 2026-09-19)
`ResourceCollector` accrues into `_pending`. **Ruling 26b auto-overflow** (`TryOverflowToBank` / `SettleOverflow`) **spills into the bank whenever storage has room**, so the mill empties itself. When the bank is full, production **stalls** at the collector (not burned) — that half is already correct.

Raid Cache (`RaidClaimService`, `dotr-raid-cache-*` PlayerPrefs) is already persistent, town-wide, no TTL. The victory line `raid.cacheFull` is a **cap**, not a timer. Do not invent a raid-cache expiry. If the claim door is hard to find, that is a **door** problem — say so in the RESULT, do not silently grow the cache.

## Ruling
1. **Pending stays at the collector until the player taps Collect.** Auto-spill to the bank while accruing is **retired**. Away/offline catch-up still fills `_pending` up to collector cap, then stalls. It does not push into the wallet by itself.
2. Bank-full: still stall, still no burn (WO-1392). Collect still banks up to headroom and leaves the remainder pending.
3. Echo silo dump (`DumpSilos`) is a **different** tap (Collect All). Do not change Echo repair. If Collect All is the only way overflow moves, that is acceptable as an explicit tap, not a background spill.
4. No save-schema bump. Pending already persists on PlayerPrefs.

## Files
- `Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs` (Accrue spill loop)
- `CollectorOverflowRegression` / `CollectorIncomeRegression` — re-point any case that **requires** auto-spill to bank. Do not delete the stall/no-burn pins.
- FlowTrace stays.

## Not in scope
Raid Cache TTL, storage cap numbers, Echo harvest formula.

## Acceptance
`COMPILE_GATE_OK` + `REGRESSION_OK n/n`. Headless or EditMode: accrue with bank room > 0 does **not** reduce `_pending` without Collect. Accrue with bank full still stalls. Collect still banks headroom.
