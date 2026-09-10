# WO-1647 - The raid readout plate renders OLIVE, and the plate colour is not what paints it

**Status:** IMPLEMENTED - awaiting gate + device frame
**Minted:** 2026-09-10 (lane RAID-CAPTURE, number **PRE-ASSIGNED by the lead** - `CLI_LANES_WO_NUMBERS.md`
was **not** edited by this lane. The banner bump is the lead's.)
**Silo / Lane:** UI-KIT / RAID-HUD. Touches a kit **caller**, or adds a kit variant - see §5.
**Severity:** P2. A colour defect on the surface WO-1639 was explicitly fixing for contrast, which means
WO-1639's contrast numbers were measured against the wrong background.
**Type:** EXISTING. **Raised out of WO-1645's hand-back** (WO-1645 §11 - the new in-raid capture is what
made it measurable at all).
**Depends on:** nothing. **Blocks:** nothing. Runs in parallel with WO-1646 (same canvases, different
axis: 1646 is band geometry, this is fill colour).

---

## 1. The measurement

Sampled by decoding `Builds/ui-capture/RaidHud_2670x1200.png` (from `Builds/wave5-capture3red`,
2026-09-10 07:35) at five plate-interior points clear of text - (2150,200), (2200,470), (2300,300),
(2500,540), (2620,180):

> ⛔ **Every one of the five reads RGB `(89, 72, 20)`** - a dark olive/ochre. Off-plate reads `(0,0,0)`.

The command bar's plate in `RaidDeployHud_*.png` is the same olive at all three aspects.

The plate is supposed to be near-black. WO-1639 points it at the canonical fill:

- `ElarionUiKit.ObsidianFill = new Color(0.02f, 0.02f, 0.025f, 0.98f)`
  (`Assets/_Modules/Core/UI/ElarionUiKit.cs:189`)
- `RaidHudController.cs:508` (HEAD): `barImg.color = ElarionUiKit.ObsidianFill;`

`(0.02, 0.02, 0.025)` at alpha 0.98 over black is `(5, 5, 6)`. **The plate is 18x brighter than that in
red and carries a hue the fill does not contain.**

## 2. Where the olive comes from - proved from the kit, not inferred

`ElarionUiKit.Panel` (`ElarionUiKit.cs:145-152`) builds the fill and then calls
**`AddInnerRim(p, AccentSoft)`** at `:150`.

⛔ **`AddInnerRim` (`ElarionUiKit.cs:2670-2683`) IS NOT A RIM.** It creates a child `Image` at
`anchorMin = Vector2.zero`, `anchorMax = Vector2.one`, `offsetMin (1,1)` / `offsetMax (-1,-1)` - a
**full-rect quad at a 1 px inset** - coloured `new Color(color.r, color.g, color.b, color.a * 0.5f)`.
`SetAsFirstSibling()` orders it first among siblings, but **a parent's own Graphic draws before all of
its children**, so it still draws ON TOP of the host's face.

- `AccentSoft` = `Gold` at alpha **0.30** (`Assets/_Modules/Core/UI/UiStyle.cs:116`)
- halved by `AddInnerRim` -> **Gold at alpha 0.15 across the entire plate**
- `ElarionUi.Gold = new Color(0.831f, 0.686f, 0.216f, 1f)` (`Assets/_Modules/Core/UI/ElarionUi.cs:58`)

**Composited in LINEAR space and re-encoded to sRGB, Gold@0.15 over black predicts `(92, 76, 18)`.**
Measured: `(89, 72, 20)`. The veil is the plate's colour, to within rounding.

**The kit already says this about itself**, in its own doc block at `ElarionUiKit.cs:2657-2668`:

> *"⚠ IT IS NOT A RIM AND IT IS NOT BEHIND THE HOST. This is a FULL-RECT filled rounded quad at a 1px
> inset ... It only reads as a rim because the fill is half-alpha, so on an ornate plate it VEILS the
> art rather than framing it ... Raise the alpha, or ship with the art absent, and every host that
> calls this gets a translucent slab over its face."*

