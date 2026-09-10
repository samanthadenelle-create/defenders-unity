# WO-1630 RESULT - the glyph-survival assert, built and wired; NOT YET RUN

**Status of the ticket:** IMPLEMENTED - awaiting gate + capture (lane GLYPH-ORACLE 2026-09-10)
**Lane:** GLYPH-ORACLE, edit-only worktree
`.claude/worktrees/agent-a45005d2de18ae581`
**Base:** `5c5419513` (fast-forwarded from `refs/heads/dev`; `git status --short` was clean before
the first edit)
**Scope of this hand-back:** EDIT ONLY. No Unity was started, no gate was run, nothing was committed.

---

## 0. ⛔ READ THIS FIRST - WHAT IS AND IS NOT PROVED

The lane was assigned **edit-only** (no Unity, no gate). Every acceptance item in the WO that
requires an executed run is therefore **OPEN**, and is listed as such below rather than ticked.

| WO acceptance | State | Why |
|---|---|---|
| 1. seen RED synthetically, both aspects | **NOT PROVED** | needs a batchmode run of the suite |
| 2. seen RED on a real panel | **NOT PROVED, and deliberately not attempted** | see sec.6 |
| 3. live run reported in full | **NOT PROVED**; the reporter now HAS the numbers (`labels=`, `glyphPanels=` on the stamp) | needs the capture entry point to run |
| 4. baseline justified line by line | **N/A - the mechanism was deliberately NOT created** | see sec.5 |
| 5. three existing rules unchanged | **argued from the diff** (sec.4), not from a run |
| 6. marker documented + fully wired | **wired: 14 call sites** (sec.3); emission unproved |
| 7. false headers amended | **DONE** (sec.7, both quoted) |
| 8. brace + NUL checks | **DONE** (sec.8) |

⛔ **The rule has NOT been seen red. Until it has, a clean `UI_GLYPH_OK` on any log proves nothing** -
that is this ticket's own governing rule (`LayoutOracle.cs` header: an oracle never seen red is not
evidence) and it applies to the lane that wrote it.

---

## 1. Files changed (3; `git diff --stat` at hand-back)

    Assets/Editor/Regression/UiTouchClampRegression.cs | 183 ++++-
    Assets/Editor/UICaptureLaunch.cs                   | 223 ++++-
    Assets/_Modules/Core/UI/LayoutOracle.cs            | 171 ++++-
    3 files changed, 567 insertions(+), 10 deletions(-)

`git status --short` lists exactly those three `M` lines and nothing else.

## 2. The marker

**`UI_GLYPH_OK <clean>/<checked> panels labels=<n> unproved=<n>`**, and its failure twin
**`UI_GLYPH_FAIL x<n>`**.

- Defined in exactly one place: `UICaptureLaunch.ReportGlyphOracle()`.
- Documented as its own row in this file's header marker table, in the shape the existing rows use.
- Grep the capture log for `UI_GLYPH_` - **a fresh log without that prefix is a FAILURE, not an
  unknown.** Judge by the marker, never by an exit code.

`labels=<n>` and `unproved=<n>` ride on **both** the OK and the FAIL line. `labels` is how many
labels Assert C actually MEASURED (survived every exclusion AND produced a real textInfo);
`unproved` counts the ones that stood down because no font resolved. `LayoutOracle.Audit` gained a
5-arg overload returning the measured count via `out`; the 4-arg one delegates with `out _`, so the
regression suite's calls are unchanged.

⛔ **`labels=0` over any number of panels is a FAIL.** Zero findings from zero measurements is, to
a grep, byte-identical to everything fitting - and an earlier draft of this reporter had that branch
unreachable, which would have printed a clean line over a run where no font resolved anywhere. The
count is what makes the clean line mean something.

### Three outcomes, not two
| Condition | Line |
|---|---|
| zero panels visited | `UI_GLYPH_FAIL x0` - nothing was proved |
| panels visited, `labels=0`, stand-downs recorded | `UI_GLYPH_FAIL x0 labels` - no font resolved anywhere |
| panels visited, `labels=0`, no stand-downs | `UI_GLYPH_FAIL x0 labels` - the exclusions skipped every label |
| truncated labels found | `UI_GLYPH_FAIL x<n>` + one line per finding + one uncapped tally line per panel |
| everything measurable fit | `UI_GLYPH_OK <clean>/<checked> panels labels=<n> unproved=<n>` |

