# WORK ORDER 1687 — The welcome-back away-window sentence is cut at every captured aspect

**Status:** IMPLEMENTED (PASS 3) 2026-09-10 - pass 1 was a proven no-op, pass 2 reserved the band but left six consumers able to draw through it AND broke the compile (CS7036, mine), pass 3 moves the floor into the room CHECK so every caller inherits it. Owner ruled 13:32: SENTENCE FIRST, drop the lines that do not fit - recorded in section 3B. `COMPILE_GATE` / `REGRESSION` / a fresh `WELCOME_BACK_CAPTURE` are owed, then PO felt-verify to close
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

## 3B. ⛔ PASS 1 WAS A NO-OP. The band was never height-limited — it was LEFTOVER-limited.

`Builds/wave9-welcome1`, run with `CappedSentenceH = 0.21f` **confirmed present in the main tree**
(`grep -n "CappedSentenceH" …/WelcomeBackPopup.cs` → `:174` `0.21f` and `:304`), printed **the same
line, with a byte-identical rect**:

```
TEXT TRUNCATED [WelcomeBack_1920x1080] 'ObsidianPanel/PanelContent/Zone_Body/Label'
  ("Your realm gathers for a limited stretch whil...") draws 60 of 72 ... (x -481.2..481.2, y -220.1..-187.5)
```

**The producer was right; the mechanism was wrong.** The sentence was seated at
`Mathf.Max(0.03f, y - CappedSentenceH) .. y`. Traced through the worst-case fixture
(`CaptureWelcomeBackOnce`: four non-zero resources, no doors, a three-line mend report,
`WasCapped = true`):

| step | consumes | `y` |
|---|---|---|
| start | — | 0.820 |
| 4 resource rows | `4 x (0.095 + 0.012)` | **0.392** |
| 0 door rows (this fixture sets no PostureSignals) | — | 0.392 |
| 3 mend lines | `3 x (0.09 + 0.01)` | **0.0920** |

At `y = 0.092` the `Mathf.Max` clamps to `0.03` **for every H above 0.062**, so the band is
`0.092 - 0.03 = 0.062` of body — at 1920x1080 that is `0.062 x 526.2 =` **32.6 ref px**, which is
`220.1 - 187.5` **exactly**. H = 0.12 and H = 0.21 yield an identical rect, which is why the second
capture reprinted the first capture's numbers.

⚠ **My pass-1 RESULT named this clamp as a residual risk and then asserted the band would grow
anyway. It could not. The clamp was already binding before the change, and one line of arithmetic
against the fixture would have shown it. That is the error, recorded rather than quietly corrected.**

### The real finding: the body is OVERSUBSCRIBED, so something must yield

| wants | of body |
|---|---|
| 4 resource rows | 0.428 |
| 3 mend lines | 0.300 |
| the away-window sentence | 0.210 |
| **total demand** | **0.938** |
| **available** (`0.82` down to `MinRowY` `0.06`) | **0.760** |

**Short by 0.178 however the space is divided.** No height constant can resolve that — only a
priority decision can.

### Pass 2 — reserve the sentence, let the mend lines yield

- **`:174-201`, new `RowStackFloor`** = `MinRowY + (WasCapped ? CappedSentenceH + RowGap : 0)`.
- **`:304`** the sentence is seated at the **absolute** band `MinRowY .. MinRowY + CappedSentenceH` —
  no longer derived from `y`, so it cannot be squeezed by whatever ran before it.
- **`AddMendLine`** now takes that floor and **returns false** instead of drawing through it;
  `AddMendRows` `FlowTrace.Warn`s the count it skipped.

Post-fix trace, same fixture: resource rows leave `y = 0.392`, the floor is `0.282`, **one** mend line
draws (`y = 0.292`), two are skipped and warned, and the sentence gets its full
`0.21 x 526.2 =` **110.5 ref px** — about **3.6 lines** at the 26 px autosize floor, against the
32.6 px (one line) it had. The 72-glyph sentence fits.

### ⭐ OWNER RULING 2026-09-10 13:32 — **SENTENCE FIRST; drop the lines that do not fit**

Pass 2 raised the priority question as open. **It is now closed by the owner:** the away-window
sentence outranks the flowing lines, and anything that does not fit is dropped rather than allowed to
squeeze it. **This is no longer "flagged, not decided" — it is the rule this file implements**, and it
is why the skip paths below are correct rather than a compromise. A sentence cut mid-word tells the
player something **false** about their rewards; a skipped line is an **omission** whose fact the Echoes
panel still holds. `AddDoorRows` had already established skip-and-`FlowTrace.Warn` as this screen's
answer to "no room", so the ruling generalises an existing precedent rather than inventing one.

## 3C. ⛔ PASS 3 — the compile break I caused, and the six consumers pass 2 missed

`Builds/wave10-compile1`:

```
Assets\_Modules\Village\Harvest\UI\WelcomeBackPopup.cs(571,33): error CS7036: There is no argument
given that corresponds to the required formal parameter 'floor' of
'WelcomeBackPopup.AddMendLine(Transform, ref float, string, Color, float)'
```

**Mine.** Pass 2 added the `floor` parameter to `AddMendLine` and updated three of its **four**
call sites. The fourth is `AddDestinyFooter`'s silo-stalled line. I swept the mend rows and never
grepped the method's own name.

**And the miss was not only a compile error — it was a hole in the reservation.** That call was guarded
by `HasRoom(y)`, which compared against `MinRowY` (0.06), **not** the reserved floor. So did five
others: job rows, the "ALSO FINISHED" aggregate, collector rows, "ALSO WAITING", and the table footer.
Pass 2 taught the three loudest consumers to respect the band and left six free to draw straight
through it. **Fixing the callers one at a time is how a reservation becomes a suggestion.**

**Pass 3 moves the rule into the CHECK, so every caller inherits it:**

- `HasRoomFor(y, h)` is new and measures against **`RowStackFloor`**; `HasRoom` and `HasDoorRoom` are
  now expressed in terms of it. All three became **instance** members (the floor depends on
  `_result.WasCapped`); every one of the eight call sites was already in an instance method, verified
  method-by-method, so nothing else moved.
- The silo-stalled line passes `RowStackFloor` and traces its skip.
- ⚠ **A latent bug found while doing it:** the table footer asked `HasRoom(y)` — which reserves
  `RowH` **0.095** — and then called `AddFooterSentence`, which consumes **0.19**. It could clear its
  own gate and still overrun by a full row, landing on the reserved band. It now asks
  `HasRoomFor(y, FooterSentenceH)`, and `AddFooterSentence` reads the same named constant so the check
  and the consumption can never disagree again.

**Post-pass-3 trace, worst-case fixture:** floor `0.282`; resource rows leave `y = 0.392`; **`mended`
draws** (`y = 0.292`); `spent` and `stalled` skip; the silo-stalled line and the footer sentence both
skip; every skip is `FlowTrace.Warn`ed with `y` and the floor. The sentence keeps its full
`0.21 x 526.2 = ` **110.5 ref px** ≈ **3.6 lines**.

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
