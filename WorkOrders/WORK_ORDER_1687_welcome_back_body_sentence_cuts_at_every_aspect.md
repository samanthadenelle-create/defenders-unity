# WORK ORDER 1687 — The welcome-back away-window sentence is cut at every captured aspect

**Status:** IMPLEMENTED 2026-09-10 — band raised at the driver (`CappedSentenceH` 0.12 -> 0.21); RED line quoted below; `COMPILE_GATE` / `REGRESSION` / a fresh `WELCOME_BACK_CAPTURE` run are owed, then PO felt-verify to close
**Result:** `WorkOrders/WORK_ORDER_1687_welcome_back_body_sentence_cuts_at_every_aspect.RESULT.md`
**Silo:** UI / layout, one file (`WelcomeBackPopup.cs`). No economy, no gameplay, no scene files.
**Raised by:** the glyph oracle, on its **first ever run** over the welcome-back capture path (WO-1664 wired it there).
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.

---

## 1. The measured defect

`Builds/wave8-welcome1`, all three captured aspects:

```
[glyph-oracle] TEXT TRUNCATED [WelcomeBack_2670x1200 @2670x1200]
  'ObsidianPanel/PanelContent/Zone_Body/Label'
  ("Your realm gathers for a limited stretch whil...") draws 68 of 72 printable glyphs.
  (x -538.3..538.3, y -196.7..-167.6 ...
```

| aspect | drawn / printable | cut |
|---|---|---|
| 1920x1080 | **60 / 72** | 12 glyphs |
| 2340x1080 | **67 / 72** | 5 |
| 2670x1200 | **68 / 72** | 4 |

`UI_GLYPH_FAIL x3 NEW over 6 panels`.

**The producer is identified, not guessed.** The string is
`"Your realm gathers for a limited stretch while you are away. Nothing gathered is lost."` —
**exactly 72 non-space characters**, matching the oracle's printable count to the digit. It is the
`_result.WasCapped` sentence in `WelcomeBackPopup.Show` (the label built at `:302-305`), which is the
only full sentence in the body and the player's one explanation of the away-window cap.

---

## 2. ⛔ DID THE WO-1664 BAND MOVE CAUSE THIS? No — and here is the proof, not an opinion

The coordinator asked directly, because WO-1664 moved bands in this very file hours earlier
(`ActionBandY0/Y1` 0.040/0.180 and `BodyY0` 0.22 -> 0.24). Three independent lines of evidence:

### 2a. The oracle had NEVER run on this path. `UI_GLYPH_FAIL ... NEW` means new to the TALLY, not new to the BUILD.

`git show HEAD:Assets/Editor/UICaptureLaunch.cs` — `RunWelcomeBackCaptureHeadless` at HEAD reads, in
full:

```csharp
Directory.CreateDirectory(OutDir);
int count = ForEachTarget("WelcomeBack", CaptureWelcomeBackOnce) +
            ForEachTarget("WelcomeBackDoors", CaptureWelcomeBackDoorsOnce);
if (count == 6) Debug.Log("WELCOME_BACK_CAPTURE_OK 6/6");
```

**No `ResetGlyphOracle()`. No `ReportGlyphOracle()`.** WO-1664 added both. So there is no prior
measurement of this label anywhere, and no run in which it was ever proven whole. This is the same
class of finding WO-1664 was itself about — a verdict computed and thrown away — one panel further on.

### 2b. The arithmetic says it was already cutting at HEAD's band

`FitBlock` arms Normal wrap + **Truncate** + autosize 26..`FontMicro`(32); TMP's Truncate drops
whatever overflows the band's HEIGHT. At the 26 px floor a line costs ~`26 x 1.2 = 31` ref px, so two
lines need ~62 and three need ~94. Reference heights are 1080.0 / 978.4 / 965.4 (kit scaler
`referenceResolution (1080,1920)`, match 0.5); content = 0.84 x refH; body = (0.82 − `BodyY0`) x content;
the label band was 0.12 x body.

| aspect | body @HEAD (0.60) | band @HEAD | body @WO-1664 (0.58) | band @WO-1664 | 2 lines need |
|---|---|---|---|---|---|
| 2670x1200 | 486.5 | **58.4** | 470.3 | **56.4** | ~62 |
| 2340x1080 | 493.2 | **59.2** | 476.7 | **57.2** | ~62 |
| 1920x1080 | 544.3 | **65.3** | 526.2 | **63.1** | ~62 |

