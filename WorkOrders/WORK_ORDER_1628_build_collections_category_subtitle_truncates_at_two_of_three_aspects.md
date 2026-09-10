# WO-1628 - Build Collections: every category card's affordability caption renders as "nothing affordable y" at two of the three capture aspects

**Status:** FIXED 2026-09-10 - gated (Builds/wave2-compile6, Builds/wave2-reg6 493/493) and captured (Builds/wave2-capture4: UI_CAPTURE_OK 91; all 21 probe lines bandPx h=50, fontSize=21, rendered=2 lines, 22 chars, truncated=False at every aspect); the 2670x1200 PNG shows "nothing affordable / yet" on two lines clear of the bezel, sent to the owner; owner felt-test closes. (was: IMPLEMENTED - awaiting gate + capture (lane SUBTITLE 2026-09-10))
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; bumped 1628 -> 1629 in the SAME edit)
**Silo / Lane:** Village / BuildMode UI (`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`)
**Severity:** P2 felt-legibility. Seven cards, seven truncated captions, on the FIRST build screen a
new player sees. Not dormant - it is in the frame that was captured tonight.
**Type:** EXISTING system. The caption exists, is on-screen, carries the right source text, and the
1920x1080 aspect renders it correctly. This is a fit/band defect, not a missing feature.
**Owner words:** none on this specific caption. The standing rule that makes it a ticket:
**"I want images to verify anything that is a viewable issue"** (owner, 2026-09-09) - the frame is
attached below and the defect is visible in it without any interpretation.

**SEQUENCING - READ BEFORE STARTING:** WO-1626 is open in a worktree and is editing
`BuildManageDefensesFooterLink()` (`:299-316`) in THIS SAME FILE. **This ticket lands AFTER WO-1626
and rebases onto it.** See sec.7.

---

## 1. What was measured (read at source 2026-09-10)

### 1a. The frame - the defect, as a picture

`Builds/ui-capture/BuildCollections_2670x1200.png`, written 2026-09-10 01:34 (its save line is
`Builds/wave2-capture:3073`; the 1920 save is at `:2949`, the 2340 save at `:3011`).

All seven category cards - Gathering, Realm, Towers, Crafting, Storage, Walls & Gates, Trade - print
their sub-caption as:

    nothing affordable y

The word `yet` is cut after its first letter. There is no ellipsis glyph and no visible reason on
screen: the caption stops mid-word with card width to spare on both sides.

### 1b. The three aspects do NOT agree - and that is the finding

Opened all three PNGs from the same 01:34 run:

| PNG | Renders |
|---|---|
| `BuildCollections_1920x1080.png` | **CORRECT** - wraps to two lines: `nothing` / `affordable yet` |
| `BuildCollections_2340x1080.png` | **TRUNCATED** - one line, `nothing affordable y` |
| `BuildCollections_2670x1200.png` | **TRUNCATED** - one line, `nothing affordable y` |

So two lines IS the intended render, the 16:9 aspect achieves it, and the two wider aspects lose it.
Whatever the fix is, it must not be judged on 1920 alone - that aspect was already green.

### 1c. The source text is CORRECT. The loss is at RENDER, and that is proven, not inferred

`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:241-242` already prints the string it
handed the label. On the same fresh capture log, `Builds/wave2-capture:2935-2944` (1920 pass) and
`:3000-3006` (2340 pass), every one of the seven reads:

    [Flow:Build] collection=build-gathering affordable=0 subtitle='nothing affordable yet'

Full word, every card, every aspect. **The VM, the words and the count are not suspects.** Do not
open the affordability path.

### 1d. Where the words are authored

`Assets/_Modules/Village/BuildMode/StructureCardVM.cs:416-420`:

- `:410-415` the WO-1411 note: the state is the WORDS, not a colour (colourblind law).
- `:418` `if (affordable <= 0) return "nothing affordable yet";`

The string is PINNED by `Assets/Editor/Regression/BuildAffordabilityWordsRegression.cs:59-60`, which
fails if `StructureCardVM` stops owning the exact literal `nothing affordable yet`. **The copy is not
available as a fix.** See sec.2.

### 1e. Where the label is built and fitted

`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`, inside `RenderPage`:

- `:235` `int affordable = StructureCardVM.AffordableCount(c);`
- `:236-237` the label, authored at size `21`, `TextAlignmentOptions.Top`, band
  `new Vector2(.08f, .05f)` to `new Vector2(.92f, .21f)` - **a FRACTION of the card's height**
  (0.16 of it), which is the exact authoring pattern WO-1623 was raised against.
- `:238` `subtitle.color = ElarionUi.Parchment;`
- `:239` `subtitle.raycastTarget = false;`
- `:240` `ElarionUiKit.FitBlock(subtitle, 18f, 21f);`

The local `Label` helper at `:822-831` sets `enableWordWrapping = true`, `enableAutoSizing = true`,
`fontSizeMin = Mathf.Min(20f, size)`, `fontSizeMax = size`, and
`overflowMode = TextOverflowModes.Overflow` - but **`FitBlock` at `:240` runs afterwards and
overrides all of it**, so the helper's `Overflow` never reaches the render.

### 1f. The fitter, at source - what the label actually ends up as

`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3077-3092`, `FitBlock`:

- `:3079-3081` `maxSize` defaults to the label's `fontSize`; `minSize` defaults to `FontFloor`.
- `:3083` `if (minSize < FontHardFloor) minSize = FontHardFloor;` - **the `18f` passed at
  `BuildCollectionBrowser.cs:240` is clamped UP to 20.** The call site's own number is dead. (This is
  the same sub-floor-literal class as WO-1626, but here the kit catches it, so it is a note, not the
  defect.)
- `:3085` `t.textWrappingMode = TextWrappingModes.Normal;`
- `:3086` `t.overflowMode = TextOverflowModes.Truncate;`
- `:3087-3089` `enableAutoSizing = true`, `fontSizeMin = 20`, `fontSizeMax = 21`.

Constants: `FontFloor = 30f` (`:3033`), `FontHardFloor = 20f` (`:3044`).

**So the shipped label is: wrap Normal, overflow Truncate, auto-size in the range [20, 21].** A one
point-size of shrink room, and a hard cut with no ellipsis.

### 1g. Why it clips instead of ellipsising - answered, and it is by design

`FitBlock` chooses `Truncate` (`:3086`). Its single-line sibling `FitSingleLine` chooses
`Ellipsis` (`:3065`). The kit's own doc comments say why: a BLOCK of copy that runs out of room is
cut so it "never paints past its rect onto siblings" (`:3073-3076`), while a single-line control
label gets an ellipsis. This label went through the block path, so there is no ellipsis to draw.

**Note for the lane: switching to `Ellipsis` is NOT the fix.** It would render `nothing affordabl...`
instead of `nothing affordable y` - the word `yet` is still lost, and an ellipsis on a two-word status
line reads as an error state. The room is the problem, not the terminator.

### 1h. The kit's post-layout rescue did NOT run in this frame, and cannot have

`ArmFitGuard` (`ElarionUiKitObsidian.cs:3094-3100`) returns immediately at `:3096` when
`!Application.isPlaying`. The capture that produced these PNGs is `RunCaptureHeadless`
(`Assets/Editor/UICaptureLaunch.cs:532`), documented at `:525-531` as a synchronous EDIT-MODE render
that deliberately does NOT enter Play mode. Consistent with that, a scan of `Builds/wave2-capture`
returns **zero** lines containing `TextFitGuard`.

**Therefore: what these PNGs show is the AUTHORED fit with no runtime rescue.** Whether the guard's
band-growth path (`:3170-3185`) would rescue this caption in a play-mode or on-device run is
**NOT PROVEN either way** and must not be assumed in the fix. Sec.4 Step 1 settles it.

And even where the guard does run, it would not have flagged this: its only assert is
`Blank(_t)` - **zero** visible glyphs (`:3218-3226`, helper at `:3229`). A caption that renders 20 of
its 22 characters passes that assert cleanly.

### 1i. Why the capture gate stayed green on a frame with seven truncated captions

The geometry oracle passed on this exact run - `Builds/wave2-capture:3079` reports 91 canvases clean,
and the capture marker on the same log reads 91. That is correct behaviour, because **no rule in
either oracle file looks at whether a TMP label's glyphs survived.**

`Assets/_Modules/Core/UI/LayoutOracle.cs` declares exactly three finding kinds (`:56-64`):