**So: `barImg.color` sets what sits UNDERNEATH a gold veil.** Re-tinting it from
`(0.04, 0.035, 0.03, 0.42)` to `ObsidianFill` - which is exactly what WO-1639 did - cannot make the
plate near-black on its own, and **the WO-1639 contrast ratios were computed against a background that
is not the one on screen.**

## 3. ⛔ TWO MEASUREMENTS BEFORE ANY EDIT. Both are cheap. Neither is optional.

The §12 hard gate applies: the analysis above is read at source, but it does **not** prove the defect
reaches the player, and it does **not** prove what HEAD renders. Do both first.

### Measurement 1 - DOES THE VEIL DRAW ON DEVICE? (a device frame)

`AddInnerRim` **returns early** when `BlinkChromeActive` (`ElarionUiKit.cs:2672`:
`if (host == null || BlinkChromeActive) return;`). That property
(`Assets/_Modules/Core/UI/ElarionUiKitConformance.cs:59-70`) is `FeatureFlags.BlinkChrome` **AND** a
live art-presence probe through `RpgUiCatalog`. Headless with the Blink art absent it is false, so the
veil draws - which is what the capture PNG shows.

> ⛔ **IF THE SHIPPING BUILD HAS THE ART PRESENT, THE VEIL IS SKIPPED AND THIS IS A HEADLESS-ONLY
> ARTIFACT.** In that case the fix is NOT to the veil at all, and the real question becomes why the
> owner's WO-1639 device frame read low contrast. **Capture one device frame of the in-raid readout and
> sample the plate before writing a line of code.** A fix authored against a headless-only artifact
> would repaint every panel in the game to chase a colour no player sees.

### Measurement 2 - WHAT DOES HEAD ACTUALLY RENDER? (a green re-capture)

The six PNGs on disk are the **RED** run (`wave5-capture3red`, 07:35); the GREEN run at 07:33 wrote the
same filenames and was overwritten (WO-1645 §4.1). So **HEAD's plate has never been seen rendered** -
the §2 argument is from construction, not from a HEAD pixel. Re-run `RunCaptureHeadless` at HEAD (no
code change needed) and sample `RaidHud_2670x1200.png` the same way. Record the RGB.

**If measurement 2 returns ~(89,72,20) again, §2 is confirmed empirically** (the veil dominates
regardless of the fill). If it returns near `(5,5,6)`, §2 is wrong and this ticket closes as
already-fixed - say so and hand back.

## 4. The fix options - the kit already has the seam

**`Panel` takes an `innerRim` parameter and it defaults to `true`** (`ElarionUiKit.cs:145-146`:
`Panel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, bool deep = false, bool innerRim = true)`).
So there is no new API to invent.

- **Option A (preferred - smallest blast radius): opt the two HUD readout plates out at the CALLER.**
  `RaidHudController.cs:469` and `RaidDeployController.cs:1664` both call `Panel(...)` without naming
  `innerRim`. Pass `innerRim: false`. **This is the pattern the tree already uses** -
  `HeroInventoryController.cs:632` does exactly `Panel(parent, min, max, deep: deep, innerRim: false)`.
  Two lines, two files, no kit change, no other surface moves.
- **Option B: a veil-free `Panel` variant / a `HudPlate` kit primitive**, if the lead wants a named
  concept for "a passive HUD plate that must not be veiled" rather than a boolean at two call sites.
  More honest as documentation, wider to review.
- ⛔ **Option C - lowering `AccentSoft`'s alpha, or changing `Gold` - IS FORBIDDEN WITHOUT A RULING.**
  That repaints every kit surface in the game to fix two. CLAUDE.md §7's standing form of this: lowering
  a kit constant so one screen reads is the inverse of the fix.

**Reach, counted 2026-09-10:** `grep -rn "ElarionUiKit.Panel(" Assets/ --include=*.cs` returns **10**
call sites; **2** already pass `innerRim: false`, so **8 plates carry the veil today** (including two
editor stubs in `Assets/Editor/HudComposer/HudStubGenerator.cs`). Option A moves **2** of those 8 and
leaves the other 6 exactly as they are - deliberately: this ticket is not a licence to de-veil the
whole game, and any other surface's look changes only on an owner ruling (§6).

