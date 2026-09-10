# WO-1621 RESULT — the caption FITS in landscape. Closed on a device frame, no code changed.

**Status:** FIXED (no code edit required — closed on evidence per the ticket's own OWNER RULING item 3)
**Lane:** TITLE-CAPTION (isolated worktree), 2026-09-10
**Tree:** dev HEAD `f33451b11a28428c201961ef43b130c6027cc776`
**Files changed by this lane:** the WO's `**Status:**` line and this RESULT. **ZERO `.cs` files.**

---

## 1. The measurement — and it is a device frame, not an inference

The ticket's OWNER RULING (2026-09-10) reduced this ticket to one question:

> *"THE ONE QUESTION THIS TICKET IS NOW REDUCED TO: does `PLAY INTRO` fit in **LANDSCAPE** on the
> device? Nothing answers it — `Builds/device-frames/` holds no landscape frame."*

**That last clause is no longer true, and it is the whole finding.** A landscape Seeker title frame
exists on disk and postdates the ruling's own evidence sweep:

| | the ruling's frame (sec.1a) | **the frame that answers it** |
|---|---|---|
| path | `Builds/device-frames/2026-09-10_0028_title_363195.png` | **`Builds/device-frames/2026-09-10_0558_raid_title_363529.png`** |
| IHDR (decoded this session, `struct.unpack('>II', bytes[16:24])`) | **1200 x 2670 — PORTRAIT** | **2670 x 1200 — LANDSCAPE** |
| build | `2026.09.10.363195` | `2026.09.10.363529` |
| mtime | 2026-09-10 00:27 | **2026-09-10 05:58** |
| faces in the row | three (`CONTINUE / START NEW / PLAY INT...`) | **three (`CONTINUE / START NEW / PLAY INTRO`)** |

**Device + build proven from the logcat beside it**, `Builds/device-frames/2026-09-10_raid_logcat_preraid.txt`,
line read this session:

```
09-10 05:57:03.115 I/Unity (26759): ApplicationInfo 'com.denellestudios.echoesofelarion',
                                    Version '2026.09.10.363529', Min API Level '26', Target API Level '36'
09-10 05:57:08.891 V/LevelPlaySDK: ... deviceOEM=Solana Mobile Inc., deviceModel=Seeker, ...
```

Session starts 05:57:03; the frame is stamped 05:58. It is **the Seeker**, not an emulator.

### The image itself

Cropped from that frame and opened this session (the action row is anchored `y 0.045..0.135`,
`x 0.20..0.80` — `TitleController.cs:281-282`):

```
python -c "from PIL import Image; im=Image.open('Builds/device-frames/2026-09-10_0558_raid_title_363529.png'); \
im.crop((480,1014,2189,1164)).save('title_row.png')"
```

The row reads, left to right, in three equal slots:

```
CONTINUE   |   START NEW   |   PLAY INTRO
```

**`PLAY INTRO` is drawn in full. No ellipsis. Both `O` glyphs complete, with visible padding between
the final `O` and the slot's gilt frame edge** (verified again on a 2x zoom of the third face alone,
crop `(1602,1000)-(2216,1175)`). That is the ticket's symptom, absent, at the shipping orientation,
on the owner's own device.

### The frame speaks for HEAD, not only for build 363529

The three files that produce this row have **not been touched since the frame was taken**:

```
git log --oneline --since="2026-09-09 00:00" -- Assets/_Modules/Onboarding/TitleController.cs \
    Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs Assets/_Modules/Core/UI/MedievalUiSkin.cs
  -> (no output)
git log -1 --date=short -- Assets/_Modules/Onboarding/TitleController.cs
  -> 6979fb961 2026-09-04
git log -1 --date=short -- Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs
  -> 3f49e93d5 2026-09-04
```

Row build (`:300-307`), skin (`MedievalUiSkin.ApplyButton`) and fit (`FitSingleLine`, `:325`) are all
at their 2026-09-04 state in both the build and the tree. **The frame is a measurement of HEAD's code.**

