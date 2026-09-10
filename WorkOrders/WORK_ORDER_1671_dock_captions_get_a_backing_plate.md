# WORK ORDER 1671 — The five dock captions draw on the terrain; give each one an obsidian backing plate

**Status:** IMPLEMENTED - awaiting gate (no Unity run in this lane; the lead gates)
**Lane:** DOCK-CAPTIONS, isolated worktree `.claude/worktrees/agent-ad0b5c7a8c34ea18a`
**Base:** `e4b5906a541c65fc12255a109b5dc3723e328488`, ff-merged from `dev`
**Owner ruling:** 2026-09-10 12:16 — *"ticket a caption plate"*
**Date:** 2026-09-10

---

## 1. THE DEFECT, MEASURED — not described

Two owner device frames, both 2670x1200, read with PIL 12.3.0 on 2026-09-10:

- `Builds/device-frames/2026-09-10_0814_363660_town_dock.png`
- `Builds/device-frames/2026-09-10_1134_363866_town.png`

The five calm-dock captions render in the pixel band **y 1138..1162**, which falls **below the
authored housing art** and straight onto the town terrain. (Why: `BuildActionBarHousing`
(`Assets/_Modules/Core/UI/ElarionUiKit.cs:4005-4025`) draws the medieval plate
`UI/ElarionMedieval/buttons/button-normal-empty` as `Image.Type.Simple` stretched over the mount, and
its drawn surface does not reach the bottom of its own rect — while `SetCaption` authors the word at
y 0.02..0.26 of a slot that spans y 0.08..0.94 of the dock. The word therefore sits over whatever the
camera is showing.)

**Measured WCAG contrast, caption glyphs vs the pixels actually behind them:**

| Face | frame 0814 | frame 1134 |
|---|---|---|
| BUILD | **2.12:1** | 2.17:1 |
| TALK | **2.73:1** | 2.57:1 |
| HERO | **2.97:1** | 2.94:1 |
| JOURNEY | **1.87:1** | 1.90:1 |
| MANAGE | **2.16:1** | 2.16:1 |

Method (so it can be re-run): glyph pixels are those within an L1 distance of 45 of `ElarionUi.Parchment`
(243,234,211); background pixels are the rest of the same box; both averaged as WCAG relative luminance
and combined as `(L+0.05)/(l+0.05)`. The background sample includes the glyphs' anti-aliased halo, so
if anything these ratios are **generous** — the true terrain contrast is worse.

**Every face is under 3:1**, the floor WCAG allows even for large text. JOURNEY — the longest word,
over the brightest grass — is the worst at 1.87:1.

**This is not a taste call.** The word is the only colour-free carrier of a face's meaning (the owner
is red/green colourblind; `HudActionBarModel.cs:270-285` states the rule), so a caption the terrain can
swallow is a functional defect on the one control surface the player touches every session.

---

## 2. THE RULING, AND WHY A PLATE AND NOT A TINT

The owner ruled a **caption plate**: a dark backing band under each caption, in **the kit's obsidian
plate idiom, never a hand-tinted colour**.

⛔ Never hand-tint a caption to "lift" it. A hue chosen against grass loses again over stone, water,
snow or a night sky — and it puts meaning back onto colour, which is the one thing this HUD may not do.
The kit already owns the answer: the obsidian idiom (`ElarionUiKit.ObsidianFill`, near-black
0.02/0.02/0.025 at α 0.98 — WO-562 black+gold canon) that every panel body in the game sits on. The
caption gets that fill **by reference**, so a future reskin carries it for free.

**Predicted after-ratio: ~16.7:1** on all five faces. That number is *arithmetic from the token*, NOT a
measurement (§11B) — it must be re-measured from a device frame once this ships.

---

## 3. WHAT CHANGED

### A. The kit — `Assets/_Modules/Core/UI/ElarionUiKit.cs`
New `public static GameObject AddCaptionPlate(ActionSlotHandle slot)` plus three consts:
`CaptionPlateObjectName = "CaptionPlate"`, `CaptionPlatePadX/Y = 0.04f`.

Four shape constraints, each one a way this could have silently gone wrong:

1. **The plate is a SIBLING of the caption inside the slot root, never a wrapper.**
   `HudActionBarRegression.CheckMeasuredPeacefulDock` finds a face's caption by walking the slot root's
   **direct** children for a non-empty `TMP_Text`. Reparenting the caption under a plate would red the
   oracle with *"N dock face(s) carry NO caption"* — a true-looking failure about entirely the wrong
   thing.
2. **Inserted AT the caption's sibling index**, so the caption slides one later and draws on top.
   (`SetCaption` itself re-seats the caption under `cdText`, so a plate built *before* the caption ends
   up over the word.)
