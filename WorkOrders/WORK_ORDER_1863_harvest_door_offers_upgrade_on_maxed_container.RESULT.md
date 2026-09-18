# WORK ORDER 1863 — RESULT

**Status:** DONE
**Date:** 2026-09-18
**Seat:** SME lane (harvest/economy UI silo)

Code + oracle are landed in the working tree. The full gate and the commit are owed to the lead — this
lane was told not to run them.

---

## Proving lines (CLAUDE.md §12 — captured, not inferred)

Fresh batch run, real `GameState` + real `CatalogRegistry`, three FULL stores. Fixture: ONE Lumberyard at
level 6 (its authored ceiling); TWO Foundries, at level 6 and level 2; no Stoneyard placed.

### RED — `Builds/wo1863-red.log`

**Provenance, stated plainly (§11B):** not literal HEAD. The new `StorageGrowthFor` reader + trace were
added first, then HEAD's verb expression — `s.OverCap ? "SPEND" : (built > 0 ? "UPGRADE" : "BUILD")` —
was run against it. Log filenames were renamed `wo2116-*` → `wo1863-*` after the WO number was minted
off the banner.

```
[Flow:Bank] harvest-result growth signal Wood (Lumberyard): built=1 levels=[6] topLevel=6 rowMaxLevel=6 -> canUpgrade=False canBuild=False maxedOut=True
[Flow:Bank] harvest-result door Wood: state='FULL' overCap=False built=1 topLevel=6 maxLevel=6 canUpgrade=False canBuild=False maxedOut=True -> verb=UPGRADE text='UPGRADE LUMBERYARD'
HARVEST_MAXED_DOOR_FAIL [maxed-never-says-upgrade] the Lumberyard is at level 6 of 6 - its ceiling - and the harvest door still reads 'UPGRADE LUMBERYARD'. There is nothing to upgrade (owner, 2026-09-18).
```

HEAD never fetched the level at all; the signal line above is the new instrumentation, and it is there to
show what the decision had available and did not use. The root cause is that **the verb rule had no level
input** — which is why no comparison inside it could have been wrong.

### GREEN — `Builds/wo1863-green-final2.log` (marker asserted fresh by `run-unity-method.ps1 -ExpectMarker`)

```
[Flow:Bank] harvest-result door Wood: ... maxedOut=True  -> verb=SPEND   text='SPEND WOOD'
[Flow:Bank] harvest-result growth signal Iron (Foundry): built=2 levels=[2,6] lowestLevel=2 topLevel=6 rowMaxLevel=6 -> canUpgrade=True canBuild=False maxedOut=False
[Flow:Bank] harvest-result door Iron: ... canUpgrade=True -> verb=UPGRADE text='UPGRADE FOUNDRY'
[Flow:Bank] harvest-result door Stone: ... canBuild=True  -> verb=BUILD   text='BUILD STONEYARD'
[Flow:Bank] harvest-result door Iron: state='OVER' overCap=True ... -> verb=SPEND text='SPEND IRON'
screen: Wood | 0 | 26,000 / 26,000  FULL | 5,000 waiting, safe | SPEND WOOD | Iron | 0 | 10,000 / 10,000  FULL | 4,000 waiting, safe | UPGRADE FOUNDRY | Stone | 0 | 3,000 / 3,000  FULL | 3,000 waiting, safe | BUILD STONEYARD | ...
HARVEST_MAXED_DOOR_OK a maxed storage container's harvest door says SPEND, a container with rungs left still says UPGRADE, and an unbuilt one still says BUILD
```

`[run] VERDICT=PASS marker='HARVEST_MAXED_DOOR_OK' FOUND ... runStart=2026-09-18T08:03:14`

Two hardening findings came out of review and are pinned by their own cases: the **lowest** built
container level decides `CanUpgrade` (`BuildSlots` enforces no singleton, and the owner's 09-06 save had
two Foundries — keying off the highest level would have killed a live door), and the `Guard.Try` throw
path seeds `From(0,0,0)` so it degrades to BUILD instead of emitting the nonsense chip `SPEND LUMBERYARD`.

### Sibling suites on the changed seam — both green

| Suite | Marker | Log |
|---|---|---|
| `HarvestResultShapeRegression` | `HARVEST_RESULT_SHAPE_OK` (VERDICT=PASS, marker asserted) | `Builds/wo1863-shape2.log` |
| `HarvestOverCapCopyRegression` | `HARVEST_OVERCAP_COPY_OK - over-cap harvest result: 7/7` | `Builds/wo1863-overcap2.log` |

---

## Files changed

- `Assets/_Modules/Core/UI/HarvestResultVM.cs`
- `Assets/_Modules/Core/UI/HarvestOverflowModal.cs`
- `Assets/Editor/Regression/HarvestMaxedContainerDoorRegression.cs` (**new**, + `.meta`)
- `Assets/Editor/Regression/HarvestResultShapeRegression.cs`
- `Assets/Editor/Regression/HarvestOverCapCopyRegression.cs`
- `Assets/Editor/Regression/DataRegression.cs` (one registration line)
- `CLI_LANES_WO_NUMBERS.md` (minted 1863, bumped 1863 → 1864 in the same edit)
- `WorkOrders/WORK_ORDER_1863_*.md` + this RESULT
- `BOARD.html` (regenerated)

`python tools/gate_brace.py` — `GATE_BRACE_SUMMARY bad=0 of 6`, exit 0.

## Instrumentation is PERMANENT (CLAUDE.md §12)

Two new `FlowTrace.Step` sites stay in the code: the growth-signal read in
`HarvestOverflowModal.StorageGrowthFor` and the door decision in `HarvestResultVM.Build`. Together they
make this class of bug a one-read diagnosis forever — the door line names every input it decided from.

## Owed / not claimed

1. **Full gate + commit** — not run by this lane, per brief. The lead gates the combined tree.
2. **PO felt-verify on device** — headless cannot judge whether `SPEND WOOD` routing to
   Manage ▸ Buildings is the right destination for a spend instruction.
3. **Sibling surface still lies:** `BankOverflowToastPresenter.cs:312` prints
   *"Build or upgrade a {ContainerName}"* unconditionally. Same defect class, different surface, needs
   its own ticket + capture. Not fixed here.
4. **Unrelated concurrent-lane breakage observed:** at 07:52 a batch run failed on
   `Assets/_Modules/Wallet/StoreStrings.cs(345,24): error CS0246 Dictionary<,>` while that file was
   being edited mid-flight by the WO-1862 localization lane (mtime 07:52:34). It compiled clean on the
   next run at 07:53. Flagged so the lead knows the tree was momentarily red for a reason that is not
   this lane.