- `ButtonsOverlap` (`:132`) - two interactive rects intersect.
- `ButtonOverText` (`:153`) - a visible button covers foreign text. This one READS `t.text`, but only
  to quote it into the message; the test is rect intersection.
- `SubTouchFloorBand` (`:195`) - an authored band under `ElarionUiKit.MinTouchPx`.

The fourth rule lives in the harness, not in the oracle:
`Assets/Editor/UICaptureLaunch.cs:5851-5872`, `RULE 1 [text-off-plate]` - it measures whether the
text's **RECT** escapes its `ZoneBacking` plate (`OutsideBy`, `:5866`). A rect that sits neatly inside
its plate while its own contents are cut away is invisible to it.

**The gap, stated exactly:** every existing assert measures WHERE a rect is. Nothing measures whether
the text inside it still says what it was given. `subtitle.text` and the rendered glyphs disagreed on
seven cards at two aspects and every gate was green. Sec.5.4 scopes what to do about that, and sec.7
says what NOT to do about it in this ticket.

---

## 2. What is NOT claimed

- **The mid-word cut is NOT explained.** With `textWrappingMode = Normal`, a word that does not fit
  the line width moves to the next line whole - which is what 1920 does. A one-line render that ends
  after the `y` of `yet` is not accounted for by wrap-then-vertical-truncate, and the mechanism
  **cannot be proven from source reading**. This ticket therefore instruments before it edits
  (CLAUDE.md sec.12); Step 1 in sec.4 is a measurement, not a fix.
- **No band height is published here.** The resolved px height of the `.05-.21` band at each aspect is
  DERIVABLE from the card rect but was not MEASURED, and `LayoutOracle.cs:177-185` (the WO-1623 note)
  is explicit that authoring a fix off a derived number is the guess CLAUDE.md sec.11B forbids. Step 1
  prints the real number.
- **Not claimed that the device behaves like the capture.** Sec.1h. Edit-mode capture has no
  `LateUpdate`, so the guard's rescue is untested here. A lane that "fixes" this on the capture alone
  and never proves the play-mode path has proven half of it.
- **Not claimed the words are wrong.** They are pinned (sec.1d) and they are the owner's colourblind-law
  state string. The copy is out of scope.
- **Not claimed that the other `FitBlock` callers are broken.** `HarvestOverflowModal.cs:233` and
  `:316`, and `LoadingOverlay.cs:232`, use the same helper. They were SEEN and are NOT investigated,
  NOT claimed defective, and NOT in this ticket.
- **Not claimed to be the same bug as WO-1621.** `WorkOrders/WORK_ORDER_1621_title_play_intro_caption_truncates_on_the_seeker.md:1`
  is the same FAMILY (a caption losing characters to an exhausted fit) but a different screen, a
  different helper path and a device rather than a capture. Cross-referenced, not merged.

---

## 3. Target - what "fixed" means

At all three captured aspects, every category card's sub-caption reads the complete sentence the VM
handed it - `nothing affordable yet`, or `N you can build now` - with no character lost, no ellipsis,
and at a size no smaller than the kit's hard floor. Two lines is an acceptable and expected render;
the 1920 frame is the reference for what right looks like.

---

## 4. The fix

### Step 1 - INSTRUMENT (do this first; do not edit layout yet)

Extend the existing trace at `BuildCollectionBrowser.cs:241-242`. It already prints the source string;
make it print what the label BECAME. After the label is fitted, force a mesh update and add to the
same line:

- `subtitle.rectTransform.rect.width` and `.height` (the resolved band, the number sec.2 refuses to
  derive),
- `subtitle.fontSize`, `fontSizeMin`, `fontSizeMax`,
- `subtitle.textInfo.lineCount`,
- `subtitle.textInfo.characterCount` against `subtitle.text.Length`,
- `subtitle.isTextTruncated`,
- `subtitle.overflowMode` and `subtitle.textWrappingMode`.

Keep it on the existing `FlowTrace.Step("Build", ...)` call - one line per card, not a new tag, and
per CLAUDE.md sec.12 the instrumentation STAYS in the code afterwards.

Compute the interpolated parts into locals before building the string. The gate's brace scanner has no
interpolated-string model (CLAUDE.md sec.1), and this is a nested-quote-shaped line.

