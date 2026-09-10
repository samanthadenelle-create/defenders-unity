# WO-1621 - Title screen: the PLAY INTRO caption ellipsises to "PLAY INT..." on the Seeker

**Status:** READY TO IMPLEMENT - **instrument first** (CLAUDE.md sec.12: no code edit until a
captured line names the final fontSize and the overflow flag for all three captions on the portrait
device aspect)
**Minted:** 2026-09-10 (CLI, main-line banner; bumped 1621 -> 1625 in the SAME edit)
**Silo / Lane:** UI / Onboarding Title screen + ElarionUiKit text fit
**Severity:** P2 felt - the FIRST screen of the game, on the owner's own device, renders a
truncated word. It is the third occurrence of one already-RCA'd failure class (sec.1c).
**Type:** EXISTING system. The fit already runs; it is exhausted and ellipsises by design.
**Owner words:** none on this specific caption. The standing rule that makes this ticket a ticket:
**"I want images to verify anything that is a viewable issue"** (owner, 2026-09-09) - so the frame
below IS the evidence, not a supporting illustration (memory
`screenshots-are-primary-evidence-for-visual-defects`).

---

## 1. The defect, source-proven and image-proven (every line opened 2026-09-10)

### 1a. The image

`Builds/device-frames/2026-09-10_0028_title_363195.png` - 1200x2670 **PORTRAIT**, 3026494 bytes,
mtime 2026-09-10 00:27, production build `2026.09.10.363195`. Opened and read this session.

The bottom action row carries three faces, left to right:

```
CONTINUE   |   START NEW   |   PLAY INT...
```

The third caption is **ellipsised**. The first two are not. All three render at visibly the same
glyph size, in the same all-caps bold face, in three equal-width slots.

*(The `Wallet CHKK...sfkC` chip at top-right is ALSO ellipsised and is NOT this defect - a
truncated wallet address is the intended presentation. Do not "fix" it here.)*

### 1b. Where the row is built

`Assets/_Modules/Onboarding/TitleController.cs`:

- `:300-303` builds the entry list. `Continue` is conditional on `HasExistingSave()` (`:300-301`);
  `Start New` (`:302`) and `Play Intro` (`:303`) are unconditional. The frame shows three faces, so
  a save existed on the owner's device - **the truncating layout is the THREE-face layout.**
  The owner-spec comment sits at `:295-296`.
- `:305-307` distributes them evenly:
  `const float slotGap = 0.035f;` / `float slotW = (1f - slotGap * (entries.Count - 1)) / entries.Count;`
  With `entries.Count == 3` that is `slotW = (1 - 0.07) / 3 = 0.31` of the row width - **identical
  for all three**, so the caption that truncates is simply the LONGEST STRING in a box sized for
  the average one. `Play Intro` is 10 characters; `Start New` is 9; `Continue` is 8.
- `:316` `MedievalUiSkin.ApplyButton(btn, primary: ...)`, which at
  `Assets/_Modules/Core/UI/MedievalUiSkin.cs:86` does
  `label.text = (label.text ?? string.Empty).ToUpperInvariant();` and at `:88`
  `label.fontStyle |= FontStyles.Bold;`. **Both widen the glyph run, and both happen BEFORE the
  fit call.** The caption under test is therefore `PLAY INTRO` in bold, not `Play Intro`.
- `:325` is the fit: `if (buttonLabel != null) ElarionUiKit.FitSingleLine(buttonLabel, 24f, 34f);`

### 1c. The fit is not missing - it is EXHAUSTED, and this class is already RCA'd twice

`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3054-3070` (`FitSingleLine`):

```
t.textWrappingMode = TextWrappingModes.NoWrap;
t.overflowMode     = TextOverflowModes.Ellipsis;
t.enableAutoSizing = true;
t.fontSizeMin      = minSize;      // 24f from the Title call site
t.fontSizeMax      = maxSize;      // 34f
```

with `public const float FontHardFloor = 20f;` at `:3044` and `FontFloor = 30f` at `:3033`. The
call site's `24f` is ABOVE the hard floor, so the kit's clamp-up at `:3062` does not fire: the label
auto-shrinks to 24 px and then, having nowhere left to go, **ellipsises. That is the method doing
exactly what it is documented to do.**

This is the SAME class the bar already burned two tickets on, and both RCAs are written into the
code - read them before proposing anything:

- `Assets/_Modules/Core/HudModel/HudActionBarModel.cs:245-258` (Manage face, 2026-08-22, headed
  fleet, `break_24_error.png`): *"FitSingleLine did the only thing left to it and ellipsised. No
  layout number was wrong; the string was four times its box."*
- `Assets/_Modules/Core/HudModel/HudActionBarModel.cs:338-352` (Raids face, owner Seeker felt-test
  2026-08-26): same sentence, same conclusion.

**The fix both of those shipped was a SECOND LINE on the face**, not a smaller font -
`HudActionBarModel.cs:350-351`: *"the numerals move to a SECOND LINE on the face (the face is ~110
ref px tall - two lines cost nothing)"*. That precedent is the strongest candidate here and it is
already proven in this codebase.