"Nothing measured" and "everything fit" are the same clean line to a grep unless the reporter
separates them. It separates them.

## 3. Wiring - the count, honestly

**`grep -c "ReportTouchOracle()" Assets/Editor/UICaptureLaunch.cs` returns 15, but that is 14 CALL
SITES plus the definition line.** The WO's sec.4 Step 3 says "fifteen" and then lists fourteen line
numbers and the definition separately; the fourteen are the call sites. `ReportGlyphOracle()` is
wired at **all 14**, none skipped, verified by `grep -c "ReportGlyphOracle();"` = 14.

`ResetGlyphOracle()` is wired beside all **14** `_touchFailures.Clear();` sites, verified the same
way. It is a separate reset rather than a self-clearing reporter **on purpose**: every entry point in
this file READS the tallies AFTER its `Report*` calls, so a reporter that cleared its own state
would make the glyph counts unreadable at exactly the place a ticket's quoted numbers get checked
against the log they claim to come from. To make that literally true rather than merely argued, the
WO-1080 capture stamp now carries `glyphPanels= glyphLabels= glyphFindings= glyphUnproved=`
alongside the touch and geometry totals it already printed.

### ⚠ A FINDING FOR THE LEAD: one Build Collections entry point reports NOTHING
`UICaptureLaunch.RunBuildCollectionsCaptureHeadless` (the focused Build Collections capture entry point) **resets no tallies and calls no `Report*` at all** -
not the geometry one, not the touch one, and now not the glyph one. It is not one of the 14. So the
focused Build Collections capture measures panels and prints no oracle verdict of any kind. That is
pre-existing, is outside this ticket's scope, and is left untouched - but it means **the real-panel
proof (acceptance 2) must run through a reporting entry point**, i.e. the full headless capture or
the navigation capture, both of which call `CaptureBuildCollections()` and then report.

## 4. What the rule does

### `LayoutOracle.cs`
- Two new `FindingKind` members: **`TextTruncated`** and **`TextUnmeasured`**.
  They are **separate kinds on purpose**: if an unproved label shared the truncation kind, a run in
  which no font resolved would answer "yes, Assert C went red" and a red-first proof could be
  satisfied by a total measurement failure. A stand-down is not a red.
- **Assert C**, appended after Assert A and before `return found;`. Nothing in Asserts A / B / B2 was
  touched - their message strings are byte-identical, so the harness's prefix classifier and the
  suite's string assertions both still match.
- The test: `ForceMeshUpdate()`, then visible glyphs (`characterInfo[i].isVisible`) vs **printable
  source characters**. Shape lifted from `NightMarketRuntimeLayoutRegression.CheckNotTruncated`, not
  re-invented.
- `isTextTruncated` is printed as a corroborating field and **explicitly labelled as such in the
  message** - it is never the assertion.

**Exclusions, each justified in a comment where it sits:** null / disabled / not
`activeInHierarchy`; `color.a < 0.05f`; `ClippedOut(...)`; `overflowMode == Overflow`; zero printable
characters; and a deliberate partial reveal (`maxVisibleCharacters < count` or
`firstVisibleCharacter > 0`).

**One correction to the WO's own reasoning, measured not assumed.** The WO says the visible-vs-
printable form is "immune" to rich-text tags. **It is not.** TMP parses tag characters out so they
never reach `characterInfo`, but `char.IsWhiteSpace` counts every one of them as printable, so a raw
count convicts a healthy markup string. Live example measured this session:
`Assets/_Modules/Core/Debug/DebugCanvasUI.cs:156` builds
`"<color=#FF6B6B>No Wallet Bound</color>"` - 36 raw printable against 13 drawn.
`LayoutOracle.PrintableCount(string, bool richText)` therefore skips markup when the label parses it.
Its known limit is stated in its own doc block rather than hidden: a `<sprite>` tag draws one glyph
and contributes zero printable, so a sprite-bearing label reads visible > printable and is **passed**
- a miss, never a false conviction, which is the safe direction for a rule that has to survive its
first live run without being switched off.

**Overflow modes were bounded from the tree, not guessed.** `grep -hoE 'overflowMode\s*=\s*
TextOverflowModes\.[A-Za-z]+' Assets --include=*.cs | sort | uniq -c` returns exactly three:
Ellipsis (14), Overflow (11), Truncate (5). `Linked` and `Page` - whose glyphs legitimately live on
another label - do not occur, so they are deliberately not special-cased on a hypothetical.

