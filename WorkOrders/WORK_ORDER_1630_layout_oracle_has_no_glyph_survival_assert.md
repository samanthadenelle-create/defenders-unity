# WO-1630 - The capture oracle measures WHERE every rect is and never whether the glyphs inside it survived

**Status:** FIXED 2026-09-10 - fourth LayoutOracle finding kind (visible glyphs vs printable source) with its own UI_GLYPH marker from all 14 report sites, RED-first synthetic case, per-finding shrink-only GlyphBaseline seeded from two measured runs (68); first live run found 68 cut labels over 21 panels (WO-1636); green on wave3-capture9 / navcapture5 (was: IMPLEMENTED - awaiting gate + capture (lane GLYPH-ORACLE 2026-09-10))
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; bumped 1629 -> 1631 in the SAME edit, with WO-1629)
**Silo / Lane:** Core UI oracle + capture harness
(`Assets/_Modules/Core/UI/LayoutOracle.cs`, `Assets/Editor/UICaptureLaunch.cs`,
`Assets/Editor/Regression/UiTouchClampRegression.cs`)
**Severity:** P2 gate defect - a blindness, not a broken screen. It is why seven truncated captions
at two of three aspects passed a clean geometry run on 2026-09-10, and why the only detector left for
that class was a human opening a PNG.
**Type:** EXISTING system, widened. The oracle exists, has three rules and a proving suite; this adds
a fourth rule in the same shape.
**Raised by:** `WorkOrders/WORK_ORDER_1628_..._RESULT.md` sec.5 item 2, and WO-1628 sec.1i / sec.7,
which scoped it out deliberately and asked the lead to mint it.

**SEQUENCING - READ BEFORE STARTING:** WO-1629 edits ONE line of `Assets/Editor/UICaptureLaunch.cs`
(`:8965`). This ticket edits the geometry-audit region of the same file (roughly `:5825-5975`). The
two are NOT file-disjoint. See sec.7.

---

## 1. What was measured (every line below opened at source 2026-09-10)

### 1a. The oracle declares exactly three finding kinds

`Assets/_Modules/Core/UI/LayoutOracle.cs:56-64`:

    public enum FindingKind
    {
        ButtonsOverlap,      // Assert B - two interactive rects intersect
        ButtonOverText,      // Assert B (occlusion half) - a visible button covers foreign text
        SubTouchFloorBand,   // Assert A - an authored band under ElarionUiKit.MinTouchPx
    }

Each is measured as geometry and nothing else:

- `ButtonsOverlap` (`:100-135`) - rect intersection, `Overlaps(ar, br, OverlapPadPx, ...)`.
- `ButtonOverText` (`:137-159`) - this one READS `t.text`, but only at `:156` to quote a snippet into
  the message; the test itself is `Overlaps(br, tr, ...)`.
- `SubTouchFloorBand` (`:161-199`) - `Mathf.Min(br.width, br.height)` against
  `ElarionUiKit.MinTouchPx`.

### 1b. The fourth rule lives in the harness, and it also measures a rect

`Assets/Editor/UICaptureLaunch.cs:5851-5872`, `RULE 1 [text-off-plate]`: it resolves the label's rect
and the `ZoneBacking` plate behind it and fails when `OutsideBy(tr, pr) > GeoContainSlackPx`
(`:5868-5872`). **A rect that sits neatly inside its plate while its own contents have been cut away
is invisible to it.**

**Stated exactly: every assert on this path measures WHERE a rect is. Nothing measures whether the
text inside it still says what it was given.**

### 1c. The defect that proves the gap, with the numbers

WO-1628, on the capture written 2026-09-10: seven Build Collections category captions rendered
`nothing affordable y` - `yet` cut after one letter - at 2340x1080 and 2670x1200, correctly at
1920x1080. The step-1 probe measured 21 of 22 characters and `isTextTruncated` TRUE on fourteen
labels. **The geometry run on the same log passed 91 canvases**, correctly, because no rule looked.
Full record: `WorkOrders/WORK_ORDER_1628_build_collections_category_subtitle_truncates_at_two_of_three_aspects.RESULT.md`
sec.1.

The kit's own runtime rescue could not have caught it either. `ArmFitGuard`
(`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3094-3100`) returns at `:3096` when
`!Application.isPlaying`, and the capture is edit-mode; and even where it does run, its assert is
ZERO visible glyphs (`:3232-3233`, `if (ti == null || ti.characterCount == 0) return true;`), so 20
of 22 characters passes it cleanly.

### 1d. The red-first law this rule must satisfy

