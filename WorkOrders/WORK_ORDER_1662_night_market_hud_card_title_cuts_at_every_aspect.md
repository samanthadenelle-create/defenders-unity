# WORK ORDER 1662 — The HUD Night Market card's title is cut at EVERY aspect, and the oracle that measures it is reading the wrong font

**Status:** IMPLEMENTED - awaiting gate
**Silo:** HUD / kit layout (`Assets/_Modules/HUD/Kit/HudKitController.cs`, `Assets/Editor/Regression/HudLabelFitRegression.cs`)
**Origin:** HUD-CHIP lane, surfaced by WO-1660's own fix — the town HUD canvas entered `RunCaptureHeadless` and the glyph oracle judged it for the first time
**Number:** PRE-ASSIGNED by the lead. **The `CLI_LANES_WO_NUMBERS.md` banner was NOT edited by this lane.**
**Result:** `WorkOrders/WORK_ORDER_1662_night_market_hud_card_title_cuts_at_every_aspect.RESULT.md`
**Date:** 2026-09-10

---

## 1. THE MEASUREMENT (captured, not inferred)

Gate run `Builds/wave7-capture1`, HEAD `a89603a7a`, verbatim, at all three landscape targets and at
**all six HUD panel builds** (`UI_GLYPH_FAIL x6 NEW over 106 panels`):

```
[glyph-oracle] TEXT TRUNCATED [AdaptiveHudPeaceful_2670x1200]
'Area_Minimap/Widget_nightMarketCard/NightMarketCard/NightMarketCardButton/NightMarketCardLabelPlate/Label'
("THE NIGHT MARKET") draws 12 of 14 printable glyphs.
(x -976.5..-749.8, y 57.2..126.1) at font 20 [autosize 20..26, enabled=True] overflow=Ellipsis
```

x band at the other two aspects: **1920x1080** `-865 .. -638.3`; **2340x1080** `-962.6 .. -736`.
Every one measures **226.7 ref px wide** — the card is authored in FIXED PIXELS
(`HudLayoutBands.NightMarketCardWidthPx = 320f`, `:165`), so the defect is aspect-independent. That
is why it is six panels and not one: three targets x the postures that occupy the card.

⚠ **The label is at `font 20`, i.e. autosize has already bottomed out on `ElarionUiKit.FontHardFloor`
and STILL ellipsised.** There is no room left underneath. The remedy cannot be a smaller font.

**This is NOT a regression from WO-1660.** The card is unchanged; WO-1660 merely wired the HUD canvas
into the gated capture (`UICaptureLaunch.cs:639`), so `RenderCanvasToPng`'s glyph oracle judged this
surface for the first time. The cut is old. Nothing measured it before.

---

## 2. THE PRODUCER (read at source 2026-09-10)

`Assets/_Modules/HUD/Kit/HudKitController.cs`, `BuildNightMarketCard`:

| What | Where |
|---|---|
| The button + its label string (canon `storeWordmark` via `HudStrings.StoreFaceLabel("hud-card")`, WO-1398) | `:1320-1322` |
| The dark label plate, `x NightMarketLabelPlateX0 .. 0.97`, `y 0.46 .. 0.92` | `:1379-1381` |
| The label reparented to the plate at `x 0.04 .. 0.96`, `y 0.02 .. 0.98` | `:1386-1390` |
| `fontSize = 26`, `NoWrap`, `overflow = Ellipsis`, `FitSingleLine(face, 20f, 26f)` | `:1391-1400` |
| `NightMarketLabelPlateX0 = 0.20f` | `:1190` |

**The label rect, derived from those authored numbers and confirmed by the capture to the tenth:**
`(0.97 - 0.20) x 320 x 0.92 = ` **226.7 ref px** wide (the `0.92` is the `0.04..0.96` inset),
`(0.92 - 0.46) x 156 x 0.96 = ` **68.9 ref px** tall. The capture's x band is 226.7 and its y band is
68.9. **The model and the device agree exactly**, so the arithmetic below is a model of the render.

---

## 3. ROOT CAUSE — THE ORACLE MEASURES A FONT THE CARD DOES NOT DRAW