### `UICaptureLaunch.AuditGeometry`
The two new kinds are branched out of the `LayoutOracle.Audit` fan-out **before** the `else -> fails`
arm, so they enter **neither** the geometry bucket nor the touch one. Falling through to `fails`
would have wired glyph survival into the live geometry gate on its very first run, with nobody
having ever measured how many labels truncate - the "a widened assert wired straight into a live gate
turns every commit red and is suppressed within the week" outcome that same file already warns about.

The new prefixes were **NOT** added to the touch classifier. Routing is by KIND here, because Assert
C never enters the `fails` list a prefix test reads.

If the audit **throws**, a `TEXT UNMEASURED` entry is recorded for that panel, so a throw can never
let a panel count as measured-and-clean.

## 5. The baseline - DELIBERATELY NOT CREATED, and this is a stated deviation

The lane brief asked for a `GlyphBaseline` / `IsGlyphBaselined` pair in the shape of
`TouchBaseline` / `IsTouchBaselined`. **It was not written**, and the deviation is named here rather
than after the fact.

**Why:** the WO is explicit twice - sec.4 Step 4 *"If the live run comes back with nothing to
baseline, do not create the mechanism at all"*, and acceptance 4 *"justified line by line, or it does
not exist"*. This lane could not run the capture, so it has **zero** measured offenders. An empty
array cannot be justified line by line, and `TouchBaseline`'s own header names an empty suppression
list as the failure mode: *"an empty suppression list is an invitation to add to it."* Shipping one
now would have created exactly the artefact both documents forbid.

**`TouchBaseline` was NOT touched.** Not an entry added, not a line moved.

**The recipe for whoever runs the first capture** (a ~12-line add, mirroring the touch pair):

1. Run a reporting capture entry point. **Expect `UI_GLYPH_FAIL x<n>` - that is the real-panel red,
   not a regression.** Read every `[glyph-oracle]` line and every uncapped per-panel tally line.
2. If the run is clean, **create nothing** and delete this section.
3. If it is not, add beside `TouchBaseline`:
   - `private static readonly string[] GlyphBaseline = { ... }` - one entry per panel LABEL, each
     with a trailing comment naming what was found there and the ticket that will delete the line;
   - `private static bool IsGlyphBaselined(string label)` - the same `IndexOf(..., OrdinalIgnoreCase)
     >= 0` matcher as `IsTouchBaselined`;
   - consult it in `AuditGeometry` where `_glyphFailures` is appended, warning instead of failing,
     exactly as the touch path does;
   - carry the three laws verbatim in its own comment block: shrink-only, an addition needs an owner
     ruling, and DELETE THE MECHANISM when the last entry goes.
4. Every entry must be justified from that run's own output. Never from a guess, never from this doc.

The print cap `GeoMaxPrintedLines = 60` was **not** raised. Instead the reporter prints **one compact,
never-capped tally line per offending panel** (`<label> @<w>x<h>: <n> truncated label(s)`), because
the baseline is keyed on the panel label and a noisy first run would otherwise hide the very labels
that decide whether an entry is justified.

## 6. The real-panel red (acceptance 2) - NOT attempted, and why that is the right call

The WO's one carve-out permits a **temporary local** restore of the retired `.05f-.21f` band over
`BuildCollectionBrowser.cs:292-308` to prove the rule on a real panel. **This lane did not touch that
file**, for two reasons:

1. **It could not run Unity**, so a revert-then-unrevert would have measured nothing at all - it
   would have been a diff with no evidence attached, which is the definition of an unearned edit.
2. **WO-1629 is mid-flight in that same file's directory.** The WO itself says: if it is, do not do
   this unasked.

**Proof of zero diff:** `git status --short` at hand-back lists three files, none under
`Assets/_Modules/Village/BuildMode/`. `git diff --stat` confirms the same three. The hunk list for
`UICaptureLaunch.cs` ends at line 8344; `CaptureBuildCollections()` now begins at line 9167, so
**WO-1629's region of that file is untouched** and the two lanes merge cleanly.

