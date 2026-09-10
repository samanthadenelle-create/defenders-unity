# WO-1629 - Build Collections: the Manage Placed card's caption is still authored as a fraction of the card, and no capture has ever rendered it

**Status:** BLOCKED - Step 2 needs a layout ruling; measured requirement exceeds the .21f ceiling at every aspect (lane PLACED-CARD 2026-09-10)
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

---

## INSTRUMENTED 2026-09-10 (lane PLACED-CARD — STEP 1 ONLY, EDIT-ONLY, no Unity/gate/commit)

Step 1 is in the tree. **No band was authored, no anchor moved, no copy changed** — sec.4 forbids it
before the numbers are read. Base: `5c5419513` (the WO-1629/1630 mint commit, newest on `dev`).

### 1. What changed, file:line (post-edit numbers, all opened this session)

**`Assets/Editor/UICaptureLaunch.cs`** — three edits, all inside `CaptureBuildCollections`
(`:8946`), nothing else in the file touched (sec.7 / WO-1630 owns `:5825-5975`, untouched):

- `:8952` — `GameObject placedStub = null;` declared beside the existing `host` / `canvas`.
- `:8966-8987` — the note, the stub, and the call. `browser.Show(_ => { });` is now
  `browser.Show(_ => { }, () => { })` (`:8987`), matching the live door at
  `Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs:345-347`.
- `:9004-9005` — the stub is destroyed in the existing `finally`.

**`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`** — three edits, instrumentation only:

- `:129-139` — new private field `_subtitleProbes`, with the reason it exists instead of a second
  parameter.
- `:336-349` — `_subtitleProbes = subtitleProbes;` before `BuildManagePlacedCard(grid)` (`:341`,
  spelling **unchanged**), cleared to `null` at `:349` immediately after `ReportSubtitleFit` (`:348`).
- `:520-547` — inside `BuildManagePlacedCard`, after the existing `FitBlock` (`:519`), the caption is
  pushed onto that list. Nothing above `:519` moved: the caption is still authored at `:514-518` with
  the retired `new Vector2(.08f, .05f)` / `new Vector2(.92f, .21f)` pair, exactly as sec.1a describes.

### 2. ⚠ CORRECTION TO THIS TICKET: sec.1d NAMES ONE GATE. THERE ARE TWO.

`BuildManagePlacedCard` refuses in **two** places, and sec.1d (and the pin comment it quotes at
`Assets/Editor/Regression/BuildCollectionPlayerRegression.cs:193-204`, which states the skip reason as
the missing callback alone) records only the first:

- `:450` `if (_managePlaced == null)` — the callback gate sec.1d quotes.
- `:470-471` `int selectable = FindObjectsByType<PlacedStructure>(...).Length;` then
  `if (selectable <= 0)` — **a card that closes the browser onto a map with nothing selectable is a
  dead end**, so the builder refuses there too.

`CaptureBuildCollections` builds a bare `~UICapBuildCollections` GameObject and places nothing itself,
and `UICaptureLaunch.cs` opens no scene of its own —
`grep -n "OpenScene\|NewScene\|LoadScene" Assets/Editor/UICaptureLaunch.cs` returned **no match** this
session. **NOT PROVEN either way** is whether batchmode's open scene happens to hold a live
`PlacedStructure`; the second gate's `SKIPPED` line has never appeared in any log, because the callback
gate always fired first. What IS certain is that the second-callback edit alone would have left that
count to chance on a screen the whole ticket depends on — and if it is zero, Step 1 silently produces
a SEVEN-card grid and 21 grep lines, which sec.4 reads as "the card did not build".

The minimum honest fixture is therefore one counted-only marker: `~UICapPlacedStub` with a bare
`PlacedStructure` component (`UICaptureLaunch.cs:8985-8986`), destroyed in the `finally`. It is never
rendered, carries no catalog row and no art — it exists solely to make the second gate's count 1.
`PlacedStructure` carries **no** `[ExecuteAlways]` / `[ExecuteInEditMode]`
(`grep -n "ExecuteAlways\|ExecuteInEditMode" Assets/_Modules/Village/BuildMode/PlacedStructure.cs`
returned no match, this session), so its `Start()` — and `StorageStackView.Attach` with it
(`PlacedStructure.cs:26-31`) — does not run in this edit-mode capture.

**Step 3 must fix the pin comment too.** `BuildCollectionPlayerRegression.cs:193-204` will be rewritten
anyway; its stated skip reason is incomplete and should name both gates.

### 3. The grep the lead runs, and the ONLY number that licenses Step 2

    tr -d '\000' < Builds/<capture-log> | grep -a "Flow:Build" | grep -a subtitle | wc -l

**Expected: exactly 24** = 8 cards x 3 aspects (1920x1080, 2340x1080, 2670x1200). The seven-card frame
returned 21.

