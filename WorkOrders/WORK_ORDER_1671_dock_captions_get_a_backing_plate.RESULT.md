# WORK ORDER 1671 — RESULT

**Status:** IMPLEMENTED - awaiting gate (no Unity run in this lane; the lead gates)
**Lane:** DOCK-CAPTIONS, isolated worktree `.claude/worktrees/agent-ad0b5c7a8c34ea18a`
**Base:** `e4b5906a541c65fc12255a109b5dc3723e328488`, ff-merged from `dev`
**Date:** 2026-09-10

---

## 1. WHAT LANDED — four files

| File | Change |
|---|---|
| `Assets/_Modules/Core/UI/ElarionUiKit.cs` | `AddCaptionPlate(ActionSlotHandle)` + `CaptionPlateObjectName` / `CaptionPlatePadX` / `CaptionPlatePadY` |
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | call site in the dock slot builder; the plate is handed to the layout |
| `Assets/_Modules/HUD/Kit/HudDockSlotLayout.cs` | `_captionPlates` list, `AddSlot(slot, caption, plate)` overload, plate toggled with the caption in `Apply` |
| `Assets/Editor/Regression/HudActionBarRegression.cs` | four assertions per face inside `CheckMeasuredPeacefulDock` |

Resulting plate anchors **0.02..0.98 x / 0.00..0.30 y**, covering the caption's 0.06..0.94 / 0.02..0.26;
sibling index one below the caption; `ElarionUiKit.ObsidianFill` by reference; `raycastTarget = false`.

## 2. THE MEASUREMENT — proven, this session

Read with PIL 12.3.0 from the two owner device frames named in the WO (both 2670x1200). Captions occupy
**y 1138..1162**, below the housing art, on terrain. **BEFORE** contrast:
**BUILD 2.12:1 · TALK 2.73:1 · HERO 2.97:1 · JOURNEY 1.87:1 · MANAGE 2.16:1**
(second frame 2.17 / 2.57 / 2.94 / 1.90 / 2.16). All five under the 3:1 WCAG floor for large text.

**AFTER is a PREDICTION (~16.7:1), not a measurement** — it is arithmetic against `ObsidianFill` at
α 0.98 over black. §11B: it must be re-measured off a device frame once this ships. The background
sample includes the glyphs' AA halo, so the BEFORE figures are if anything generous.

## 3. SLOT HEIGHT — before/after, with the proof

**Unchanged, and structurally incapable of changing.** `HudDockLayout.Solve(int slotCount, float
mountWidthPx, float maxTrackWidthPx, float gapFraction)` (`HudDockLayout.cs:124-125`) is **width-only**:
no caption, plate or height term exists in its signature or body. The vertical band consts
`PeacefulDockSlotY0 = 0.08f` / `PeacefulDockSlotY1 = 0.94f` were **not touched**, the caption rect was
**not touched**, and the plate lives inside the slot rect. Before == after at all four `DockSurfaces`.

## 4. THE PIN, AND WHAT IT WOULD SAY AGAINST HEAD

`CheckMeasuredPeacefulDock` still measures count, captions, order, touch floor and label fit — the plate
does not shrink the caption box, so the existing `CaptionInset = 0.88` case is unaffected.

⛔ **RED-FIRST BY CONSTRUCTION, NOT BY EXECUTION — this lane has no Unity and DID NOT RUN THE SUITE.**
Against HEAD `e4b5906a5` no slot carries a `CaptionPlate` child, so the new block emits for each of the
five faces, verbatim:

```
[dock-measured] face 'BUILD' has NO obsidian caption plate (no 'CaptionPlate' child) — the owner's
device frames measure this caption band at 1.87–2.97:1 against the terrain it draws over, under the
3:1 floor, and the word is the only colour-free carrier of the face's meaning (WO-1671)
```

No claim is made that `HUD_ACTIONBAR_OK` was observed. It was not.

## 5. CHECKS RUN IN THIS LANE

- `python tools/gate_brace.py` (the port of the gate's OWN rule, CLAUDE.md §1) on every `.cs` this
  worktree touched → `GATE_BRACE_SUMMARY bad=0 of 6`, exit 0
- raw brace balance: ElarionUiKit 375/375, HudDockSlotLayout 13/13, HudKitController 423/423,
  HudActionBarRegression 70/70 (the other two files this worktree touched belong to WO-1672)
- NUL-byte scan on every touched file → clean
- No `.unity` scene touched, no `System.Reflection` added, cross-module calls unchanged

## 6. NOT DONE, ON PURPOSE

The combat dock's captions sit in the identical band via the same `SetCaption`. No combat device frame
was measured this session, so the bleed there is **stated as unproven**. The ruling named the calm dock;
the one-line extension is flagged in WO-1671 §5 for the owner, not applied.