**The recipe for the seat that does run it** (current values read at source this session):
`BuildCollectionBrowser.cs:292-294` builds the subtitle via
`Label(card.transform, StructureCardVM.AffordabilityWords(affordable), 21, TextAlignmentOptions.Top,
new Vector2(.08f, CaptionTopFrac), new Vector2(.92f, CaptionTopFrac))`, then `:305-308` set
`pivot = (.5f, 1f)`, `offsetMax = Vector2.zero`, `offsetMin = (0f, -CaptionBandPx)` - the WO-1628
fix, y in pixels. Restore the retired fractional band in place of those three lines, run a
**reporting** capture entry point (sec.3), and the expected finding is a `TEXT TRUNCATED` line naming
each category caption with **visible < printable** at 2340x1080 and 2670x1200, and silence at
1920x1080. Then revert, and prove zero diff in that directory before handing back.

⛔ **Do NOT expect the literal `21 of 22` from WO-1628.** That pair is `characterCount` vs
`text.Length` - a DIFFERENT metric on a different denominator. This rule reports visible glyphs vs
non-whitespace printable characters, and the copy itself varies per card
(`StructureCardVM.AffordabilityWords(affordable)`). Read the number off the run; do not carry one in
from another ticket's metric. That substitution is exactly the copied-state failure this repo keeps
paying for.

## 7. Headers amended (acceptance 7) - both new sentences quoted

