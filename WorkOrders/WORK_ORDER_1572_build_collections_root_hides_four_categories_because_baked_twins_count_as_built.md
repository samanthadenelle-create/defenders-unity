# WO-1572: Build Collections root hides four categories because baked twins count as built

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Village/BuildMode - `BuildCollectionBrowser` + `StructureCardVM` + the two collection suites.
**Source:** WO-1540 section 5, written 2026-09-07 by the WO-1571 lane. Minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1572 -> 1573 in the same edit).

## 1. EVIDENCE (all re-read at source 2026-09-07)

- `Assets/Resources/Data/Canonical/card-collections.json` authors **SEVEN** active `context:"build"`
  collections: `build-gathering` / `build-realm` / `build-defenses` / `build-crafting` /
  `build-storage` / `build-protection` / `build-trade`.
- The owner's root frame `Logs/device/screens/owner-screen-20260907-005742.png` shows **THREE**
  cards: Towers, Walls & Gates, Manage Placed.
- The filter is `BuildCollectionBrowser.CollectionHasVisibleItems` (`:613`, called at `:139`). It
  dropped a collection when every item was a singleton reading
  `StructureSingleton.IsBuilt(entry)`.
- `StructureSingleton.IsBuilt` (`StructureSingleton.cs:120-147`) is a UNION whose **step 2** is
  `GameObject.Find(bakedName) != null` over `repo.bakedTwins` - an **ACTIVE BAKED TWIN counts**.
- `structures-catalog.json` (`entries[]`, read 2026-09-07): **every** item of `build-realm`
  (`barracks` -> `CastleBarracks`, `pet-house` -> `EchoHollow_Pets_RoamingArea`, `arcane-tower` ->
  `ArcaneTower_MagicUpgrades`) and **every** item of `build-trade` (`market` ->
  `Marketplace_Monetization`, `forge` -> `Blacksmith_Weapons_Storefront`, `armorer` ->
  `Forge_Armor_Storefront`) authors a `bakedTwins` entry. Both categories therefore vanish from the
  root on any save where the bake surfaced its twins, with nothing on screen saying why.
- The distinction already exists and the ARM path already uses it:
  `StructureSingleton.IsPlayerBuilt` (`:179-183` -> `HasPlacedInstance`, which excludes bodies under
  a baked twin), asked by `BuildModeController.IsSingletonBuilt` (`:2334-2346`) since WO-843. So the
  arm path judged the row BUILDABLE while the browser hid the door to it.

## 2. FIX

Every **player-facing offer/visibility** surface asks `IsPlayerBuilt`. `IsBuilt` stays the
**enforcement / capability** query. A surfaced twin means the item is still OFFERED; placing it
stands the twin down via `NotifyPlaced -> Enforce -> StandDownBakedTwins`.

## 3. WHAT NOT TO TOUCH
ManageScreenVM/Panel, Raid*, Dungeons, EnemyContent, Harvest*, repair code. The Arm() guard needed
no change - it was already correct.

## 4. ACCEPTANCE
- [x] The three player-facing sites ask `IsPlayerBuilt`; every other `IsBuilt` consumer is named
      with a verdict.
- [x] The `Arm()` guard is verified, not assumed, and left alone.
- [x] A live fixture case: zero placed structures + every baked twin standing -> all seven
      collections shown and `arcane-tower` offered. RED against the old predicate.
- [x] A `FlowTrace.Step` names each collection's visible-item count and why.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log (gate lane).
