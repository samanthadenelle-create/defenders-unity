# WO-2007 — RESULT (lane MANAGE-DOOR, 2026-09-09)

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane MANAGE-DOOR); owner device match closes
**Scope closed by this lane:** the RED `MANAGE_BUILD_DOOR_FAIL` case `[placed-no-ladder-is-built]` ONLY.
The WO's other four acceptance items (no duplicate state copy, no dead MAX LEVEL action, no false lock
on owned buildings, no enabled upgrade CTA while queue-blocked, prerequisite CTA supplied by model)
were **not** exercised or verified by this lane and are not claimed.

---

## 1. The RED, read at source

`Builds/ready-rca-checkpoint-regression.log` (2026-09-09 14:26, NULs stripped for the read):

```
MANAGE_BUILD_DOOR_FAIL
  [placed-no-ladder-is-built] 'pet-house' is in BaseLayout but Manage still offers BUILD; the
      placement gate will contradict it as Already built
  [placed-no-ladder-is-built] 'pet-house' did not project the BUILT word
  ... same pair for 'market' and 'workshop'
=== ManageBuildDoorRegression ===
build tiles=16 BUILD doors=12 (non-defence=8) root hits=0 placed no-ladder BUILT=3/3
```

⚠ `placed no-ladder BUILT=3/3` is the count of ids **checked**, not of ids that projected BUILT — the
suite's own `ownedNoLadderChecked` set. It is not evidence of a pass and is not cited as such.

## 2. RCA — proven from source at HEAD 184c8ff06

`ManageScreenVM` had **two** build-state paths, and only one of them asked whether the structure was
already standing in this town:

| path | site | asks ownership? |
|---|---|---|
| the BUILD **grid tile** | `ComposeBuildItem` (`ManageScreenVM.cs:4386`) | YES — `IsPlacedThisTown(row.Id)` → `ComposeOwnedNoUpgradeItem` |
| the BUILD **detail card** | `ComposeSelection` `default:` case, the `InventoryRowById` tail | NO — went straight to `ComposeUnplacedItem(row)` |

So a placed **no-ladder civic singleton** — `market`, `workshop`, `pet-house`, all three physically
recorded in `GameState.BaseLayout` and none of them authoring an upgrade ladder — fell past
`BuildingChoiceFor` (null) and `DefenseChoiceFor` (null) on the card branch and was composed as
NOT BUILT with a live `BUILD` action. Its **tile** meanwhile said BUILT. The card then handed the
player a button the placement gate refuses as *"Already built"*.

This reproduces **from source at HEAD**, independent of what tree the 14:26 run compiled: the
`default:` branch tail carried no ownership test at all. The log-vs-commit timing (`f4e4630e3` landed
14:04, log mtime 14:26, log carries no wall-clock stamp) is **not resolvable from here and is not
relied on**.

## 3. The change

**One file:** `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` — the `InventoryRowById` tail of
the `default:` case in the detail composer (+31/-4 lines, comment-heavy).

```csharp
bool placedNoLadder = IsPlacedThisTown(row.Id);
item = placedNoLadder ? ComposeOwnedNoUpgradeItem(row) : ComposeUnplacedItem(row);
description = Ascii(row.Description);
FlowTrace.Step("Manage", "build detail '" + row.Id + "' projects the word '" + ... );
```

- **MVVM strict:** the decision and the word are composed in the VM. No View file touched, no
  game-state read moved into a View.