**`LayoutOracle.cs`, the class doc block** (was: *"Pure measurement - it never moves, resizes or
disables anything it inspects."*), now:

> "⚠ ASSERT C IS THE ONE EXCEPTION AND IT IS DECLARED HERE, NOT DISCOVERED LATER (WO-1630). It calls
> `TMP_Text.ForceMeshUpdate()` on every label it examines, which REGENERATES that label's mesh.
> There is no other way to read glyph survival: the count lives on the generated mesh and `textInfo`
> is stale - or null - until a layout pass has run. ... it regenerates what was already there rather
> than changing any authored value: no rect moves, no component is disabled, no string is rewritten."

**`UiTouchClampRegression.cs`, the fixture note** (was: *"NO TMP, NO CATALOG, NO PANEL BUILDERS..."*),
now:

> "⚠ NO CATALOG, NO PANEL BUILDERS, AND NO TMP IN CASES A / B / B2 / GREEN. ... ⛔ RED-C AND GREEN-C
> ARE THE DECLARED EXCEPTION, AND THEY MUST BE (WO-1630). Assert C measures GLYPHS ON A GENERATED
> MESH; there is no way to author that defect without a real TMP_Text and a resolvable font ... if no
> font resolves, the case adds a FAILURE that says the rule could not be proved. It does NOT stand
> down green. ... So a missing font asset turns this suite RED with a message naming the font as the
> cause, and can never turn it green."

## 8. The RED-FIRST cases, as authored (unrun)

`UiTouchClampRegression` gains **RED-C** and **GREEN-C**, at the same two landscape aspects as the
existing cases; `casesRun` moves from `+= 4` to `+= 6` per aspect, so the suite's own marker will
read `12/12 cases` instead of `8/8`. `DataRegression.cs` was **not** touched - the pair lives inside
`Run()`, which is already registered.

- **RED-C** builds `cut-caption` (a ~6% x 3% band) and `roomy-caption` (35%-90% x 30%-55%), both
  carrying the same 37-character string, both fitted with the production `ElarionUiKit.FitBlock(t,
  18f, 21f)` - normal wrap, bounded autosize, Truncate: the exact settings the Build Collections
  subtitle carries. It fails if no `TextTruncated` finding fires, if the finding does not NAME
  `cut-caption`, if it lacks both counts, or if **any** finding names `roomy-caption` (every finding
  is checked, not just the first - a later over-fire is over-firing just as much).
- **GREEN-C** builds only `roomy-caption` and fails if any `TextTruncated` finding appears.
- **Both** treat a `TextUnmeasured` finding as a FAILURE of the case, never as the red they were
  looking for.
- The fixture helper `KitLabel` calls `ElarionUiKit.EnsureFont` - the production resolution seam,
  the same one `NightMarketRuntimeLayoutRegression` and `ArmyMusterLayoutRegression` use - and if
  `t.font == null` it records a FAILURE naming the font as the cause and returns null, so the case
  stops rather than measuring a fontless label.

⛔ **Which font asset resolves headless is therefore NOT stated here, because this lane did not run
it.** The next seat states it from the run. If `EnsureFont` returns nothing in batchmode, the suite
goes RED with a message saying so, and per the WO the ticket is **not done** - the rule may not be
trusted on a proof that never ran.

## 5b. THE ORACLE RAN LIVE - the baseline now EXISTS, seeded from measurement (2026-09-10)

`Builds/wave3-capture2`, fresh:

    UI_GLYPH_FAIL x68 over 91 panels (70 clean, labels=876, unproved=0)

**876 labels measured, 0 unproved** - so the font resolved everywhere, the exclusions did not eat the
set, and the rule was seen red on real panels. **68 truncations over 21 panel builds**, every one of
which had passed a clean 91-canvas geometry run. That is the ticket's whole thesis, confirmed by
measurement rather than by argument.

### What was built on top of that
- **`GlyphBaseline` + `IsGlyphBaselined(label, message)`** now exist in `UICaptureLaunch.cs`,
  immediately after `IsTouchBaselined`, carrying the three laws verbatim: shrink-only, an addition
  needs an owner ruling, DELETE THE MECHANISM when the last entry goes. **`TouchBaseline` was not
  touched** - not an entry added, not a line moved.
- **It is keyed PER FINDING, not per panel**, and that is the design decision. `TouchBaseline`
  suppresses a whole panel by name; here that would mean a NEW caption cut on `NightMarket` - which
  owns 21 entries - lands silently under an existing suppression. A finding is baselined only when
  the panel build, the exact hierarchy path AND the exact drawn-of-printable counts all match. Change
  the copy, move the band, or cut one more glyph, and it reds.
- Marker shape is now `UI_GLYPH_OK <clean>/<checked> panels labels=<n> baselined=<n> unproved=<n>`,
  with `baselined=` on the FAIL line and `glyphBaselined=` on the capture stamp too.
- Every entry names **WO-1636**, the umbrella ticket that owns the 68 and whose sub-fixes delete
  these lines one panel family at a time:
  `WorkOrders/WORK_ORDER_1636_glyph_oracle_first_run_68_truncated_labels.md`.

### THE GAP IS CLOSED - 68 entries, every one measured, none inferred

The seeding run printed only **60** of its 68 finding lines: `GeoMaxPrintedLines` caps PRINTED lines,
the uncapped per-panel tally read 68, and the run's own trailing line said
`... and 8 more (the per-panel tally lines above are NOT capped)`.

This lane refused to seed the missing 8 from that subtraction. Baselining a finding nobody has read,
into a list that may only shrink, is what CLAUDE.md §11B forbids and what WO-1630 §4 Step 4 pre-empted
in writing: *"the lane must widen its own reading, not the cap."* Instead it named the five panel
builds the uncapped tally implicated and the one entry point that would print them all.

**That run happened and it agreed.** `Builds/wave3-navcapture`:

    UI_GLYPH_FAIL x19 over 15 panels (9 clean, labels=144, baselined=11, unproved=0)

19 findings, **11 already in the baseline, 8 new** - the same eight panel builds and the same
per-panel counts the tally had predicted, measured independently. Two runs agreeing on the same eight
is the corroboration; nothing was derived by subtraction. They are now in `GlyphBaseline` flagged
`(nav capture)`, tabled in WO-1636 sec.5, and the gap note in the list header is deleted.
**`GlyphBaseline` holds 68.**

⛔ **`GeoMaxPrintedLines` was NOT raised, and the list's header now says never to raise it.** Widening
the reading closed the gap; widening the cap would only have moved the ceiling for the next run.

### One reporting defect this exposed, fixed in the same edit

The full capture on the seeded tree read `UI_GLYPH_FAIL x68 ... baselined=60` - a headline counting
**68** over a run whose only red was **8**, with 8 printed lines under it. A marker whose count does
not match its own evidence is precisely the defect this rule exists to end. The FAIL line now reads
`UI_GLYPH_FAIL x<new> NEW over <n> panels (... of <total> found ...)`, so the x-number is what
actually reds and the total still travels, labelled.

### What the next captures must read

| run | expected |
|---|---|
| full capture | `UI_GLYPH_OK 91/91 panels labels=876 baselined=68 unproved=0` |
| navigation capture | `UI_GLYPH_OK 15/15 panels labels=144 baselined=19 unproved=0` |

Any NEW finding - including one on a panel that already carries entries, because the list is keyed
per finding - reds with `UI_GLYPH_FAIL x<n> NEW`.

### The 68, by panel build
`NightMarket` 21, `RumorBoard` 12, `RumorBoard_page2` 9, `RealmWorkspace` 13, `HeroSelect` 5,
`JourneyWorkspace` 4, `ManageWorkspace` 2, `BuildMenuUpgradeTower` 1,
`EndStateWaveClear_repairAll` 1 - 21 panel builds, 68 findings. Two authoring families, read off
the runs' own fields: **52** are `overflow=Ellipsis wrap=NoWrap` (single-line band too narrow) and
**16** are `overflow=Truncate wrap=Normal` (`FitBlock` band too short - the deck-card
`DeckCardPurpose_*` block across Realm and Journey is all of it). The worst single line measured is
`RumorBoard`'s poster hook at **27 of 62** printable glyphs.

## 8b. HOLLOW-PASS ARM D - caught by the gate, fixed (2026-09-10, increment)

The first regression run on this diff came back **RED with one finding**, and it was against the
GREEN-C case this lane wrote:

> `UiTouchClampRegression.cs:366 [D-vacuous-against-absent-fixture] guard 'unmeasured != null' -
> EVERY assertion in this verdict method is nested inside a positive-existence guard, and nothing
> asserts that the fixture exists. If it is absent the method checks ZERO things and still reports
> green - RaidCooldownRegression case 5, 2026-08-21 ...`

**It was a true positive, and the shape is the ticket's own subject matter.** A GREEN case's entire
verdict is SILENCE, so every assertion in it hung off "a finding exists". With the canvas or the
label absent, `CaseGlyphFits` asserted nothing and reported green - the exact "nothing measured reads
as everything fit" failure `ReportGlyphOracle` was written to prevent, reproduced one file over by
the lane that wrote it. `HollowPassScanner.ScanVacuous`
(`Assets/Editor/Regression/HollowPassScanner.cs:394-434`) is the detector; its positive side is
`PositiveExistenceGuard` at `:193-194`.

**The fix is a fixture-health floor asserted BEFORE any existence test**, using the measured-label
count the reporter already needed:

- `UiTouchClampRegression.cs:389` - GREEN-C: `if (measured < 1)` adds a failure naming
  `labels=<n>` and, when there is one, the `TextUnmeasured` line explaining why; otherwise it says
  the exclusions skipped the label outright. Silence is only allowed to mean something once the
  oracle has proved it looked.
- `UiTouchClampRegression.cs:312` - RED-C: the same floor at `if (measured < 2)`, because that case
  authors two labels; anything less is a statement about the fixture, not about the rule.
- Both cases now call the 5-arg `LayoutOracle.Audit(..., out int measured)`
  (`UiTouchClampRegression.cs:307` and `:378`).

Neither floor is a positive-existence guard, so `ScanVacuous` finds an assertion with no such
enclosing guard and clears the method (`HollowPassScanner.cs:421-426`), and each guarded block
asserts before it returns, which exonerates it from arms A/B/C at `HollowPassScanner.cs:341`.

Also corrected in the same increment: the RED-C header comment quoted WO-1628's **"21 of 22"** as if
it were this rule's expected output. It is `characterCount` vs `text.Length` - a different metric on
a different denominator - so the comment now says the last word was cut and explicitly forbids
reusing that figure as an expectation.

## 9. Gate hygiene (acceptance 8)

    python tools/gate_brace.py Assets/Editor/Regression/UiTouchClampRegression.cs \
        Assets/Editor/UICaptureLaunch.cs Assets/_Modules/Core/UI/LayoutOracle.cs
    -> GATE_BRACE_SUMMARY bad=0 of 3

NUL bytes: 0 in all three (byte scan). Line endings: CRLF throughout, no mixed endings, no BOM
introduced - the mechanical 28-line insertion pass was normalised back to CRLF and the resulting
diff is insertion-only apart from four intentionally edited blocks.

## 10. What the next capture must show

1. `UI_GLYPH_` present on the fresh log. Absent = failure.
2. the touch-oracle suite marker reading `12/12 cases`, with the `[red-C @1920x1080]` and
   `[red-C @2340x1080]` lines quoted verbatim into this file, plus both `[green-C]` silences and the
   name of the font asset that resolved.
3. The existing `[red-A] / [red-B] / [red-B2] / [green]` lines unchanged.
4. The live glyph run in full: panel builds measured, labels examined, findings, `unproved=`, and
   the complete finding list - not a summary.
5. Then, and only then, the baseline decision per sec.5.