`LayoutOracle.cs:16-21`, in the file's own header:

    *  UiTouchClampRegression  - measures SYNTHETIC canvases whose defects are authored
       on purpose, so the oracle can be SEEN going red before anyone trusts it green
       (PROD-008's rule: an oracle never seen red is not evidence).

`Assets/Editor/Regression/UiTouchClampRegression.cs:1-48` states the same in longer form:
*"A rule with a typo, an inverted comparison, or a predicate that silently excludes every control
reports a clean run and looks identical to a healthy one."* Its cases are RED-A, RED-B, RED-B2 and a
GREEN control case, each built at two landscape aspects (`:62-66`).

**The constraint the lane must resolve:** that suite's header at `:44-48` says
*"NO TMP, NO CATALOG, NO PANEL BUILDERS ... so the suite cannot go red because a font asset or a
sprite failed to resolve headless."* A glyph-survival case NEEDS a TMP label. See sec.4 Step 2.

### 1e. Two existing implementations of the discriminator - do not invent a third

**`Assets/Editor/Regression/NightMarketRuntimeLayoutRegression.cs:705-772`, `CheckNotTruncated`** is
the fuller one and is the shape to lift:

- `:724-727` counts `printable` = non-whitespace characters of the source string.
- `:740` `t.ForceMeshUpdate();` before reading `textInfo`.
- `:744-753` when `textInfo` is null it records a **PartialSkip that NAMES the label**, with the
  reason written in the code: *"A run where nothing could be measured must never be able to read as
  a run where everything fit."*
- `:755-758` counts glyphs with `characterInfo[i].isVisible`.
- `:761-766` zero visible glyphs = culled whole.
- `:768-772` `visible < printable` = TRUNCATED, with both counts, the rect and the font size in the
  message.

**`Assets/_Modules/Village/Walls/HubRepairAffordance.cs:600-632`, `WarnIfClipped`** is the lighter
one and carries the caveat this ticket must respect, in its own doc block at `:610-612`:
*"characterCount is TMP's PARSED count, so a newline in the two-line string can read as one character
short on its own."*

**So the discriminator is visible glyphs vs printable source characters, NOT raw
`characterCount < text.Length`.** The brief that raised this ticket named the raw form. It is the
weaker signal: it false-positives on a source string carrying an explicit newline or a rich-text tag,
which TMP parses out of the count. **Soft wrap alone does NOT trip it** - WO-1628's 1920x1080 probe
lines read `2 lines, 22 chars` against `sourceLen=22` on a two-line render, so a wrapped label that
kept every character passes it cleanly. The visible-vs-printable form is immune to both, which is why
it is the test.

`isTextTruncated` is a useful SECOND field to print in the message, never the test on its own: its
semantics are overflow-mode dependent and **were NOT verified against TMP source this session**. The
glyph count is mode-agnostic - it catches a `Truncate` cut and an `Ellipsis` substitution the same
way - and that is the reason to prefer it, not a claim about what the flag does.

### 1f. How findings are routed today, and why a fourth kind does not route itself

`UICaptureLaunch.cs:5895-5901` fans `LayoutOracle.Audit` output into two buckets by KIND
(cross-parent `ButtonsOverlap` to `crossFails`, everything else to `fails`). Then `:5916-5930`
classifies into the touch tally **by message PREFIX**:

    if (f.StartsWith("BUTTONS OVERLAP", ...) ||
        f.StartsWith("BUTTON OVER TEXT", ...) ||
        f.StartsWith("SUB-TOUCH-FLOOR BAND", ...))

with the comment at `:5910-5912` stating the prefix classification exists *"so a new rule cannot be
added to one bucket and silently forgotten in the other."* **A fourth kind with a new prefix
therefore lands in the geometry bucket and is absent from the touch tally by default.** Which bucket
it belongs in is a decision this ticket makes explicitly - sec.4 Step 3.

### 1g. The baseline mechanism the touch rule used, and its shrink-only law

`UICaptureLaunch.cs:5936-5975`: the WO-1060 allow-list. Four panels were known-bad when the rule
landed; they are reported as warnings and excluded from the marker, and **nothing else is**. The law
is at `:5946-5949`:

    THIS LIST MAY ONLY EVER SHRINK. Each fix deletes its own entry in the same commit;
    adding an entry requires an owner ruling. When the last one goes, DELETE THE MECHANISM -
    an empty suppression list is an invitation to add to it.

`TouchBaseline` is the array (`:5955-5967`), `IsTouchBaselined` the matcher (`:5969-5975`, called at
`:5915`). The reasoning at `:5936-5941` is the one that matters here: *"Turning the oracle on red
would block every commit, and a gate that blocks everything gets switched off, not fixed."*

Print cap: `GeoMaxPrintedLines = 60` (`:5831`), applied at `:6013` and `:6039` - all findings are
still counted, only the printed lines are capped.

---

## 2. What is NOT claimed

- **The number of labels that will go red on the first live run is UNKNOWN.** Nobody has ever
  measured glyph survival across the captured panel set. It could be two labels or two hundred. The
  baseline in sec.4 Step 4 is therefore seeded FROM THE FIRST RED RUN's own output, never from a
  guess, and the ticket is not done until every seeded entry is justified in the hand-back.
- **It is NOT proven that the default TMP font resolves in the batchmode regression environment.**
  `NightMarketRuntimeLayoutRegression.cs:744-753` carries a PartialSkip branch precisely because
  sometimes it does not. If it does not resolve for the synthetic red case, **the rule has not been
  seen red and this ticket is NOT done** - see sec.5.1.
- **Not claimed which panels are truncating besides Build Collections.** WO-1628 measured that one
  screen. This ticket builds the detector; it does not pre-judge what it finds.
- **Not claimed that a truncated label is always a defect.** Some copy is deliberately elided. That is
  what the baseline and the exclusions in sec.4 Step 2 are for, and each exclusion must be justified
  in the code, not assumed.
- **Not claimed this replaces WO-1629.** That ticket fixes a specific caption and opens the capture
  door for a card. This one makes the next such defect fail a gate instead of needing an eye.

---

## 3. Target - what "fixed" means

A label that renders fewer glyphs than the string it was given is a FINDING with a name, a path, both
counts, its rect and its font size - on the same capture run that already measures rect geometry, at
all three aspects. The rule has been SEEN going red on a canvas whose defect was authored on purpose,
and SEEN staying silent on the same label given room. Known offenders are listed explicitly, shrink
only, and each entry names the ticket that will delete it.

---

## 4. The fix

### Step 1 - the fourth finding kind, in `LayoutOracle.cs`

Add a fourth member to `FindingKind` (`:56-64`) - `TextTruncated` or equally plain - and a fourth
assert block in `Audit` (after the existing three, before `return found;` at `:205`), iterating the `texts` array the method
already collects at `:94`.

**The test** (sec.1e): `ForceMeshUpdate()`, then visible glyphs (`characterInfo[i].isVisible` over
`Mathf.Min(characterCount, characterInfo.Length)`) against printable (non-whitespace) source
characters. Lift the shape from `NightMarketRuntimeLayoutRegression.cs:705-772` rather than writing a
third variant of the same idea.

**The message is the deliverable** (`LayoutOracle.cs:28-33` - the owner is colourblind, a failure may
never be a colour). It must carry: the hierarchy path via the existing `PathOf`, a `Snippet` of the
text, visible-of-printable counts, the resolved rect via `RectStr`, the font size with its floor and
ceiling, and `overflowMode` / `textWrappingMode`. Also print `isTextTruncated` as a corroborating
field. A reader must be able to act without opening the PNG.

**Exclusions, each justified in a comment where it sits.** Mirror the `ButtonOverText` predicate at
`:144-151`: skip null / disabled / not `activeInHierarchy`, empty text, `color.a < 0.05f`, and
`ClippedOut(...)` (masked content is clipped by construction and cannot be judged). **Additionally
skip labels left in `TextOverflowModes.Overflow`** - by definition they do not truncate, and sweeping
them is noise that pushes real findings past the print cap. **`textInfo == null` after
`ForceMeshUpdate` is NOT a silent continue** - it means no font resolved and nothing was proved; emit
it as a distinct, clearly-worded finding or a warning line that names the label, in the spirit of the
PartialSkip at `NightMarketRuntimeLayoutRegression.cs:744-753`. A branch that returns having asserted
nothing is the most expensive defect class in this repo, and worse inside an oracle that polices
others for it.

**Declare the side effect.** `LayoutOracle`'s header says it is *"Pure measurement - it never moves,
resizes or disables anything it inspects"* (`:45-47`) and `ForceMeshUpdate` regenerates a mesh. It is
the same call WO-1628's own probe makes and it is acceptable - but amend that sentence in the same
commit to say what the fourth rule does and why, rather than leaving the header false.

### Step 2 - the RED-FIRST proof, in `UiTouchClampRegression.cs`

Add a case pair in the existing shape (`CaseSubFloor`, `:115-160`, is the model), at the same two aspects (`:62-66`):

- **RED:** a TMP label given a string that cannot fit its authored band - wrap Normal, overflow
  Truncate, autosize floor/ceiling as `FitBlock` leaves them - plus a healthy label in the same
  canvas, so a rule that flags EVERYTHING also fails. Assert the finding fires, NAMES the offending
  label, carries both counts, and does NOT name the healthy one.
- **GREEN:** the same string with room. The oracle must be silent.

**Resolve the no-TMP constraint explicitly.** The suite header at `:44-48` says the fixture avoids TMP
so a missing font cannot red it. That reasoning stands for the other three cases; this case cannot
honour it. Amend the header in the same commit to say which case uses TMP and why, and:

**A font that will not resolve must NOT read as a pass.** If the label produces no `textInfo`, the
case reports that it could not prove the rule - it does not return green. The hand-back must state
which font asset the red case resolved and how it was obtained, so the next seat knows the proof was
real. **If it cannot be made to resolve headless, STOP and hand back with that finding** - the rule
may not be wired into the live gate on a proof that never ran (CLAUDE.md sec.11B).

### Step 3 - routing, and the decision this ticket makes

Findings flow through `UICaptureLaunch.cs:5895-5901` (by kind) and `:5916-5930` (by prefix, sec.1f).
**Give the new kind its own tally and its own distinct marker**, documented alongside the other
capture markers in **this file's own header table, `UICaptureLaunch.cs:33-57`** - that is where the
capture path's markers are written down, one row each. (`DataRegression.cs:14-24` is a DIFFERENT
registry: it maps the three REGRESSION entry points, not the capture ones. Do not add a capture row
there.)

