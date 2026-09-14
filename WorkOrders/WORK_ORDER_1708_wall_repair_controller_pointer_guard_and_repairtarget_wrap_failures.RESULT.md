# WO-1708 RESULT — pointer guard, wrap-failure instrumentation, Repair-All vs PAUSE

**Status:** IMPLEMENTED - awaiting lead gate 2026-09-14
**Lane:** implementation (edit-only). No Unity run, no gate, no build, no commit — by instruction.
**Branch:** dev

---

## 0. Summary per criterion

| # | Criterion | Status |
|---|---|---|
| 1 | Pointer-over-UI guard | **IMPLEMENTED + source-linted. Capture proof NOT produced by this lane** (needs a device run). |
| 2 | `RepairTarget could not wrap it` | **INSTRUMENTED. NO behavioural fix made — the ticket's hypothesis is DISPROVEN at source; see §2.** |
| 3 | Repair-All over PAUSE | **IMPLEMENTED; regression AUTHORED, not run — no Unity ran in this lane, so the lead's `REGRESSION_OK` on a fresh log is the proof. UNPROVEN item 4 is now PROVEN from source — see §3.** |

Files changed (4):
- `Assets/_Modules/Village/Walls/WallRepairController.cs`
- `Assets/_Modules/Village/Walls/HubRepairAffordance.cs`
- `Assets/Editor/Regression/RepairTapGuardRegression.cs` (NEW)
- `Assets/Editor/Regression/DataRegression.cs` (**ONE line added, `:1863`** — other lanes have uncommitted edits in this file; nothing else in it was touched)

`PauseController.cs` and `RepairTarget.cs` were **NOT** touched.

---

## 1. Criterion 1 — pointer-over-UI guard

**Changes**
- `WallRepairController.cs:50` — added `using UnityEngine.EventSystems;`
- `WallRepairController.cs:406` — the guard call, placed in `HandleTap()` **after** the
  `_suppressTapUntilFrame` and `HasSelection` early returns and **before** the world raycast, so it
  cannot change the existing modal-prompt semantics.
- `WallRepairController.cs:1502` — `private static bool PointerIsOverUi()`, seated beside the
  existing input helpers (`TapPressedThisFrame` / `PointerScreenPosition`).

**The fingerId overload — verified against the doc BEFORE writing, as the ticket required.**
Fetched `docs.unity3d.com/Packages/com.unity.ugui@2.0/.../EventSystem.html` this session. Verbatim
remark on the `int pointerId` overload:

> "If you use IsPointerOverGameObject() without a parameter, it points to the 'left mouse button'
> (pointerId = -1); therefore when you use IsPointerOverGameObject for touch, you should consider
> passing a pointerId to it Note that for touch, IsPointerOverGameObject should be used with
> ''OnMouseDown()'' or ''Input.GetMouseButtonDown(0)'' or ''Input.GetTouch(0).phase ==
> TouchPhase.Began''."

Two consequences, both acted on:
- the parameterless form answers about a **mouse** and would return false for every real finger on
  the Seeker — i.e. no guard at all on the platform that produced this ticket. The touch branch uses
  `IsPointerOverGameObject(t.fingerId)`.
- the doc **endorses** the `TouchPhase.Began` frame as the call site rather than warning about it,
  which is exactly where `TapPressedThisFrame()` already gates this path. No extra ordering
  backstop was added — adding one would have been an unrequested deviation against a doc that does
  not ask for it.

`EventSystem.current == null` returns **false** (world tap allowed), so a scene with no UI event
system is not silently deafened.

**Proof path for the lead (this lane cannot produce it).** The skipped branch emits
`FlowTrace.Throttle("Repair", "tap-over-ui", 2f, ...)` naming mouse-vs-touch, the fingerId and the
screen position. **Grep a fresh device log for `tap-over-ui`**; criterion 1 is met when pressing an
on-screen button emits that line and emits **no** `tap-not-repairable`.

⛔ **The attribution stays UNPROVEN per §4 of the ticket.** Nothing here is written as "she flagged
the dead wall taps"; the code comments state only what was measured.

---

## 2. Criterion 2 — `RepairTarget could not wrap it`