Re-run the capture and read the three passes. That log names the mechanism, the band height and the
delta - and it is the evidence the fix in Step 2 is authored from.

### Step 2 - FIX, from the Step 1 numbers

Do not choose between these before reading Step 1's output. They are listed so the lane knows the
shape of the decision, not so it can pick one now:

- **If the band seats only one line at the two wide aspects:** the band is the defect. It is authored
  as a FRACTION of card height (`.05f`-`.21f`, `:236-237`), which is precisely the pattern WO-1623
  retired for the footer in this same file when a fraction resolved to 52-60 reference px
  (`Builds/wave2-capture:2946`). Author the caption band in reference px, sized from the measured
  two-line requirement, the same way `FooterLinkBandPx` now is.
- **If the label is rendering as one line despite `TextWrappingModes.Normal`:** that is the mechanism
  sec.2 says is unexplained, and the Step 1 line will name it (`lineCount`, `isTextTruncated`,
  `textWrappingMode` as it stands at render). Fix the cause the line names.
- **If the card's own height is what changed between aspects:** fix the grid, not the label - but say
  so explicitly in the hand-back, because that widens the blast radius to every card on the screen and
  the lead needs to know before gating.

**Whatever is chosen: the fix is judged on the PNGs at all three aspects, not on the log.**

---

## 5. Acceptance

1. **The frames.** A fresh capture is run and all three of
   `Builds/ui-capture/BuildCollections_1920x1080.png`, `_2340x1080.png`, `_2670x1200.png` are OPENED
   and reported. Every one of the seven cards shows the complete caption at every aspect. Paste what
   each caption reads, per aspect - not "looks fine".
2. **The trace.** The Step 1 line appears in the fresh capture log for all seven cards at all three
   aspects, and `characterCount` equals `text.Length` on every one of the twenty-one.
3. **The 1920 aspect did not regress.** It was already correct; a fix that repairs the wide aspects by
   breaking the narrow one is not a fix. Report its captions explicitly.
4. **The pinned copy is untouched.** `BuildAffordabilityWordsRegression` still passes and
   `StructureCardVM.cs:418` still returns the exact literal.
5. **The neighbours did not move.** The card title (`:219-226`), the divider (`:216-218`) and the
   artwork are unchanged on screen. If the caption band grew, say what it grew INTO and show it.
6. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 6. Pins - what must not move

- **`StructureCardVM.AffordabilityWords` (`:416-420`) and its regression
  (`BuildAffordabilityWordsRegression.cs:59-60`).** The words are the colourblind-law state signal.
  Read-only in this lane.
- **`ElarionUiKitObsidian.cs` and `ElarionUiKit.cs` are READ-ONLY.** `FontFloor`, `FontHardFloor`,
  `FitBlock`, `FitSingleLine`, `ArmFitGuard`, `MinTouchPx` - cite them, change nothing. Lowering a kit
  constant so one caption fits is the inverse of this fix, and it is the trap WO-1626 names in its own
  sec.6.
- **WO-1623's footer geometry** - `FooterLinkBottomInsetPx`, `FooterLinkBandPx`, the `x .28-.72`
  fractions, and the grid-bottom calculation traced at `Builds/wave2-capture:2946` and `:2992`. The
  footer band is not this ticket. Do not re-tune a px of it.
- **The `.08f`/`.92f` horizontal fractions of the caption** unless Step 1 proves width is implicated.
  Sec.1a records visible slack on both sides, so width is not the presumed cause.
- **The card title's own fit** (`:219-226`: NoWrap, autosize from 20, `Overflow`). Seen, left alone.
- **Two other sub-floor writers in this file, SEEN and OUT OF SCOPE:** `:409-410`
  (`manageTitle.fontSizeMin = 15f`) and `:826-829` (`t.fontSizeMin = Mathf.Min(20f, size)`). WO-1626's
  sec.6 already records both. Do not widen into them.

---

## 7. What NOT to touch

- **WO-1626's lines.** That lane is editing `BuildManageDefensesFooterLink()` (`:299-316`) in a
  worktree, in THIS FILE. **This ticket must land second.** Rebase onto WO-1626's diff before
  starting, confirm the line numbers in sec.1e still resolve to the same statements after the rebase,
  and say in the hand-back which commit was rebased onto. Two lanes editing one file concurrently is
  the serialization hazard CLAUDE.md sec.9 names; the lead sequences, the lanes do not overlap.