**AND IT MUST BE EMITTED FROM EVERY ENTRY POINT THAT EMITS THE TOUCH ONE.** `ReportTouchOracle()` is
called from **fifteen** sites in this file (`:626`, `:675`, `:1562`, `:1622`, `:2227`, `:2725`,
`:2818`, `:6710`, `:6840`, `:7104`, `:7124`, `:7230`, `:7292`, `:8126` - and it is defined at
`:5993`). A marker wired into one of them prints on one path and is ABSENT on the others, and this
repo reads marker-absent-on-a-fresh-log as a FAILURE, not an unknown. Report the emit sites you wired
in the hand-back, counted, against that list.

Model the reporter on `ReportTouchOracle` itself, **including its zero-panels guard** (`:5995-6001`):
a run that measured nothing must say so loudly rather than print a clean marker over an empty tally.

**Why, and it is the precedent in this same file** (`:5977-5986`): the touch/overlap class was given
its own marker rather than folded into the geometry one, because the geometry marker *"also covers
text-off-plate, so a reader could not tell from it whether the touch/overlap class specifically was
clean"* - the same defect that once let a small suite's pass read as a full suite's pass. Folding
glyph survival into an existing tally repeats exactly that. Do not add the new prefix to the touch
classifier at `:5919-5921`.

