# WO-1629 RESULT — the Manage Placed caption is authored in reference px, and the copy fits

**Lane:** PLACED-CARD **Date:** 2026-09-10 **Base:** `c10e4f5d1`
**State:** IMPLEMENTED, EDIT-ONLY — **not gated, not captured, not committed by this lane.**
Every claim below is either a line read at source this session or a line read out of
`Builds/wave2-capture5`. The acceptance numbers in sec.4 are what the NEXT capture must show; this
lane has not seen them and does not claim them.

---

## 1. What the ticket turned out to be

Two defects, one of which was invisible:

1. **An evidence hole.** `UICaptureLaunch.CaptureBuildCollections` called the single-argument
   `Show(...)`, so `BuildManagePlacedCard` returned at its callback gate and **every
   `BuildCollections_*.png` in the repo showed a SEVEN-card grid the player never gets** — on a
   `HorizontalLayoutGroup`, where the missing card also changed every sibling's width.
2. **A caption that shipped truncated and unseen.** Once the eighth card was captured, the caption
   measured as cut at all three aspects.

Step 1 (committed separately as `064f60ed6`) closed the hole. Steps 2 + 3 are this change.

## 2. The measurement that decided the fix

From `Builds/wave2-capture5`, all 24 probe lines present (8 cards x 3 aspects):

| aspect | need (`preferredHeightPx` @ fontSize 20) | max band below `.21f` = `.21 x cardH` | rendered |
|---|---|---|---|
| 1920x1080 | 95.9 | 68.5 | 2 lines, **32 of 45** chars |
| 2340x1080 | 71.8 | 56.7 | 1 line, **16 of 45** chars |
| 2670x1200 | 71.8 | 55.2 | 1 line, **18 of 45** chars |

`truncated=True` everywhere, `fontSize=20 floor 20 ceiling 21` — the fitter had already spent its one
point of shrink room and was sitting ON the kit floor. **No band constant that fits under `.21f` could
seat 45 characters**, so the ticket was reported BLOCKED rather than "fixed" with a constant that
could not work.

**Owner ruling 2026-09-10: shorten the copy; the layout does not move.** That overrides sec.6's
"do not shorten" for this one string, and leaves the title, artwork, divider and kit floor untouched.

## 3. What changed

**`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`**
- `:113-125` — new `private const float ManageCaptionBandPx = 50f;`, deliberately a **second**
  constant rather than a reuse of `CaptionBandPx`: the two bands seat different strings, and sharing
  one would re-tune this caption whenever the category words change length.
- `:527-541` — the copy, in the seven category captions' voice (lowercase, no period, ~22 chars):
  **`manage what you built`** (21 chars, was 45). The card's title still reads "Manage Placed".
- `:543-556` — the band: y anchors collapsed onto `CaptionTopFrac`, `pivot = (.5f, 1f)` set BEFORE
  `offsetMax = Vector2.zero` / `offsetMin = new Vector2(0f, -ManageCaptionBandPx)` — the identical
  shape the category captions use at `:305-308`. **The retired fraction pair now occurs ZERO times in
  the file** (verified by grep this session).
- `:553-564` — the Step 1 probe comment, CORRECTED not deleted: it claimed the caption was a fraction
  and unmeasured, and both statements went false with this change.

**`Assets/Editor/Regression/BuildCollectionPlayerRegression.cs`**
- The count-to-one clause becomes an absence test (`Contains(...)` must be FALSE), with the comment
  rewritten to record that the allowance is spent and why, and a red proof naming **either** caption.
- A second assertion added: `ManageCaptionBandPx` must be DECLARED **and** SPENT.

**Copy authority, for the record:** the string is a bare C# literal at the call site — no localization
key, no `canon-strings.json` row — exactly how the category captions are authored
(`StructureCardVM.cs:416-420`). There was no key to keep.

## 4. WHAT THE NEXT CAPTURE MUST SHOW

    tr -d '\000' < Builds/<capture-log> | grep -a "Flow:Build" | grep -a subtitle | wc -l

**24 lines**, unchanged (8 cards x 3 aspects). Then:

**The three `manage-placed` lines — every one of them, at every aspect:**
- `sourceLen=21` and `rendered=... 21 chars` — **characterCount == sourceLen**
- `truncated=False`
- `fontSize=21` (ceiling reached, NOT the 20 floor)
- `rendered=2 lines`
- `bandPx` height **50**, and `preferredHeightPx` **at or below 50**

**The 21 category lines — unchanged from `wave2-capture5`:** `fontSize=21`, `rendered=2 lines,
22 chars`, `sourceLen=22`, `truncated=False`, `bandPx` h=50, `preferredHeightPx=47.6`, card widths
167.6 / 186.6 / 189.3. **WO-1628 is NOT re-opened** — it was re-asserted on the eight-card grid and
holds; this change does not touch `CaptionBandPx` or the category call site.

**The three PNGs** (`BuildCollections_1920x1080.png`, `_2340x1080.png`, `_2670x1200.png`) must each
show **EIGHT** cards, with the Manage Placed caption reading `manage what you built` in full, and the
title, divider and artwork visibly where they were.

**Any `fontSize=20` on any of the 24 lines means a band is short somewhere and it is NOT done.**

## 5. Gate

`python tools/gate_brace.py` on both `.cs` → `GATE_BRACE_SUMMARY bad=0 of 2` (exit 0). NUL scan → 0
bytes each. No gate-marker string written into any file. No Unity run, no commit — the lead gates and
commits.

## 6. Pins verified at source (post-edit, this session)

`PlacedStructureDoorRegression` C4a/C4b/C4c green, **C4b not re-pointed**;
`BuildAffordabilityWordsRegression.cs:67` literal intact (`StructureCardVM.cs` read, not edited);
`CopyHygieneRegression`'s `_Modules` sweep forbids only `& Pet` / `to every node's yield` — neither
present; `LocalizationAuthorityRegression` checks table READERS, not literals.

**`PlayerTextLiteralLeakRegression` — the one that could have bitten, recorded rather than assumed
away.** Its own red-recipe #2 is *"change an existing baselined sentence"*, and its scan root covers
this file (`LocalizationPolicy.json:6-8` → `Assets/_Modules`). It is **not armed on this tree**:
`docs/localization/manifest.json` has no `literalDebtBaseline` property, so
`LocalizationAuditIO.cs:260` leaves `hasBaseline=false` and the suite PartialSkips with
*"fail-on-new/stale debt is not armed"*. **The moment that baseline is armed, this copy change becomes
one NEW + one STALE fingerprint** and whoever arms it must regenerate through
`PlayerTextBaselineGenerator`.

## 7. ⚠ For the next seat — the pin has no comment model

The first draft of the Step 2 comment quoted the retired literal to explain what it replaced. The pin
is a plain source-text `Contains` over the whole file, so **that comment alone would have RED-ed the
gate on correct code.** The retired pair is now spelled nowhere in `BuildCollectionBrowser.cs`,
comments included; the exact string lives only in the pin's own red-recipe. Same family as the
interpolated-string brace trap in CLAUDE.md sec.1.
