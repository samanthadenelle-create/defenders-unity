# WORK ORDER 1663 — RESULT

> ## ⚠ CORRECTION APPENDED 2026-09-10 (WO-1667 §C) — THE "PRE (Body)" COLUMN BELOW IS WRONG
>
> This ledger's Body figures were measured from `Assets/Resources/RpgUi/font/font_body.asset`.
> **The live measurer never loads that asset.** `FontFor(FontRole.Body)` runs the numeral-legibility
> gate, `font_body` **FAILS** it (`[Flow:UI] role font 'font_body' REJECTED - its numeral 1 is a bare
> stroke`, `Builds/wave8-reg1`), so it falls through to `ResolveDefaultFont()` =
> **`ElarionLocaleFallback`**, which is WIDER. Every measurement in that whole gate log names that one
> asset.
>
> **What survives:** the `Title` column and every charged/post number — `font_title` is **accepted**
> by the same gate, so those were measured from the asset that really draws. **Every conclusion in
> this ledger stands**, and §2's headline gets stronger: re-measured with the REAL Body font,
> `"Tap to collect"` @30 is **178.4** px in a 202.4 px box — even further inside than the 182.7
> reported below — and it still shipped cut, while `Title x1.15 = 232.2` reds.
>
> **What to distrust:** any "PRE (Body)" number in §3's table. The re-measured values are in the
> WO-1667 RESULT §C1. Per CLAUDE.md §15 this dated ledger's body is NOT rewritten — this banner
> supersedes the column.



**Status:** PARTIALLY IMPLEMENTED — awaiting gate + three rulings
**Lane:** LABEL-PINS
**Date:** 2026-09-10
**Tree:** worktree off `dev` @ `8470a9b18e2b4e77a7af6986e8b516461bb3bcb4` (WO-1662's
`CheckNightMarketTitleFit` present, `HudLabelFitRegression.cs:1911`)
**Files changed:** `Assets/Editor/Regression/HudLabelFitRegression.cs` (ONE file), plus this
RESULT and the WO's own Status line.
**Not touched:** `HudKitController.cs`, `MedievalUiSkin.cs`, `ElarionUiKitObsidian.cs`, the font
assets, every font floor, `GlyphBaseline`, `RailChipWidthPx`, cases 11c/12e, `RumorBoardPanel.cs`.
**No Unity run, no commit** (per the brief).

---

## 1. WHAT WAS BUILT

**One shared skinned-face measurer, no third copy of the constants.**

`MeasureFacePx(ElarionUiKit.FontRole role, string text, float sizePx, out string detail)`
(`HudLabelFitRegression.cs:1938-1945`) calls `ElarionUiKit.MeasureLineWidthPx`, returns the `-1`
sentinel untouched, and multiplies by `SkinnedFaceWidthSlack` **iff** `role == SkinnedFaceRole`.
The role is the switch, decided in exactly one place. `SkinnedFaceWhy()` (`:1949-1954`) is the one
copy of the failure sentence. `SkinnedFaceRole` / `SkinnedFaceWidthSlack` are WO-1662's own
constants, reused, not re-declared.

`TryWrapLines` (WO-1662) and `CheckNightMarketTitleFit`'s one-line branch now route through
`MeasureFacePx` as well — three inlined `* SkinnedFaceWidthSlack` expressions collapsed into the
one function. Behaviour identical; cases 11c/12e are otherwise untouched.

The two shared helpers take a **role parameter**:
- `WrappedLineCount(text, boxW, fontSize, ElarionUiKit.FontRole role)` — `:1228`
- `LongestOf(lines, fontSize, ElarionUiKit.FontRole role)` — `:1248`

**Four sites re-pointed** (Body -> `MeasureFacePx(SkinnedFaceRole, ...)`), old line -> new line:

| WO # | old | new | site |
|---|---|---|---|
| 1 | `:540` | `:566` | Case 2 `[collector-chip]` action lines |
| — | `:555` | `:582` | Case 2 height, via `WrappedLineCount(..., SkinnedFaceRole)` |
| 2 | `:566` | `:593` | Case 2 chip title |
| — | `:571` | `:600` | Case 2 note, via `LongestOf(..., SkinnedFaceRole)` |
| 3 | `:603` | `:648` | Case 3 `[manage-face]` |
| 9 | `:2524` | `:2625` | Case 15 `[builders-chip-idle]` |

**Three plain-label sites UNCHANGED, and confirmed unchanged** — still
`ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, ...)`:
`:741` (Case 4 wave countdown), `:1832` (Case 10 heartfire), `:2465` (Case 13 heart objective).
Grep for `MeasureLineWidthPx` in the file now returns exactly those three plus the single call
inside `MeasureFacePx`.