Judge the result by the marker on a fresh log, never by an exit code.

### Step 4 - the baseline, and it is a SEPARATE list

Run the rule live over the capture set FIRST and read every finding. Then seed a **new**
`GlyphBaseline[]` + `IsGlyphBaselined(label)` pair in the same shape as `TouchBaseline` /
`IsTouchBaselined` (`:5955-5975`), keyed on the panel label, carrying the same three laws in its own
comment block: shrink-only, an addition needs an owner ruling, and DELETE THE MECHANISM when the last
entry goes.

**Do NOT add entries to `TouchBaseline`.** That list is shrink-only by owner ruling (`:5946-5949`) and
belongs to a different rule; growing it would violate its own header.

**Seed only from the run's own output.** Each entry names the panel and, in a trailing comment, what
was found there - so the ticket that fixes it can delete its own line. If the live run comes back with
nothing to baseline, do not create the mechanism at all; an empty suppression list is an invitation to
add to it, and this file says so.

Note the print cap (`GeoMaxPrintedLines = 60`, `:5831`): if the first run's findings exceed it, the
counts still tally but the lane must widen its own reading, not the cap.

---

## 5. Acceptance

1. **The rule has been SEEN RED.** Paste the synthetic red finding's message verbatim, at both
   aspects, and the green case's silence. State which font asset resolved. **A PartialSkip is not a
   red** - if the font did not resolve, the ticket is not done and the hand-back says so instead.