**At 2670 and 2340 the band was already short of TWO lines before WO-1664 touched anything.** The
oracle's own measured mesh extent corroborates it: `y -196.7..-167.6` is **29.1 px — a single rendered
line**.

### 2c. The cut is driven by WIDTH, which WO-1664 did not touch at all

**1920x1080 has the TALLEST band in pixels and cuts the MOST (60/72).** Height alone cannot explain
that. The narrower reference width (1920 vs 2148) forces more wrapped lines into a band sized for
fewer. WO-1664 changed only `y` values.

### ⚠ What is NOT claimed

WO-1664 **did** shrink the band by 3.3% (≈2 ref px) via `BodyY0` 0.22 -> 0.24, and at 1920 that crossed
the two-line threshold (65.3 -> 63.1 against ~62). So it is **excluded as the cause and NOT excluded as
a marginal contributor at one aspect.** The definitive check, if the lead wants it, is one line:
revert `BodyY0` to `0.22f` alone and re-run `RunWelcomeBackCaptureHeadless` — the truncation will still
red. It is not worth a build; §2a settles causation on its own.

---

## 3. The fix

`Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs` — the band, at its driver:

- **`:174` new `private const float CappedSentenceH = 0.21f;`** (was an inline `0.12f`), carrying the
  line arithmetic in its doc block.
- **`:304`** `Mathf.Max(0.03f, y - 0.12f)` -> `Mathf.Max(0.03f, y - CappedSentenceH)`.

`0.21 x 470.3 = 98.8` ref px at the **smallest** reference height — three lines at the autosize floor,
so it clears every aspect including the narrowest.

⛔ **NOT the font, and NOT the copy.** The autosize floor stays 26 (`ElarionUiKit.FontHardFloor` is 20;
going under it is forbidden), and the sentence is unchanged — shortening it needs an owner ruling,
because its subject has already been corrected twice (WO-1434 removed a false storage claim, WO-1499
closed the matching header suffix) and it is the only place the away-window cap is explained.

**Precedent, deliberately left alone:** `AddFooterSentence` already reserves `0.19f` for *"the one full
sentence on this screen"*. There were **two** full sentences and only one got that treatment — this was
the other. That helper is **not** re-pointed here: it is not reported as cutting, and moving a band
nobody measured to fix a band somebody did is how a felt-test report gets spent on working code.

---

## 4. Acceptance

1. A fresh `RunWelcomeBackCaptureHeadless`: `UI_GLYPH_OK <n>/<n>` with the `Zone_Body/Label` finding
   **gone**, and `WELCOME_BACK_CAPTURE_OK 6/6; touch=clean` still emitting. Marker on a fresh log, not
   the exit code.
2. The `WelcomeBack_*.png` frames **opened** at all three aspects — the sentence reads whole and does
   not collide with the row above it or the ready band below.
3. ⚠ **Watch the row budget.** The sentence takes 0.09 more of the body from the leftover `y`, and it is
   drawn LAST. In a worst-case report the `Mathf.Max(0.03f, ...)` clamp can still bite; if the capture
   shows it clamped, the next move is the body's own ceiling, not the font.
4. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a fresh log.
5. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written.

## 5. What NOT to touch

- ⛔ The **sentence text** — owner ruling required (WO-1434 / WO-1499 history).
- ⛔ The **autosize floor** — never below `ElarionUiKit.FontHardFloor` (20).
- ⛔ `AddFooterSentence`'s `0.19f` — different sentence, not reported as cutting (§3).
- ⛔ The **glyph baseline** — do not add this finding to it. It is fixed at the driver; a baseline entry
  would be the inversion, and that list is shrink-only.
- ⛔ WO-1664's `ActionBandY0/Y1` and `BodyY0` — they are the touch-floor fix, proven green
  (`UI_TOUCH_OK 6/6` on `Builds/wave8-welcome1`). §2 clears them of causing this.
- No scene files. No economy or gameplay change.

## 6. Evidence index

- `Builds/wave8-welcome1` — the three `TEXT TRUNCATED` lines and `UI_GLYPH_FAIL x3 NEW over 6 panels`
- `Builds/wave8-frontdoor1` — `UI_TOUCH_OK 6/6` + `FRONT_DOOR_CAPTURE_OK 6/6; touch=clean` (WO-1664 green)
- `git show HEAD:Assets/Editor/UICaptureLaunch.cs` — the glyph oracle's absence on this path at HEAD (§2a)
- `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs:174`, `:304` — the fix
- `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3044` — `FontHardFloor = 20f`
- `Assets/_Modules/Core/UI/ElarionUi.cs:115` — `FontMicro = 32`