- **No new oracle assert in this ticket.** A glyph-survival rule (`isTextTruncated`, or
  `characterCount < text.Length` on a visible TMP) is the right long-term answer to sec.1i, but
  `LayoutOracle.cs:17-20` requires any new rule be SEEN RED first, via a synthetic authored-defect
  canvas in `UiTouchClampRegression`, before it is trusted green. That is its own ticket with its own
  red-first proof. **Scoped out deliberately, not forgotten** - raise it in the hand-back so the lead
  can mint it.
- No other file in `Assets/_Modules/Village/BuildMode/`.
- No kit file, no `card-collections.json`, no panel router, no capture harness.
- Do **not** change the caption's TEXT.
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 8. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1628_build_collections_category_subtitle_truncates_at_two_of_three_aspects.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.

---

## INSTRUMENTED 2026-09-10

Lane SUBTITLE. **Step 1 ONLY. No layout edit was made and none is licensed yet** - sec.4 forbids it
until the capture below is read. Rebased onto `b3f6c9187` (WO-1626's `FitSingleLine(label,` at
`BuildCollectionBrowser.cs:333` is present in the base, and sec.1e's statements re-resolved after the
rebase: the caption is still authored at `:245-247`, coloured `:248`, `raycastTarget` `:249`,
`FitBlock(subtitle, 18f, 21f)` `:250`).

### What was added (one file: `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`)

| file:line | what it is |
|---|---|
| `BuildCollectionBrowser.cs:198-207` | note + `var subtitleProbes = new List<SubtitleFitProbe>();` declared before the card loop |
| `BuildCollectionBrowser.cs:251-262` | the loop no longer emits the trace; it pushes a `SubtitleFitProbe` (prefix, label, card rect) instead |
| `BuildCollectionBrowser.cs:265-268` | `ReportSubtitleFit(grid, subtitleProbes);` (`:268`) called AFTER `BuildManagePlacedCard(grid)` (`:265`), before the footer link |
| `BuildCollectionBrowser.cs:839-848` | `private sealed class SubtitleFitProbe` - doc `:839-842`, body `:843-848` (prefix / label / card) |
| `BuildCollectionBrowser.cs:850-869` | the doc block: why a separate pass, why edit-mode has no fit guard, units = canvas reference px |
| `BuildCollectionBrowser.cs:870-907` | `ReportSubtitleFit` - `Guard.Try("Build", ...)`, `Canvas.ForceUpdateCanvases()`, `LayoutRebuilder.ForceRebuildLayoutImmediate(grid)` (`:876`), `t.ForceMeshUpdate(true, true)` (`:886`), one `FlowTrace.Step("Build", ...)` per card (`:898`) |

### The tag text

The emitted line keeps the **exact prefix the pre-WO-1628 trace emitted**, then appends the
measurement:

```
collection=<id> affordable=<n> subtitle='<text>' bandPx=<w>x<h> cardPx=<w>x<h> gridPx=<w>x<h>
 fontSize=<size> floor <min> ceiling <max> rendered=<lineCount> lines, <characterCount> chars
 sourceLen=<text.Length> truncated=<isTextTruncated> preferredHeightPx=<preferredHeight>
 modes=<overflowMode> / <textWrappingMode>
```

(one physical line per card; wrapped here only for the page). Tag is the existing `Build` system -
no new tag was introduced. Units are canvas **reference px**.

### Why the line MOVED out of the build loop (read this before judging the numbers)

Measuring at the old site would have measured nothing. Every card is a child of the
`HorizontalLayoutGroup` authored at `BuildCollectionBrowser.cs:176`, so each card added re-sizes its
siblings, and while the loop is still filling no layout pass has run at all. The capture harness does
not force one until after the panel is built -
`Assets/Editor/UICaptureLaunch.cs:5703-5710` (`Canvas.ForceUpdateCanvases()` then
`LayoutRebuilder.ForceRebuildLayoutImmediate` on the panel root). The reading of "keep it on the
existing call" taken here is therefore: **same system tag, same prefix, one line per card** - relocated
post-layout. The in-loop emission was DELETED rather than kept, because a pre-layout twin of every
line in the capture log is worse than no measurement.

