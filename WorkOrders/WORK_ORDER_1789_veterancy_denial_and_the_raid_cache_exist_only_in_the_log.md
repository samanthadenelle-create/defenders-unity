# WORK ORDER 1789 — "You missed veterancy by one star" and "your overflow went to the Raid Cache" exist **only in the log**

**Status:** READY TO IMPLEMENT

Scope note: the captions' wording is an owner call — §3 holds it.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** victory-screen captions — `Assets/_Modules/Village/UI/EndState/EndStateVM.cs`, `Assets/_Modules/Village/UI/EndState/EndStateView.cs`, `Assets/Resources/Data/Canonical/en.json`. **No `.unity`, no bake, no combat, no economy maths.**

**Build under test:** `2026.09.16.371701`, built 2026-09-15 22:06 from `f6653501f`.

**Video path:** act 2 shot 11. The owner's own 2026-09-16 run ended at 2 stars and **the game never told her what that cost her.** P2 — but it is the cheapest legibility win on the whole raid screen.

---

## 1. SYMPTOM

Two outcomes the player earned or lost are decided, written to the log, and never shown:

- **Veterancy.** At fewer than 3 stars no ranks are granted. The screen says nothing.
- **The Raid Cache.** Loot the bank could not hold is retained in a per-resource cache. The screen says nothing about that either — only the generic *"Some of the reward could not be paid out."*

## 2. EVIDENCE — read at source 2026-09-16

**2a. Veterancy is log-only.** Granted at `Assets/_Modules/Village/Troops/RaidDeployController.cs:1873` → `GrantVeterancy` declared `:1884`; the gate is a **hardcoded literal** at `:1886` `if (starsEarned < 3)`, and the only output is `:1888-1889`:

```
FlowTrace.Step("Raid", $"veterancy: {starsEarned} star(s) - no ranks granted (3 stars required).")
```

which is verbatim the line the owner's run produced (`veterancy: 2 star(s) - no ranks granted (3 stars required).`). A grep of `Veteran|veterancy` across `Assets/_Modules/Village/UI/EndState/` returns **zero hits**; the only player-facing `Veteran` copy in the repo is Grom's NPC title (`Assets/_Modules/Village/NPCs/CompanionDialogue.cs:49`).

**2b. The cache is log-only.** Retained at `Assets/_Modules/Village/World/Camps/RaidClaimService.cs:320-321`; cap `CacheCapPerResource` `:192-204` reading `raid.cacheCapPerResource` (default **1800** — `Assets/_Modules/Core/Ops/RemoteTunables.cs:411`, key `:458`, spec `:1440`); overflow beyond the cap only warns (`RaidClaimService.cs:323-331`). `grep 'cache' EndStateVM.cs` returns **nothing**. The one on-screen hint is the shortfall sentence at `RaidVictoryController.cs:962` / `EndStateVM.cs:501` — *"Some of the reward could not be paid out."* — which does not say where it went or that it is recoverable.

**2c. The screen already has the machinery to say both.** The band system exists and measures itself: the shortfall band is planned at `EndStateView.cs:1524` (`SummaryBandFirst`, sentinel `-1` at `:1532`), the trim reports itself at `:1902-1911`, and the overflow band already renders `"Showing " + (vm.Spoils.Count - more) + " of " + vm.Spoils.Count + " results"` at `:2017`. **Nothing structural has to be built.**

⚠ **Two adjacent things are NOT broken and must not be changed here:**
- The spoils trim is presentation-only — rows are built from `credited` (the measured wallet delta, `RaidVictoryController.cs:939-946`) and `EndStateVM.AddSpoil:661` only draws. **A dropped row never drops a resource**; the grant/bank/cache split is settled earlier in `RaidClaimService` (`Banked + Cached + Refused == amount`, `:314-322`).
- The `"Stone"` spoils label is correct and current: `EndStateVM.cs:653` `FoodSpoilLabel = "Stone"` with the owner ruling recorded at `:650-652` (*"we completely removed food"*), used at `:469`, `:611`, `:972`. Zero `"Food"` string literals remain in that file.

## 3. THE WORK

1. **A veterancy caption on the star row.** At `starsEarned < 3`, say what the third star would have granted. ⚠ **Coordinate with WO-1783 §4.2**, which also wants to state the 3-star capture gate on that same row — **one of the two lanes edits the star row, not both.**
2. **Name the cache.** Replace or extend the generic shortfall sentence so it says the overflow is held in the Raid Cache and is recoverable, rather than implying it was lost.
3. **Retire the literal.** `RaidDeployController.cs:1886`'s hardcoded `3` should read from the same source as `OwnedBaseProgression.CaptureStarsRequired` (`:20`) or a named const — two independent 3-star gates is duplicated state, the exact failure mode CLAUDE.md §2/§5/§8 each describe.
4. **Two stale internals to tidy while in the file** (no player impact, but they will mislead the next reader): `EndStateVM.cs:626`'s comment still claims *"WO-1374 sets this to 'Food'"*, superseded by the `:650` ruling; `:986`'s trace still prints `food={pay.Stone}`.

⛔ **HELD for the owner:** the exact wording of both captions.

## 4. ACCEPTANCE

- A device **screenshot** of a 2-star raid victory showing the veterancy caption.
- A device **screenshot** of a victory where the bank was full, showing the cache caption. (The 09-16 capture already produced the precondition: `[Flow:Bank] BANK FULL [Grant] Wood/Stone/Iron: requested N, banked N, LOST N`.)
- The owner accepts the wording by eye.
- `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs. ⚠ `en.json` is canonical JSON — patch from HEAD bytes, prove the LF count, update the StreamingAssets twin.

## 5. DO NOT TOUCH

The spoils trim logic, `RaidClaimService`'s bank/cache split, the `"Stone"` label, the honor thresholds (WO-1787 lane), the capture route (WO-1778), any `.unity` file.
