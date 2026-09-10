# WO-1571 - Manage BUILD door for a non-defence structure lands on the Build Collections root, a dead end

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Build / Manage (UI door + placement seam)
**Minted from:** `CLI_LANES_WO_NUMBERS.md` banner, hundred-and-eighth pass (1571 -> 1572, same edit)
**Source:** owner device build 358872, logcat 2026-09-07 00:58:40 - *"clicking BUILD on it takes me
back to build collection"*; frame `Logs/device/screens/owner-screen-20260907-005742.png`

## 1. THE DEFECT, FROM CAPTURED DATA

From Manage > BUILD > Cathedral of Magic (`arcane-tower`, `manageFilters: ["CRAFT"]`, NOT BUILT),
tapping the card's BUILD button logs:

```
[Flow:Navigation] opened workspace 'Build Collections' at root
[Flow:Build] BuildMode.Enter - palette shown
```

and stops there. The captured root frame offers three cards: **Towers**, **Walls & Gates**,
**Manage Placed**.

**This is a dead end BY CONSTRUCTION, not a missing tap** - and the reason is worse than "the data
authors no such collection". `card-collections.json` DOES author seven collections (Gathering /
Realm / Towers / Crafting / Storage / Walls & Gates / Trade), and `arcane-tower` sits in **Realm**.
Four of the seven are FILTERED OUT AT RENDER by
`BuildCollectionBrowser.CollectionHasVisibleItems` (`:613-628`), which drops a collection when
every item is a singleton reading `StructureSingleton.IsBuilt` - the predicate that COUNTS AN
ACTIVE BAKED TWIN. Realm / Crafting / Trade / Gathering are singleton-heavy rows with authored
`bakedTwins`, so the BAKE hides them. Recorded in
`WorkOrders/WORK_ORDER_1540_*.md` section 5, which owns that `IsBuilt` vs `IsPlayerBuilt` split.
**This ticket does NOT fix that** - it gives the row a door that does not depend on the root.

**Where the id is lost:** `ManageScreenVM.ComposeUnplacedItem` built the BUILD action as
`Invoke = () => OpenTownBuilderRequested?.Invoke()` - the command takes no argument, so `row.Id`
was discarded at the door. `ManageScreenPanel` bound that to `OpenTownBuilder`, which calls
`BuildModeController.EnterBuildMode(BuildType.Town)` -> `Enter()` -> `_palette.Show()` ->
`BuildCollectionBrowser.Show()` -> `Open(Root())`.

## 2. FIX

The BUILD door for a not-built row opens **placement for THAT id** - the ghost, armed - and never
the collections root.

- `ManageScreenVM`: new `Action<string> PlaceStructureRequested`; the BUILD action carries `rowId`.
  A private `RequestPlacement` keeps the §12 no-silent-failure rule (falls back to the legacy
  command with a `Warn`, and `Fail`s when neither is bound).
- `ManageScreenPanel`: binds it to a new `OpenPlacementFor(id)`.
- `BuildModeController.EnterBuildModeForStructure(string)`: derives the verb from
  `BuildFilter.Matches(entry, DEFENSE)`, enters, then forwards the pick.
- `BuildPaletteUI.PlaceById` -> `BuildCollectionBrowser.PlaceById`, which reuses the browser's own
  private `Place(entry)` / `Done(...)` - so the first-use guide step is committed and the browsing
  pause released exactly as a card tap does.

**GATES KEPT, NOT BYPASSED.** The offer gate is the browser's own
`IsCollectionItemVisible` (the ONE offer authority; it carries the WO-1379 first-raid and
`build-categories.json` lockedIds soft gates) and it is asked BEFORE anything closes, so a refusal
leaves the ordinary root standing. Singleton stays in `Arm`. **Affordability is deliberately NOT a
door test** - an unaffordable row still opens its ghost and gets the why-band at the placement
commit, exactly as the ticket asks.

## 3. WHAT NOT TO TOUCH
- `StructureCardVM.cs`, `Raid*`, `Dungeons/**`, `EnemyContent/**` - untouched.
- Do NOT delete `OpenTownBuilderRequested`. The "Need another town structure? / Open build" action
  rows are a legitimate browse door and still use it.
- Do NOT add an affordability refusal to the new seam.

## 4. ACCEPTANCE
- [x] The BUILD action carries the row id.
- [x] Direct placement routes through the browser's existing Place/Done commit, not a bare Close.
- [x] `IsCollectionItemVisible` gates the direct door, checked before anything closes.
- [x] Fixture case pins that every offered not-built row's BUILD face requests placement for its own
      id, and that the collections-root command fires zero times.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log (CLI gate lane).
- [ ] Owner felt-test: Manage > BUILD > Cathedral of Magic > BUILD raises the Cathedral ghost.