### ⚠ THE TICKET'S UNPROVEN ITEM 3 IS FALSE, AND IT WAS DISPROVEN BY READING, NOT INFERENCE

The ticket guessed the cause was "a component on a parent the wrap does not walk" (e.g.
`GetComponent` where `GetComponentInParent` is needed). **`RepairTarget.TryWrap` already walks
parents for all three kinds** — read at source this session:

- `Assets/_Modules/Village/Walls/RepairTarget.cs:85` — `hitCollider.GetComponentInParent<WallSegment>()`
- `RepairTarget.cs:89` — `GetComponentInParent<Gate>()`
- `RepairTarget.cs:93` — `GetComponentInParent<Building>()`
- `RepairTarget.cs:82-84` + `:112-118` — the Player-tag reject (`IsHeroOrPlayer`) walks ancestors too.

There is no wrap defect to fix at those lines. **No behavioural change was made.**

### What the source DOES say about the 15 non-terrain failures (measured this session)

| Measurement | Result |
|---|---|
| `grep -c WallSegment Assets/Editor/WallTools/SyntyCastlePerimeterBuilder.cs` | **0** |
| `grep -c WallSegment Assets/Editor/CastleWallsFromRecipe.cs` | **0** |
| `_selectableMask` default (`WallRepairController.cs:122`) | `~0` — **every layer** |

Those two editor files are what mint the logged names: `Battlement_{i}` at
`SyntyCastlePerimeterBuilder.cs:242`, `Wall_South_DoorJamb_L/R` at `CastleWallsFromRecipe.cs:233-234`
(put on `structureLayer` at `:237-238`). They add **no** `WallSegment` anywhere. The only
`AddComponent<WallSegment>` in non-test code is `Assets/Editor/WallTools/GridWallBuilder.cs:149` — a
different builder.

So the most consistent reading is that the logged `Wall_*` / `Battlement_*` hits are **non-repairable
Synty perimeter decor on a raycast mask that accepts everything**, and `Hero (Blaise)` is the
**deliberate** Player-tag reject — i.e. the wrap behaved correctly in every logged case, and the old
message's "RepairTarget covers WallSegment" line is what made it read as a contradiction.

⛔ **THIS REMAINS UNPROVEN BY CAPTURE.** It is a source-level explanation, not a measurement of the
running build, and §11B forbids stating it as settled. It is also a **design ruling for the owner**,
not this lane's edit: either those perimeter objects get a `WallSegment`, or they come off the
selectable mask. Neither was done.

### What shipped: the trace that settles it in one read

- `WallRepairController.cs:442-453` — the emitter now computes `hitName` / `hitChain` /
  `notRepairableLine` into locals and logs the hit's parent chain. The old message's
  self-contradicting claim is rewritten.
- `WallRepairController.cs:1541` — `public static string DescribeHitChain(Transform, int levels)`,
  rendering `[0] name tag=X {Comp, Comp} <- [1] ...`, `Transform` omitted, a destroyed script slot
  rendered as `<missing script>`.

**Chosen seam: the EMITTER, not inside `RepairTarget.TryWrap`.** Two reasons, both deliberate: the
emitter already holds `hit.collider` (the wrap only receives it), and `RepairTarget.cs` is outside
this lane's file set. Public+static so the regression can assert the shape with no play session.

**Proof path for the lead.** Grep a fresh capture for `tap-not-repairable`; each line now carries
`HIT CHAIN (up to 3 levels, hit object first): ...`. A chain with **none** of
WallSegment/Gate/Building = non-repairable object on the mask. A chain with `tag=Player` = the
intended hero reject. A chain that **does** carry one of the three = a real wrap defect, and the only
reading under which a code change is warranted.

---

## 3. Criterion 3 — Repair-All vs PAUSE

### UNPROVEN item 4 ("why does a 905 canvas read over a 31500 one") — **ANSWERED, from source**

It does **not** draw over it. Sorting order is being respected; the scrim is translucent:

- `Assets/_Modules/Settings/PauseController.cs:195` — modal built at `sortingOrder: 31500`.
- `Assets/_Modules/Core/UI/ElarionUiKit.cs:965-967` — `BuildObsidianModal` calls
  `BuildModalCanvas(name, sortingOrder)` and sets `c.overrideSorting = true`. **Both canvases are
  root `ScreenSpaceOverlay` canvases in the same sorting layer** — `ElarionUiKit.cs:104` for the
  modal (`BuildModalCanvas`, opened at source) and `HubRepairAffordance.cs:577` for the card — so
  `sortingOrder` 31500 orders above 905 on the root-canvas rule alone. (`overrideSorting` governs
  NESTED canvases and is not what decides this; it is noted only because it is on the line.)
- **`ElarionUiKit.cs:125`** — `Scrim` paints the backdrop at `new Color(0.02f, 0.015f, 0.04f, 0.85f)`,
  i.e. **alpha 0.85**. 15% of whatever sits underneath reads straight through.

That is the whole mechanism, and it is why the screenshots show `PAIR ALL` / `Wood 15  Iron 7`
**bleeding through** the pause panel rather than sitting on top of it. **Raising the card's sorting
order would have made it worse and lowering it changes nothing** — the only fix is to not draw it.

### Seam used: `PanelManager` (cited, not invented)

- `PauseController.cs:247` — `PanelManager.RegisterBattleAllowed("Pause", Resume, () => _paused)`
- `PauseController.cs:293` — `PanelManager.NotifyOpened(_panelHandle)` on pause; `:326` closes it.
- `Assets/_Modules/Core/UI/PanelManager.cs:19` (comment), `:70` `OpenStateChanged`, `:73` `AnyOpen` —
  and that comment records **MobileInteractButton already reads `AnyOpen` for exactly this
  prompt-suppression purpose**, so this is the established seam, not a new one.

Gating on `AnyOpen` rather than on `PauseController` specifically is deliberate and is the
wider-correct scope: every registered modal is built by the same kit and therefore carries the same
0.85 scrim and the same bleed-through. It also keeps `DeNelle.Village` free of a `DeNelle.Settings`
dependency it does not have.

### Changes

- `HubRepairAffordance.cs:83` — `Vis.HiddenPanelOpen` added to the diagnostic enum.
- `HubRepairAffordance.cs:274-297` — `OnEnable` / `OnDisable` subscribe/unsubscribe
  `PanelManager.OpenStateChanged` (neither method existed before), and `OnPanelStateChanged` hides
  **on the frame the modal opens**. Without this the card stays painted for up to `RefreshInterval`
  (0.75 s) — long enough to be exactly the screenshot in the ticket. On close it sets `_timer = 0f`
  so `Refresh()` re-decides rather than popping the card straight back.
- `HubRepairAffordance.cs:364-369` — the `PanelManager.AnyOpen` gate, placed **first in `Refresh()`**,
  before `FindAnyObjectByType<WaveManager>()` and `EnsureRepair()`, so the hidden branch has **no side
  effects** — in particular it must not self-install a `WallRepair_HubEngine` while a menu is up.
- `HubRepairAffordance.cs:464-470` — the `Vis.HiddenPanelOpen` `Announce` case, naming
  `PanelManager.OpenPanelName` and the alpha-0.85 mechanism.

---

## 4. Regression — `Assets/Editor/Regression/RepairTapGuardRegression.cs` (NEW)

Markers `REPAIR_TAP_GUARD_OK` / `REPAIR_TAP_GUARD_FAIL`. Contract `public static bool Run(out string)`
plus a `RunAll()` entry point, matching `CollectorOverflowRegression` (its neighbour). Per-case
revert recipes in the header, per house convention.

