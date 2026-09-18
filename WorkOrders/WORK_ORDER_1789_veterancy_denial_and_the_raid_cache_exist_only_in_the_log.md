# WORK ORDER 1789 — "You missed veterancy by one star" and "your overflow went to the Raid Cache" exist **only in the log**

**Status:** DONE - committed db7a84302, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW

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

---

## 4b. IMPLEMENTATION — written 2026-09-17, HELD before any Unity run

Code written; **no Unity process was started** (the lead gates the combined tree — several lanes open).
`python tools/gate_brace.py` on all four `.cs` files: `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0. Zero NUL
bytes and valid UTF-8 in all six touched files; both `en.json` twins parse and are byte-identical.

**§3.1 veterancy caption** — new `EndStateVM.StarCaption` field + `VeterancyDeniedCaption(int)`.
`EndStateView.BuildStarRow` gained an optional `caption` and seats it under the stars inside a new
`StarCaptionPx` (40) allowance. The band height is now `StarsBandPx(vm)` at **all four** sites that
read `StarsPx` (`RequiredBodyPxAtRows`, `NarrativeStripPx`, the strip's `stackedPx`, `bands.Add`), so
the solve and the layout cannot disagree. A VM with no caption returns exactly `StarsPx` — every
shipped screen budgets and lays out to the pixel it does today, and `EndStateBodyFitRegression`'s
fixtures (which never set `StarCaption`) are unaffected. The stars are **grown around**, never shrunk.

**§3.2 name the cache** — `RaidVictoryController` now measures `_overflowCached` / `_overflowLost`
right after STEP 3.5b and `EndStateVM.RaidOverflowSentence` picks between **three** sentences: held in
the Raid Cache (recoverable), both ceilings hit (part genuinely gone), or the existing generic line
when the shortfall was on an **uncapped** axis (crystals/gold are never cached — `TownBankCapacity`
Law 1, and `_rewardShort` is set by all five axes). "Recoverable" is only ever said when it is true.

**§3.3 literal retired** — `RaidDeployController.VeterancyStarsRequired`, valued from
`OwnedBaseProgression.CaptureStarsRequired`. The gate **and** both trace strings read it; the caption
reads the same const.

**§3.4 tidy** — the `FoodSpoilLabel` doc block carries a `⚠ SUPERSEDED` banner (body kept: it records
an owner-level conflict); the wave-clear trace now prints `stone=`.

**⚠ STAR-ROW COORDINATION WITH WO-1783 — RESOLVED, no collision.** WO-1783's lane is editing
`EndStateVM.cs` concurrently and put its capture-gate sentence in the **SUBTITLE**
(`CaptureRequirementSentence` appended to `body`), not on the star row — read in the working tree
2026-09-17. **This lane owns the star row**; 1783 must not take it. Anything else that wants the row
sets `StarCaption`. Its `en.json` key landed in the same region during this edit and survived.

**⛔ THE WORDING IS PROPOSED, NOT RULED** (§3's hold stands). All three sentences are `en.json` rows
(`raid.veterancyDenied`, `raid.cacheHeld`, `raid.cacheFull`) with plain-English code fallbacks, so the
owner's re-wording is a table edit with no code change:
- "A 3-star clear promotes every surviving troop."
- "The bank was full - the overflow is held in your Raid Cache until you make room."
- "The bank was full and the Raid Cache is at its limit - part of the haul could not be kept."

**Known degrade, stated rather than discovered:** in the WO-952 narrative-STRIP escalation the caption
lands in a ⅓-width cell. `FitSingleLine` ellipsizes at `ElarionUiKit.FontFloor` (30) rather than going
sub-legible, so it shortens, never vanishes or overprints. Judged by eye at §4.

### 4c. LOCALE PARITY — the gate's second half, closed 2026-09-17

The combined-tree gate flagged `LOCALE PARITY` + `SMART ARGUMENT`: the new keys existed in English
alone. **All 4 keys are now in all 9 non-base locales, both copies** —
`Assets/{Resources,StreamingAssets}/Data/Canonical/{es,pt-BR,de,fr,ru,ar,ja,ko,zh-Hans}.json`
(18 files). `ownedTown.captureRequirement` is **WO-1783's key**, carried here only because the audit
fails on the whole table; that lane still owns its English wording.

**Convention — read at source, not invented.** `Assets/Editor/Localization/LocalizationPolicy.json`
gives every non-base locale `status: "ai-first-draft"`, and the live files carry real translated copy
(`ownedTown.captureRetry` is translated in every locale). There is **no needs-translation marker** in
this project, and English text appears in a locale file only for proper nouns
(`heroSelect.title` = "Defenders of the Realm"). So these are AI first-draft translations, matching the
terminology already established in `hud.heart.objective.*`, `feedback.*`, `ownedTown.saleQuote` and
`armyScreen.armyFull`. Russian parenthesises the count (`({0} звёзд)`) so the sentence needs no case
agreement with the argument.

Verified by a port of both audits' own rules (`LocaleParityRegression.cs` + `SmartArgumentRegression.cs`,
no Unity): per locale `missing=0 extra=0 empty=0 argMismatch=0 mirror=True`, and each new key's
placeholder multiset is identical across all 10 locales (`{0}`×1 for the two star lines, none for the
two cache lines). Line terminators preserved per file (502/499 → 506/503, the 3 bare LFs intact); zero
NUL bytes; Resources and StreamingAssets byte-identical per locale. The manifest needs no entry —
`docs/localization/manifest.json` has `reviewed != true`, so its declarations are not binding
(`SmartArgumentRegression.cs:140-142`).

**⛔ STILL RED UNTIL A UNITY STEP THE LANE IS HELD FROM RUNNING.** `LocaleParityRegression.cs:196-206`
also requires, for every `enabledInBuild` locale (en, es, pt-BR, de, fr, ru), that
`Assets/Localization/Tables/GameStrings_<locale>.asset` + `GameStrings Shared Data.asset` equal
canonical exactly — its own comment: *"A JSON-only change must stay red until every enabled table is
rebuilt."* Those assets are git-clean and contain **none** of the 4 keys (grep = 0). The regenerator is
**`DeNelle.Editor.LocalizationBuilder.BuildAll`** (menu *Defenders > Week 1 > Build Localization*,
`Assets/Editor/LocalizationBuilder.cs:96`). **The lead must run it with the gate.**

Still owed by §4: the two device screenshots and the owner's acceptance by eye.

## 5. DO NOT TOUCH

The spoils trim logic, `RaidClaimService`'s bank/cache split, the `"Stone"` label, the honor thresholds (WO-1787 lane), the capture route (WO-1778), any `.unity` file.
