# WO-1646 - The raid deploy bar: every face is under the 112 px touch floor at every aspect, and the empty-tray label renders underneath two of them

**Status:** IMPLEMENTED - awaiting gate
**Minted:** 2026-09-10 (lane RAID-HUD; number **PRE-ASSIGNED by the lead** - `CLI_LANES_WO_NUMBERS.md`
was **not** edited by this lane, per the lead's instruction. The banner bump is the lead's.)
⚠ **Minted and implemented in ONE lane pass; this ticket never sat READY.** `tools/board_build.py`
derives the board from this file, so the history will show it appearing already implemented - that is
accurate, not a missed flip: the lead handed the measurement and the fix as one instruction.
**Silo / Lane:** UI - `Assets/_Modules/Village/Troops/RaidDeployController.cs` **only**.
**Severity:** P1. Defect 1 is the game's one deploy control being silently re-laid-out every frame on
a phone; defect 2 paints two buttons over the sentence that tells a new player why the tray is empty.
**Type:** EXISTING system, two defects on one surface.
**Found by:** the **WO-1645** capture, on its FIRST real run - `Builds/wave5-capture2`, fresh 07:33
2026-09-10: **`UI_GEOMETRY_FAIL x15` over 97 canvases, all 15 on `RaidDeployHud`, none on `RaidHud`.**
⭐ This is the ticket WO-1645 was built to produce, and it reds on **real authored geometry**, not on a
synthetic fixture - the red-first proof that oracle needed, delivered by the defect itself.

---

## 1. Defect 1 - SUB-TOUCH-FLOOR BAND, at all three aspects

### 1a. What the gate measured

`Panel/ObsBtn_Deploy All`, `Panel/ObsBtn_Rally` and `Panel/ObsBtn_Retreat` resolve, in reference px:

| aspect | face height | vs `ElarionUiKit.MinTouchPx` = 112 (`ElarionUiKit.cs:347`) |
|---|---|---|
| 1920x1080 | **103.7** | UNDER by 8.3 |
| 2340x1080 | **93.9** | UNDER by 18.1 |
| 2670x1200 (the Seeker's real surface) | **92.7** | UNDER by 19.3 |

Three faces x three aspects = 9 of the 15 failures; the remaining 6 are defect 2 (sec.2), 2 per aspect.

### 1b. The arithmetic that produced them - reproduced exactly, so nothing here is inferred

The faces were authored `y 0.18 - 0.82` **of the bar** = 0.64, and the bar is `DeployBarHeight`
(**0.150** of screen, `RaidDeployController.cs:1592`). So a face is `0.150 * 0.64 = 0.096` of the
canvas's REFERENCE height, where refH is
`HudLayoutBands.CanvasReferenceSize(w, h).y` (`HudLayoutBands.cs:379-387`, Unity's own match-0.5
formula over the 1080x1920 reference at `:55-59`):

```
1920x1080 -> refH 1080.00 -> 0.096 * 1080.00 = 103.68   (gate reported 103.7)
2340x1080 -> refH  978.29 -> 0.096 *  978.29 =  93.92   (gate reported  93.9)
2670x1200 -> refH  965.38 -> 0.096 *  965.38 =  92.68   (gate reported  92.7)
```

**All three reproduce the oracle's own numbers.** The model is confirmed, so every "after" figure in
sec.4 is arithmetic rather than hope.

### 1c. Why it was invisible for so long, and why it is not cosmetic

`ElarionUiKit.ClampMinTouch` (`ElarionUiKit.cs:1069`) **rescues** an under-floor control at runtime by
**growing** it - symmetrically, in `LateUpdate`, into whatever sits beside it. So the bar was never
untappable; it was silently re-laid-out every frame into a shape nobody authored. The kit says this
about itself at `:1082-1098`:

> *"by the time the clamp grows a control, the layout it was meant to protect is already spilled into
> its neighbours, and nothing anywhere says so. Four panels shipped that way in three days."*

and, critically:

> *"LateUpdate never runs in an edit-mode batchmode capture, so a headless run records ZERO growths on
> a genuinely broken panel. The gate-time assert is the AUTHORED-BAND measurement in
> `UICaptureLaunch.AuditGeometry` (rule 4)."*

Which is precisely what fired. **The clamp is not a fix and its silence is not a pass.**

### 1d. The code sites

- `RaidDeployController.cs:1592` - `DeployBarHeight = 0.150f`.
- `:1736-1744` (pre-fix) - the three faces, each `new Vector2(x, 0.18f) .. new Vector2(x, 0.82f)`.
- `:1892` (pre-fix) - **the tray TILES carry the identical `0.18f/0.82f` band.** The gate could not
  see them: headless there is no `GameStateService`, so the tray builds EMPTY (the WO-1645 capture
  logs that caveat itself). **Fixing only the three named faces would have left the defect shipping
  for every player who owns troops.**

---

## 2. Defect 2 - BUTTON OVER TEXT: the empty-tray label runs under two faces

### 2a. What the gate measured (2670x1200, root-canvas local px)

| element | x span | y span |
|---|---|---|
| `Panel/Label` - `"No troops to deploy - train at the Barracks f..."` | **-427.4 .. 549.9** | -302.2 .. -209.5 |
| `Panel/ObsBtn_Deploy All` | **143.9 .. 504.8** | -302.2 .. -209.5 |
| `Panel/ObsBtn_Rally` | **527.3 .. 767.9** | -302.2 .. -209.5 |

Identical y band, overlapping x. Both faces are painted straight over the sentence. 2 overlaps x 3
aspects = the other 6 failures.

Every one of those five coordinates reproduces from the authored fractions (bar x 0.280-0.980 on a
2148.0-px-wide reference canvas = 1503.6 px, label x 0.03-0.68, faces at WO-1639's x bands) - the
same confirmation as sec.1b.

### 2b. The cause: a HALF-APPLIED WO-1639

The tray's right edge and the label's right edge were the **same edge held in two places**:

- `BuildTrayTiles` local `right` (`:1882` pre-fix), which **WO-1639 moved from `0.55f` to `0.390f`**
  to free width for the `Deploy All` face;
- the empty-state label's `x1` (`:1866` pre-fix), which **kept `0.68f`**.

`0.68` now reaches straight across `Deploy All` (0.410-0.650) and into `Rally` (0.665-0.825). The
duplicated-state failure CLAUDE.md sec.2 / sec.5 / sec.16 each describe, in miniature: one copy moved,
the other did not.

⚠ **Note this is a defect WO-1639 INTRODUCED**, and the WO-1645 capture caught it on its first run,
before any player saw it. That is the coverage hole closing exactly as designed.

---

## 3. What "fixed" means

1. Every tappable thing on this bar - the three faces **and** the tray tiles - resolves at or above
   `ElarionUiKit.MinTouchPx` at every supported aspect, **without** `ClampMinTouch` having to rescue it.
2. The band is **DERIVED**, never a per-aspect literal. Three typed numbers would be the same
   duplicated-state failure this ticket is half made of, and there is no hook to apply them at.
3. The empty-tray label's rect does not intersect any face rect. It is the tray's copy and belongs in
   the tray's strip.
4. **No font under `ElarionUiKit.FontFloor` (30)** and **no player copy shortened.**
5. **WO-1639's face WIDTHS are kept exactly** - that ticket sized them so `Deploy All` stops
   ellipsising; this one only makes the faces tall enough to touch.

---

## 4. Acceptance

1. A fresh WO-1645 capture reports **`UI_GEOMETRY_OK`** with the 15 `RaidDeployHud` failures gone, and
   no new failure anywhere. **Judge by the marker on a FRESH log, never the exit code** (CLAUDE.md
   sec.8).
2. The six `RaidDeployHud_*` / `RaidHud_*` PNGs **opened and pasted**; the bar's faces read as a
   normal command bar (not a squashed strip) and the empty-tray sentence is fully visible beside them,
   not under them.
3. `UI_GLYPH_OK` still covers both panels with `labels` non-zero, and the empty-tray sentence is not
   truncated (it wraps).
4. On a device: the `[wo1646-touch]` trace line reports `faceH` >= 112 and says *"clears the floor"*.
5. `python tools/gate_brace.py` + NUL on the touched file (CLAUDE.md sec.1).

---

## 5. Pins and what NOT to touch

- ⛔ **`ElarionUiKit.cs` / `ElarionUiKitObsidian.cs` / `ElarionUi.cs` are READ-ONLY.** In particular
  **`MinTouchPx` (112) must not move.** Lowering the floor so a screen passes is the inverse of this
  fix and would silently relax every button in the game.
- ⛔ **`ClampMinTouch` must not be weakened or disabled.** It is the correct runtime rescue for a
  build that shipped wrong; the kit's own comment at `:1080-1082` says removing it would *"turn a
  visible defect into an untappable one"*. The fix is to stop needing it.
- ⛔ **`DeployBarHeight`, `DeployStatusHeight`, `DeployBarBand` and `DeployStatusBand` do not move.**
  `DeployStatusBand` is stacked on `DeployBarHeight` (`:1639-1640`), and its height is asserted by
  `RaidHudThumbBandRegression` against `NeedPx(FontHardFloor)` with a documented **0.04 px** margin.
  Raising the bar to solve the touch floor would move a shared band with almost no slack. **Solve it
  inside the bar, in the face fraction.**
- ⛔ **WO-1639's face x bands (0.410-0.650 / 0.665-0.825 / 0.840-0.985) do not move**, nor does the
  literal `"Deploy All"` (pinned, `RaidDeployUiRegression.cs:411-415`).
- ⛔ **`RaidScoring.cs`, `RaidDeployScreen.cs` (WO-1640), `RaidHudController.cs`, `RaidBaseDresser` /
  `RaidBaseGenerator` and every `RaidBase_*` scene** - untouched. `RaidHud` had **zero** failures.
- ⛔ **Do not commit. Do not push.**

---

## 6. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1646_raid_deploy_bar_under_the_touch_floor_and_empty_tray_label_under_the_faces.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.