- **`[pause-hides-repair-all]`** — opens the real seam with `PanelManager.Register` + `NotifyOpened`,
  invokes `Refresh`, asserts `DiagnosticState == "HiddenPanelOpen"`, then closes the modal and
  asserts the state **leaves** `HiddenPanelOpen` (a gate, not a latch). Handle released in `finally`
  because `PanelManager` is static state shared with every other suite in the batch.
  It asserts `DiagnosticState`, **not canvas activity**, because edit-mode `AddComponent` does not run
  `Awake`, so `_canvas` is null and `SetVisible` is a no-op — and `DiagnosticState` is the same seam
  the F8 capture reads.
  **Leak containment (two layers, deliberate).** The modal-CLOSED half walks the full `Refresh()`
  path and so reaches `HubRepairAffordance.EnsureRepair()`, which mints
  `new GameObject("WallRepair_HubEngine")` when the scene has no controller — an object a naive
  `finally` would not own, and which would then survive into `[repair-probe]` and
  `[repair-hud-contract]`, both of which resolve a controller by `FindFirstObjectByType`. The case
  therefore (a) pre-seeds a `WallRepairController` on its own host so `EnsureRepair` finds that one
  and it dies with the host, and (b) sweeps any stray `WallRepair_HubEngine` in `finally`. Verified
  safe without `Awake`: every collection `RepairAllCost` touches is `readonly ... = new List<>()` at
  its declaration (`WallRepairController.cs:169-178`), not built in `Awake`. Checked and found
  **no** sibling suite already constructs a `WallRepairController` in edit mode
  (`grep -rn "AddComponent<WallRepairController>" Assets/Editor/` returns nothing), so this risk was
  not already retired elsewhere.
- **`[hit-chain-names-the-refusal]`** — builds a `Wall_4` + `BoxCollider` under a plain parent (the
  shape of the real logged failures) and asserts the chain names the hit, walks to the parent, lists
  `BoxCollider`, does **not** invent a `WallSegment`, omits `Transform`, and does not throw on null.
- **`[pointer-guard-exists]`** — a source lint, and the header says so plainly: it pins that the
  `UnityEngine.EventSystems` reference, the **fingerId** overload, the `PointerIsOverUi()` call and
  the `tap-over-ui` Throttle key have not been deleted again (the file shipped with **zero**
  EventSystem references).

Registered at **`Assets/Editor/Regression/DataRegression.cs:1863`**, one line, immediately after the
`repair-probe` suite, in the existing `Guard.Try` form. **The suite denominator in
`REGRESSION_OK <n>/<n>` goes up by one.**

---

## 5. Gate evidence (this lane)

```
$ python tools/gate_brace.py Assets/_Modules/Village/Walls/WallRepairController.cs \
    Assets/_Modules/Village/Walls/HubRepairAffordance.cs \
    Assets/Editor/Regression/RepairTapGuardRegression.cs \
    Assets/Editor/Regression/DataRegression.cs
GATE_BRACE_SUMMARY bad=0 of 4
exit=0
```

NUL scan + raw brace count:
```
WallRepairController.cs:      NUL=0 braces 192/192 OK
HubRepairAffordance.cs:       NUL=0 braces 89/89   OK
RepairTapGuardRegression.cs:  NUL=0 braces 42/42   OK
DataRegression.cs:            NUL=0 braces 1214/1214 OK
NUL_SCAN_OK
```

All `.cs` written with the Write/Edit tools (CLAUDE.md §0) — no shell redirects. Every FlowTrace
string is computed into a local before the call (CLAUDE.md §1: the gate's brace scanner has no
interpolated-string model).

---

## 6. UNPROVEN — carried forward, deliberately

1. **Criterion 1 is not capture-proven.** No device run by this lane. Grep a fresh log for
   `tap-over-ui` (must appear on a button press) and `tap-not-repairable` (must not).
2. **Criterion 2 has no fix and is not capture-proven.** The ticket's stated cause is disproven at
   source (§2); the replacement explanation is source-level only. The instrumentation is what will
   prove it. **Do not "fix" the wrap on the strength of §2's reading alone.**
3. **Open owner ruling, surfaced not decided:** should Synty perimeter decor (`Battlement_*`,
   `Wall_*_DoorJamb_*`) be repairable? Either give those objects a `WallSegment`, or take them off
   `_selectableMask` (`WallRepairController.cs:122`, currently `~0`). WO-1708 §4 forbids widening
   `RepairTarget`'s scope, so neither was done here.
4. **Criterion 3's acceptance is a screenshot** ("pause menu with no `PAIR ALL` / price text"). The
   regression proves the affordance reports hidden; only the owner's frame proves the pixels.