### 1d. UNPROVEN, and it must stay unproven until instrumented

Three things are consistent with the frame and are **not measured**:

1. **That the label actually bottomed out at the 24f floor.** All three captions rendering the same
   size is consistent with all three hitting 24f, and it is also consistent with all three sitting
   at some larger common size with only the third overflowing. Do not write "it floored at 24" in
   any RESULT until a line prints `fontSize`.
2. **That the ellipsis came from `FitSingleLine` and not from `UiKitTextFitGuard`.** The kit arms a
   post-layout guard (`ElarionUiKitObsidian.cs:3094` `ArmFitGuard`, runtime only) which can relax a
   label further. Which of the two produced the visible state is not established.
3. **Character-count-per-box arithmetic.** The bar RCAs quote "roughly ten characters" at the 30 px
   floor for a ~144 ref px rect; the Title row's slot is a different width in a different aspect.
   Do not carry that number across - measure this one.

## 2. Target - what "fixed" means

Every caption on the Title screen's action row is fully legible on the owner's 1200x2670 portrait
device, at or above the kit's legibility floor, with no ellipsis - and the row still reads as three
equal, tappable faces.

## 3. Architecture ruling

`docs/ARCHITECTURE_PRINCIPLES.md` - presentation is a separate layer; one owner per concern.
CLAUDE.md sec.12 - instrument first.

- **The fit rule belongs to the KIT, not to TitleController.** Whatever shape wins in sec.4, it is
  authored in `ElarionUiKitObsidian.cs` beside `FitSingleLine` / `FitBlock` and CALLED from
  `TitleController.cs:325`. A bespoke measure-and-shrink loop inline in the Title screen is the
  wrong answer even if it looks smaller.
- **A hardcoded narrower font is FORBIDDEN.** Lowering the `24f` toward `FontHardFloor` (20f) is
  the one fix explicitly ruled out by this ticket's brief, and memory
  `mobile-ui-touch-contrast-standard` is the reason: sub-legible phone text is a defect, not a fix.
  If the data says 24f is genuinely unreachable in that box, the string or the layout moves - not
  the floor.
- **There is NO group/uniform-fit helper in the kit today.** Grepped
  `Assets/_Modules/Core/UI` 2026-09-10 for `FitGroup` / `UniformFit` / `FitSiblings` /
  `MatchFontSize`: **zero hits**; the only public fit entry points are `FitSingleLine` (`:3054`)
  and `FitBlock` (`:3077`). So "make the three captions share one computed size" means AUTHORING a
  new kit helper. That is legitimate - it is a kit concern - but it is new surface, so say so and
  do not describe it as "using the existing helper".
- **Any number that survives this ticket becomes a named const beside the kit's `FontFloor` /
  `FontHardFloor`,** with today's value as its default and a comment naming this WO. A magic
  literal surviving a ticket about a magic literal is the ticket failing (CLAUDE.md sec.2 / sec.5 /
  sec.8 / sec.16, same rule in four voices).

## 4. Lane split - TWO steps, and step 1 is not optional

**Step 1 - INSTRUMENT (no behaviour change; may ship alone).**
At `TitleController.cs:325`, after the fit, emit **one `FlowTrace.Step` line per caption** naming at
minimum: the entry index, `entries.Count`, the caption text AS FITTED (post-`ToUpperInvariant`), the
resolved label rect width in reference px, the final `fontSize`, `fontSizeMin`/`fontSizeMax`, and
`isTextOverflowing`. An overflowing caption logs `FlowTrace.Warn`, not `Step` - "I could not render
what I was asked to" is an anomaly (CLAUDE.md sec.12 step 2).

Then **CAPTURE IT AT THE PORTRAIT ASPECT AND READ IT.** The numbers for all three captions go in the
RESULT before any layout edit. Static code-reading LOCATES; it never CONCLUDES.

**Step 2 - DECIDE FROM THE DATA, then fix.** The data will name which axis is wrong:

- **The string is too long for the box** (the bar's proven case) -> the precedent fix is a
  **two-line caption** through the kit (`PLAY` / `INTRO`), following
  `HudActionBarModel.cs:349-350`. Check the face's height budget from the measured rect first.
- **The three captions disagree about size** -> a kit-level uniform group fit: measure all three,
  adopt the smallest satisfying size for all, so the row reads as one control set.
- **The slot is too narrow only in portrait** -> an aspect-aware `slotGap` / `slotW`, authored as a
  named const, not a magic 0.035f.

Do not pick from the armchair. The instrumented numbers pick it.

## 5. Pins - what must not move

- **`entries` ORDER and CONDITIONALITY** (`:300-303`). Continue-only-when-a-save-exists is owner
  spec, stated in the comment at `:298-299`. Do not make Continue unconditional to get an even row.
- **The caption STRINGS.** `"Continue"`, `"Start New"`, `"Play Intro"` are player-facing copy. If
  the fix needs a shorter word, that is an **owner ruling**, not a lane decision - raise it, do not
  take it (CLAUDE.md sec.7; memory `owner-statements-are-ground-truth`).
- **`OnPlayIntro`'s behaviour** (`:402-420`) - opting INTO the full tutorial plus clearing the
  persisted hero. This is a caption ticket, not a routing ticket.
- **`MedievalUiSkin.ApplyButton`'s uppercase (`:86`) and bold (`:88`).** They contribute to the
  width and they are the screen's authored look. Do not remove either to buy pixels.
- **`ElarionUiKit.FontHardFloor` (20f) and `FontFloor` (30f)** - kit-wide floors, load-bearing for
  every other screen.
- **`ElarionUiKit.MinTouchPx` (112f, `ElarionUiKit.cs:347`)** - the faces must still clear the touch
  floor after any height change (see WO-1623, the same floor on a different screen).
- **The `whiteLabel` branch (`:328-333`)** - owner F8: *"make the start new text white as well"*.

## 6. RED-first suite spec

Extend **`Assets/Editor/Regression/HudLabelFitRegression.cs`** (markers `HUD_LABEL_FIT_OK` /
`HUD_LABEL_FIT_FAIL`) - it is the existing label-fit oracle family and already owns exactly this
question for the bar. Do NOT mint a second label-fit suite.

- **`CaseTitleActionRowCaptionsFit`** - for the three-entry row, at each aspect in
  `Aspects` (`:177-181`), the longest caption's glyph run fits its computed slot at or above the
  kit floor.
  **RED at HEAD:** `PLAY INTRO` is predicted not to fit the 0.31 slot at the portrait aspect - which
  is what the device frame shows. **That is a PREDICTION until the case runs; the case must confirm
  it, and it must reuse the suite's EXISTING character-width heuristic** (the one `Case3_ManageFace`
  at `HudLabelFitRegression.cs:581` already uses for the bar's "roughly ten characters" arithmetic,
  alongside the declared `LineHeightFactor` at `:174`). **Do not invent a second width model** - a
  new heuristic that reds is not evidence, it is a coincidence.
- **`CaseTitleFitGoesThroughTheKit`** - `TitleController.cs` calls a kit fit entry point and
  contains no inline font-size arithmetic.
  **RED at HEAD if** step 2 lands a bespoke loop in the Title screen instead of a kit helper.

**Two blockers the lane must handle, both read at source 2026-09-10 - do not discover them late:**

> **(a) `Aspects` HAS NO PORTRAIT ENTRY.** `HudLabelFitRegression.cs:177-181` declares exactly two:
> `2670x1200 (the capture)` and `1920x1080`. **Both are LANDSCAPE.** The defect frame is
> **1200x2670 PORTRAIT**. A case added to this suite as-is **cannot red on the reported defect.**
> Adding a portrait aspect is the first edit of the suite work, and it may turn other cases red -
> report that, do not suppress it.
>
> **(b) THE SUITE CANNOT INSTANTIATE THE TITLE SCREEN.**
> `Assets/Editor/Regression/DeNelle.EditorRegression.asmdef` `references` (read 2026-09-10) lists
> `DeNelle.Core`, `DeNelle.HUD`, `DeNelle.Commerce`, `DeNelle.Cosmetics`, `DeNelle.Village`,
> `DeNelle.Village.PiAds`, `DeNelle.Pets`, `DeNelle.Data`, `DeNelle.BattleATB`, `DeNelle.Dungeons`,
> `DeNelle.Wallet` and the Unity assemblies. **`DeNelle.Onboarding` is NOT among them**, so a
> regression cannot call `TitleController`. The case is therefore **part arithmetic, part source
> lint** - the exact shape `HudLabelFitRegression` already documents for itself at `:30`
> (*"THE BOX IS PART MEASURED, PART SOURCE LINT - and Case 0 says which is which"*). Adding the
> asmdef reference to enable a live-mount case is a **lead-ruled edit, not this lane's** - raise it,
> do not take it.

**Name the mutation in the RESULT** (CLAUDE.md sec.12): revert `:325` to an unbounded fit, or
lengthen a caption by one character, and say which case reds. And CLAUDE.md sec.8 is right that a
source lint is not coverage: **a fresh device or capture frame showing the full caption is the real
proof** (memory `screenshots-are-primary-evidence-for-visual-defects`).

## 7. Not in scope

- Do **not** change the wallet chip's truncation (`Wallet CHKK...sfkC`) - intended presentation.
- Do **not** lower `24f` toward `FontHardFloor`, and do **not** move `FontFloor` or `FontHardFloor`.
- Do **not** rename or reword any of the three captions without an owner ruling (sec.5).
- Do **not** touch `HudActionBarModel`, the peaceful dock, `HudDockSlotLayout` or
  `HudActionBarRegression` - CLAUDE.md sec.7 is explicit that the shipped bar is the adaptive dock
  and that no face count may be reasoned about from that model. This ticket is the TITLE screen and
  shares only the failure class.
- Do **not** add `DeNelle.Onboarding` to `DeNelle.EditorRegression.asmdef` - hand the request back
  (sec.6b).
- Do **not** register a new suite in `DataRegression.cs` - lead-owned. This work extends an existing
  registered suite, so there should be nothing to register; if that changes, hand back the line.
- Do **not** run Unity, gate, or commit from the lane. Edit-only; the lead holds the Unity lock and
  is the sole committer.
