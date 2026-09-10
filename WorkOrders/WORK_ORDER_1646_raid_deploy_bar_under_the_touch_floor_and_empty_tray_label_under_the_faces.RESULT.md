# WO-1646 RESULT - raid deploy bar touch floor + empty-tray label (lane RAID-HUD, 2026-09-10)

**Status of the work:** IMPLEMENTED - awaiting gate.
**Lane:** UI / raid HUD. **EDIT ONLY** - no Unity, no gate, no commit, per the lane brief.
**Worktree:** `D:\EoA\.claude\worktrees\agent-a223665cd051ef469`
**Base:** `ff42319de` (`dev`), reached by `git merge --ff-only refs/heads/dev`.

**File changed (1):** `Assets/_Modules/Village/Troops/RaidDeployController.cs`

> **Merge note.** The ff was refused on the first attempt: my WO-1639 working copies were still in
> the tree and had landed on `dev` as `b2fa255cf`. I proved they were the same bytes before
> discarding anything - `diff --strip-trailing-cr` against `refs/heads/dev` reported **SAME** for
> `RaidDeployController.cs`, `RaidHudController.cs` and all three WO files (the only delta was
> CRLF vs LF: 128201 vs 126042 and 41828 vs 41118 bytes, i.e. exactly the line counts). Nothing was
> discarded that was not already committed.

---

## 1. Evidence discipline

- The **"before"** numbers are the WO-1645 capture's own, quoted by the lead from
  `Builds/wave5-capture2` (fresh 07:33): `UI_GEOMETRY_FAIL x15` over 97 canvases, all on
  `RaidDeployHud`, none on `RaidHud`. **Measured.**
- The **"after"** numbers are **computed** from the authored fractions and the canvas formula.
  **They are not from a capture** - no capture was run by this lane. What raises them above a
  prediction is sec.2: the same arithmetic reproduces **every one of the gate's own reported
  coordinates to the decimal**, so the model is confirmed against the oracle before being used
  forward. The re-run is still the proof and it is acceptance item 1.
- No claim is made about how the bar FEELS. That is the owner's.

---

## 2. The model is confirmed, not assumed

Before changing anything I reproduced the gate's readings from the authored fractions
(`refH = HudLayoutBands.CanvasReferenceSize(w,h).y`; bar x 0.280-0.980 on a 2148.0-px reference
canvas = 1503.6 px; face band 0.18-0.82 of a bar that is 0.150 of screen):

| gate reported | computed here | |
|---|---|---|
| face height 103.7 @ 1920x1080 | **103.68** | match |
| face height 93.9 @ 2340x1080 | **93.92** | match |
| face height 92.7 @ 2670x1200 | **92.68** | match |
| `Panel/Label` x -427.4 .. 549.9 | **-427.4 .. 549.9** | match |
| `ObsBtn_Deploy All` x 143.9 .. 504.8 | **143.9 .. 504.8** | match |
| `ObsBtn_Rally` x 527.3 .. 767.9 | **527.3 .. 767.9** | match |
| shared y band -302.2 .. -209.5 | **-302.2 .. -209.5** | match |

**Seven of seven.** Nothing below is a guess about how this layout resolves.

---

## 3. Defect 1 - the touch floor. DERIVED, not three literals.

### What was added

| new member | value | why |
|---|---|---|
| `TouchFloorWidestAspect` | `21f / 9f` | the worst case: `refH = sqrt(RefW*RefH / aspect)`, so reference height **falls** as a device gets wider |
| `TouchFloorSafetyPx` | `2f` | the floor is a floor, not a target; authoring exactly 112 is one rounding from failing the assert it was written to pass |
| `LegacyFaceHeightFraction` | `0.64f` | the derived value never goes BELOW what shipped - a narrower band could only be a regression |
| `MaxFaceHeightFraction` | `0.96f` | a face can never eat the plate's inset entirely |
| **`DeployFaceHeightFraction`** | derived, **public** | `(MinTouchPx + safety) / (DeployBarHeight * widestRefH)`, clamped |
| `FaceY0` / `FaceY1` | derived | the band, centred, so the plate keeps equal inset above and below |

```
widestRefH = sqrt(1080 * 1920 / (21/9))            = 942.70
barPx      = 0.150 * 942.70                        = 141.41
fraction   = (112 + 2) / 141.41                    =   0.80619   -> y 0.09690 .. 0.90310
```

### What it resolves to

| aspect | refH | bar px | **OLD** (0.64) | **NEW** (0.80619) | vs floor 112 |
|---|---|---|---|---|---|
| 1920x1080 | 1080.00 | 162.00 | **103.68** FAIL | **130.60** | +18.6 |
| 2340x1080 | 978.29 | 146.74 | **93.92** FAIL | **118.30** | +6.3 |
| 2670x1200 | 965.38 | 144.81 | **92.68** FAIL | **116.74** | +4.7 |
| 21:9 (the worst case it is derived against) | 942.70 | 141.41 | 90.50 FAIL | **114.00** | +2.0 |

