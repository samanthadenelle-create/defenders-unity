# WORK ORDER 1708 - WallRepairController: no pointer guard, walls never wrap, Repair-All overprints PAUSE

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-14 by the CLI lead from F8 triage
**Source of truth:** `docs/F8_TRIAGE_2026-09-14.md` section 2, findings 1 (`:55-61`), 2-3 (`:62-72`) and
4 (`:73-74`). Every code line below was re-opened at source by this lane.

---

## 1. Objective

Three defects on the same input path, in one ticket because they share one file. Each has its own
numbered acceptance criterion; they may be implemented in one pass but must be proven separately.

## 2. Evidence

**A. No `IsPointerOverGameObject()` guard.**
File: `Assets/_Modules/Village/Walls/WallRepairController.cs`.
- `:1441` is a bare `return Input.GetMouseButtonDown(0);` (opened at source; the doc cites the same
  line at `:62-68`).
- `:43` reads, verbatim: `// workstream constraints. Tap = Input.GetMouseButtonDown(0) OR a began touch.`
- **Proven by measurement:** `grep -c "EventSystem" WallRepairController.cs` returns **0**. There is no
  UI-blocking guard anywhere in the file.
- Consequence the doc draws (`:66-72`): pressing an on-screen button - **including the FLAG button that
  produces this lane's own evidence** - also raycasts the world behind it.

**B. `RepairTarget could not wrap it` on walls.**
- Emitter: `WallRepairController.cs:418` opens the `FlowTrace.Throttle("Repair", "tap-not-repairable",
  2f, ...)` call; the doc cites `:419`, which is the first string line of the same call. Throttle
  interval 2 s, so **any count is a floor, not a total** (doc `:56-57`).
- Verbatim log text (from the emitter at source): `tap hit '<name>' but RepairTarget could not wrap it -
  no repair prompt. RepairTarget covers WallSegment/Gate/Building only; towers, harvest sites and
  collectors are reachable ONLY through Repair-All.`
- Counts: the doc reports **~25 lines from 22:32:00 onward** (`:55-56`). This lane measured the whole
  file: `grep -c "RepairTarget could not wrap it" logs/debug/seeker-365962-logcat.txt` = **52**, of which
  **37 are `ExteriorTerrain`** (scenery - expected) and **15 are not**:
  `Wall_4` x3, `Hero (Blaise)` x3, `Wall_3`, `Wall_13`, `Wall_DoorJamb_R`, `Battlement_1`,
  `Battlement_3`, `Battlement_13`, `Archer Tower`, `Iron Mine`, `Jeweler_Gems_Storefront` (x1 each).
- **The message contradicts itself:** it claims `RepairTarget` covers `WallSegment`, yet walls failed.
- **UNPROVEN:** *why* a wall collider fails to wrap. Nothing captured so far says whether the hit
  collider's GameObject lacks `WallSegment`, or carries it on a parent the wrap does not walk. Proving
  it costs one trace line (see criterion 2).

**C. Repair-All overprints the pause menu.**
- Screenshots `flag_20260912-033141_00/_01.png` show `PAUSE` and `PAIR ALL` overprinting with
  `Wood 15  Iron 7` painted across the panel (doc `:46-47`, `:73-74`).
- It functions: `22:35:31.609 [Flow:Repair] RepairAll: repaired 'Archer Tower' (dmg 0.04 -> post-fix
  0.00) for Wood 15  Iron 7` (doc `:74`).
- Two draw sites, opened at source: the pause card is `Assets/_Modules/Settings/PauseController.cs:214`
  (`ElarionUiKit.Label(body, "PAUSED", ...)`) on a canvas at `sortingOrder: 31500` (`:195`); the repair
  card is `Assets/_Modules/Village/Walls/HubRepairAffordance.cs:506-510` (`HubRepairCanvas`,
  `SortingOrder = 905` at `:61`). The named widget `ObsBtn_REPAIR ALL` appears in
  `Assets/_Modules/Core/UI/HudLayoutBands.cs:462`.
- **Proven seam:** `grep -n "PanelManager" HubRepairAffordance.cs` returns **nothing** - the repair
  affordance has no subscription to modal-panel state, so it never hides when a panel opens.
  **UNPROVEN:** why a 905-order canvas is legible over a 31500-order one; that is what the fix must
  explain or make moot by hiding the card.

## 3. Acceptance criteria

1. **Pointer guard.** `WallRepairController` ignores a tap that lands on UI. Implementation constraint:
   on touch, `EventSystem.current.IsPointerOverGameObject()` must be called with the **`fingerId`
   overload** - verify the parameterless form's touch behaviour against Unity's `EventSystem` docs
   first. Proven by a capture in which pressing an on-screen button emits **no** `tap-not-repairable`.
2. **Wall wrap.** A trace line inside the wrap path reports which components the hit collider and its
   parents carry, captured on a real wall tap; then the wrap is fixed so `Wall_*` / `Battlement_*` tap
   to a repair prompt. Proven by a capture showing the prompt, not by reading the wrap code.
   `ExteriorTerrain` and `Hero (Blaise)` must still be rejected.
3. **Repair-All vs pause.** The Repair-All card is not visible or readable-through while the pause
   panel is open. Proven by a screenshot of the pause menu with no `PAIR ALL` / price text on it.

## 4. What NOT to touch

- **Do not write "she flagged the dead wall taps" as fact anywhere.** The doc rules the attribution
  UNPROVEN (`:62-69`): two stories fit the same bytes, and criterion 1 is what settles it.
- Do not ack seq 5025-5030; the owner has not ruled (doc `:89-90`).
- Do not change `RepairTarget`'s documented scope (WallSegment/Gate/Building) to swallow towers,
  mines or storefronts - Repair-All is their route by design (`WallRepairController.cs:418-421`).
- No `Assets/Resources/Data/Canonical/*.json` edits, no scene hand-edits, no R2 push.