### One honest qualification on the ruling's wording

The ruling asked for a landscape frame *"taken after the WO-1631 settings flip lands"*. ⛔ **Whether
build 363529 carried that orientation flip is NOT PROVEN from here** — this lane did not diff
`ProjectSettings/ProjectSettings.asset`, did not establish that the flip postdates 363529, and memory
`build-scripts-overwrite-projectsettings-rulings` records that `AndroidBuild.cs` sets PlayerSettings
at build time, so that asset's on-disk state would not settle it anyway. **It does not matter for this
measurement:** the canvas resolves 2670x1200 and the row's slot arithmetic
(`slotW = (1 - 0.035*2)/3 = 0.31` of a 0.60-wide row, `TitleController.cs:305-307`) is a function of
the surface, not of whether rotation is locked. **The measured box is the box the game will ship in.**
A post-flip frame would be a nicer artifact; it would not be a different number.

## 2. What is NOT proven — stated as unproven, per CLAUDE.md §11B

- ⛔ **The final `fontSize` of any of the three captions. NOT MEASURED.** No trace exists; sec.4
  Step 1 was made conditional by the ruling on the caption *not* fitting, and it fits.
- ⛔ **`isTextOverflowing` on any caption. NOT MEASURED** — inferred-absent from the pixels only.
- ⛔ **Whether `FitSingleLine` (`ElarionUiKitObsidian.cs:3054`) or the post-layout
  `UiKitTextFitGuard` (`:3094 ArmFitGuard`) settled the label. NOT ESTABLISHED**, exactly as sec.1d
  item 2 warned. The ticket's own sec.1d items 1 and 3 likewise remain unmeasured and are NOT
  asserted anywhere in this RESULT.
- The proof standing in their place is the pixel evidence, which is the standard the owner set for
  this exact class: *"I want images to verify anything that is a viewable issue"* (memory
  `screenshots-are-primary-evidence-for-visual-defects`).

**The only variable between the two frames is the aspect.** Both are the three-face layout (a save
existed on both runs), same strings, same skin, same fit call. So the 00:28 cut was real and was
**a cut in an orientation the game is not allowed to reach** (WO-1631, owner ruling 2026-09-10).

## 3. Coverage finding — handed BACK, deliberately not fixed by this lane

The lane was asked whether the Title caption is covered by the 09-10 glyph oracle. **It is measured
and never reported.** Read at source 2026-09-10:

- `Assets/Editor/UICaptureLaunch.cs:2304-2330` `CaptureTitleOnce` builds the real `TitleController`
  by reflection and renders through `RenderCanvasToPng`, which runs the oracle at `:5775`
  (`AuditGeometry(canvasGo, Path.GetFileNameWithoutExtension(path), w, h)`). So a panel build named
  `Title_1920x1080 / Title_2340x1080 / Title_2670x1200` **is** audited — and all three targets are
  LANDSCAPE (`LandscapeTargets`, `:215-220`; `2670x1200` is commented *"THE SEEKER'S REAL SURFACE"*).
- **Its ONLY caller is `RunFrontDoorCaptureHeadless` (`:2215-2223`), and that method calls neither
  `ResetGlyphOracle()` nor `ReportGlyphOracle()`** — compare the GooglePlay login path
  (`RunGooglePlayLoginCaptureHeadless`), which calls both, at **`:2243`** and **`:2274`**
  (line numbers re-read with `grep -n "ResetGlyphOracle()\|ReportGlyphOracle()"` this session, after
  a first draft of this RESULT cited them one and fifteen lines wrong — the repo's own copied-cite
  failure class, caught before hand-back). Findings therefore accumulate into the statics at `:6272-6280` and **no
  `UI_GLYPH_OK` / `UI_GLYPH_FAIL` marker is ever emitted from the front-door run.**