**One fraction, four aspects, all clear.** No per-aspect number exists anywhere in the file.

### Why it is derived against the widest aspect and not against `Screen.*`

Two reasons, both hard:

1. **Correctness.** `refH` shrinks as aspect widens, so a fraction that clears the floor at the
   widest supported aspect clears it at every narrower one. Deriving at the worst case is the only
   single value that is safe everywhere.
2. ⛔ **`Screen.*` DOES NOT MOVE in a batchmode capture.** `UICaptureLaunch`'s own banner says so and
   tracks it as `_screenStuckBuilds`. A `Screen`-derived fraction would author one shape headless and
   a different one on the device - **the capture would stop being evidence.** A constant expression
   authors the identical band in both, which is the only way the gate can prove the device.

### The tray tiles were fixed too, and the gate could not have told me to

`:1892` carried the **identical** `0.18f/0.82f` band on `ElarionUiKit.Button` tiles. Headless there is
no `GameStateService`, so the tray builds EMPTY and the oracle never saw a tile. **Fixing only the
three faces the gate named would have left the defect shipping for every player who owns troops.**
They now take the same derived band.

---

## 4. Defect 2 - the empty-tray label

### The change

| | before | after |
|---|---|---|
| label x band | `0.03f .. **0.68f**` (typed at the widget) | `TrayLeftX .. TrayRightX` = `0.03 .. **0.390**` |
| label y band | `0.18 .. 0.82` (typed) | `FaceY0 .. FaceY1` (derived) |
| fit | `FitSingleLine` | **`FitBlock`** |
| tray edges | `left`/`right` locals at `:1882`, a second copy at the label | **two consts, one definition** |

### Resolved geometry at 2670x1200 (root-canvas local px)

```
label   OLD  x -427.4 .. 549.9      NEW  x -427.4 .. 113.8
DeployAll    x  143.9 .. 504.8      (unchanged - WO-1639's width is kept)
Rally        x  527.3 .. 767.9      (unchanged)
gap from the label's right edge to Deploy All's left edge:  +30.1 ref px
```

**Both overlaps are gone**, and the faces did not move a pixel horizontally.

### The cause, and why the fix is structural rather than a new number

The tray's right edge was held **twice** - once as `BuildTrayTiles`' `right` local, once as the
label's `x1`. **WO-1639 moved one (0.55 -> 0.390) and not the other**, so the label reached across two
faces. It is now `TrayLeftX` / `TrayRightX`, consumed by both sites, so the next edit to the tray's
width cannot leave anything behind. ⚠ **This defect was introduced by WO-1639 and caught by WO-1645's
first real run, before any player saw it** - the coverage hole closing exactly as designed.

### Why `FitBlock`, and the fit arithmetic

The tray strip is 0.360 of a 1503.6-px bar = **541.3 ref px**, and the sentence is 49 characters. On
ONE line at `FontLabel` 40 it needs ~1078 px, so `FitSingleLine` would autoshrink to the 30 px
`FontFloor` and then **CUT** it - trading a covered sentence for a truncated one. `FitBlock` wraps to
two lines: `2 x NeedPx(40) = 100.8 px` against the derived band's **116.7 px** at 2670x1200.

⛔ **THAT LAST FIGURE IS A HEURISTIC AND I AM NOT CLAIMING IT AS A FACT.** `NeedPx` is a
**single-line** seat estimate (`RaidSelectionScreen.cs:248`); TMP's real multi-line height is
`lineCount x fontSize x the FONT ASSET's line-height ratio`, and **I did not open that font asset**.
`FitBlock` ends in TMP's **Truncate** mode, which DROPS a line that will not fit - so a ~16 px margin
resting on an unread number is the WO-1464 "0.04 px margin" class again. Two things stand between that
and a cut sentence: the fitter self-rescues down to `FontFloor` (30) first, where two lines need far
less; and `[wo1646-label]` (sec.5) **measures it and WARNs** rather than leaving anyone to assume.
**Unproven until that line reads clean on a real run.**

**No font goes under `FontFloor`. No player copy was shortened.**

---

## 5. Instrumentation added (PERMANENT - CLAUDE.md sec.12)

**`[wo1646-label]`**, from `LogEmptyTrayLabel()`: the empty-tray sentence's band px, seated font, the
autosize window, `lineCount`, drawn characters vs `text.Length` and `isTextTruncated`. ⚠ **This one
exists because my own fit arithmetic in sec.4 is a HEURISTIC and I will not ship it as a fact.**
`FitBlock` ends in TMP's **Truncate** mode, which DROPS a line that will not fit the rect, and
`RaidSelectionScreen.NeedPx` is a *single-line* seat estimate - TMP's real multi-line height is
`lineCount x fontSize x the FONT ASSET's line-height ratio`, and **I did not read that font asset**.
The predicted margin is ~16 px at 2670x1200 and the fitter self-rescues down to `FontFloor` before it
would cut, but a margin resting on an unread number is the WO-1464 "0.04 px margin" class again. The
trace **WARNs** if the sentence is cut and names the two lawful remedies - explicitly **not**
shortening the copy, which is the owner's call. **Treat the sec.4 fit figures as unproven until this
line reads clean on a real run.**