Exactly **two** emitted strings in the tree contain `subtitle='`, verified by
`grep -n "subtitle='" Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs` this session:
`:330` (the seven category probes, prefix unchanged) and `:540` (the new one). Both are emitted from the
single `ReportSubtitleFit` pass (`:348`), so a pre-layout twin cannot appear.

**The Manage Placed line's exact emitted prefix** (`:540-541`):

    collection=manage-placed card=ManagePlacedCard subtitle='Move, upgrade or sell anything already built.'

It deliberately does **not** rebuild `"collection=" + c.CollectionId + " affordable="`, the source
literal pinned by `Assets/Editor/Regression/BuildAffordabilityWordsRegression.cs:67`. Each of the 24
lines then carries the same fields `ReportSubtitleFit` already emits: `bandPx`, `cardPx`, `gridPx`,
`fontSize` (with floor and ceiling), `rendered` (lines + chars), `sourceLen`, `truncated`,
`preferredHeightPx`, `modes`. That method's body was NOT edited by this lane.

**Any count other than 24 = STOP, author nothing.**
- **21** — the card did not build. Read the `SKIPPED` reason string to see WHICH gate fired: the
  callback gate emits `... without a managePlaced callback ...` (`:452-454`), the body gate emits
  `... zero live PlacedStructure bodies ...` (`:473-476`).
- **42 / 48** — a pre-layout twin came back; the numbers are unresolved rects, not measurements.
- **Anything between 22 and 27 that is not 24** — most likely the `Guard.Try("Build", "WO-1628 subtitle
  fit measurement", ...)` wrapper emitted a `Fail`, whose own label contains the word `subtitle`, so it
  is counted by the grep: a probe threw and the loop aborted part-way. Read that line first.
- My verifying grep was the narrower `subtitle='`; the lead's is the bare word `subtitle`. The 21
  baseline (7 x 3) is what proves no other emitted string carried the bare word before this lane, and
  this lane added exactly one emitted string.

