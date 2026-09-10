# WO-1629 - Build Collections: the Manage Placed card's caption is still authored as a fraction of the card, and no capture has ever rendered it

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; bumped 1629 -> 1631 in the SAME edit, with WO-1630)
**Silo / Lane:** Village / BuildMode UI (`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`)
**Severity:** P2 felt-legibility, with a P2 evidence defect attached. The caption carries the longest
copy on the screen (45 characters against the category cards' 22) in the band that was just proven
too short for 22, and it sits on a card the PLAYER gets and the CAPTURE has never built.
**Type:** EXISTING system. The card, the caption and the copy all exist and ship. This is a fit/band
defect plus a hole in the capture set, not a missing feature.
**Raised by:** `WorkOrders/WORK_ORDER_1628_build_collections_category_subtitle_truncates_at_two_of_three_aspects.RESULT.md`
sec.5 item 1, which scoped this card out of WO-1628 deliberately and asked the lead to mint it.

**SEQUENCING - READ BEFORE STARTING:** this ticket edits `Assets/Editor/UICaptureLaunch.cs:8965`.
**WO-1630 edits the same file** in the `:5825-5975` geometry-audit region. The two are NOT
file-disjoint. See sec.7.

---

## 1. What was measured (every line below opened at source 2026-09-10, in the current tree)

### 1a. The caption is still authored as a fraction of the card, and it is the ONLY one left

`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:492-497`:

    var manageSubtitle = Label(manageCard.transform,
        "Move, upgrade or sell anything already built.", 21, TextAlignmentOptions.Top,
        new Vector2(.08f, .05f), new Vector2(.92f, .21f));
    manageSubtitle.color = ElarionUi.Parchment;
    manageSubtitle.raycastTarget = false;
    ElarionUiKit.FitBlock(manageSubtitle, 18f, 21f);

That is character-for-character the pair WO-1628 retired for the seven category cards. Those now
read `new Vector2(.08f, CaptionTopFrac)` / `new Vector2(.92f, CaptionTopFrac)` (`:292-294`) with the
HEIGHT authored in reference px below them: `pivot = (.5f, 1f)` then `offsetMax = Vector2.zero`,
`offsetMin = new Vector2(0f, -CaptionBandPx)` (`:305-308`), against
`private const float CaptionBandPx = 50f;` (`:107`) and
`private const float CaptionTopFrac = .21f;` (`:112`).

So one caption on this screen is authored in px and one is authored as 0.16 of a card height that
itself changes with the aspect.

### 1b. The copy is 45 characters - twice the string WO-1628 proved the band could not seat

Counted this session from the literal at `:493`: `Move, upgrade or sell anything already built.`
is **45** characters. The category caption WO-1628 measured is `nothing affordable yet` - **22**.

### 1c. The fitter it goes through, at source

`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3077-3092`, `FitBlock`:

- `:3083` `if (minSize < FontHardFloor) minSize = FontHardFloor;` - the `18f` passed at
  `BuildCollectionBrowser.cs:497` is clamped UP to 20. The call site's own number is already dead.
- `:3085` `t.textWrappingMode = TextWrappingModes.Normal;`
- `:3086` `t.overflowMode = TextOverflowModes.Truncate;` - a hard cut, no ellipsis.
- `:3087-3089` `enableAutoSizing = true`, `fontSizeMin = minSize`, `fontSizeMax = maxSize`.

`FontHardFloor = 20f` (`:3044`). So the shipped label is wrap Normal, overflow Truncate, auto-size in
[20, 21] - one point of shrink room and a silent cut, identical to the category caption's condition.

`ArmFitGuard` (`:3094-3100`) returns at `:3096` when `!Application.isPlaying`, so nothing rescues it
in an edit-mode capture.

### 1d. The card has NEVER been built in a capture, and the reason is one line

`Assets/Editor/UICaptureLaunch.cs:8965`:

    browser.Show(_ => { });

That is the SINGLE-ARGUMENT overload (`BuildCollectionBrowser.cs:148`), which supplies no
`managePlaced` callback. `BuildManagePlacedCard` refuses to build a card it cannot honour and says so
in the log - `BuildCollectionBrowser.cs:426-434`:

    if (_managePlaced == null)
    {
        FlowTrace.Step("BuildCollections",
            "Manage Placed card SKIPPED - Show() was called without a managePlaced callback ...");
        return;
    }

**Therefore its render is UNMEASURED. Every `BuildCollections_*.png` in the repo shows a screen the
player does not get.**

### 1e. The player DOES get the card - so the capture is rendering a different screen

`Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs:345-347`:

    _collectionBrowser.Show(
        entry => OnEntrySelected?.Invoke(entry),
        () => OnManagePlacedRequested?.Invoke());

with the comment at `:343-344` stating the second callback is what makes the card exist at all. The
door is pinned end to end by `Assets/Editor/Regression/PlacedStructureDoorRegression.cs:202-221`
(C4a declares, C4b calls, C4c invokes).

**This is the finding that makes the ticket bigger than one caption:** the live grid holds **EIGHT**
cards, the captured grid holds **SEVEN**, and the cards are children of a `HorizontalLayoutGroup`
(`BuildCollectionBrowser.cs:216`, restated at `:324-326`: the Manage Placed card "is an additional
child of the SAME grid and therefore changes every sibling's width"). So WO-1628's measured
`cardPx` widths, and the `CaptionBandPx = 50f` authored from them, were taken on a grid the player
never sees. See sec.3 and sec.5.3 - re-asserting WO-1628's own acceptance on the eight-card frame is
part of THIS ticket.

### 1f. The pin that is deliberately loose today, and its own instruction to tighten

`Assets/Editor/Regression/BuildCollectionPlayerRegression.cs:205-209`:

    if (!browser.Contains("CaptionBandPx") ||
        !browser.Contains("-CaptionBandPx") ||
        browser.IndexOf("new Vector2(.08f, .05f)", StringComparison.Ordinal) !=
        browser.LastIndexOf("new Vector2(.08f, .05f)", StringComparison.Ordinal))

The comment above it (`:193-204`) states why it counts to one instead of forbidding - the Manage
Placed card legitimately still carries the pair - and ends: *"Exactly ONE occurrence is therefore
correct today; tighten this to zero when that card is re-pointed on its own measured frame."*
This ticket is that frame.

### 1g. The probe that must reach it, and the pin that constrains how

`ReportSubtitleFit` (`BuildCollectionBrowser.cs:929-968`) already computes everything this ticket
needs per label: it forces a canvas update and `LayoutRebuilder.ForceRebuildLayoutImmediate(grid)`,
calls `t.ForceMeshUpdate(true, true)`, and emits band/card/grid rects, `fontSize` with floor and
ceiling, `textInfo.lineCount`, `textInfo.characterCount` against `text.Length`, `isTextTruncated`,
`preferredHeight` and both modes. It is fed from `List<SubtitleFitProbe>` (`:902-908`) built inside
the category loop (`:315-321`), and it is called at `:327` - AFTER `BuildManagePlacedCard(grid)` at
`:324`, which is exactly the ordering this ticket needs.

**The constraint on how the label reaches it:** `PlacedStructureDoorRegression.cs:210` matches the
regex `BuildManagePlacedCard\s*\(\s*grid\s*\)`. **Changing the call to
`BuildManagePlacedCard(grid, probes)` REDS C4b.** See sec.4 Step 1 for the two routes.

---

## 2. What is NOT claimed

- **It is NOT proven that this caption truncates.** That is the whole point: it has never been
  rendered in any capture (sec.1d), so there is no measurement either way. The RESULT that raised
  this ticket says its "45-character copy needs more lines than a 50 px band gives" - that is an
  INFERENCE from the 22-character measurement, not a number anyone has read. `preferredHeightPx` for
  THIS label, at these three aspects, is the number, and Step 1 prints it. No band may be authored
  before it is read (CLAUDE.md sec.11B and sec.12).
- **Not claimed that 50 px is right or wrong for this card.** It was sized from a 22-character
  two-line requirement of 47.6 ref px on a seven-card grid. Both inputs change here.
- **Not claimed that WO-1628's category fix survives the eight-card grid.** It may; the cards get
  narrower, which makes a wrapped caption TALLER, and 50 px carries only 2.4 px of headroom over
  47.6. This is a real risk and sec.5.3 makes re-asserting it an acceptance criterion rather than an
  assumption in either direction.
- **Not claimed the device behaves like the capture.** `ArmFitGuard` returns at
  `ElarionUiKitObsidian.cs:3096` when not playing, so the capture shows the AUTHORED fit with no
  runtime rescue. That is what makes authoring it correctly the fix.
- **Not claimed the copy is wrong.** The string at `:493` is out of scope; do not shorten copy to
  make it fit. That is the inverse of the fix, the same way lowering a kit floor is.

---

## 3. Target - what "fixed" means

At all three captured aspects, on a grid that contains the Manage Placed card, every caption on the
screen - the seven category captions AND the Manage Placed caption - renders every character it was
given, with no cut, no ellipsis, and at no size below the kit's hard floor. The capture set builds
the same eight-card screen the player gets, so the frame is evidence again.

---

## 4. The fix

### Step 1 - INSTRUMENT AND OPEN THE DOOR (both halves; neither works alone)

Two edits, and they must land together: a probe with no card renders nothing, and a card with no
probe produces no number.

**1a. Make the capture build the card.** `Assets/Editor/UICaptureLaunch.cs:8965` currently calls the
single-argument overload. Pass a second, no-op callback - nothing is tapped in an edit-mode capture,
and `BuildCollectionBrowser.Close()` runs before the callback anyway (`:463-464`). This is the
minimum edit; do not restructure `CaptureBuildCollections`.

**1b. Route the Manage Placed caption into the existing probe list.**
`ReportSubtitleFit(grid, subtitleProbes)` (`:327`) already runs after the card is built (`:324`), so
only the label has to reach `subtitleProbes`. **Do NOT add a parameter to
`BuildManagePlacedCard(grid)`** - `PlacedStructureDoorRegression.cs:210` pins that exact call shape
by regex (sec.1g) and would go red. Two routes that do not touch it:

- have `BuildManagePlacedCard` assign the built label to a private field that `RenderCategories`
  reads and pushes as a probe after `:324`; or
- have `BuildManagePlacedCard` push directly onto a private field holding the same
  `List<SubtitleFitProbe>` the loop fills.

Either is acceptable. If the lane finds a third route it prefers, it must state which pins it checked
it against. **If a route genuinely requires re-pointing C4b, that re-point happens in the SAME commit
with its own red proof stated** - it is not a silent widening.

Give the Manage Placed probe a **DISTINCT prefix**. The category prefix is pinned as a source literal
by `BuildAffordabilityWordsRegression.cs:67` (`"collection=" + c.CollectionId + " affordable="`) and
must not be reused or rebuilt. `manage-placed` as the collection token is the obvious shape; the
hand-back must state the exact emitted prefix, because the grep discipline in WO-1628's RESULT sec.1
counts lines and a new line without a stated identity reads as drift.

**The line count changes and the hand-back must say so.** The narrowed grep
`tr -d '\000' < Builds/<capture-log> | grep -a "Flow:Build" | grep -a subtitle` returned **21** (7
cards x 3 passes). With this card it returns **24** (8 x 3). Anything else - 21, 42, 48 - means the
card did not build, or a pre-layout twin came back, and no band may be authored from those numbers.

**Instrumentation stays in the code afterwards** (CLAUDE.md sec.12). Compute interpolated parts into
locals before building any string (CLAUDE.md sec.1: the gate's brace scanner has no
interpolated-string model).

Re-run the capture and read all **24** lines.

### Step 2 - FIX, from the Step 1 numbers, and re-check the seven

Do not choose before reading Step 1's output. The shape of the decision:

- **The Manage Placed caption's band.** Read its `preferredHeightPx` (the two-or-more-line
  requirement TMP computes at `fontSizeMax` while auto-sizing is on) against its `bandPx` height at
  each aspect. Author the band in reference px hanging below its existing top edge, the same shape as
  `CaptionBandPx` / `CaptionTopFrac` (`:107`, `:112`, applied `:292-308`) and WO-1623's
  `FooterLinkBandPx` before them. **If its requirement differs from the category captions', it gets
  its OWN const** - do not stretch one constant over two different strings, and do not shrink the
  copy.
- **Its top edge.** The caption currently spans `.05f`-`.21f`; the card title sits at `.22f`-`.34f`
  (`:482-483`). Growing DOWNWARD from `.21f` keeps every neighbour still. If the measured requirement
  does not fit between `.21f` and the card's bottom, say so with the numbers rather than moving the
  title.
- **The seven category captions on the NEW grid.** Their `bandPx`, `fontSize`, `rendered` and
  `truncated` must be re-read from the same 24-line output. If `CaptionBandPx = 50f` no longer clears
  their `preferredHeightPx` now that the cards are narrower, that is a WO-1628 re-open and it is in
  scope HERE - re-author the constant from the new measurement and say so explicitly in the hand-back
  (it changes what WO-1628's RESULT predicted).

### Step 3 - TIGHTEN THE PIN, SAME COMMIT

`BuildCollectionPlayerRegression.cs:205-209`: replace the count-to-one clause
(`IndexOf == LastIndexOf`) with an absence test - `browser.Contains("new Vector2(.08f, .05f)")` must
be FALSE - and update the comment block at `:193-204`, which currently explains why exactly one
occurrence is correct, to record that the card was re-pointed on its own measured frame and the
allowance is spent. **State the red proof in the comment, in the shape the neighbouring pins use:
restoring the retired pair at either caption reds it.**

---

## 5. Acceptance

1. **The frames.** A fresh capture is run and all three of
   `Builds/ui-capture/BuildCollections_1920x1080.png`, `_2340x1080.png`, `_2670x1200.png` are OPENED
   and reported. **Each must now show EIGHT cards.** Paste what the Manage Placed caption reads at
   each aspect, and what each of the seven category captions reads - not "looks fine".
2. **The trace.** The narrowed grep returns exactly **24** lines. On every one of the 24,
   `characterCount` equals `sourceLen` and `truncated=False`. Paste the Manage Placed card's three
   lines in full, with its `bandPx`, `preferredHeightPx` and `fontSize`.
3. **WO-1628's acceptance is RE-ASSERTED on the eight-card grid, not assumed.** Its own criteria were
   `characterCount == sourceLen`, `truncated=False` and `fontSize=21` on all 21 category lines. Report
   them from the new run. If `fontSize` reads 20 anywhere, the band is short somewhere and it is not
   done, whichever caption it is.
4. **No neighbour moved.** The Manage Placed title (`:482-490`), its divider (`:478-480`) and its
   artwork (`:468-476`) are unchanged on screen. If the caption band grew, say what it grew INTO and
   show it in the PNG.
5. **The pin is tightened** (Step 3) and the retired fraction occurs **zero** times in the file.
6. **The door pins are still green.** `PlacedStructureDoorRegression` C4a/C4b/C4c
   (`:202-221`) and `BuildAffordabilityWordsRegression.cs:64-67`. State whether C4b was re-pointed
   and why.
7. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 6. Pins - what must not move

- **`PlacedStructureDoorRegression.cs:202-221`** - the ruling-25 door, end to end. Prefer a route that
  leaves `BuildManagePlacedCard(grid)` spelled exactly as pinned (sec.4 Step 1b).
- **`BuildAffordabilityWordsRegression.cs:64-67`** - the WO-1411 trace literal and the two
  `StructureCardVM` call shapes. `StructureCardVM.cs` is READ-ONLY in this lane; the words are the
  owner's colourblind-law state signal.
- **`ElarionUiKitObsidian.cs` and `ElarionUiKit.cs` are READ-ONLY.** `FontFloor` (`:3033`),
  `FontHardFloor` (`:3044`), `FitBlock` (`:3077-3092`), `ArmFitGuard` (`:3094-3100`), `MinTouchPx` -
  cite them, change nothing. Lowering a kit constant so one caption fits is the inverse of this fix.
- **The copy at `:493`.** Do not shorten, re-word or abbreviate it to buy room.
- **`manageTitle.fontSizeMin = 15f` (`:488`)** - a sub-floor literal, SEEN and OUT OF SCOPE. WO-1626's
  sec.6 already records it. Do not widen into it.
- **The `.08f` / `.92f` horizontal fractions** unless Step 1 proves width is implicated. On the seven
  category cards it was not: the two aspects that lost a word measured MORE band width.
- **WO-1623's footer geometry** - `FooterLinkBottomInsetPx`, `FooterLinkBandPx`, the `x .28-.72`
  fractions. Cite the idiom, re-tune none of it.
- **The other `FitBlock` callers** - `HarvestOverflowModal.cs`, `LoadingOverlay.cs`. Not investigated,
  not claimed defective, not in this ticket.

---

## 7. What NOT to touch

- **WO-1630's region of `UICaptureLaunch.cs`.** That ticket adds a glyph-survival finding kind and
  edits the geometry-audit block at roughly `:5825-5975`. **This ticket touches ONE line of that
  file, `:8965`, and nothing else in it.** The two are not file-disjoint (CLAUDE.md sec.9): the lead
  sequences them or gives both to one lane. Whichever lands second states which commit it rebased
  onto and re-confirms its own line numbers after the rebase.
- **No new oracle assert here.** A rule that catches a truncated caption automatically is WO-1630's
  whole subject, and it needs a red-first synthetic proof this ticket is not set up to give.
- No other file in `Assets/_Modules/Village/BuildMode/`.
- No kit file, no `card-collections.json`, no panel router.
- Do **not** commit. Do **not** push. Do **not** run a Unity gate unless the lead says the tree is
  yours. Hand the diff back (CLAUDE.md sec.11).

---

## 8. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1629_manage_placed_card_caption_is_still_a_fraction_and_has_never_been_rendered_in_any_capture.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.