`[wo1646-touch]`, one line per raid, from `LogTouchFloor()`: the live `Screen`, the resolved reference
size, the bar height in px, the derived fraction, the resulting face height, and the kit floor. It
**WARNs** if a device is wider than `TouchFloorWidestAspect` assumes and names the two remedies. The
gate measures the authored band headless; this is the runtime half, so a device outside the assumption
says so in the log instead of being silently rescued by `ClampMinTouch` - which, per
`ElarionUiKit.cs:1082-1098`, would already have spilled the layout into its neighbours by then.

Interpolated parts are computed into locals first (CLAUDE.md sec.1 - the gate's brace scanner has no
interpolated-string model).

---

## 6. Checks run

```
python tools/gate_brace.py Assets/_Modules/Village/Troops/RaidDeployController.cs
-> GATE_BRACE_SUMMARY bad=0 of 1   (exit 0)

RaidDeployController.cs   NUL=0   raw braces 199/199
```

Verification that no authored face band survives:
`grep -n "0.18f\|0.82f\|0.68f\|0.03f, 0.390f" Assets/_Modules/Village/Troops/RaidDeployController.cs`
returns **nothing**. Every band on this bar is now derived or named.

No gate was run and **no gate marker string is written anywhere in this document** - the lane is EDIT
ONLY.

---

## 7. ⚠ ONE REQUIRED FOLLOW-UP THE FILE SCOPE FORBADE ME FROM DOING

`Assets/Editor/Regression/RaidHudThumbBandRegression.cs` asserts:

```
RequireSeats(failures, "the raid deploy tray", deployBar.height * 0.64f * 0.48f, refH,
             needPx, "the tile count badge is 0.48 of a tile that is 0.64 of the bar");
```

That typed **`0.64f`** is now **STALE**. It **still passes** - it under-states the real height
(0.80619), so the assert is strictly more conservative than the truth - **but it is measuring
something the code no longer does**, which is the exact class of quiet drift CLAUDE.md keeps
documenting. I did **not** re-point it, because this ticket is scoped to
`RaidDeployController.cs` only.

**I made the re-point a one-liner:** `DeployFaceHeightFraction` is **public** for this purpose,
mirroring how the readout seat is required to read `HudLayoutBands.RaidReadoutBand` rather than a
copied rect (`RaidHudThumbBandRegression:207-209`). The fix is:

```
RequireSeats(failures, "the raid deploy tray",
             deployBar.height * RaidDeployController.DeployFaceHeightFraction * 0.48f, refH,
             needPx, "the tile count badge is 0.48 of a tile whose height is DERIVED from MinTouchPx");
```

**Please route it** - one line, one file, and it converts a stale literal into a live read.

⚠ **And note its sibling, which I am NOT claiming to have fixed.** The `0.48f` in that same
expression is *also* a copy - of the count badge's own `cr.anchorMin` / `cr.anchorMax`
(`RaidDeployController.cs:2108-2109`, `0.26f`..`0.74f` = 0.48 of the tile). I re-pointed only the
literal this ticket's change made stale (`0.64f`); `0.48f` is still accurate because the badge's
anchors did not move. It is the same duplicated-state shape and worth folding into the same
re-point - **say so rather than leaving the reader to assume the expression is now fully derived.**

---

## 8. What the re-run must show

1. `UI_GEOMETRY_OK` with all **15** `RaidDeployHud` failures gone and no new failure anywhere.
   Marker on a FRESH log, never the exit code.
2. `RaidDeployHud_1920x1080 / _2340x1080 / _2670x1200` **opened and pasted**: the faces read as a
   normal command bar rather than a squashed strip, and the empty-tray sentence sits **beside** them
   in the tray strip, wrapped to two lines, fully readable.
3. `UI_GLYPH_OK` still covers both panels with `labels` non-zero, and the empty-tray sentence is not
   truncated. **Cross-check the `[wo1646-label]` line in the same run:** it must be a `Step`, not a
   `Warn`, with `chars` equal to the string length. That line - not sec.4's arithmetic - is what
   proves the sentence wrapped instead of being cut.
4. `RaidHud_*` unchanged - it had zero failures and this ticket did not touch that file.
5. On a device, `[wo1646-touch]` reports `faceH` >= 112 and *"clears the floor"*.

---

## 9. Not touched

`ElarionUiKit*` / `ElarionUi` (`MinTouchPx` **not** moved, `ClampMinTouch` **not** weakened),
`DeployBarHeight`, `DeployStatusHeight`, `DeployBarBand`, `DeployStatusBand`, WO-1639's face x bands,
the `"Deploy All"` literal, `RaidScoring.cs`, `RaidDeployScreen.cs` (WO-1640), `RaidHudController.cs`,
`RaidBaseDresser` / `RaidBaseGenerator`, every `RaidBase_*` scene, and
`CLI_LANES_WO_NUMBERS.md`. No commit, no push.
