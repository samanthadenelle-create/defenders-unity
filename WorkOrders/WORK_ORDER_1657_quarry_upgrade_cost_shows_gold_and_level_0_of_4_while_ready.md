# WORK ORDER 1657 — Quarry detail shows a **Gold** upgrade cost and reads "Level 0 of 4" while status is READY

**Status:** READY TO IMPLEMENT
**Silo:** Economy / Manage detail-card VM + building-tier data
**Opened:** 2026-09-10 by the DEVICE-FRAMES-2 lane
**Source:** device play-mode session, APK **2026.09.10.363722**, Seeker `SM02G4061955851`
**⚠ THIS TICKET CONTAINS AN OWNER QUESTION. Item A cannot be implemented until it is ruled.**

---

## 1. THE FRAME

`Builds/device-frames/2026-09-10_0915b_363722_build_detail_quarry_placed.png`
(2670x1200 landscape, opened by the capturing lane). Route: town dock MANAGE -> BUILD -> ECONOMY ->
Quarry tile. The card reads, verbatim off the frame:

- Title **QUARRY**, status chip **READY**
- **`Level 0 of 4`**
- "Extracts Stone for your town over time." / "Stone production +10%."
- `Production / hr    1,872 -> 2,016`
- `Next level    Stone production +10%.`
- **`Upgrade Cost`  →  `820 Wood`  and  `1150 Gold`**, plus `57s`
- One action face: `UPGRADE`

Two separate observations on one card. They are filed together because one frame carries both; they
are **not** assumed to share a cause.

---

## 2. OBSERVATION A — Gold is in a regular structure's upgrade basket

### The canon it appears to contradict

**WO-947 cost-basket separation:** regular structures cost **wood + iron**; magical structures cost
**crystals**. Gold is in neither basket for a structure upgrade.

### What the data actually says — read at source 2026-09-10

- The **catalog row agrees with WO-947.** `Assets/Resources/Data/Canonical/structures-catalog.json`,
  the `collector_farm` row (displayName **"Quarry"**, `manageArtKey` `building-quarry`, role
  `stone_producer`), authors `repo.cost` as **`wood: 240, food: 0, iron: 80, crystals: 0`**. No gold
  field exists on that shape.
- `BuildModeController.UpgradeCostFor` (`Assets/_Modules/Village/BuildMode/BuildModeController.cs:2951-2972`)
  returns a `DeNelle.Core.Catalog.ResourceCost` carrying only `wood / food / iron / crystals`. **It
  cannot emit gold.**
- ⭐ **The gold line is added by the Manage VM, deliberately.**
  `ManageScreenVM.BuildingUpgradeCostParts` (`Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2185-2194`)
  composes the rows and appends gold explicitly:

  ```
  return CostFormat.Parts(new[]
  {
      ("wood", "Wood", cost.wood), ("stone", "Stone", cost.food),
      ("iron", "Iron", cost.iron), ("crystal", "Crystals", cost.crystals),
      ("gold", "Gold", tier != null ? tier.CostGold : 0)
  });
  ```

  Called at `ManageScreenVM.cs:1648` (`UpgradeCostParts = isMax ? Array.Empty<CostPart>() : BuildingUpgradeCostParts(next)`).
  The value comes from `BuildingTierDef.CostGold`, authored in
  `Assets/Resources/Data/Canonical/building-tiers.json` — **a different data source from
  `structures-catalog.json`**, which is why the catalog row can be WO-947-clean while the card is not.
- ⛔ **A regression already PINS the gold line as required.**
  `Assets/Editor/Regression/BuildingUpgradeRegression.cs:593-594` fails with:
  *"[shortfall-named][gold-line] BuildingUpgradeVM no longer emits the Gold (CostGold) cost line -
  CanAffordTier checks Coins >= CostGold, so the page would again read MissingResources with no short
  line to name"*. So gold is not a stray row: an affordability check reads it, and a test defends it.

### ⚠ THE OWNER QUESTION — this is a RULING, not a defect, until Samantha says otherwise

Two mutually exclusive readings, and the code is self-consistent under both:

- **Reading 1 — WO-947 governs, and this is a DATA defect.** Resource-building upgrades should not
  charge gold; `CostGold` for these rows in `building-tiers.json` should be 0, and the display follows.
- **Reading 2 — WO-947 governs BUILD cost only, and the upgrade LADDER legitimately charges gold.**
  `CanAffordTier` already gates on it and `BuildingUpgradeRegression:593` defends it, which is what a
  deliberate design looks like rather than an accident.

⛔ **DO NOT PICK ONE.** Under Reading 1 zeroing `CostGold` breaks a pinned regression and changes live
economy pricing on a game that is shipping on the Solana dApp Store. Under Reading 2 the correct
change is zero code and a canon line. **Route to the owner before any edit** (§13: PO rules, CLI
implements).

---

## 3. OBSERVATION B — "Level 0 of 4" on a placed, READY building