## 5. Acceptance

1. **Both §3 measurements recorded in the RESULT, with numbers**, before any edit. If measurement 1
   shows the veil is skipped on device, STOP and hand back - that is a finding, not a failure.
2. **Plate samples near `ObsidianFill` in the capture PNG.** Re-run `RunCaptureHeadless` and sample
   `Builds/ui-capture/RaidHud_2670x1200.png` at the same five interior points as §1. Expect ~`(5,5,6)`
   (`ObsidianFill` at 0.98 over black), not `(89,72,20)`. **Paste the five samples.**
3. **A device frame of the in-raid readout**, plate sampled, agreeing with the capture.
4. **WO-1639's contrast ratios RE-MEASURED against the real background** and recorded: timer, `SPIRE`,
   `Razed`, unlit stars, `Troops`. WO-1639 §1a reported 2.67 / 1.98 / 1.72 / 1.55 / **1.12** against the
   0.42 plate; those numbers are now known to have been taken against the wrong ground on both sides of
   the fix. **Every row must clear 3:1** - that was WO-1639's own bar and it has never actually been
   verified against what renders.
5. **`UI_CAPTURE_OK`, `UI_GLYPH_OK` and `UI_GEOMETRY_OK` do not regress.** Judge by the MARKERS on a
   FRESH log, never the exit code (CLAUDE.md §8). ⚠ `UI_GEOMETRY_OK` is currently held red by **WO-1646**
   on these same two canvases - so the bar here is *no NEW geometry findings*, not a green marker, until
   1646 lands.
6. **Brace + NUL on every `.cs` touched**, including `python tools/gate_brace.py` (CLAUDE.md §1).

## 6. What NOT to touch

- ⛔ **`UiStyle.cs` constants** - `AccentSoft`, and anything it derives from. `ElarionUi.Gold` likewise.
  These are the game's palette; changing one to fix two plates is Option C and needs an owner ruling.
- ⛔ **`AddInnerRim` itself, and `Panel`'s `innerRim` DEFAULT.** Flipping the default de-veils 6 other
  live surfaces in one edit, unreviewed. If that is the right answer it is a separate, owner-ruled
  ticket with before/after captures of all 8 callers.
- ⛔ **Any other panel's look, without a ruling.** The 6 remaining veiled callers stay veiled.
- ⛔ **`FeatureFlags.BlinkChrome` / the `BlinkChromeActive` art probe.** If measurement 1 shows the flag
  is the real variable, that is a finding to report, not a flag to flip.
- ⛔ **`Assets/Editor/UICaptureLaunch.cs`** - WO-1645's, landed. Re-run it; do not edit it.
- ⛔ **The band geometry on these two canvases** - that is **WO-1646**, running in parallel. Colour only
  here, or the two lanes collide in the same files.
- **Do not commit. Do not push.** Hand the diff back (CLAUDE.md §11).

## 7. Why this was invisible until today