- Consequence for anyone reading the baseline: **`Title_*` is absent from the 4 surviving
  `GlyphBaseline` entries (`:6160-6172`) because it was NEVER MEASURED IN A REPORTING RUN — not
  because it measured clean.** Corroborated: `tr -d '\000' < Builds/wave3-capture10 | grep -ac 'Title_'`
  returns **0** (that is the very run the 68→4 shrink was proven on, per the header at `:6137-6140`),
  and `Builds/wave4-capture3` likewise has no `Title_` panel line.

**Not fixed here, and that is a decision, not an omission.** Wiring `ResetGlyphOracle` /
`ReportGlyphOracle` into `RunFrontDoorCaptureHeadless` is the durable pin this ticket actually wants,
but the lane cannot run Unity, and turning the marker on for a path that has never emitted it may red
on Login/Title labels nobody has ever measured — which would land in the lead's gate as an unexplained
`UI_GLYPH_FAIL`. **Editing it blind is a guess** (CLAUDE.md §11B). Handed to the lead as a follow-up
ticket: *"front-door capture builds Title + Login through the glyph oracle and reports no marker"*,
with the four line cites above.

## 4. Why no suite case was added (sec.6)

Sec.6's case was specified **RED-first on the portrait aspect**, and the OWNER RULING item 1 dissolves
that: the portrait `Aspects` row stays out, so the specified RED cannot be produced. A landscape case
would be **green at HEAD**, would have to be authored against the suite's existing character-width
heuristic (`HudLabelFitRegression.cs:581`, `LineHeightFactor` `:174`) — and the lane cannot execute it
to see whether that heuristic agrees with the device. **A heuristic that reds against a frame showing
the caption drawn in full is a false alarm, not coverage.** Sec.6 blocker (b) is untouched and stands:
`DeNelle.Onboarding` is still absent from `Assets/Editor/Regression/DeNelle.EditorRegression.asmdef`,
so a live-mount Title case remains a lead-ruled edit. **Nothing was registered in `DataRegression.cs`.**

## 5. Owner ruling framed for the lead — NOT taken by this lane

None needed. **No shorter word is required**, so sec.5's caption-strings pin is not in play: `Continue`,
`Start New`, `Play Intro` all render in full at the shipping aspect. For the record, the budget that was
in question: the row spans `x 0.20..0.80` (`:281-282`) = 60% of 2670 = **1602 px**, split
`slotW = (1 - 0.035*2)/3 = 0.31` → **~497 px per face**, and `PLAY INTRO` (10 chars, uppercased at
`MedievalUiSkin.cs:86`, bolded at `:88`) fits it with margin. In portrait the same 0.31 slot is 60% of
1200 = 720 px of row → **~223 px per face**, which is where it ran out. *(The two slot widths are
arithmetic from the anchors read at source; they are not a claim about the resolved rect, which was
never printed — see §2.)*

## 6. Gate evidence

**No `.cs` file was touched by this lane**, so `python tools/gate_brace.py` and the NUL scan have no
inputs. Reported explicitly rather than reported as "passed": running them over an unchanged tree
would prove something other than this lane's work (CLAUDE.md §11B — measuring something is not the
same as measuring the right thing). Nothing to gate, nothing to commit but two markdown files.

## 7. Pins — all intact

`entries` order/conditionality (`:300-303`), the three caption strings, `OnPlayIntro` (`:402-420`),
`ApplyButton`'s uppercase + bold, `FontFloor`/`FontHardFloor`, `MinTouchPx`, the `whiteLabel` branch
(`:328-333`), the wallet chip's truncation — **none touched.** The forbidden `24f -> 20f` drop was not
made. No `HudActionBarModel` / dock / `HudActionBarRegression` file was opened for edit.

## 8. Residual for the owner (this is a felt close, per §13 — PO closes, not CLI)

The one thing left is the owner's eyes on a **post-WO-1631** landscape title screen. If the next
Seeker frame after the orientation flip shows the row as above, this is closed for good. If the flip
changes the safe-area insets enough to narrow the row, the ticket's sec.4 Step 1 instrumentation at
`TitleController.cs:325` is still the correct opening move — measured at landscape, never at 1200x2670.
