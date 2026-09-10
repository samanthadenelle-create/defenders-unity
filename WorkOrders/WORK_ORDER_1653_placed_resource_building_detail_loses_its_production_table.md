# WO-1653: a PLACED resource building's detail card loses its production table to the Defense branch

**Status:** READY TO IMPLEMENT
**Silo:** `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` (VM only — the renderer is correct).
**Number:** PRE-ASSIGNED by the lead. ⛔ **Do NOT edit `CLI_LANES_WO_NUMBERS.md`.**
**Source:** WO-1566 audit re-tick, RESULT row **3.3** (audit `2039e2c41`, re-tick `9592cdd6f`).
**Yardstick row:** WO-1566 §2 panel 3, row **3.3** — *"A before -> after stats table (e.g.
`Production 120/hour -> 180/hour`) with the after value visually distinct."*

---

## 1. THE EVIDENCE — two frames from one run disagree, which is what localises it

`Builds/wave5-manageflow1` (08:09, HEAD `2039e2c41`), `MANAGE_FLOW_MAP_OK 20 frames`.

| Frame | Subject | Stats band renders |
|---|---|---|
| `ManageFlow_BUILD_action_2670x1200.png` (opened) | **Crystal Mine**, `MANAGE_FLOW_STATE BUILD/action -> build-category:ECONOMY / mine_crystal state=Available screen=Detail` | ⛔ **NO table.** One sentence, `Raises Crystal Mine to Level 3 of 3.`, then a single row `Placed .......... 1 placed . L2`, then Upgrade Cost. |
| `ManageFlow_BUILD_max_2670x1200.png` | **Forge**, `MANAGE_FLOW_STATE BUILD/max -> build-category:ECONOMY / forge state=Max screen=Detail` | ✅ trace fired: `[Flow:Manage] building detail production id=forge harvest-level 1 tier 4 now=576/hr (no further tier)` |
| `ManageFlow_ARMY_action_2670x1200.png` (opened) | Archer | ✅ `Health 60 -> 66`, `Damage 29.0 -> 31.9`, `Range 14.0 -> 15.7`, **after value in GOLD** |

**So the renderer, the contract and the gold-delta treatment all work.** `BuildingStatRows` works —
it fired for `forge`. The trace **never fires for `mine_crystal`**:
`grep -a "building detail production" <log>` returns **one line, and it says `forge`**.

## 2. THE CAUSE — proven at source, not inferred

`Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs`, the detail composer's `default:` case. The
branches are tried **in order**, and the Defense branch wins first:

```
:5301   var d = DefenseChoiceFor(nav.ItemId);
:5303   if (d != null)
:5306       stats = TwoFacts("Placed", Ascii(d.PlacedText), null, null);   // <-- the whole defect
:5307       costs = CostVms(d.UpgradeCostParts);
:5310       break;                                                          // <-- never reaches below
...
:5284   var b = BuildingChoiceFor(nav.ItemId, nav.ItemId);
:5295       stats = BuildingStatRows(b);                                    // the production table
```

`mine_crystal` **resolves a DefenseChoice** — the log says so directly:

> `[Flow:Manage] defense choice id=mine_crystal placed=1 lowest=L2/3 state=Upgradable ready=True
> key='mine_crystal@21_9' portrait='Portraits/Buildings/mine_crystal-2'`

So **any PLACED resource building takes the Defense branch and is handed a one-line `Placed` fact
instead of its production table.** `forge` renders because that frame reached it as a BuildingChoice.
The `Placed .......... 1 placed . L2` row visible in the frame is `TwoFacts` output — the defect is
visible in the picture, exactly where the table should be.

⚠ **`BuildingStatRows` itself is NOT at fault and must not be edited.** It is gated on
`ResourceBuildingProgression.IsResourceBuilding(b.Id)` and `nowPerHour > 0.0` (`:5591`, `:5604`), both
of which `mine_crystal` satisfies — it never gets called.

## 3. FILES TO EDIT

| File | Change |
|---|---|
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | The Defense branch (`:5301-5311`) must not discard the production/storage table for an item that also has one. Compose the stats from `BuildingStatRows` when the subject is a resource/storage building, and keep `Placed` as an ADDITIONAL fact rather than the whole table. |
| `Assets/Editor/Regression/*` | A case that fails when a placed resource building's detail carries no numeric row. See §4.2. |
| `Assets/Editor/Regression/DataRegression.cs` | Registration line only, if a new suite is added. |

## 4. ACCEPTANCE

1. **Row 3.3 from a frame:** a fresh `RunManageFlowMapCaptureHeadless` shows the Crystal Mine detail
   card carrying `Production / hr <now> -> <next>` with the after value visually distinct, **and** the
   `Placed` fact. Open the PNG.
2. **RED-first oracle:** a case that composes the detail for a PLACED resource building and fails when
   `stats` contains no numeric before->after row. It must fail against today's tree before the fix.
   ⛔ Do not pin it on `nav` ordering or on branch text — pin the **composed VM**, or it re-breaks the
   moment the branches are reordered again.
3. `[Flow:Manage] building detail production id=mine_crystal ...` appears on the run — the trace already
   exists (`:5610`) and is the cheapest proof the branch was reached.
4. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.
5. Per WO-1566 §2.0, `**Status:**` goes to `AWAITING OWNER MATCH` — a device frame judged by the owner
   is the verdict, not this list.

## 5. WHAT NOT TO TOUCH

- ⛔ **`BuildingStatRows` (`:5583-5650`) and `TroopStatRows` (`:5498-5525`).** Both work. The bug is
  which branch runs, not what the rows contain.
- ⛔ **`StatRow` (`:5655`) and the gold-delta treatment.** `[detail-next-by-weight]` pins it and the
  Archer frame proves it renders.
- ⛔ **The unit-in-the-label decision** (`"Production / hr"`, `:5606-5608`). It is deliberate and
  reasoned in-code: a bare `1,008` is the mockup's `180 / hour` stripped of the half that means
  anything.
- ⛔ **The five-row cap** in `ManageWorkspacePanel.BuildStatRows` (`Mathf.Min(count, 5)`) — deliberate.
- ⛔ Do not touch the Defense rail itself (`MANAGE_DEFENSE_CARD_OK` is green), only what the DETAIL
  composer does with a Defense choice.
- ⛔ No renderer edits. `ManageDumbViewRegression` pins the view's shape.