Three fields are emitted **beyond** sec.4's enumerated bullets, and are named here for the lead:
`cardPx` + `gridPx` (sec.4 Step 2's third branch is "the card's own height changed between aspects" -
that branch cannot be decided without them, and they also make a mis-timed measurement self-evident as
zeros) and `preferredHeightPx` (Step 2's first branch must author a band from the measured two-line
requirement; without it the capture has to be run twice).

### The grep the lead runs on the capture log

```
tr -d '\000' < Builds/<capture-log> | grep -a subtitle
```

⚠ **That bare grep returns 27 lines, not 21, and always did.** Run against
`Builds/wave2-capture2` on 2026-09-10 it returned 21 `[Flow:Build] collection=... subtitle='nothing
affordable yet'` lines (`:2933-2942`, `:2998-3004`, `:3060-3066`) PLUS 6 `[Flow:Journey] deck
card=... subtitle='...'` lines (`:2623`, `:2624`, `:2641`, `:2642`, `:2656`, `:2657`) that match the
bare word and have nothing to do with this screen. **The count that matters is the Build one** - so
either read past the Journey rows, or narrow the grep by piping through `grep -a "Flow:Build"`
first. The narrowed form must return **21** lines - 7 cards x 3 aspect passes - not 42. 42 means the
in-loop emission came back and the numbers are pre-layout.

### What the numbers must show to license Step 2

Read the 21 lines. The three branches sec.4 Step 2 lists are decided as follows, and **exactly one**
must be named in the Step 2 hand-back:

1. **Band is the defect** - at 2340 and 2670 `bandPx` height is less than `preferredHeightPx`, while
   at 1920 it is not, and `rendered` reads `1 lines` at the two wide aspects against `2 lines` at
   1920. Then author the caption band in reference px from the measured `preferredHeightPx`, the way
   `FooterLinkBandPx` is - sec.6's pins stay untouched.
2. **Wrap is the defect** - `bandPx` height is ample at all three aspects yet `rendered` still reads
   `1 lines` at the wide ones. Then `modes` names what the label was actually left in and the fix is
   that cause, not the band.
3. **The card is the defect** - `cardPx` height differs between aspects by more than the band delta.
   Then it is the grid, and sec.4 requires that to be said out loud in the hand-back because it widens
   the blast radius to every card on the screen.

In every branch the discriminator that proves the loss is `characterCount` (inside `rendered`) vs
`sourceLen`: with a block fit, TMP's `characterCount` is the PROCESSED count, so a cut reads
`characterCount < sourceLen` and `truncated=True`. Acceptance sec.5.2 is met only when all 21 lines
read `characterCount == sourceLen`.

**If the 21 lines come back with `bandPx=0x0` or `cardPx=0x0`, the measurement site is wrong and NO
step-2 branch may be chosen from them.** That is the guard the extra rect fields exist for.

### Pins checked this session (each read at source before the edit)

- `Assets/Editor/Regression/BuildCollectionPlayerRegression.cs:116-121` scans this file for
  `TextOverflowModes.Ellipsis` / `TextOverflowModes.Truncate` as **whole-file negative** pins and
  requires `enableWordWrapping=true`, `enableAutoSizing=true`,
  `overflowMode=TextOverflowModes.Overflow`. Verified after the edit: the two forbidden literals occur
  **0** times (the new trace prints `t.overflowMode.ToString()`, never the literal, and the comments
  deliberately avoid spelling either), and the three required literals occur once each.
- `BuildCollectionPlayerRegression.cs:74-79` walks (the `StringLiterals` helper at `:218`) every double-quoted literal and fails on a `[`
  glyph or `NO COST`. The diff introduces **no** `[` in any added line.
- `BuildCollectionPlayerRegression.cs:153-176` - `link.name = "ManageDefensesFooterLink"`,
  `"Already built? Manage defenses >"`, `PanelRouter.Open(PanelId.Manage, "Defense")`,
  `FitSingleLine(label,` present; `"Upgrade Defenses"` and `fontSizeMin = 16f` absent. All verified
  post-edit.
