# WORK ORDER 1667 — RESULT

**Status:** IMPLEMENTED — (a) unchanged and green; (b) RED on chain 38, cause found and fixed. Awaiting re-gate.
> ⚠ **READ §C FIRST.** It corrects a measurement error of mine that also touches the WO-1663
> and WO-1666 RESULTs, and it records the portrait-row removal.
**Lane:** LABEL-PINS
**Date:** 2026-09-10
**Tree:** worktree base `8e170b555417545ea4d871b08954e09179cc35d0` (WO-1666's edits kept in place
as instructed, uncommitted alongside).
**Files changed (3, plus WO/RESULT):**
- `Assets/Editor/Regression/SessionShapeRegression.cs` — part (a), the re-anchor
- `Assets/_Modules/HUD/Kit/HudKitController.cs` — **the one permitted comment edit** (`:1933`)
- `Assets/Editor/Regression/HudActionBarRegression.cs` — part (b), the weight term
- `Assets/Editor/Regression/HudLabelFitRegression.cs` — part (b), the ONE shared constant

**Not touched:** `RumorBoardPanel.cs`, `MedievalUiSkin`, `ElarionUiKit.Label`,
`ActionSlotHandle.SetCaption`, `MeasureLineWidthPx`, every font floor, `MinSlotPx`, `CaptionInset`,
the `hud.nav.*` canon strings, `GlyphBaseline`, the WO-1662/1663 helpers, the numbering banner.
**No Unity, no commit.** `gate_brace` `bad=0 of 5`, NUL 0 on all five.

⚠ **`HudKitController.cs` shares a lane.** My edit is **one contiguous comment** inside
`BuildQueueStatusChip`'s doc block (`:1933-1937`), nowhere near the PartyNameplate producer the
PROBE-READBACK lane is editing. It should 3-way merge cleanly; flagging it so the lead knows to look.

---

## PART (a) — the one-door anchor. **RED-FIRST PROVEN.**

### a1. What changed
`SessionShapeRegression.Case7_OneDoor` now anchors on a named `const string RetirementLine`
carrying the **full** text, `// BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)`
(three spaces, copied from the source line, not retyped). Counted at source before the edit:

```
full-text occurrences : 1
short  occurrences    : 2
  at line 811  -> '// BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)'
  at line 1932 -> '// BuildQueueStatusChip(pool);` in Build(), so nothing here ever runs,'