There was no gate that rendered these two canvases at all - that was **WO-1645**, and this is the second
thing its first run surfaced (after WO-1646's touch/overlap findings). A colour literal is legal source
text and every suite covering this HUD read source text, so a plate wearing a translucent gold slab over
a correct fill looked identical to a correct plate from every automated angle. The only detector was the
owner's eyes on a device frame, which is precisely what CLAUDE.md §14 exists to stop relying on.

---

## 7B. IMPLEMENTATION ADDENDUM (lane RAID-HUD, 2026-09-10) - the §5.4 re-measurement

*Appended by the implementing lane at the lead's instruction; the body above is the minter's and is
left as written (CLAUDE.md §15 - a WO is frozen by its date, so this is an addendum, not a rewrite).*

**Measurement 2 is CLOSED and §2 is CONFIRMED EMPIRICALLY.** The lead re-ran the capture at HEAD:
`Builds/wave5-capture4` (HEAD files, 07:43), `RaidHud_2670x1200.png` plate interior = **RGB (88, 71, 17)**
against §1's red-run `(89, 72, 20)`. **The veil is live at HEAD, on top of `ObsidianFill`.** The ticket
does not close as already-fixed.

**Measurement 1 (device `BlinkChromeActive`) is STILL OPEN** - it needs the next APK. **Option A was
implemented anyway, at the lead's instruction**, and §5.1's stop-rule is discharged as follows: even
if the veil turns out to be skipped on device, `innerRim: false` makes the CAPTURE and the DEVICE
render the same plate, which is what makes the gate evidence about the device at all. A gate measuring
a different surface than the player sees is the WO-1645 hole reopening in a new place.

### §5.4 - WO-1639's five rows, re-measured against three grounds

WCAG relative luminance; ratio = `(L_text + 0.05) / (L_plate + 0.05)`. Text floor 4.5:1, non-text
component floor 3:1.

| row | source colour | WO-1639 §1a<br>(0.42 plate, device) | **HEAD, VEILED**<br>(88,71,17) | **FIXED, capture**<br>(5,5,6) | **FIXED, device**<br>(10,10,11) |
|---|---|---|---|---|---|
| `3:00` timer | `Parchment` | 2.67 | **7.54** | **17.01** | **16.54** |
| `SPIRE 100%` | `Gilt` | 1.98 | **5.58** | **12.59** | **12.24** |
| `Razed 0%` | `ParchmentDim` | 1.72 | **4.84** | **10.91** | **10.61** |
| star diamonds (unlit) | `EmptyTrackFill` white@0.40 | 1.55 | ⛔ **2.86 - UNDER 3:1** | **3.71** | **3.77** |
| `Troops 0/0` | `ParchmentDim` | 1.12 | **4.84** | **10.91** | **10.61** |

Plate luminances: veiled `L = 0.066217`; `ObsidianFill` over black `L = 0.001544`; `ObsidianFill` at
alpha 0.98 over the arena background WO-1639 back-solved from the owner's frame `L = 0.003008`. The
last two barely differ, so the conclusion is robust to which ground you take.

### What that table actually says - two findings, one of them uncomfortable

1. ⛔ **WO-1639 left a floor unmet and did not know it.** On the ground that HEAD really renders, the
   **unlit honor diamonds sit at 2.86:1 - below the 3:1 component floor** WO-1639 sized
   `EmptyTrackFill` (white @ 0.40) to clear. That ticket predicted 3.77:1 and was right about the
   *fill* and wrong about *what paints the plate*. **This ticket is what makes that prediction true.**
2. **WO-1639's headline fix held anyway.** Every TEXT row clears 4.5:1 even on the veiled ground
   (4.84:1 at worst, up from 1.12:1). So the plate change was sound and the ticket was not wasted -
   what was wrong was the confidence, not the direction.

### ⚠ An observation that bears on Measurement 1, offered as UNPROVEN

WO-1639 §1a sampled the owner's **device** frame at **RGB (136, 145, 154)** - blue > green > red. A
gold veil shifts the opposite way (red > green > blue), and every headless sample of the veiled plate
is olive. **That is consistent with `BlinkChromeActive` being TRUE on device (Blink art present ->
`AddInnerRim` returns early at `ElarionUiKit.cs:2672`), i.e. the veil being a headless-only artifact.**

⛔ **I have NOT proven this** and it must not be treated as settled: one device frame with the plate
sampled closes it either way, and that is §5.3. If it holds, the honest reading is that this ticket's
value is **making the gate and the device agree**, not repairing what the player sees - and the
2.86:1 star row above would then be a capture-only number. **Both readings are written down here on
purpose so the next seat cannot inherit only the flattering one.**

---

## 8. Board

Minted READY and **unassigned**. Number pre-assigned by the lead; `CLI_LANES_WO_NUMBERS.md` deliberately
not edited by this lane. Whoever takes it owns flipping this file's `**Status:**` line and writing
`WorkOrders/WORK_ORDER_1647_raid_readout_plate_is_veiled_gold_by_the_kit_inner_rim.RESULT.md`; the lead
regenerates `BOARD.html`.