**Gate checks:** `python tools/gate_brace.py Assets/Editor/Regression/HudLabelFitRegression.cs`
-> `GATE_BRACE_SUMMARY bad=0 of 1`, exit 0. NUL bytes: 0. Raw braces 219/219.

---

## 2. ⛔ §6's RED-FIRST ACCEPTANCE CANNOT BE MET AS WRITTEN — AND THAT IS A FINDING, NOT A MISS

The WO predicts two reds. **Both were computed on the UPPER-CASE form of the string.** My offline
reproduction of §3's method matches the WO's §3 table to the decimal — and it matches the
**`TITLE_UPPER`** column, not the authored-case column. So the prediction is only true if those
chips draw upper case.

**They do not, and the proof is at source (all read 2026-09-10):**

1. `MedievalUiSkin.ApplyButton:86` — `label.text = (label.text ?? string.Empty).ToUpperInvariant();`
   is a **one-shot on the VALUE**, at apply time.
2. `:88` sets `label.fontStyle |= FontStyles.Bold;` — **weight only**.
   `grep -rn "FontStyles.UpperCase" --include=*.cs Assets/_Modules/` returns **nothing**. There is
   no persistent upper-casing style anywhere in the module tree.
3. Both rail chips re-assign `.text` **after** the skin ran, so the upper-casing is overwritten:
   - `HudKitController.cs:5373` — `_collectorsChipLabel.text = FormatCollectorChip(cs);`
   - `HudKitController.cs:2749` — `_collectorsChipLabel.text = HudStrings.Get(KeyCollectorsTitle);`
     (in `RefreshLocalizedHudCopy`, which `BindLocalizedHudCopy` calls at the end of
     `BuildCollectorsChip`, so it runs immediately at build too)
   - `HudKitController.cs:5348` — `_queueChipLabel.text = FormatQueueChip(qs);`

So the drawn glyphs are `Harvest`, `99 waiting`, `Builders idle 2` — not `HARVEST`,
`99 WAITING`, `BUILDERS IDLE 2`. **Measuring the upper form here would model a render that does
not happen — which is this file's own root cause, one step to the other side.** I did not do it,
and I did not fix a producer to satisfy a red that would have been manufactured.

WO-1662's own site is unaffected: its title is a **build-time** string and it uppers it itself
(`upper = word.ToUpperInvariant()`), which is correct for that face.

### The re-point is still right, and here is the evidence that proves it without the predicted reds

`"Tap to collect"` — the string the 2026-08-22 fleet captured **shipped cut** to `"Tap to collec"`
in all 8 runs, the reason Case 2 exists:

| measured @30, rail-chip box 202.4 ref px | width | verdict |
|---|---|---|
| **Body (what the pin measured before)** | **182.7** | **"it fits" — the pin could NEVER have caught its own defect** |
| Title x1.15 (what it measures now) | 232.2 | **RED, by 29.8 px** |

