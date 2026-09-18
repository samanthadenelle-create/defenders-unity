# WORK ORDER 1863 — Harvest result offers UPGRADE on a storage container that is already at max level

**Status:** DONE
**Silo:** Economy / Harvest UI (`DeNelle.Core.UI` + `DeNelle.Core.Economy` read-only)
**Raised:** 2026-09-18 — live owner report during felt-testing (verbal, not F8-flagged)
**Owner quote, verbatim:**

> "when you go to harvest it says upgrade lumber, mill foundry in stoneyard, but if you're at max
> level, it shouldn't do that"

---

## 1. What the player saw

On the HARVEST RESULT card, a blocked resource row carries one action chip. With a **Lumberyard at
level 6 — its authored ceiling** — the chip still read **`UPGRADE LUMBERYARD`**. Tapping it routes to
Manage ▸ Buildings, where there is no upgrade to buy. The same applied to the Foundry (iron) and the
Stoneyard (stone, catalog id `silo`) — the three storage containers the owner named.

## 2. Root cause — PROVEN BY CAPTURE, not by reading code

Not a wrong comparison, not an off-by-one, and not a stale cached level. **The ceiling never reached
the decision at all.**

`HarvestResultVM.Build` took its one live signal as `Func<BankResource, int>` — a **container COUNT** —
and chose the verb with `built > 0 ? "UPGRADE" : "BUILD"`. A level-1 container and a level-6 container
are the same integer at that point. The level existed one field away the whole time
(`TownBankCapacity.StorageSlot.Level`, written from the layout record at `TownBankCapacity.cs:1020`)
and `HarvestOverflowModal.BuiltContainers` walked straight past it while counting.

Captured proof, `Builds/wo1863-red.log`. **Provenance, stated plainly:** this is not literal HEAD — the
new `StorageGrowthFor` reader and its trace were added FIRST (that is what §12 asks for), and HEAD's verb
expression `s.OverCap ? "SPEND" : (built > 0 ? "UPGRADE" : "BUILD")` was then run against it. So the run
shows the count-only rule, fed real slot data in which `level == ceiling`, emitting UPGRADE — because
that rule has no level input at all:

```
[Flow:Bank] harvest-result growth signal Wood (Lumberyard): built=1 levels=[6] topLevel=6 rowMaxLevel=6 -> canUpgrade=False canBuild=False maxedOut=True
[Flow:Bank] harvest-result door Wood: state='FULL' overCap=False built=1 topLevel=6 maxLevel=6 canUpgrade=False canBuild=False maxedOut=True -> verb=UPGRADE text='UPGRADE LUMBERYARD'
```

HEAD never fetched the level; the "known" line above is the new instrumentation, and it exists to show
what the decision had available to it and did not use. The defect is that **the verb rule has no level
input**, which is why no comparison in it could ever have been wrong.

## 3. Ruling applied — a maxed container's door says SPEND

Owner ruling 23 (2026-09-06), recorded verbatim in the `_singletonNote` on the storage rows of
`structures-catalog.json`: *"also cap only one of each storage type, the idea is they should level
them."* All three rows carry `singleton: true` and `maxLevel: 6` (= `RepoProps.MaxStructureLevel`).

So a container at its ceiling has **no growth on either axis** — no upgrade, and no second container to
build. `BUILD` would be the same lie pointed elsewhere. The honest verb is **`SPEND <RESOURCE>`**, which
is exactly what the existing WO-1099 over-cap branch already uses for "storage is not the fix", and it
keeps the row's door instead of leaving a blocked row with no instruction (the WO-1525 dead end).

## 4. Change

