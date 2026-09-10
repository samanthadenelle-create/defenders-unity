# WO-1647 RESULT - the raid plates' gold veil (lane RAID-HUD, 2026-09-10)

**Status of the work:** IMPLEMENTED - awaiting gate + device frame.
**Lane:** UI / raid HUD. **EDIT ONLY** - no Unity, no gate, no commit, per the lane brief.
**Worktree:** `D:\EoA\.claude\worktrees\agent-a223665cd051ef469`
**Base:** `ff42319de` (`dev`). This tree also carries **WO-1646**, handed back and not yet committed -
the two lanes touch the same two files by design (1646 = band geometry, 1647 = fill colour), and the
diffs do not overlap.

**Files changed by THIS ticket (3):**
- `Assets/_Modules/Village/Troops/RaidHudController.cs` - `innerRim: false` at the readout plate
- `Assets/_Modules/Village/Troops/RaidDeployController.cs` - `innerRim: false` at the command bar plate
- `Assets/Editor/Regression/RaidHudThumbBandRegression.cs` - **the WO-1646 §7 re-point** (separate
  errand from the lead, folded into this pass; see sec.5)

**WO path:** `WorkOrders/WORK_ORDER_1647_raid_readout_plate_is_veiled_gold_by_the_kit_inner_rim.md`
(brought in from the main tree - not on `dev` yet - and its `**Status:**` flipped, plus a §7B
implementation addendum carrying the re-measured table the lead asked for).

---

## 1. The §3 measurement gate - discharged honestly, one closed and one open

| | state | evidence |
|---|---|---|
| **Measurement 2** - what does HEAD render? | ⭐ **CLOSED. §2 CONFIRMED.** | `Builds/wave5-capture4` (HEAD files, 07:43), `RaidHud_2670x1200.png` plate interior = **RGB (88, 71, 17)**, against §1's red-run `(89, 72, 20)`. `ObsidianFill` over black is `(5, 5, 6)`. **The veil is live at HEAD, on top of the corrected fill.** Measured by the lead. |
| **Measurement 1** - does the veil draw on DEVICE? | ⛔ **STILL OPEN** - needs the next APK | Not measured. **I am not claiming it either way.** |

**Implemented anyway, at the lead's explicit instruction.** §5.1's stop-rule (*"if measurement 1 shows
the veil is skipped on device, STOP"*) is discharged rather than ignored, and here is the reasoning
written down so it can be overruled: **even if the veil is skipped on device, `innerRim: false` makes
the CAPTURE and the DEVICE render the same plate.** A gate that measures a different surface than the
player sees is the WO-1645 coverage hole reopening in a new place - and the WO-1639 contrast work
already lost a round to exactly that. So the change is worth making on gate-fidelity grounds alone.
**But see sec.4: if Measurement 1 comes back "skipped on device", the framing of this ticket changes
and the RESULT says so up front rather than after.**

---

## 2. The fix - Option A, two callers, zero kit bytes

```
RaidHudController.cs:469     Panel(..., deep: false)  ->  Panel(..., deep: false, innerRim: false)
RaidDeployController.cs:1776 Panel(..., deep: false)  ->  Panel(..., deep: false, innerRim: false)
```

- `Panel`'s `innerRim` parameter already exists and **defaults to `true`** (`ElarionUiKit.cs:145-146`,
  read at source). No API invented.
- `AddInnerRim` (`:2670-2683`) is a **full-rect quad at a 1 px inset**, coloured `AccentSoft`
  (`Gold` @ 0.30, `UiStyle.cs:116`) at **half alpha** - gold at 0.15 over the whole plate. The kit
  documents this about itself at `:2657-2668`.
- **Precedent verified at source:** `HeroInventoryController.cs:632` -
  `Panel(parent, min, max, deep: deep, innerRim: false)`; `HeroEquipHud.cs:224` does the same.