Two pins already exist for exactly this label and **both are GREEN on the cut build**:
`HudLabelFitRegression` **11c** `[night-market-standout]` (`:1888-1918`) and **12e**
`[night-market-aurora]` (`:2023-2043`). Each measures
`ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, upper, FontHardFloor, out d)` against
that same 226.7 px plate.

**The card never draws FontRole.Body.** `ElarionUiKit.BuildObsidianButton` ends on
`MedievalUiSkin.ApplyButton(btn, ...)` (`ElarionUiKitObsidian.cs:688`), and `ApplyButton`
(`MedievalUiSkin.cs:83-95`) does four things to the label:

```
:86  label.text = label.text.ToUpperInvariant();     // already known (WO-1466 fixed the CASE half)
:88  label.fontStyle |= FontStyles.Bold;
:89  label.characterSpacing = 2f;
:91  ElarionUiKit.EnsureFont(label, ElarionUiKit.FontRole.Title);   // <- Merriweather Bold, NOT Alata
```

The card's slice afterwards overrides size/alignment/colour, but **never resets the font role or the
spacing**. So the drawn face is **Title**, bold, spaced — and `MeasureLineWidthPx` sums **regular
weight advances with no spacing term** (`ElarionUiKitObsidian.cs:2905-2938`, read at source).

**The arithmetic, computed offline this session from the committed font assets** (glyph advances
summed exactly as `MeasureLineWidthPx` does; `m_PointSize 64`, `m_Scale 1`):

| Model | "THE NIGHT MARKET" at font 20 | vs the 226.7 px rect |
|---|---|---|
| `font_body` (Alata Regular) — **what the pins measure** | **177.6** | 78% — comfortable pass |
| `font_title` (Merriweather Bold) — **what is drawn** | **214.7** | 95% — nearly full before anything else |
| + `characterSpacing = 2` | ~221 | 97.5% |
| + TMP faux-bold (`font_title.asset:2968` `boldSpacing: 7`, ~7% of font size per char = ~22.4 px over 16 chars) | **~243** | **OVER by ~17 px — the captured cut** |

~17 px over is two glyphs plus the ellipsis. **The pins were 37-65 px light because they were reading
Alata Regular against a Merriweather Bold render**, which is exactly why a cut label survived two
oracles written to catch it. WO-1466 already fixed the CASE half of this same blind spot in the same
two cases; **this is its weight-and-role half.**

---

## 4. FIX SHAPE

**A. Two lines, at the ceiling — not a wider plate, and never a smaller font.**
The band is 68.9 px tall. Two Title lines at the **26 px ceiling** need
`2 x 26 x 1.257 = 65.4 px` (`font_title` `m_LineHeight 80.448 / m_PointSize 64 = 1.2570`) and the
widest wrapped line, `"THE NIGHT"`, measures **154.5 px raw / 177.7 px with slack** in a 226.7 px
rect — **30% of margin**, against a one-line widening whose best case is a few px. So:
`TextWrappingModes.NoWrap` + `FitSingleLine(face, 20f, 26f)` becomes
`ElarionUiKit.FitBlock(face, ElarionUiKit.FontHardFloor, 26f)`.
The title gets **BIGGER** (26 rather than the bottomed-out 20), which also serves WO-1384's own owner
ruling — *"night market ... needs to be the shining gem ... it should stand out"*.

⛔ **Do not lower a font floor. Do not shorten the wordmark** — it is canon `storeWordmark`, single
-sourced by `StoreNameSingleSourceRegression`, and changing it is an owner ruling, not a layout fix.
⛔ **Do not touch `NightMarketLabelPlateX0`, `MedievalUiSkin`, `BuildObsidianButton` or
`MeasureLineWidthPx`** — see §6.

**B. Re-point both pins onto the font that is actually drawn.** 11c and 12e must measure
`FontRole.Title` with a named slack for the bold weight + character spacing, and must model the TWO
-LINE contract when the slice wraps. A pin that measures the wrong face is worse than no pin: it
reports green over a defect, and it did so here for two builds.

---

## 5. RED-FIRST

The pre-fix `[glyph-oracle] TEXT TRUNCATED` line in §1 **is** the red, already captured on
`Builds/wave7-capture1`. The re-pointed 11c/12e must ALSO be red on that same tree — with the slack
in place, `214.7 x 1.15 = 246.9 > 226.7`. A pin that goes straight to green is a pin that was never
seen fail.