2. **The rule has been SEEN RED ON A REAL PANEL.** Re-run the capture against a tree in which the
   Build Collections category caption is restored to its retired `.05f-.21f` band (locally, NOT
   committed), and show the rule naming those labels with 21 of 22 characters. Then revert. This is
   the proof it catches the defect it was written for; a synthetic-only proof leaves open that the
   real path never reaches it.
3. **The live run is reported in full.** How many panel builds were measured, how many labels were
   examined, how many findings, and the complete list. Do not summarise.
4. **The baseline is justified line by line**, or it does not exist. Every entry names its panel and
   what was found. State explicitly that `TouchBaseline` was not touched.
5. **The three existing rules are unchanged.** `LayoutOracle` still produces byte-identical messages
   for `ButtonsOverlap`, `ButtonOverText` and `SubTouchFloorBand`, RULE 1 [text-off-plate] is
   untouched, and the existing routing at `:5895-5930` still classifies them into the same buckets.
   `UiTouchClampRegression`'s RED-A / RED-B / RED-B2 / GREEN cases still pass.
6. **The marker is documented and fully wired.** A row of its own in the header table at
   `UICaptureLaunch.cs:33-57`, in the shape the existing rows use, AND emitted from every entry point
   that emits the touch marker - state the count of sites you wired against the fifteen
   `ReportTouchOracle()` call sites listed in sec.4 Step 3, and name any you deliberately skipped.
7. **The headers that became false were amended in the same commit** - `LayoutOracle.cs:45-47` (pure
   measurement) and `UiTouchClampRegression.cs:44-48` (no TMP). Quote both new sentences.
8. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1).

---

## 6. Pins - what must not move

- **The three existing finding kinds and their message wording.** `UiTouchClampRegression` asserts on
  the message text, not just the count (`:29-34`), and the harness classifies by prefix (`:5919-5921`).
  A reworded existing message breaks both.
- **`TouchBaseline` and `IsTouchBaselined`** (`:5955-5975`) - shrink-only, not this rule's list.
- **RULE 1 [text-off-plate]** (`:5851-5872`) - a different assert about a different failure. Do not
  merge them, do not "improve" it while passing.
- **`GeoMaxPrintedLines = 60`** (`:5831`) - do not raise it to fit a noisy first run. Fix the
  exclusions or baseline instead.
- **`ElarionUiKitObsidian.cs` / `ElarionUiKit.cs` are READ-ONLY.** `FitBlock` (`:3077-3092`),
  `ArmFitGuard` (`:3094-3100`), `FontFloor`, `FontHardFloor`, `MinTouchPx`. Cite them, change nothing.
  This ticket adds a detector; it fixes no label.
- **`NightMarketRuntimeLayoutRegression.cs`** - the source of the discriminator's shape. Read it,
  copy the shape, do not refactor it into a shared helper in this ticket (that would put a suite and
  an oracle in one dependency and is its own decision).

---

## 7. What NOT to touch

- **WO-1629's line.** That ticket edits `UICaptureLaunch.cs:8965` (`browser.Show(_ => { });`) and
  files under `Assets/_Modules/Village/BuildMode/`. **This ticket ships no diff in either.** The two
  are not file-disjoint (CLAUDE.md sec.9): the lead sequences them or gives both to one lane.
  Whichever lands second states which commit it rebased onto and re-confirms its own line numbers
  after the rebase.
  - **THE ONE CARVE-OUT, and it is temporary by construction.** Acceptance 5.2 requires the rule be
    seen red on a REAL panel, which means locally restoring the retired `.05f-.21f` band over
    `BuildCollectionBrowser.cs:292-308`, running the capture, reading the finding, and **reverting
    before the hand-back**. That revert is not a fix and not a diff: the hand-back must show **zero**
    changed lines in `Assets/_Modules/Village/BuildMode/` (paste the `git status --short` /
    `git diff --stat` proving it). **If WO-1629 is mid-flight in that file, do not do this unasked** -
    tell the lead and let it sequence, because a temporary local revert over another lane's live edit
    is exactly the two-lanes-one-file hazard sec.9 names.
- **Do not fix any label this rule finds.** Every real finding is a ticket for the lead to mint, with
  the finding's own line as its evidence. Fixing them here would mix a detector with an unbounded
  number of layout edits and no lane could gate it.
- No panel builder, no kit file, no `card-collections.json`.
- Do **not** commit. Do **not** push. Do **not** run a Unity gate unless the lead says the tree is
  yours. Hand the diff back (CLAUDE.md sec.11).

---

## 8. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1630_layout_oracle_has_no_glyph_survival_assert.RESULT.md` is written, with
both paths reported. The lead regenerates `BOARD.html`.