**Reach, re-counted this session:** `grep -rn "ElarionUiKit.Panel(" Assets/ --include=*.cs` = **10**
call sites. **2** already passed `innerRim: false`; this ticket adds **2**; **6 remain veiled,
deliberately untouched** - exactly the split WO-1647 §4 specifies. No kit constant moved:
`AccentSoft`, `ElarionUi.Gold`, `AddInnerRim` and `Panel`'s default are all **unchanged** (Option C is
forbidden without an owner ruling, §6).

---

## 3. §5.4 - WO-1639's five rows, re-measured against the ground that actually renders

WCAG relative luminance; ratio `(L_text + 0.05) / (L_plate + 0.05)`. Text floor 4.5:1; non-text
component floor 3:1.

| row | colour | WO-1639 §1a<br>(0.42 plate) | **HEAD, VEILED**<br>(88,71,17) | **FIXED, capture**<br>(5,5,6) | **FIXED, device**<br>(10,10,11) |
|---|---|---|---|---|---|
| `3:00` timer | `Parchment` | 2.67 | **7.54** | **17.01** | **16.54** |
| `SPIRE 100%` | `Gilt` | 1.98 | **5.58** | **12.59** | **12.24** |
| `Razed 0%` | `ParchmentDim` | 1.72 | **4.84** | **10.91** | **10.61** |
| star diamonds (unlit) | `EmptyTrackFill` white@0.40 | 1.55 | ⛔ **2.86 - UNDER 3:1** | **3.71** | **3.77** |
| `Troops 0/0` | `ParchmentDim` | 1.12 | **4.84** | **10.91** | **10.61** |

Plate luminances: veiled `0.066217`; `ObsidianFill` over black `0.001544`; `ObsidianFill` @ 0.98 over
the arena background WO-1639 back-solved from the owner's frame `0.003008`. The last two differ by
under 3% in the resulting ratios, so nothing here turns on which ground you pick.

### Two findings, and one of them is against my own previous ticket

1. ⛔ **WO-1639 LEFT A FLOOR UNMET AND DID NOT KNOW IT.** On the ground HEAD really renders, the unlit
   honor diamonds sit at **2.86:1 - below the 3:1 component floor** that ticket sized
   `EmptyTrackFill` (white @ 0.40) to clear. WO-1639 predicted 3.77:1; it was right about the *fill*
   and wrong about *what paints the plate*, because it composited `ObsidianFill` over the arena and
   never over the veil it could not see. **This ticket is what makes that prediction true.** I wrote
   the prediction, so I am naming it plainly rather than letting the new green number bury it.
2. **WO-1639's headline fix held regardless.** Every TEXT row clears 4.5:1 even on the veiled ground
   (4.84:1 at worst, up from 1.12:1). The plate change was sound and the ticket was not wasted - what
   was wrong was the confidence, not the direction.

---

## 4. ⚠ An observation that bears on Measurement 1 - recorded as UNPROVEN, on purpose

WO-1639 §1a sampled the **owner's device** frame at **RGB (136, 145, 154)** - blue > green > red. A
gold veil shifts the opposite way (red > green > blue), and every headless sample of the veiled plate
is olive. **That is consistent with `BlinkChromeActive` being TRUE on device** (Blink art present, so
`AddInnerRim` returns early at `ElarionUiKit.cs:2672`), i.e. **the veil being a headless-only
artifact** - which is precisely the case WO-1647 §3.1 warns about.

⛔ **I have NOT proven this.** One device frame with the plate sampled closes it either way (§5.3). If
it holds, the honest reading of this ticket becomes *"it made the gate and the device agree"* rather
than *"it fixed what the player sees"*, and the 2.86:1 star row in sec.3 would be a **capture-only**
number - in which case WO-1639's device-side contrast question is still open and needs its own look.
**Both readings are written down so the next seat cannot inherit only the flattering one.**

---

## 5. The WO-1646 §7 re-point (the lead's second errand)

`Assets/Editor/Regression/RaidHudThumbBandRegression.cs:406` now reads:

```
RequireSeats(failures, "the raid deploy tray",
             deployBar.height * RaidDeployController.DeployFaceHeightFraction * 0.48f, refH,
             needPx,
             "the tile count badge is 0.48 of a tile whose height is DERIVED from MinTouchPx");
```

- Verbatim from WO-1646 RESULT §7. `using DeNelle.Village;` is already present (`:83`) and
  `DeployFaceHeightFraction` was made `public` by WO-1646 for exactly this.
- The typed **`0.64f` is gone.** It went stale when WO-1646 derived the tile band from
  `ElarionUiKit.MinTouchPx`, and it still *passed* - it under-stated the real height, so the assert was
  strictly conservative. **That is what makes a stale copy dangerous**: it measures something the code
  no longer does and says nothing when the two diverge. Same rationale this suite already applies to
  the readout seat at `:207-209`.
- ⚠ **The `0.48f` sibling is deliberately LEFT TYPED**, as I judged in WO-1646 §7. It is the count
  badge's own anchor span (`cr.anchorMin 0.26` / `cr.anchorMax 0.74`) and those anchors **did not
  move**, so unlike the 0.64 it is still accurate. Re-pointing it now would be an unrequested change
  to a passing, correct assert; it is the same duplicated-state shape and belongs in the next touch of
  that badge. **Recorded rather than silently done or silently skipped.**

---

## 6. Checks run

```
python tools/gate_brace.py  RaidHudController.cs  RaidDeployController.cs  RaidHudThumbBandRegression.cs
-> GATE_BRACE_SUMMARY bad=0 of 3   (exit 0)

RaidHudController.cs             NUL=0   braces  44/44
RaidDeployController.cs          NUL=0   braces 200/200
RaidHudThumbBandRegression.cs    NUL=0   braces  24/24
```

`grep -rn "innerRim: false"` confirms **4** of the 10 `Panel` call sites now opt out (2 pre-existing +
2 from this ticket); the other 6 are untouched.

No gate was run and **no gate marker string is written anywhere in this document** - the lane is EDIT
ONLY.

---

## 7. What the next run must show (acceptance, §5)

1. **Re-run `RunCaptureHeadless` and sample `Builds/ui-capture/RaidHud_2670x1200.png` at §1's five
   interior points** - (2150,200), (2200,470), (2300,300), (2500,540), (2620,180). Expect **~(5,5,6)**,
   not (88,71,17). **Paste all five.** Same for `RaidDeployHud_*` at all three aspects.
2. **A device frame of the in-raid readout, plate sampled** - this is **Measurement 1** and it is the
   one thing this RESULT cannot supply. It decides whether sec.4's reading is right, and therefore
   whether WO-1639's device-side contrast question is closed or still open. **Please prioritise it on
   the next APK.**
3. **`UI_CAPTURE_OK` / `UI_GLYPH_OK` / `UI_GEOMETRY_OK` do not regress**, judged by the MARKERS on a
   FRESH log. ⚠ `UI_GEOMETRY_OK` should now go **green** on these canvases: WO-1646 (in this same tree)
   fixes the 15 findings that were holding it red, so the bar rises from *"no NEW findings"* to *"the
   marker is clean"*.
4. **`REGRESSION_*` still passes** with the re-pointed `RaidHudThumbBandRegression` - the new
   expression asserts a LARGER height than the old literal, so it is a stricter test, not a looser one.

---

## 8. Not touched

`ElarionUiKit.cs` (including `AddInnerRim`, `Panel`'s `innerRim` default and `AccentSoft`),
`UiStyle.cs`, `ElarionUi.cs` (`Gold`), `FeatureFlags.BlinkChrome` / the `BlinkChromeActive` art probe,
`Assets/Editor/UICaptureLaunch.cs`, the 6 remaining veiled `Panel` callers, the band geometry on these
two canvases (WO-1646's axis), `RaidScoring.cs`, `RaidDeployScreen.cs`, `RaidBaseDresser` /
`RaidBaseGenerator`, and `CLI_LANES_WO_NUMBERS.md`. No commit, no push.