```

The failure message now names *why* it looks for the full line. The `:1932` prose is **untouched**
(it is how a reader learns the method never runs), and the case carries a `⛔` against deleting it.

### a2. The four-step mutation — the table, as run

Both anchors were ported and replayed on the real `HudKitController.cs` plus three mutations,
with 15d alongside to show the split:

| input | Case7 **OLD** anchor | Case7 **NEW** anchor | 15d |
|---|---|---|---|
| HEAD, unmodified | GREEN | **GREEN** | note |
| `:811` replaced with a **LIVE** call | GREEN ⛔ | **RED** ✅ | FAIL |
| live call added **ELSEWHERE**, line intact | GREEN | GREEN | **FAIL** |
| every mention of the token deleted | RED | RED | note |

**Row 2 is the ticket: GREEN → RED.** The defect reproduced before the fix and is caught after it.
Row 1 confirms no false positive on HEAD. Row 3 is **deliberately** still green on Case7 — that
shape is `HudLabelFitRegression` 15d's job (WO-1666 §5), and widening Case7 to duplicate it would
put two oracles on one fact. Said in-code and repeated here, per the ticket's acceptance.

*(Script kept at the session scratchpad, `prove1667a.py`; it asserts the full line is unique and
the short form matches twice before it mutates, so it fails loudly if the authoring is reflowed.)*

### a3. The false claim, corrected in the same change
`HudKitController.cs:1933` said *"SessionShapeRegression Case7_OneDoor FAILS the build if that
byte-exact retirement line disappears."* Row 2 disproved it. It now reads that this was **not true
until 2026-09-10**, names the cause (Case7 searched for the short form, **which that very comment
contains**), and states it is anchored on the full text now. One comment, one edit.

---

## PART (b) — the dock caption's weight term. **NO RED, AS PREDICTED — proof is the numbers.**

### b1. The classification was re-read at source and CONFIRMED, all three parts

| claim | source, read 2026-09-10 | verdict |
|---|---|---|
| role is **Body** | `ActionSlotHandle.SetCaption` (`ElarionUiKitObsidian.cs:1108-1112`) calls `EnsureFont(caption, FontRole.Body)` **explicitly** | **correct — kept** |
| caption is **bold** | same call builds it via `Label(..., bold: true)`; `ElarionUiKit.Label:1905-1923` does `if (bold) t.fontStyle = FontStyles.Bold` | **weight term was MISSING** |
| `characterSpacing` is **0** | `Label` sets `t.characterSpacing = spacing`, and `SetCaption` never passes one (default `0f`) | **no spacing term owed** |

Nothing on `BuildAdaptivePeacefulDock:2594` → `BuildPeacefulDockSlot:2681` → `SetCaption:2717`
calls `MedievalUiSkin.ApplyButton` or `EnsureFont(..., FontRole.Title)`. **So `MeasureFacePx` /
`SkinnedFaceRole` were NOT used**, and the code says why at the site.

### b2. Where the shared constant lives, and the two options I rejected

**CHOSEN:** `internal const float BoldOnlyWidthSlack = 1.10f` in `HudLabelFitRegression`, declared
**next to `SkinnedFaceWidthSlack`**, referenced by `HudActionBarRegression`. Both suites are in
`Assets/Editor/Regression/DeNelle.EditorRegression.asmdef`, so `internal` reaches across the two
namespaces (`DeNelle.Editor.Regression` → `DeNelle.Editor`) with **no asmdef change**.

- **REJECTED — reference `RumorBoardPanel.PageButtonBoldSlack` (runtime) as the source.** It **is**
  reachable: that asmdef lists `DeNelle.Village`, so this was a choice, not a limitation. Rejected
  because it would couple the **shipped dock oracle's strictness** to a rumor-board page-button
  constant — retune one screen and an unrelated oracle silently changes; because it points the
  dependency the wrong way (an editor oracle importing a presentation class's layout constant); and
  because that constant's doc names it for *that* button, so it would have to be reworded to serve
  two masters. WO-1667 also lists `RumorBoardPanel` as not-to-touch.
- **REJECTED — a new shared static class for slacks.** It would have to leave `SkinnedFaceWidthSlack`
  behind (the ticket forbids touching it), producing exactly the split it was meant to prevent: one
  allowance in a new file, its sibling still in the old one.

**⛔ It is NOT a second copy of `SkinnedFaceWidthSlack` — it is a different term**, and the
declaration says so: 1.15 covers bold **+ characterSpacing 2** (what `ApplyButton` installs); 1.10
covers **weight only**. Charging a bold-but-unskinned face 1.15 over-reports it; charging a skinned
face 1.10 under-reports it. Neither is derived from the other — deriving would mean asserting TMP's
internals, which WO-1662 explicitly refused to do.

**Why 1.10 and not a fresh number:** the repo already chose it for this exact term.
`RumorBoardPanel.cs:128` is `1.10f` and its doc states the identical reasoning verbatim.

**⚠ Residual, stated:** `RumorBoardPanel` still holds its own 1.10 and was not touched, so there is
now **one editor-side home and one runtime copy**. Collapsing those needs the kit-side
`boldSlack`-parameterised measurer WO-1663 §9 describes — deliberately not this ticket.

### b3. Sentinel safety
The slack is applied **after** the `raw < 0f` branch, not before. `MeasureLineWidthPx` returns `-1`
for "no font resolvable", and multiplying it would yield `-1.1` — still negative here, but it
corrupts a sentinel every caller of that API tests against. The reason is written at the site.

### b4. PROOF-OF-TAKE — per-surface solved `boxW`, before and after

The oracle now logs `raw -> charged` per caption per surface. `HudDockLayout.CanvasLocalWidthPx` and
`Solve` (tiers 1-4) were ported offline from source and run against the four `DockSurfaces`
(`HudActionBarRegression.cs:282-288`), with `mountFrac = ActionBarMaxX - ActionBarMinX` = 0.460 and
`headroom = (SafeRightX - ActionBarMaxX) / (ActionBarMaxX - ActionBarMinX)` = 0.5761:

| surface | mount | tier | slot | **boxW** | JOURNEY raw → charged | % of box **before → after** |
|---|---|---|---|---|---|---|
| 2340x1080 | 975.0 | 1 | 173.9 | 153.1 | 86.63 → **95.29** | 56.6% → **62.3%** |
| 1920x1080 | 883.2 | 1 | 157.6 | 138.7 | 86.63 → **95.29** | 62.5% → **68.7%** |
| 2048x1536 | 764.9 | 1 | 136.5 | 120.1 | 86.63 → **95.29** | 72.1% → **79.4%** |
| **1080x1920** | 496.8 | **2** | **112.0** | **98.6** | 86.63 → **95.29** | 87.9% → **96.7%** |

All five captions, every surface: `BUILD` 54.04→59.44, `TALK` 45.22→49.74, `HERO` 50.92→56.02,
`JOURNEY` 86.63→95.29, `MANAGE` 83.72→92.09.

**⛔ NOTHING REDS, AND THAT IS THE PREDICTED, HONEST OUTCOME — not a failure and not a
non-event.** WO-1667 b4 said so in advance and forbade manufacturing one. What the change bought is
visible in the last row: the **portrait surface solves to tier 2, i.e. the slot sits exactly on the
`MinSlotPx` 112 touch floor**, and `JOURNEY` there is **96.7% of its box, where the oracle used to
report 87.9%**. A ~3.3 px margin was being reported as ~12 px. The next word that gets one glyph
longer now reds instead of shipping cut — which is the entire point of the suite.

*(The offline port reproduces WO-1667 b2's advance prediction — floor-slot boxW **98.56**,
`JOURNEY` **95.29** — from an independent route, which is the cross-check that it is faithful.
Script: `prove1667b.py`.)*

⚠ **The gate log is still the authority.** These are ported numbers, not a Unity run: the lead's
`REGRESSION_OK` run prints the same `raw -> charged ... in a NNN px face (slot ... x inset ...)`
lines from the live solver. **If a logged `boxW` disagrees with the table above, believe the log**
— the port would then be wrong, and that is worth knowing.

---

## b5 — RECORDED AS A SEPARATE TICKET SUBJECT, **NOT IMPLEMENTED**

**Subject: no oracle measures a non-English dock caption.** `CheckMeasuredPeacefulDock` measures
`HudStrings.Get(key)` — the **active locale only**, English in a gate run. Measured offline at
`FontHardFloor` 20 against the floor-slot box (**98.56**, the 1080x1920 tier-2 case above):

| locale | caption | Body @20 | vs 98.56, before any weight term |
|---|---|---|---|
| es | `CONSTRUIR` | **108.82** | OVER by 10.3 |
| es | `GESTIONAR` | **108.32** | OVER by 9.8 |
| de | `VERWALTEN` | **111.30** | OVER by 12.7 |

⚠ **This is NOT proof of a shipped defect** and must not be written up as one: it is the
floor-width slot, `FitSingleLine` ellipsises rather than overflowing, and the phone surface is the
only one that solves that tight. What it **does** establish is a coverage gap on a screen every
player sees. **A localisation sweep was deliberately not smuggled into this ticket.** Recommend a
ticket that measures every shipped locale's `hud.nav.*` captions at the solved widths and rules on
the remedy (shorter copy vs. an icon-only tier on the tightest surface).

---

## ACCEPTANCE, LINE BY LINE

- [x] (a) Case7 **REDS** on the scratch mutation and is green on HEAD — table in a2. Fix shape (i)
      chosen; (ii) not needed because the full line is unique (1 occurrence, counted).
- [x] (a) `HudKitController.cs:1933`'s false claim corrected in the same change.
- [x] (a) RESULT states row 3 is 15d's job, not Case7's, and that this is deliberate.
- [x] (b) Face re-read at source: role Body **correct**, weight term **missing**, spacing **0** —
      all three confirmed, none corrected.
- [x] (b) Weight-only slack added as **ONE** shared `internal const`; `MeasureFacePx` and the Title
      role explicitly NOT used; the `-1` sentinel still a stated skip, with the slack applied after it.
- [x] (b) Per-surface solved `boxW` and per-caption before/after recorded (b4). No red occurred;
      stated plainly and not treated as failure.
- [x] (b) b5 written up as its own ticket subject, not fixed here.
- [x] `python tools/gate_brace.py` on all five touched `.cs` → `bad=0 of 5`, exit 0; NUL 0.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on fresh logs — **the lead's step.**
      **`<n>/<n>` must be UNCHANGED:** no suite was added or removed by either half; both edits are
      inside existing registered suites.
- [x] Status flipped by this lane; this RESULT written; both paths reported.

---

## WHAT A REVIEWER SHOULD LOOK AT FIRST

1. **a2 row 2** — the whole ticket in one cell: GREEN → RED.
2. **b4 last row** — 87.9% → 96.7% on the portrait surface. That is what (b) bought.
3. **b2's rejected options** — if the lead prefers coupling to the runtime constant instead, it is a
   one-line change and the reasoning to overturn is written down.


---

# §C — CHAIN 38 WENT RED ON (b). TWO CAUSES. BOTH CLOSED. (added 2026-09-10)

`Builds/wave8-reg1`:

```
[dock-measured] the caption 'JOURNEY' MEASURES 105.1 px at the hard font floor (regular advances
95.6 x1.10 for the bold weight it is DRAWN at) against a 98.6 px face at 1080x1920
('ElarionLocaleFallback' @20px: 7 chars = 95.6 ref px) - it can only render elided
```

**The red was correct arithmetic on a surface that must not be measured, using a font I had
mis-identified.** Both halves are mine to own.

## C1. ⛔ MY OFFLINE FONT WAS THE WRONG ASSET — and this is a §11B failure, not a rounding error

I measured `Assets/Resources/RpgUi/font/font_body.asset`. **The live measurer never loads it.**
From the same gate log, `[Flow:UI]` lines, read this session:

```
[Flow:UI] role font 'font_body'  REJECTED - its numeral 1 is a bare stroke, indistinguishable from
                                 '|' and 'l' on a HUD made of counts ('1' ink 7.23 vs narrowest bare
                                 stroke 6.14 ('|' 6.14, 'l' 6.84) = ...)