---

## 6. WHAT NOT TO TOUCH

- ⛔ `MedievalUiSkin.ApplyButton` and `ElarionUiKit.BuildObsidianButton` — the skin is the shared
  button contract; changing the font role there re-skins every button in the game.
- ⛔ `ElarionUiKit.MeasureLineWidthPx` — it does exactly what it documents (regular-weight advances).
  The bug is the CALLER passing the wrong role, not the measurer.
- ⛔ `NightMarketLabelPlateX0`, the card's px size, the ring/aura/comets — pinned by 11a/11b/12a-12d.
- ⛔ The canon `storeWordmark` string.
- ⛔ The `GlyphBaseline` array (`UICaptureLaunch.cs:6174-6179`). This row is NOT baselined and must
  never be added to it — the fix is the fix.

---

## 7. THE DECK-CARD TWIN — RELATED, DIFFERENT PRODUCER, ALREADY LEDGERED

`WORK_ORDER_1636_glyph_oracle_first_run_68_truncated_labels.RESULT.md:1109` holds, as **accepted
debt** in the `GlyphBaseline` array:

```
RealmWorkspace_1920x1080|ObsidianPanel/PanelFill/Zone_Body/RealmCardGrid/DeckCard_The Night Market/Label|12 of 14
```

Same string, same 12-of-14 count, same `overflow=Ellipsis wrap=NoWrap` shape — and **almost certainly
the same root cause** (an obsidian face wearing the skin's Title/bold while its band was sized off a
Body-weight estimate). It is a **different producer**: that title is authored by
`PlayerDeckWorkspace`, this one by `HudKitController`, and WO-1636 §"Accepted debt" records it was
out of that ticket's lane scope. **This WO fixes the HUD instance only.** The deck-card row stays
baselined; whoever takes it should read §3 here first — the diagnosis transfers even though the file
does not.

---

## 8. FOLLOW-UP THIS WO DELIBERATELY DOES NOT DO

`grep -c "MeasureLineWidthPx(ElarionUiKit.FontRole.Body" Assets/Editor/Regression/HudLabelFitRegression.cs`
returns **12**. Every one of those that measures a face built through `BuildObsidianButton` is
measuring Alata Regular against a Merriweather Bold render — **+21% before bold** on this string.
`RumorBoardPanel.cs:126-128` already concedes the weight half of the gap and carries a
`PageButtonBoldSlack = 1.10f` for it, on Body advances. **A systemic sweep of those 12 call sites is
its own ticket** (and may want the slack folded into a kit helper rather than copied a thirteenth
time). This lane re-points the two that own the captured defect and names the rest here.

---

## 9. ACCEPTANCE

- [ ] The re-pointed 11c/12e were seen **failing** on `a89603a7a`, and pass after the fix.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs (markers, never exit codes).
- [ ] A fresh `RunCaptureHeadless` carries **no** `[glyph-oracle] TEXT TRUNCATED` line naming
      `NightMarketCardLabelPlate/Label`, and `UI_GLYPH_OK` with **0 new**.
- [ ] The three `AdaptiveHudPeaceful_*.png` are **opened** and the title reads whole on two lines at
      or above the hard floor.
- [ ] `UI_TOUCH_OK` and `UI_GEOMETRY_OK` stay clean — the label moved inside a plate that did not.

---

## 10. UNPROVEN

- **That `FontRole.Title` is what TMP finally RESOLVED** is proven only as far as the source request
  (`MedievalUiSkin.cs:91`) plus elimination: a Body render measures 177.6 px in a 226.7 px rect and
  could not cut. `EnsureFont` can fall through when an asset is absent. The capture PNG and a device
  frame are the direct proof.
- **Where autosize settles.** Two lines at 26 leave 3.5 px in the 68.9 band; TMP's block bounds may
  settle at 24-25. Still far above the 20 px hard floor, so within contract either way — but the
  number comes from the PNG, not from here.
- **The `ArmFitGuard` interplay.** `FitBlock` arms the same guard as `FitSingleLine`
  (`ElarionUiKitObsidian.cs:3090`); the device-side `TextFitGuard` behaviour on the new two-line band
  is unmeasured until a logcat.