| File | Change |
|---|---|
| `Assets/_Modules/Core/UI/HarvestResultVM.cs` | New `StorageGrowthSignal` (Built / TopLevel / MaxLevel / CanUpgrade / CanBuild / MaxedOut, pure `From`). `Build` now takes `Func<BankResource, StorageGrowthSignal>`. Three-way verb: OverCap → SPEND, CanUpgrade → UPGRADE, CanBuild → BUILD, maxed → SPEND. One `FlowTrace.Step` per door naming every input. |
| `Assets/_Modules/Core/UI/HarvestOverflowModal.cs` | New `StorageGrowthFor(BankResource)` reads slot levels off the real `Apportion` and the ceiling off `TownBankCapacity.TryGetContainerRow`; traces the numbers. `BuiltContainers` kept (now `public`) as the legacy count seam. |
| `Assets/Editor/Regression/HarvestMaxedContainerDoorRegression.cs` | **NEW** oracle, 10 cases, drives the REAL `GameState` + catalog. |
| `Assets/Editor/Regression/HarvestResultShapeRegression.cs` | Fixture returns `FromBuiltCount(n)` (unknown ceiling ⇒ original verbs preserved). |
| `Assets/Editor/Regression/HarvestOverCapCopyRegression.cs` | Same fixture adaptation. |
| `Assets/Editor/Regression/DataRegression.cs` | Registers `[harvest-maxed-door]`. |

**Two deliberate safeties, both pinned:**

1. **The LOWEST built level decides, not the highest.** One container per resource is a RULING, not an
   enforced invariant — `TownBankCapacity.BuildSlots` walks every matching `BaseLayout` row with no
   singleton check, and the owner's own 2026-09-06 save carried TWO Foundries. On slots `[6,2]` keying
   off the highest level would report "maxed" and kill a door the player can actually buy. Pinned by
   `[lowest-rung-decides]`, whose fixture places two Foundries (one at the ceiling, one below it).
2. **The `Guard.Try` throw path seeds `From(0,0,0)`, not `default`.** A default struct reads `Built=0`
   with `CanBuild=false`, which the VM resolves to SPEND targeting the CONTAINER — the nonsense chip
   `SPEND LUMBERYARD`. Pinned by `[throw-path-degrades-to-build]`.

**Unknown ceiling:** `maxLevel <= 0` means the ceiling is UNKNOWN (an editor batch with an
unpopulated `CatalogRegistry`, which `TryGetContainerRow` already traces). That keeps the legacy
UPGRADE answer rather than silently retiring a real door on ignorance — pinned by case
`[unknown-ceiling-keeps-the-door]`.

## 5. Verification — red/green pair from the SAME real fixture

Fixture: ONE Lumberyard at L6 (= its ceiling); TWO Foundries, at L6 and L2 (a rung left); no Stoneyard
placed. Three FULL stores.

**RED** (`Builds/wo1863-red.log`, HEAD's count-only rule):

```
door wood  : 'UPGRADE LUMBERYARD'      <- the defect
door iron  : 'UPGRADE FOUNDRY'
door stone : 'BUILD STONEYARD'
HARVEST_MAXED_DOOR_FAIL [maxed-never-says-upgrade] the Lumberyard is at level 6 of 6 - its ceiling -
and the harvest door still reads 'UPGRADE LUMBERYARD'.
```

**GREEN** (`Builds/wo1863-green-final2.log`, marker asserted fresh — with the second Foundry in place):

```
door wood  : 'SPEND WOOD'              <- maxed: no upgrade offered
door iron  : 'UPGRADE FOUNDRY'         <- Foundries at [6,2]: the L2 rung keeps the door live
door stone : 'BUILD STONEYARD'         <- unbuilt STILL builds
HARVEST_MAXED_DOOR_OK
```

Sibling suites re-run green on the changed seam: `HARVEST_RESULT_SHAPE_OK`
(`Builds/wo1863-shape.log`), `HARVEST_OVERCAP_COPY_OK` (`Builds/wo1863-overcap.log`).

## 6. Not in this lane — one sibling surface still carries the same false copy

`Assets/_Modules/Core/UI/BankOverflowToastPresenter.cs:312` prints
`"... Build or upgrade a {ContainerName}, or spend {resource}."` unconditionally. Same lie, different
surface, and it needs its own capture. `TownBankCapacity.cs:792` prints the same clause but it is a
`FlowTrace` warn line, not player-facing.

## 7. Owed to the PO

Felt-verify on device: harvest with a maxed container and confirm the chip reads `SPEND WOOD` and
routes somewhere useful. Headless cannot judge whether SPEND-to-Manage▸Buildings is the right
destination for a spend instruction — that is a PO call.