That is the whole ticket in one row. (Side note: `HudKitController.cs:2226` claims Body
`"Tap to collect"` is "~214 ref px"; it reproduces as **182.7**. The comment's number is wrong,
its conclusion is right — flagged, not edited, since that file is out of this lane's scope.)

---

## 3. EVERY SITE, PRE AND POST, WITH NUMBERS

Method: §3 of the WO, reproduced offline from the committed TMP YAML
(`Assets/Resources/RpgUi/font/font_body.asset`, `font_title.asset`; both `m_PointSize 64`,
`m_Scale 1`, 106 glyphs / 106 chars each). Byte-for-byte the sum `MeasureLineWidthPx` performs.

**Rail chip label box = `RailChipWidthPx * ButtonLabelInset` = 220 x 0.92 = 202.4 ref px.**

| # | Case | string | @ | PRE (Body) | POST (Title x1.15) | Title-UPPER x1.15 (the WO's column) | verdict |
|---|---|---|---|---|---|---|---|
| 1 | 2 `[collector-chip]` | `Collect` | 30 | 92.9 | **118.8** | 161.7 | green |
| 1 | 2 | `99% - tap` | 30 | 145.6 | **175.9** | 192.0 | green |
| 1 | 2 | `99 waiting` | 30 | 147.5 | **185.3** | 219.4 (WO predicted RED) | **green** |
| 2 | 2 | `Harvest` (title) | 30 | 103.1 | **135.2** | 169.6 | green |
| — | 2 | height: `Collectors 12/12 full` wrap +1 | 30 | 3 lines = 108.0 | **3 lines = 108.0** | — | green (112 px chip, 4.0 px margin) |
| — | 2 | `LongestOf` note | 30 | 147.5 | **185.3** | — | note only |
| 3 | 3 `[manage-face]` @2670x1200, box 220.4 | `Manage` / `3 idle` / `2/3 idle` | 30 | 115.2 / 77.0 / 110.2 | **138.1 / 92.0 / 127.7** | 159.5 / … | green |
| 3 | 3 @1920x1080, box 197.0 | same three | 30 | same | **same** | — | green |
| 9 | 15 `[builders-chip-idle]` | `Builders idle 2` | 22 | 142.9 | **180.6** | 222.2 (WO predicted RED) | **green** |
| 4 | 4 `[wave-band]` | — | — | Body | **Body, UNCHANGED** | — | plain label |
| 7 | 10 `[heartfire-inside-plate]` | — | — | Body | **Body, UNCHANGED** | — | plain label |
| 8 | 13 `[heart-objective-state]` | — | — | Body | **Body, UNCHANGED** | — | plain label |

**Net: every re-pointed site is a PROVEN-FITTING face under the correct measurement** — which the
WO itself names as a legitimate and useful outcome (§6, last paragraph). No producer fix was
needed, so `HudKitController.cs` was not opened for edit and the WO-1660/1662 regions are untouched.

⚠ **One thin margin worth a note:** Case 2's height check now lands at **108.0 of 112 px**. One
more wrapped line in `hudCollectorsCount` would red it. Not a defect today — and see §4B: that
string has **no drawing caller either**, so the margin is over a surface nobody paints.

**No external pin was disturbed.** `grep -rn "WrappedLineCount\|LongestOf\|SkinnedFaceWidthSlack\|
SkinnedFaceRole" Assets/Editor/ --include=*.cs` outside this file returns only
`InventoryArmoryRailRegression.cs:527/775`, which declares its **own private** `WrappedLineCount`
(§4F) — no caller of this file's helpers exists elsewhere, and nothing pins its source text beyond
the `DataRegression` registration.

---

## 4. THREE FINDINGS SURFACED FOR A RULING — recorded, NOT silently fixed

All three are written into the code at the site, so the next reader meets them where it matters.

### A. Site #3 / §5 — Case 3 measures a RETIRED surface (the WO's own open question, confirmed)
`HudActionBarModel.MaxVisibleFaces = 4` (`HudActionBarModel.cs:139`) still sizes this case's box.
Read at source: `HudKitController.BindActionBar` (`:3473`) opens at `:3480-3486` with
`if (_peacefulDockRoot != null) { ...SetActive(false) on every _barButtons[i]...; FlowTrace.Step(
"HudKit", "adaptive peaceful dock owns the actionBar; legacy repacker retired"); return; }`.
**CLAUDE.md §7 holds as written.** So Case 3 is a green oracle over a dead geometry, and the
re-point makes it a *correctly-measured* green oracle over a dead geometry. Recorded at
`HudLabelFitRegression.cs:611-625` (`grep -n "WO-1663"` anchors: 523, 611, 625, 1223, 1247, 1918, 2610).
**Ruling needed:** retire Case 3, or re-point it at the dock's measured slots
(`HudActionBarRegression.CheckMeasuredPeacefulDock` is the live authority). Not a lane's call.

### B. Site #1 — FOUR collector keys are no longer drawn on the chip, and the height check goes with them
`FormatCollectorChip` (`HudKitController.cs:2239-2246`) returns
`HudStrings.Get(HudStrings.KeyCollectorsTitle)` and nothing else; WO-1194 moved the storage state
to the three resource rows. Grepped `--include=*.cs` across `Assets/` 2026-09-10:

- `KeyCollectorsFullLine` / `KeyCollectorsNearlyLine` / `KeyCollectorsWaitingLine` — **no drawing
  caller.** Only `HudStrings.cs:61/65/68` + `:130-131`, this suite, and
  `CollectorTellRegression.cs:233-235` (a copy-law check, not a render).
- `KeyCollectorsCount` (`"Collectors {0}/{1} full"`) — **also no drawing caller.** Hits are
  `HudStrings.cs:55` + `:130`, `CollectorTellRegression.cs:231/249`, `SmartArgumentRegression.cs:26`,
  `LocalizationSmartStringPackageTests.cs:19`, and `HudLabelFitRegression.cs:555`. All oracles.

So Case 2 branch **(a)** measures three dormant strings **and branch (b)'s height check wraps a
fourth** — which means §3's "thin 108.0 of 112 px margin" is a margin on a string nobody paints.
Recorded at `HudLabelFitRegression.cs:523-547`.
**Ruling needed:** retire those four keys + branches (a)/(b), or keep them as a pre-emptive pin.
Retiring canon copy is a COPY decision, so this lane did not touch it.

### C. Site #9 — the Builders chip does not build today, and that is DELIBERATE
`HudKitController.cs:811` — `// BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)`. The
method's header (`:1930-1936`) says the wiring is kept on purpose ("two lines from returning") and
that `SessionShapeRegression Case7_OneDoor` FAILS the build if that byte-exact retirement line
disappears. So Case 15c is a legitimate pre-emptive pin — but **no red can fire on a shipped
screen from it**, and its green must not be read as the live bar being measured. Recorded at
`HudLabelFitRegression.cs:2610-2622`. **No ruling strictly needed; flagged so the board is honest.**

### D. WO table correction (minor, but the WO asked for the classification to be re-read)
The WO's §2 row 6 says `LongestOf` is "used by Case 7 `[deck-card-packaging]`". It is not.
`grep -n "WrappedLineCount\|LongestOf"` on the pre-edit file returned exactly two call sites,
`:555` and `:571`, **both in Case 2**. Both helpers had one caller each, both obsidian. The role
parameter was added anyway (acceptance requires it, and it is what stops the next caller
inheriting a hardcoded role).

### E. Also noted, not acted on
Case 2 measures at `ElarionUiKit.FontFloor` (30) while `BuildRailChip` sets `lbl.fontSizeMin = 22f`
(`HudKitController.cs:2291`), `fontSizeMax = 30f` (`:2292`) and `ElarionUiKit.FitSingleLine(lbl, 22f, 30f)`
(`:2293`), after `MedievalUiSkin.ApplyButton(btn, primary: true)` at `:2287`. The suite is therefore
**conservative** (it measures at the ceiling of that range), which is the safe direction. Left alone.

### F. The §9 follow-on already has a second copy — worth knowing before it grows a third
`Assets/Editor/Regression/InventoryArmoryRailRegression.cs:775` declares its **own private**
`WrappedLineCount(text, boxW, fontSize)`, hardcoded to Body, called at `:527`. It is a different
class in the same assembly, so this lane's role parameter does not reach it. Whether the faces it
measures are obsidian was **not** investigated — out of scope, flagged only. Together with
`RumorBoardPanel.PageButtonBoldSlack = 1.10f` (WO §9) that is now **two** known duplicates of this
measurement outside the one function. Nothing was added here that makes unifying them harder.

---

## 5. ACCEPTANCE, LINE BY LINE

- [x] All 9 sites re-read at source at implementation time and classified — the WO's line numbers
      matched exactly on `8470a9b18`; the classification table is §3 above.
- [ ] **The two named §6 sites seen RED.** ⛔ **NOT MET, AND DELIBERATELY NOT FORCED** — §2 above:
      the predictions are upper-case measurements of labels that draw authored case. The re-point
      *did* take; the diff and the `Tap to collect` row prove it.
- [x] The three plain-label sites are unchanged, and are said to be unchanged (§1).
- [x] No new copy of the role or the slack — one `SkinnedFaceRole`, one `SkinnedFaceWidthSlack`,
      one `MeasureFacePx`, one `SkinnedFaceWhy`, a role PARAMETER on both shared helpers. Three
      pre-existing inline `* SkinnedFaceWidthSlack` expressions were folded into the one function.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` — **the lead's step** (this lane runs no Unity).
      `gate_brace` clean, NUL clean, 219/219 braces.
- [ ] `RunCaptureHeadless` / `UI_GLYPH_OK` — the lead's step.
- [x] Site #3's second finding written up and routed (§4A), plus two more the re-point exposed.

---

## 6. WHAT THE LEAD/OWNER HAS TO DECIDE

> **⭐ RULED 2026-09-10 (lead): items 1 and 2 below are TECHNICAL, not the owner's** —
> *"a pin over a retired surface is not coverage."* They are minted as ONE ticket,
> `WorkOrders/WORK_ORDER_1666_hud_label_fit_pins_over_retired_surfaces.md` (READY TO IMPLEMENT),
> together with §4C's dormancy pin and §4F's two duplicate Body-hardcoded measurers. Item 3 stands
> as written: nothing in WO-1666 restores the upper-case measurement.

1. **Case 3** — retire, or re-point at the peaceful dock's measured slots? (§4A)
2. **The three collector action-line canon keys** — retire, or keep as a dormant pin? (§4B)
3. **§6's red-first acceptance** — accept that it cannot be met honestly, given §2's proof? If the
   answer is instead "the chips SHOULD draw upper case", that is a **producer** change in
   `HudKitController` (or a persistent `FontStyles.UpperCase` in the skin) and a separate ticket —
   and it would then make `99 waiting` (219.4) and `Builders idle 2` (222.2) real defects needing
   the WO-1144 fewer-characters remedy. **This lane did not pre-judge that.**