**⚠ ONE THING THE LEAD SHOULD EXPECT AND NOT RCA AGAINST THIS LANE.** The capture's geometry audit
(`UICaptureLaunch.cs:5825-5975`, WO-1630's region — untouched here) runs `LayoutOracle` over the
captured frame, and this is the first frame in which the eighth card exists at all. Its finding kinds
are `ButtonsOverlap`, `ButtonOverText` and `SubTouchFloorBand`
(`Assets/_Modules/Core/UI/LayoutOracle.cs:56-64`, read this session). The eighth card is an interactive
rect the audit has never seen, and it makes every sibling narrower, so a **NEW** finding on this frame
is possible. Any such finding is **pre-existing geometry, not this lane's edit** — nothing was moved.
Note also that the oracle has **no font-floor finding kind**, so the sub-floor
`manageTitle.fontSizeMin = 15f` (WO-1626 sec.6, out of scope) cannot surface through it.

### 4. What the 24 numbers must SHOW to license Step 2

**The Manage Placed card (3 lines) — the number the whole ticket waits on** is
`preferredHeightPx` against its `bandPx` HEIGHT at each aspect. `preferredHeight` is what TMP needs at
`fontSizeMax` with wrapping on; the band is what it was given. `sourceLen` must read **45**.
- `preferredHeightPx` > `bandPx` height at ANY aspect, or `truncated=True`, or
  `rendered chars < sourceLen`, or `fontSize` reading below **21** → the fraction is short and Step 2
  authors its **OWN** reference-px const hanging below the existing `.21f` top edge (sec.4 Step 2).
  Do not stretch `CaptionBandPx` over two different strings; do not shorten the copy (sec.6).
- All three clear it with room → the fraction happens to survive; Step 2 still re-points the anchor to
  px so the pin can be tightened to zero (Step 3), and says so with the numbers.

**The seven category cards (21 lines) — WHETHER WO-1628's 50 px STILL HOLDS ON THE NARROWER CARDS.**
This is not assumed in either direction: the eight-card grid gives every sibling **less width**, and a
narrower band makes a wrapped caption **taller**. `CaptionBandPx = 50f`
(`BuildCollectionBrowser.cs:107`) was authored against a measured two-line requirement of **47.6** px —
**2.4 px of headroom** — on the seven-card frame.
- **50 px HOLDS** only if all 21 lines read `fontSize=21`, `truncated=False`,
  `rendered chars == sourceLen`, **and** `preferredHeightPx <= 50`.
- **Any** line reading `fontSize=20` (the auto-size floor, clamped up from the call site's dead `18f`
  by `ElarionUiKitObsidian.cs:3083`), or `preferredHeightPx` above 50, means the narrower card pushed a
  caption onto a third line. **WO-1628 re-opens inside Step 2** and `CaptionBandPx` is re-authored from
  the new measurement — sec.5.3 makes that in scope here, and it contradicts what WO-1628's RESULT
  predicted, so the hand-back must say so explicitly.
- Also compare `cardPx` against WO-1628's recorded widths: it quantifies how much narrower the
  eight-card grid made every card, which is the input both bands were sized from.

### 5. Pins re-asserted as source text (this session, post-edit)

- `PlacedStructureDoorRegression.cs:210` C4b — `BuildManagePlacedCard\s*\(\s*grid\s*\)` still matches
  (1 occurrence, `:341`). **C4b was NOT re-pointed.** C4a (`private void BuildManagePlacedCard`, `:449`)
  and C4c (`_managePlaced?.Invoke`) both still match.
- `BuildCollectionPlayerRegression.cs:205-209` — `new Vector2(.08f, .05f)` occurs **exactly once**
  (`:514`), which is what that pin requires *today*; `CaptionBandPx` and `-CaptionBandPx` both present.
  Tightening it to zero is **Step 3**, and cannot happen before the caption is re-pointed.
- `BuildAffordabilityWordsRegression.cs:67` — the WO-1411 literal is present and untouched.
  `StructureCardVM.cs` was not opened for edit.
- Read-only per sec.6 and NOT touched: `ElarionUiKitObsidian.cs`, `ElarionUiKit.cs`, the copy at `:514`,
  `manageTitle.fontSizeMin = 15f`, the `.08f`/`.92f` fractions, WO-1623's footer constants.
- A regression-wide grep for
  `ReportSubtitleFit|SubtitleFitProbe|subtitleProbes|isTextTruncated|Show(_ =>|CaptureBuildCollections`
  found **no pin** on the probe shape or on the capture's single-arg call — only a prose comment at
  `BuildCollectionPlayerRegression.cs:183`. So neither edit can red a suite by shape.

### 6. Quality gate

`python tools/gate_brace.py Assets/Editor/UICaptureLaunch.cs Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`
→ `GATE_BRACE_SUMMARY bad=0 of 2`. NUL scan of both files → 0 bytes. No Unity run, no compile gate, no
commit (sec.7). Instrumentation stays in the code permanently (CLAUDE.md sec.12).

### 7. Next

**BLOCKED on the lead running the capture.** Step 2 and Step 3 — and the `.RESULT.md`, deferred by lead
instruction because Step 1 alone does not close the ticket — begin once the 24 lines exist and are read.
WO-1630 edits `UICaptureLaunch.cs:5825-5975` and is sequenced AFTER this lane; it must state which commit
it rebased onto and re-confirm its own line numbers, since this lane added ~24 lines at `:8952-9005`,
below its region.

---

## STEP 2 BLOCKED 2026-09-10 — the measured requirement does not fit under `.21f` at ANY aspect

**Step 2 was NOT authored and Step 3 was NOT applied.** Step 1's numbers came back and they do not
license the fix this ticket describes. sec.4 Step 2 anticipated this exact outcome and says what to do:
*"If the measured requirement does not fit between `.21f` and the card's bottom, say so with the numbers
rather than moving the title."* This section is that report.

### 1. The three Manage Placed lines, verbatim from `Builds/wave2-capture5` (read this session)

    [Flow:Build] collection=manage-placed card=ManagePlacedCard subtitle='Move, upgrade or sell anything already built.' bandPx=140.8x52.2 cardPx=167.6x326.2 gridPx=1454.7x342.2 fontSize=20 floor 20 ceiling 21 rendered=2 lines, 32 chars sourceLen=45 truncated=True preferredHeightPx=95.9 modes=Truncate / Normal
    [Flow:Build] collection=manage-placed card=ManagePlacedCard subtitle='Move, upgrade or sell anything already built.' bandPx=156.7x43.2 cardPx=186.6x270.1 gridPx=1606.5x286.1 fontSize=20 floor 20 ceiling 21 rendered=1 lines, 16 chars sourceLen=45 truncated=True preferredHeightPx=71.8 modes=Truncate / Normal
    [Flow:Build] collection=manage-placed card=ManagePlacedCard subtitle='Move, upgrade or sell anything already built.' bandPx=159x42.1 cardPx=189.3x263 gridPx=1628.1x279 fontSize=20 floor 20 ceiling 21 rendered=1 lines, 18 chars sourceLen=45 truncated=True preferredHeightPx=71.8 modes=Truncate / Normal

The caption is **cut at all three aspects** — 32 / 16 / 18 of 45 characters — and the fitter has already
been driven **onto the kit floor** (`fontSize=20`, `floor 20 ceiling 21`), so it has no shrink room left
to spend. sec.2's "not proven that this caption truncates" is now closed: it truncates, everywhere.

### 2. THE BLOCKER — the requirement exceeds the space the `.21f` top edge leaves, at every aspect

The band can only hang **downward from `.21f`**, so its absolute ceiling is `.21 x cardHeight`. That
ceiling already INCLUDES the `.05f` empty margin below today's caption — the margin is inside the number,
not additional to it.

| aspect | measured need (preferredHeightPx @ fontSize 20) | max band below `.21f` = `.21 x cardH` | short by AT LEAST |
|---|---|---|---|
| 1920x1080 | **95.9** | `.21 x 326.2` = **68.5** | **27.4 px** |
| 2340x1080 | **71.8** | `.21 x 270.1` = **56.7** | **15.1 px** |
| 2670x1200 | **71.8** | `.21 x 263.0` = **55.2** | **16.6 px** |

**Those deficits are a LOWER BOUND, and the acceptance criterion makes them worse.** `preferredHeight`
was computed at fontSize **20** — the floor the fitter was already driven to. The pass criterion is
`fontSize=21`, and larger glyphs at the same width need at least as much height, never less. Scaling by
21/20 with the line count held (an ESTIMATE, not a measurement — the probe never ran at 21) puts the need
near **100.7 / 75.4 / 75.4**, i.e. short by roughly **32 / 19 / 20 px**. At 1920x1080 the line count may
also go 4 -> 5, since that aspect has the NARROWEST band (140.8) and already needs one more line than the
other two.

**No band constant authored below `.21f` can clear this.** Authoring one to the maximum the geometry
allows would still ship a truncated caption while claiming a fix, which is the failure CLAUDE.md sec.11B
names. So nothing was authored.

### 3. Every lever this ticket leaves open, and what forbids each

- **(a) Move the title up** (`:530-538`, `.22f`-`.34f`). The only free vertical space on the card. sec.4
  Step 2 explicitly says to REPORT rather than move it. **Needs a ruling.**
- **(b) Shorten the copy** (`:514-516`). Forbidden by name, sec.6: *"Do not shorten, re-word or
  abbreviate it to buy room."*
- **(c) Lower the font floor.** Forbidden by name, sec.6 — `ElarionUiKit` / `ElarionUiKitObsidian` are
  READ-ONLY and *"lowering a kit constant so one caption fits is the inverse of this fix."* The label is
  already ON the floor regardless.
- **(d) Shrink the artwork** (`:504-512`, `.38f`-`.91f`) or the divider (`.35f`-`.355f`) to give the lower
  half of the card more room. Blocked by acceptance criterion 4, *"No neighbour moved."* **Needs a ruling.**
- **(e) Widen the caption's x fractions** (`.08f`/`.92f`). sec.6 permits this *"unless Step 1 proves width
  is implicated"* — and here it **is** implicated: the narrowest band (140.8) needs one more line than
  156.7 / 159 do. But widening alone is **ESTIMATED insufficient**, and this is arithmetic, not a
  measurement: the category cards render 2 lines at 47.6 px at fontSize 21, giving ~23.8 px per line, so
  three lines cost ~71.4 px — already past the 68.5 px ceiling at 1920x1080 **even at full card width with
  zero side margins**, which is not an acceptable layout anyway. Only a re-measure can settle it.

### 4. What this needs

**A layout ruling on which neighbour yields.** The deficit is roughly **15-30 px of vertical real estate**
on a card whose lower `.21` is fully spoken for — not a rounding error a constant can absorb. The caption
needs about three lines at fontSize 21 (~71-76 px) against the 55.2-68.5 px the card gives below `.21f`.

### 5. Step 3 was deliberately NOT applied

`BuildCollectionPlayerRegression.cs:205-209` counts the retired `new Vector2(.08f, .05f)` pair to **one**
*because* the Manage Placed caption legitimately still carries it. On this tree it still does (`:514`,
verified this session — exactly one occurrence). Tightening the clause to zero now would **red that pin on
the very next gate**. The tightening belongs in the same change as the re-point, exactly as the pin's own
comment says, and the re-point is what is blocked.

### 6. WO-1628's acceptance IS re-asserted on the eight-card grid — it HOLDS

Read from the same log, all **24** lines present (8 cards x 3 aspects, the count sec.4 requires). All 21
category lines read `fontSize=21 floor 20 ceiling 21`, `rendered=2 lines, 22 chars`, `sourceLen=22`,
`truncated=False`, `bandPx` height **50**, `preferredHeightPx=47.6` — on card widths **167.6 / 186.6 /
189.3**. So `CaptionBandPx = 50f` still clears its 47.6 px requirement on the narrower eight-card grid,
with the same 2.4 px of headroom. **sec.5.3 is satisfied and WO-1628 does NOT re-open.** The risk sec.2
flagged did not materialise.