The same frame reads **`Level 0 of 4`** while the status chip reads **READY** and the card offers a
live `UPGRADE` face with a real cost and a real `Production / hr  1,872 -> 2,016` delta. A placed,
producing building presenting as level **zero** reads as un-built to a player, and it disagrees with
the sibling rail phrasing, which writes `"Level " + choice.Level` with no "of N"
(`ManageScreenPanel.cs:5024` and `:6027`).

⚠ **The exact producer of the `Level N of M` head on this detail card was NOT pinned by this lane.**
Located candidates, none proven to be the site that rendered this string:

- `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs:971` —
  `word + " " + cur + " of " + max`
- `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:1840` —
  `"Raises " + name + " to Level " + nextLevel + " of " + ceiling + "."` (next-description, not the head)

**Discriminating step, to run first:** instrument or breakpoint the Manage detail-card head
composition for `collector_farm` and capture what supplies `cur`. Then decide between:

- **B1 — an off-by-one display bug:** the ladder is 1-based (the frame's own "of 4" implies levels
  1..4) and a 0-based index is reaching the head.
- **B2 — a genuine level-0 state:** a placed collector really does sit at level 0 before its first
  upgrade, and the *phrasing* is what is wrong (a level-0 building should read e.g. "Not yet
  upgraded", never "Level 0").

B1 and B2 have different fixes and B2 may itself be an owner phrasing call. Do not assume B1.

⛔ **`RepoProps.MaxStructureLevel` (`Assets/_Modules/Core/Catalog/RepoProps.cs:111`) is the SINGLE
ceiling.** Whatever supplies the "4", do not introduce a second hardcoded ceiling to fix the display.

---

## 4. ACCEPTANCE

1. **Observation A is RULED by the owner** and the ruling is written into this WO, naming Reading 1 or
   Reading 2. No code changes before that line exists.
2. If **Reading 1**: `CostGold` is corrected in `building-tiers.json` for the affected rows, and
   `BuildingUpgradeRegression.cs:593-594` is re-pointed **in the same commit** with its comment
   updated to record the ruling that superseded it (§15) — never deleted silently.
   If **Reading 2**: no code change; the ruling is added as a canon line clarifying that WO-947's
   wood+iron basket governs **build** cost and the upgrade ladder additionally charges gold. Close A
   as no-op with that citation.
3. **Observation B: the head's producer is named with file:line, from a run, not from a grep** — §12
   applies. The B1/B2 verdict is recorded before the fix.
4. **A frame proves the result:** a fresh device or headless frame of the Quarry detail card showing
   the corrected cost rows and the corrected level line. Open the PNG.
5. **RED first** on whichever branch lands — a case that fails against today's tree. Pin the **composed
   VM cost parts** (the `ManageScreenVM.cs:2185` output), not the rendered string; a string pin
   re-breaks the moment the rows are reordered.
6. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.
7. `**Status:**` -> `AWAITING OWNER MATCH` per WO-1566 §2.0 — a device frame judged by the owner is the
   verdict, not this list.

---

## 5. WHAT NOT TO TOUCH

- ⛔ **NEVER rename `collector_farm`.** The catalog row's own `_quarryNote` states it: it is a LIVE
  SAVE KEY (`everBuiltStructureIds`, `BaseLayout` records, `repo.bakedTwins`, `repo.collectorBuildingId`
  `'farm'`, the PlayerPrefs level key behind `ResourceBuildingState`) and the game is live on the
  Solana dApp Store — renaming orphans every existing town. Only the ROLE and the RESOURCE moved.
- ⛔ **A `HarvestResource.Food` site in C# is a STONE site, not a bug.** Same note: `Food` is the frozen
  persisted STONE wallet slot, and `ResourceBuildingProgression.LabelFor` is the ONE place that turns
  it into the player's word. Do not "fix" `Food` to `Stone` anywhere.
- ⛔ **Do not delete `BuildingUpgradeRegression.cs:593-594`** to make a Reading-1 change pass. Re-point
  it with the ruling, or the next seat re-adds the gold line believing the test was noise.
- ⛔ **Do not re-hardcode a level ceiling** (§8) — `RepoProps.MaxStructureLevel` is the single source.
- Do not touch `Production / hr`, the storage caps, or the harvest/overflow path. WO-1653's production
  table renders correctly on this very frame and is not in scope.
- Do not hand-edit `.unity` scenes; canonical JSON edits are **binary-mode only**, patched from HEAD
  bytes with the LF count proven (text-mode rewrites have flattened these files before).

---

## 6. WHAT IS UNPROVEN

- That the gold row is wrong at all — §2 shows the code is self-consistent and defended by a test.
  The conflict is between a canon sentence and a live pinned behaviour; only the owner resolves it.
- The producer of the `Level 0 of 4` head (§3) — two candidates located, neither proven.
- Whether other resource buildings share both symptoms. This lane framed **only** the Quarry; Lumber
  Mill and Forge also read READY on
  `Builds/device-frames/2026-09-10_0914c_363722_build_economy_grid.png` and were not opened. Check
  them before scoping the fix to one row.