[Flow:UI] role font 'font_title' accepted ('1' ink 25.72 vs narrowest bare stroke 6.41 = 4.01x, floor 1.60x)
[Flow:UI] role font 'font_stamp' accepted
```

`FontFor(FontRole.Body)` runs the numeral-legibility gate (`ElarionUiKitObsidian.cs`, `FontFor`),
**`font_body` FAILS it, so `_roleFonts[Body]` is set to null and `MeasureLineWidthPx` falls through
to `ResolveDefaultFont()` — `ElarionLocaleFallback`.** Every single measurement in the whole gate log
names that one asset: `font asset names appearing in measurements: ['ElarionLocaleFallback']`.

**My parser was correct; I fed it the wrong file.** Re-run against `ElarionLocaleFallback`, it
matches the live gate to 0.05 px on all five captions:

| caption | ported | live (wave8-reg1) |
|---|---|---|
| BUILD | 58.91 | 58.9 |
| TALK | 50.02 | 50.0 |
| HERO | 57.78 | 57.8 |
| JOURNEY | 95.57 | 95.6 |
| MANAGE | 86.68 | 86.7 |

⚠ **What this DOES and DOES NOT invalidate — checked, not assumed:**
- **`Title` measurements were RIGHT.** `font_title` is **accepted** by the numeral gate, so
  `FontFor(Title)` returns the asset I measured. Every "post" / charged number I reported in
  WO-1663, WO-1666 and this ticket stands.
- **`Body` measurements were WRONG** — my "pre" column throughout. The real Body baseline is the
  wider fallback face.
- **No conclusion changes, and the WO-1663 headline gets STRONGER.** Re-measured with the real Body
  font against the 202.4 px rail-chip box: `"Tap to collect"` @30 is **178.4** — even further inside
  the box than the 182.7 I reported — yet it **shipped cut**. `Title x1.15 = 232.2` reds. The old pin
  could not have caught it under either Body figure. Rail-chip pins re-checked: `Harvest` 135.2,
  `Builders idle 2` 180.6, `99 waiting` 185.3 (all Title x1.15, all unchanged, all fit).

**Corrective note appended to the WO-1663 RESULT** so its "pre (Body)" column is not read as fact.
**Lesson for the next lane: `FontFor(role)` can REJECT a role asset and silently fall back — an
offline port must read the gate log's `[Flow:UI] role font` verdicts before trusting any Body figure.**

## C2. THE RED WAS ON A PORTRAIT SURFACE, AND THE GAME IS LANDSCAPE ONLY

`1080x1920` was the only row that solved to **tier 2** (slot pinned at the `MinSlotPx` 112 touch
floor, box **98.6**) — the tightest box in the list, and the only one that could red. It is a
**portrait** presentation. Owner ruling 2026-09-10, WO-1631, proven at source this session:

- `ProjectSettings/ProjectSettings.asset:63-64` — `allowedAutorotateToPortrait: 0`,
  `allowedAutorotateToPortraitUpsideDown: 0`
- `Assets/Editor/Regression/ScreenOrientationRegression.cs` — its header states *"LANDSCAPE ONLY.
  Portrait is not a supported presentation of this game, on any"*, and CASE 2
  `[no-portrait-autorotate]` FAILS the build if either flag returns to 1
- `UICaptureLaunch.LandscapeTargets:215-220` — `1920x1080`, `2340x1080`, `2670x1200`. **No portrait row.**

So that row asserted a presentation the game refuses to enter: **a pin over a surface no player can
reach — the same defect WO-1666 removed, one ticket later and pointing the other way.**

**Removed**, with the ruling cited in the `DockSurfaces` doc comment.
⚠ **`DockSurfaces` is PRIVATE to `HudActionBarRegression`** (grepped: no other reader), so no other
suite is affected. The other `1080f, 1920f` literals in the tree are the **CanvasScaler REFERENCE
resolution** (`HudDockLayout.CanvasLocalWidthPx` defaults, `UICaptureLaunch` scaler setup) — a
different axis, deliberately untouched. `ArmyMusterLayoutRegression.cs:79` carries its own
`"portrait-1080x1920"` surface row; **out of this ticket's silo, flagged for the lead** — the same
ruling would seem to apply, but it is a different suite and not mine to change here.

## C3. RE-RUN — landscape only, live font, weight term KEPT

Port re-validated against `wave8-reg1` first: **0 mismatches** on all five raw widths and all four
solved `boxW` (153.1 / 138.7 / 120.1 / 98.6). Then, landscape only:

| surface | tier | slot | **boxW** | JOURNEY charged | % of box | verdict |
|---|---|---|---|---|---|---|
| 2340x1080 | 1 | 173.9 | 153.1 | 105.1 | 68.7% | fits |
| 1920x1080 | 1 | 157.6 | 138.7 | 105.1 | 75.8% | fits |
| **2048x1536** (tightest) | 1 | 136.5 | **120.1** | **105.1** | **87.5%** | **fits, 15.0 px spare** |
| 2670x1200 *(Seeker, reported only)* | 1 | 176.3 | 155.1 | 105.1 | 67.8% | fits |

All five captions on the tightest surface: `TALK` 55.0 (45.8%), `HERO` 63.6 (52.9%),
`BUILD` 64.8 (54.0%), `MANAGE` 95.3 (79.4%), `JOURNEY` 105.1 (87.5%).

**The smallest landscape slot seats the charged `JOURNEY` with 15.0 px to spare — so there is no
caption defect, and the slack was NOT widened** (the ticket forbade it, and it was not needed).

⚠ **`2670x1200` — THE SEEKER'S REAL SURFACE — IS NOT IN `DockSurfaces`.** It is in
`UICaptureLaunch.LandscapeTargets`, so the capture matrix shoots it while the dock oracle does not
solve it. It measures **comfortably** (67.8%), so this is not urgent — but **adding a surface is a
scope decision and was not taken unilaterally.** Recommend the lead rule on adding it; it would cost
nothing today.

## C4. What changed in this pass

- `Assets/Editor/Regression/HudActionBarRegression.cs` — the `1080x1920` row removed from
  `DockSurfaces`, with the ruling + the three source citations + the "do not re-add" and
  "reference-resolution is a different axis" warnings in the doc comment. **The weight term, the
  Body role, the shared constant and the before/after logging are all UNCHANGED.**
- `gate_brace` `bad=0 of 1` on that file, exit 0; NUL 0; braces 51/51.
