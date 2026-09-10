# WO-1570 — Lumber Mill upgrade cost names STONE and GOLD; the retired-key premise is wrong, the View is the defect

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Build/Economy (edit-only lane) · **Capture:** build 358872, `Logs/device/screens/owner-screen-20260907-004903.png`

## Symptom
The Lumber Mill detail card renders `2600` and `970` as bare numbers with no resource
words, and its upgrade button reads `UPGRADE . STONE 2600 GOL...`, truncated mid-word.

## Diagnosis (read at source 2026-09-07 — nothing below is inferred)

**The premise "retired keys reach the button" is FALSE on every count.**

1. **The data row is `Assets/Resources/Data/Canonical/building-tiers.json:67`** —
   `"tier": 2, "costWood": 2600, "costGold": 970, "costCrystal": 0`. Both are LIVE
   authoring keys (`BuildingTierCatalog.cs:61-62`). The legacy `costFood` alias
   (`BuildingTierCatalog.cs:64`, which deserializes into Gold) matches **0** rows in
   either canonical twin, so no row is silently priced through it.
2. **"Stone" is not retired.** It is the live player-facing word for the *Food* wallet
   slot — `TownBankCapacity.DisplayName` / `WordOf` (`TownBankCapacity.cs:332,369`),
   `HudKitController.cs:3024`. WO-1416 retired FOOD *as a resource*; Stone reuses the
   frozen persisted slot. "Gold" is `BankResource.Coins`. Both live.
3. **The tier-2 -> Stone charge lane is owner-ruled, not a bug.**
   `BuildingTierChargeLane.For` (T1 Wood / T2 Food / T3+ Iron, by tier NUMBER) is
   **OWNER RULING 22 (WO-2005, 2026-09-06): "the CHARGE is right and the AUTHORING is
   wrong."** The button honestly names the lane the wallet is debited in.
4. **The screen is the MANAGE detail, not a StructureCardVM surface.**
   `ManageVmProjection.cs:306` ("Level N of M") + `ManageScreenVM.cs:4859`
   ("Upgrade time"). Catalog row `lumbermill` authors no `maxLevel`, so
   `StructureCardVM.HasNextTier` is false for it and `NextTierCost` is never 2600/970.

**Spend path: NOT affected.** `BuildingUpgradeService.TryUpgrade` debits
`state.Resources.Coins -= 970` and `ResourceLedger.TrySpend(TierCost(def))` = Stone 2600.
The player is charged exactly what the button says. No retired resource is spent.

**The real defect (another lane).** `ManageScreenVM.CostVms` (:5070-5085) builds
`ManageCostVM.Label` correctly ("Stone", "Gold") but sets `IconKey = null`;
`ManageWorkspacePanel.BuildCostRow` (:1288-1291) paints only the sprite and
`c.AmountText` and **never draws `c.Label`** — so with no icon the resource identity is
nowhere on screen, which is a colorblind-law breach. The CTA face is `Cta = "UPGRADE"`
(`ManageScreenVM.cs:4299+`) with the price concatenated from `CostLine`, which is what
truncates. Handed back with file:line; Manage is excluded from this lane.

**No data oracle was missing.** `CostBasketSeparationRegression` already walks every
structure's every `repo.upgradeCost` step (`[invariant]`/`[regular]`, :759-828) and
every `building-tiers.json` tier (`[tiers-basket]`, :384-440). Adding a second walk
would be the duplicated-oracle shape this repo forbids.

## Change
- `Assets/_Modules/Village/BuildMode/StructureCardVM.cs`
  - Exposes for a detail layout: `CurrentLevel`, `EffectiveCostParts`,
    `NextTierCostParts` (`(word, amount)` rows), `UpgradeStats`
    (`(Label, Current, Next)`), `UpgradeSeconds`, `UpgradeTimeText`,
    `UpgradeButtonLabel` (one word). `MaxLevel` / `Description` / `CurrentStats`
    already existed.
  - Every resource word comes from `TownBankCapacity.DisplayName` — no literal. The
    pre-existing hand-typed `("stone", "Stone", c.food)` tuple in `PlacementCostWords`
    was re-pointed at the same speller; output is identical (`CostWords` is read by
    `CostBasketSeparationRegression.cs:662`, so identical output was required).
  - `UpgradeStats` stays EMPTY when no stat is measured — no invented player copy.
  - `NextTierStats` is now DERIVED from `UpgradeStats`, so table and sentence cannot drift.
  - `UpgradeSeconds` runs the same two steps `BuildTimerService.StartUpgrade` runs
    (tier = max(0, targetLevel - 2), then the config curve for `BuildJobKind.Upgrade`);
    grace deliberately not applied (`GraceAdjustedDurationMs` returns early for upgrades).
  - `FlowTrace.Once("Build", "upgrade-basket-<id>")` names the non-zero cost KEYS, the
    formatted words, the seconds, and the authoring file — and states that
    `building-tiers.json` is a *different* authority with a different charge rule.
- `Assets/Editor/Regression/BuildEconomyRegression.cs`
  - New `[card-cost-words]` case: the detail-layout fields must exist, the cost words
    must come from `TownBankCapacity.DisplayName`, and `UpgradeButtonLabel` must stay
    the bare word `"UPGRADE"`.

## Not touched (deliberate)
`building-tiers.json` (both twins), `BuildingTierChargeLane`, `BuildingUpgradeService`,
`BuildingUpgradeVM` — all covered by owner ruling 22. Manage panels/VM, Raid*,
Dungeons/**, EnemyContent/**, `DataRegression.cs`.

**No canonical JSON was edited**, so no byte-safe rewrite / LF proof was required.

## Acceptance
- `COMPILE_GATE_OK`
- `BUILDECON_OK` with `[card-cost-words]` on the log
- `COST_BASKET_OK` unchanged (no data moved)
- Owner felt-verifies the Manage card only after the Manage lane draws `ManageCostVM.Label`.

## Follow-up for the Manage lane
1. `ManageWorkspacePanel.BuildCostRow` must render `c.Label` (words carry the state).
2. `ManageScreenVM.CostVms` sets `IconKey = null` — either resolve an icon key or the
   label is the only identity available.
3. Keep the price out of the CTA label; it belongs beside the button.