- ⛔ `BuildAffordabilityWordsRegression.cs:67` pins the **exact source literal**
  `"collection=" + c.CollectionId + " affordable="` as the WO-1411 proving trace. Relocating the
  emission would have RED-ed it if the string had been rebuilt; it was not - the probe's `Prefix`
  at `BuildCollectionBrowser.cs:257-258` is that literal character for character, and `:64-65`
  (`StructureCardVM.AffordabilityWords(` / `StructureCardVM.AffordableCount(`) are untouched. All
  three verified GREEN against the edited file this session. `StructureCardVM` itself was **not
  touched**.
- The other three suites that `ReadAllText` this file were checked too, all GREEN post-edit:
  `BuildFirstUseGuideRegression.cs:50` (`BuildFirstUseGuide.CategorySelected();`),
  `CardCollectionFoundationRegression.cs:74-76` (`ResolveAsync(collection.CollectionId`,
  `foreach (var card in _remoteCollection.Cards)`, `result.Add(card.StableId)`), and
  `PlacedStructureDoorRegression.cs:202-217` C4a/C4b/C4c (`BuildManagePlacedCard` declared, called
  as `BuildManagePlacedCard(grid)`, `_managePlaced?.Invoke`) - the new call was inserted AFTER
  `BuildManagePlacedCard(grid)`, never in place of it.
- `ElarionUiKitObsidian.cs` / `ElarionUiKit.cs` - **not touched** (read only: `FitBlock` `:3077-3092`,
  `ArmFitGuard` `:3094-3100`).

### Checks run

- `python tools/gate_brace.py Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs` ->
  `GATE_BRACE_SUMMARY bad=0 of 1`
- NUL scan: `0` NUL bytes in the file (59490 bytes).
- No Unity run, no gate, no commit - EDIT ONLY, per the lane's instruction.

### Raised for the lead (sec.7 asked for this)

The glyph-survival oracle rule sec.1i describes is still unminted. `isTextTruncated` /
`characterCount < text.Length` on a visible TMP is now trivially available - `ReportSubtitleFit`
computes both - but `LayoutOracle.cs:17-20` requires a new rule be SEEN RED first against a synthetic
authored-defect canvas in `UiTouchClampRegression`. That is its own ticket with its own red-first
proof and is deliberately NOT in this diff.


---

## IMPLEMENTED 2026-09-10 - step 2, branch 1

Lane SUBTITLE, rebased onto `446c8b992` (this ticket's own step-1 commit).

The 21 probe lines on `Builds/wave2-capture3` license **sec.4 Step 2's FIRST branch** - the band
seats only one line at the two wide aspects. `bandPx` height reads **52.2** at 1920x1080
(`:2936-2942`, 2 lines, 22 of 22 chars, `truncated=False`), **43.2** at 2340x1080 (`:2998-3004`)
and **42.1** at 2670x1200 (`:3060-3066`) - both one line, 21 of 22 chars, `truncated=True` -
against a `preferredHeightPx` of **47.6 on all twenty-one lines**. That requirement does not move
with `fontSize` because TMP computes it at `fontSizeMax` while auto-sizing is on
(`TMP_Text.cs:3762`), so it is the two-line height at the largest size the fitter may choose.

Branch 3 is ruled out: `bandPx` height is exactly 0.16 of `cardPx` height at every aspect, so the
card and the band are one fact - the card is legitimately shorter on a 965.4-ref-px canvas.
Branch 2 is ruled out: `modes=Truncate / Normal` on all 21, wrapping was never disabled.

**The fix:** the caption's band HEIGHT is authored in reference px (`CaptionBandPx = 50f`,
`BuildCollectionBrowser.cs:107`) hanging below its own existing top edge
(`CaptionTopFrac = .21f`, `:112`) - both y anchors collapsed, pivot set before the offsets, same
shape as WO-1623's `FooterLinkBandPx`. X stays a fraction (width was never implicated: the two
failing aspects had MORE band width). No font floor, no kit file and no neighbour was touched.
Pinned RED-first by `BuildCollectionPlayerRegression.cs:178-208`.

Full record, including what the next capture must show and the two items raised for minting:
`WorkOrders/WORK_ORDER_1628_build_collections_category_subtitle_truncates_at_two_of_three_aspects.RESULT.md`