- **No new decider.** The shared decider is the **ownership** one, `IsPlacedThisTown` — the same
  private helper `ComposeBuildItem` calls, reading the same per-town `BaseLayout` truth. **No
  family-resolution or upgrade-start site was added**, per `docs/ARCHITECTURE.md` §6 (read at source
  2026-09-09, `:213-221`: *"UpgradeFamilyResolver is the single decider … Never add a second
  family-resolution or upgrade-start site."*).
- **Every FlowTrace kept**; one `FlowTrace.Step` added that names the projected word AND the reason,
  per structure, on both branches.

### Deviation from the lane brief, stated rather than buried in the diff
The brief said to fix *"through `UpgradeFamilyResolver` as the single decider."* **I did not, and the
reason is that `UpgradeFamilyResolver` cannot answer this question.** Its API is
`Resolve(string buildingId) → UpgradeFamily` / `IsDualFamily` / `LadderName`
(`Assets/_Modules/Village/Buildings/Progression/UpgradeFamilyResolver.cs:57,70,78`) — a **ladder
family**, not ownership. These three rows have **no ladder at all**; the question the card got wrong
is *"is one already standing in this town"*, which is `BaseLayout` truth. Routing an ownership
question through a family resolver would have been the second decider §6 forbids, dressed as
compliance with it.

### Why `ComposeBuildItem` was NOT called wholesale (the near-miss)
The obvious one-liner — `item = ComposeBuildItem(row)` — was written, then withdrawn. It opens with
`BuildingChoiceFor(row.Id, row.TierLadderId)` (`:4388`), and `TierLadderId` is the **family** id from
`CatalogRegistry.ResolveUpgradeId` (`BuildInventoryModel.cs:302-305`), which need not equal `row.Id`.
`BuildingChoiceFor` matches on **either** argument (`:4229-4232`), so it can return a
`BuildingChoiceVM` that the card branch's own `BuildingChoiceFor(nav.ItemId, nav.ItemId)` already
missed — and that earlier branch is the **only** one that fills `stats` / `costs` / `costCaption` /
`timeText`. A laddered building would then have rendered with an empty cost band. Documented in-code
at the change site so it is not "simplified" back later.

## 4. The assertion that flips, and why

`Assets/Editor/Regression/ManageBuildDoorRegression.cs:198-202`, inside `CheckLiveDoor`, for each id
in `ownedNoLadder = { "market", "workshop", "pet-house" }`:

```csharp
if (action != null && string.Equals(action.Label, "BUILD", StringComparison.Ordinal))
    failures.Add("[placed-no-ladder-is-built] '" + id + "' is in BaseLayout but Manage still offers BUILD; ...");
if (selection == null || !string.Equals(selection.StateText, "BUILT", StringComparison.Ordinal))
    failures.Add("[placed-no-ladder-is-built] '" + id + "' did not project the BUILT word");
```

The suite's fixture puts all three in `BaseLayout` via
`new PlacedStructureData("market", 4, 2, 0, 1)` — arg 1 is `itemId`
(`Assets/_Modules/Core/State/PlacedStructureData.cs:76-79`), which is exactly the field
`IsPlacedThisTown` compares (`ManageScreenVM.cs:4404-4406`, `OrdinalIgnoreCase`). So the new branch
takes `ComposeOwnedNoUpgradeItem`, and:

- **second assert** — `ComposeOwnedNoUpgradeItem` sets `BadgeText = "BUILT"`
  (`ManageScreenVM.cs:4424`), and `ManageVmProjection.ProjectSelection` sets
  `StateText = item.BadgeText` (`Assets/_Modules/Core/Manage/ManageVmProjection.cs:326`) with
  `Visible = true`. → `StateText == "BUILT"`.
- **first assert** — `ComposeOwnedNoUpgradeItem` adds **no** `ManageAction`, so `item.PrimaryAction`
  is null, so `ProjectAction(null, …)` returns `ManageActionVM.Hidden`
  (`ManageVmProjection.cs:97`; `Hidden` is `{ Visible=false, Enabled=false }`,
  `ManageViewContract.cs:151`, `Label` defaulting to null). → `action.Label != "BUILD"`.

Both `[placed-no-ladder-is-built]` adds are therefore unreachable for all three ids, and the third
guard (`ownedNoLadderChecked.Count != ownedNoLadder.Count`) is untouched — the ids still appear in
`tileIds` and are still visited.

### Blast radius checked
- Only three suites call `OpenDetail(ManageTabId.Build, …)`:
  `ManageBuildDoorRegression`, `ManageNavigationRegression` (synthetic ids `alpha`/`beta`, which
  resolve no inventory row and are unaffected), `ManageProgressiveDisclosureRegression` (a **laddered**
  mill, which exits at the `BuildingChoiceFor` branch **above** the change and never reaches it).
- The BUILD-door cases `[build-door-carries-the-id]` / `[non-defence-row-has-a-door]` /
  `[build-door-never-lands-on-root]` are untouched: unplaced rows still take `ComposeUnplacedItem`
  with `Invoke = () => RequestPlacement(rowId)`.

## 5. Gate evidence

- **Braces:** `ManageScreenVM.cs` — 449 open / 449 close, balanced.
- **NUL bytes:** 0.
- **No Unity run.** This is an **edit-only** lane: no batchmode, no gate, no `git add`, no commit. The
  lead gates.

## 6. Unproven — stated, not ticked

1. **The suite has not been run.** Section 4 is a source-level derivation through four files opened
   this session; it is not a green marker. Only `REGRESSION_OK <n>/<n>` on a fresh log proves it.
2. **Compilation is unproven** (no compile gate fired from this lane).
3. **The owner device match** for WO-2007's detail panel is not attempted here and remains the
   closing condition.
4. **The other RED suite** in the same log — `[hero-element-cast]` — is a different lane and untouched.
5. **The four remaining WO-2007 acceptance items** are not verified; the status flip claims the RED
   fix only.
6. **Whether the 14:26 run compiled `f4e4630e3`** cannot be determined from the log (it carries no
   wall-clock stamp). Immaterial to the RCA, which reproduces from HEAD source.

---

*Lane MANAGE-DOOR, edit-only. Files changed: `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs`,
`WorkOrders/ManageRedesign/WO-2007_BUILD_DETAIL_AND_UPGRADE_ACTION.md` (Status), this file.*