3. **Bigger than the caption rect on both axes, and the caption rect is NOT touched.** The oracle's
   `CaptionInset = 0.88` encodes `SetCaption`'s authored x 0.06..0.94; shrinking the word to fit a
   padded plate would silently invalidate its label-fit case. Resulting plate anchors: **0.02..0.98 x,
   0.00..0.30 y** vs the caption's 0.06..0.94 / 0.02..0.26.
4. **`raycastTarget = false`** — the plate must never eat a tap meant for the medallion.

The pad is a **fraction of the slot, not a pixel inset**, because the slot's pixel width is solved per
surface by `HudDockLayout` — a fixed px inset would be correct at exactly one aspect (the WO-1468
lesson, one seam over). Idempotent: a second call returns the existing plate.

### B. The dock builder — `Assets/_Modules/HUD/Kit/HudKitController.cs`
`BuildPeacefulDockSlot` (now the shared `BuildDockSlot`) calls `AddCaptionPlate` after `FitSingleLine`
and hands the plate to the layout.

### C. The layout — `Assets/_Modules/HUD/Kit/HudDockSlotLayout.cs`
`AddSlot(slot, caption, captionPlate)` overload; the plate is toggled **with** the caption in `Apply`.

⚠ **This is the non-obvious half.** `HudDockSlotLayout` already hides captions on the icon-only
degradation tier (`cap.gameObject.SetActive(sol.ShowCaptions)`). A plate that stayed visible there
would paint an **empty black smear** under five wordless medallions on exactly the narrow surfaces the
ladder exists to protect. One list, one toggle, no second owner of caption visibility.

### D. Does the plate change the solved slot height? **NO, and here is the proof.**

`HudDockLayout.Solve(int slotCount, float mountWidthPx, float maxTrackWidthPx, float gapFraction)`
(`Assets/_Modules/Core/UI/HudDockLayout.cs:124-125`) takes **no caption, plate or height term at all** —
it is width-only. The dock's vertical band is the pair of consts `PeacefulDockSlotY0 = 0.08f` /
`PeacefulDockSlotY1 = 0.94f` (`HudKitController.cs`), and **neither was touched**. The caption rect is
unchanged and the plate lives inside the slot rect. So the before/after solved slot geometry is
**identical at all four `DockSurfaces`** — not "measured the same", but structurally incapable of
differing.

---

## 4. THE PIN — RED-FIRST BY CONSTRUCTION

`Assets/Editor/Regression/HudActionBarRegression.cs`, inside `CheckMeasuredPeacefulDock` (marker
`HUD_ACTIONBAR_OK` / `HUD_ACTIONBAR_FAIL`). It keeps measuring the captions exactly as before — count,
words, order, touch floor, label fit — and adds four assertions per face:

- the plate **exists** (found by the kit's const, not a retyped literal);
- it **covers** the caption rect on both axes — a narrower plate leaves the ends of "JOURNEY" back on
  the grass, which *is* the defect, with a green test beside it;
- it **draws under** the caption (lower sibling index) — a plate above the word hides it completely,
  and a bare "a plate exists" test would call that a fix;
- it **is `ObsidianFill` by value** and **takes no raycasts**.

**Red-first is by construction, not by execution.** Against HEAD `e4b5906a5` no slot carries a
`CaptionPlate` child at all, so the block emits, for all five faces:

```
[dock-measured] face 'BUILD' has NO obsidian caption plate (no 'CaptionPlate' child) — the owner's
device frames measure this caption band at 1.87–2.97:1 against the terrain it draws over, under the
3:1 floor, and the word is the only colour-free carrier of the face's meaning (WO-1671)
```

⚠ **This lane holds no Unity and did not execute the suite** — see the RESULT file. The lead gates.

---

## 5. FLAGGED FOR THE OWNER — not fixed unasked

**The COMBAT dock's captions sit in the identical band.** `BuildCombatDockSlot` authors ATTACK / BLOCK /
skill names / ITEM through the same `SetCaption` at the same y 0.02..0.26, in the same `ActionBar`
mount. They almost certainly bleed the same way over a raid floor — but no device frame of the combat
dock was measured this session, so that is **stated as unproven, not asserted**. The fix is one line
(`ElarionUiKit.AddCaptionPlate(slot)` in `BuildCombatDockSlot`) and is deliberately **not** applied:
the ruling named the calm dock. One capture of a combat frame closes it.

---

## 6. FILES

- `Assets/_Modules/Core/UI/ElarionUiKit.cs` — `AddCaptionPlate` + 3 consts
- `Assets/_Modules/HUD/Kit/HudKitController.cs` — call site in the dock slot builder
- `Assets/_Modules/HUD/Kit/HudDockSlotLayout.cs` — plate list + toggle + `AddSlot` overload
- `Assets/Editor/Regression/HudActionBarRegression.cs` — the pin

**Do NOT touch:** the caption rect (`ActionSlotHandle.SetCaption`), `PeacefulDockSlotY0/Y1`,
`HudDockLayout`'s arithmetic, `HudActionBarModel` (inert for this bar — CLAUDE.md §7).
