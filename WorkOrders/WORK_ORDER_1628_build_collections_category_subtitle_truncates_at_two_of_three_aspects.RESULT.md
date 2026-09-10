# WO-1628 RESULT — the category caption band is authored in reference px, not a share of the card

**Lane:** SUBTITLE (Village / BuildMode UI)
**Date:** 2026-09-10
**Rebased onto:** `446c8b992` (this ticket's own step-1 instrumentation, gate-green on
`Builds/wave2-compile5` + `Builds/wave2-reg5` 493/493)
**Stage:** IMPLEMENTED — awaiting gate + capture. **Edit only: no Unity run, no gate, no commit.**

---

## 1. Which branch the numbers licensed, and the lines that license it

Step 1's probe returned exactly 21 lines from
`tr -d '\000' < Builds/wave2-capture3 | grep -a "Flow:Build" | grep -a subtitle`. Pass order is
confirmed by the harness's own save lines in the same log (`saved 1920x1080` after the first seven,
`saved 2340x1080` after the second, `saved 2670x1200` after the third).

| aspect | probe lines | `cardPx` h | `bandPx` h | `fontSize` | `rendered` | `sourceLen` | `truncated` |
|---|---|---|---|---|---|---|---|
| 1920x1080 | `wave2-capture3:2936-2942` | 326.2 | **52.2** | 21 | **2 lines, 22 chars** | 22 | **False** |
| 2340x1080 | `:2998-3004` | 270.1 | **43.2** | 20 | 1 line, 21 chars | 22 | **True** |
| 2670x1200 | `:3060-3066` | 263.0 | **42.1** | 20 | 1 line, 21 chars | 22 | **True** |

`preferredHeightPx=47.6` on **all twenty-one lines**, unchanged even where `fontSize` differs.

> **⛔ SECTION 4 STEP 2, FIRST BRANCH — "the band seats only one line at the two wide aspects".**
> `bandPx` height 52.2 ≥ 47.6 at 1920 and the caption renders both lines and all 22 characters;
> 43.2 and 42.1 are **below** 47.6 at 2340 and 2670 and fourteen labels come back one line, 21 of 22
> characters, `truncated=True`. That is the branch, stated by the measurement, not inferred.

**Why `preferredHeightPx` is the right requirement and not an artefact:** it reads 47.6 at `fontSize`
21 *and* at 20 because TMP computes it at `fontSizeMax` while auto-sizing is on —
`Library/PackageCache/com.unity.ugui@a9ea81766fbd/Runtime/TMP/TMP_Text.cs:3762`,
`float fontSize = m_enableAutoSizing ? m_fontSizeMax : m_fontSize;`. So 47.6 is the two-line
requirement at the **largest** size the fitter may choose — the aspect-invariant number the band must
clear.

**Why branch 3 (the card) is ruled OUT even though `cardPx` does differ.** `bandPx` height is exactly
0.16 of `cardPx` height at every aspect (52.2/326.2, 43.2/270.1, 42.1/263.0) — the band differs
*because* it is a fraction of the card, so "the card changed" and "the band changed" are one fact, not
two. The card is legitimately shorter on a wider aspect: the same run's fidelity line reports
1920x1080 resolving at canvas height 1080 ref px while 2670x1200 resolves at **965.4**. Making the
cards taller to feed a caption would move every rect on the screen to fix one label. Branch 2 is ruled
out too: `modes=Truncate / Normal` on all 21 — wrapping was never disabled.

**Not claimed:** that the device or a play-mode run behaves like the capture. `ArmFitGuard` returns at
`ElarionUiKitObsidian.cs:3096` when `!Application.isPlaying`, and the capture is edit-mode
(`UICaptureLaunch.cs:525-532`), so these are the AUTHORED fits with no runtime rescue — which is
precisely why authoring them correctly is the fix rather than relying on the guard.

---

## 2. Per-file changes

### `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`

| file:line | change |
|---|---|
| `:73-112` | new comment block + two consts, sat directly under the WO-1623 `FooterLink*` block whose idiom they mirror |
| `:107` | `private const float CaptionBandPx = 50f;` — the band HEIGHT in reference px, sized from the measured 47.6 requirement plus 2.4 px headroom so the fitter can still reach its 21 pt ceiling instead of being driven onto the floor |
| `:112` | `private const float CaptionTopFrac = .21f;` — the edge the band hangs from: the caption's **existing** top edge, so the fix grows DOWNWARD into the card's empty bottom margin and no neighbour moves |
| `:248-254` | one `FlowTrace.Step("BuildCollections", …)` per render naming the authored px and the retired fraction's three resolved values — WO-1623's `:340-345` idiom |
| `:292-294` | the caption's `Label` call: both y anchors collapse onto `CaptionTopFrac`; the retired `new Vector2(.08f, .05f)` / `new Vector2(.92f, .21f)` pair leaves this site. `ElarionUiKit.Label` (`ElarionUiKit.cs:1905-1924`) only writes anchors and zero offsets — no height math — so this is safe to do in the call itself |
| `:297-308` | the px band: `pivot = (.5f, 1f)` set **before** the offsets, then `offsetMax = Vector2.zero`, `offsetMin = new Vector2(0f, -CaptionBandPx)` |

**Deliberately NOT changed, and each is a stated decision:**

- **`ElarionUiKit.FitBlock(subtitle, 18f, 21f)` (`:309`) is untouched.** The `18f` is dead already —
  the kit clamps it up to `FontHardFloor` at `ElarionUiKitObsidian.cs:3083` — so it is sec.1f's note,
  not a knob. Lowering a kit floor to make 47.6 fit a 42 px band is the inverse of this fix and the
  trap WO-1626 names in its own sec.6.
- **The `.08f` / `.92f` horizontal fractions stay fractions.** Width is not implicated and the data
  says so out loud: the two aspects that LOST the word measured **more** band width (180.8, 183.4)
  than the one that kept it (162.6). Sec.6 forbids touching them unless width is implicated.
- **The card title (`:275-283`), the divider and the artwork are untouched.** Because the caption kept
  its top edge, the gap between the title's bottom (`.22` of card) and the caption's top (`.21`) is
  unchanged at every aspect: 3.26 px at 1920, 2.70 at 2340, 2.63 at 2670.
- **The Manage Placed card's caption (`:492-497`) still uses the retired fraction, on purpose.** Three
  reasons, all recorded. (a) The WO names that card only to scope its **title's** font floor out
  (sec.6, `manageTitle.fontSizeMin = 15f`) and **never names its caption at all** — so this is a gap in
  the ticket, not a ruling in it. (b) The card was **SKIPPED in all three passes** of `wave2-capture3`
  (the log says why — `Show()` was called without a `managePlaced` callback), so its caption's render
  is **UNMEASURED**. (c) Its 45-character copy needs more lines than a 50 px band gives, so re-pointing
  it here and calling it fixed would be a claim without evidence. **Raised for minting.**

### `Assets/Editor/Regression/BuildCollectionPlayerRegression.cs`

| file:line | change |
|---|---|
| `:178-208` | new source-text pin, placed next to WO-1626's at `:175` and written in the same shape |

Positive: `browser.Contains("CaptionBandPx")` **and** `browser.Contains("-CaptionBandPx")` — declared
**and** spent as the band's bottom offset, because a const that is declared but unread would let the
fraction creep back under a green pin. Negative: `new Vector2(.08f, .05f)` must occur **at most once**
(`IndexOf == LastIndexOf`), since the Manage Placed card legitimately still carries it; the comment
says to tighten it to zero when that card is re-pointed on its own measured frame.

**RED PROOF (written into the comment):** restore the
`new Vector2(.08f, .05f), new Vector2(.92f, .21f)` pair at the seven-card caption and delete the
pivot/offset block — either half alone reds the pin. Both new literals are RED before the fix because
neither string existed in the file.

⛔ **This is a source-text pin, NOT a `LayoutOracle` rule.** Sec.7 forbids adding a glyph-survival
oracle assert in this ticket (it needs a red-first synthetic canvas in `UiTouchClampRegression` and is
its own ticket). That remains unminted and is raised again below.

---

## 3. What the next capture must show

Re-run `RunCaptureHeadless` and read the same 21 probe lines. **Per line, on all twenty-one:**

- `characterCount == sourceLen` — i.e. `rendered=2 lines, 22 chars` against `sourceLen=22`
- `truncated=False`
- `bandPx=<w>x50` — the band is now the authored constant at every aspect, not 52.2 / 43.2 / 42.1
- `fontSize=21` — a NEW discriminator this ticket did not have before: with 2.4 px of headroom over
  the 47.6 requirement the fitter can reach its ceiling instead of being driven to the 20 floor, so a
  20 here means the band is still short somewhere
- `rendered=2 lines` specifically at **2670x1200** and **2340x1080**, the two that were one line

**And the 1920 aspect must not regress** (acceptance 5.3): its caption's TOP edge is unchanged —
`.21 x 326.2 = 68.50` before and after — and the band goes 52.2 → 50.0, still 2.4 px over the
requirement. Expect its two captions to read identically to the previous frame.

**Predicted resolved rects, from the measured `cardPx` heights (open the PNGs against these):**

| aspect | caption top | caption bottom (was) | title bottom | gap to title |
|---|---|---|---|---|
| 1920x1080 | 68.50 (unchanged) | 18.50 (was 16.31) | 71.76 | 3.26 (unchanged) |
| 2340x1080 | 56.72 (unchanged) | 6.72 (was 13.51) | 59.42 | 2.70 (unchanged) |
| 2670x1200 | 55.23 (unchanged) | 5.23 (was 13.15) | 57.86 | 2.63 (unchanged) |

The band grew **into the card's empty bottom margin** — nothing is authored below `.21` on this card
(acceptance 5.5), with one thing to LOOK AT rather than assume.

⚠ **Open `BuildCollections_2670x1200.png` FIRST — the tightest rect on the screen is there.** The gold
bezel's bottom bar is itself a fraction, `.008f`-`.018f` of card height
(`BuildCollectionBrowser.cs:892`, `Edge("GoldBottom", …)`), so it resolves to **2.10-4.73 px** at
2670x1200, **2.16-4.86** at 2340x1080 and **2.61-5.87** at 1920x1080. Against a caption rect bottom of
5.23 / 6.72 / 18.50, the clearance to the top of that bar is **0.50 / 1.86 / 12.63 px**. The rect is
not where the glyphs are — the copy is `TextAlignmentOptions.Top`, so two lines fill 47.6 of the 50 px
from the TOP and the last descender lands roughly 2.4 px above the rect's bottom — but 0.5 px of rect
clearance at 2670 is close enough that **the PNG, not this arithmetic, is the judge** (acceptance 5.1
says so anyway). If the caption reads as kissing the bezel there, the knob is `CaptionTopFrac`, not
`CaptionBandPx` — do not shrink the band back under the 47.6 requirement to buy margin.

**Also expect** one `WO-1628 category caption band authored in px` line per render pass, and
`BUILD_COLLECTION_PLAYER_OK` still in the regression log with `BuildAffordabilityWordsRegression`
green.

---

## 4. Checks run in the lane

- `python tools/gate_brace.py` on both files → `GATE_BRACE_SUMMARY bad=0 of 2`
- NUL scan → 0 bytes in both
- No gate marker string is spelled in any edited file
- Pin sweep against the edited browser, all GREEN: `TextOverflowModes.Ellipsis`/`.Truncate` absent
  (0 each); `enableWordWrapping=true`, `enableAutoSizing=true`,
  `overflowMode=TextOverflowModes.Overflow` present once each; no `[` glyph in any added string
  literal; `fontSizeMin = 16f` and `"Upgrade Defenses"` absent; `FitSingleLine(label,`,
  `link.name = "ManageDefensesFooterLink"`, `PanelRouter.Open(PanelId.Manage, "Defense")`,
  `card.anchorMin = new Vector2(0f, .18f)` present; the WO-1411 trace literal
  `"collection=" + c.CollectionId + " affordable="` (`BuildAffordabilityWordsRegression.cs:67`)
  intact; `BuildFirstUseGuideRegression.cs:50`, `CardCollectionFoundationRegression.cs:74-76` and
  `PlacedStructureDoorRegression.cs:202-217` C4a/b/c all green.

**No Unity was run from this lane, so nothing here is gate-proven — it is a claim until the lead's
fresh log and the three PNGs say otherwise.**

---

## 5. Raised for the lead (both need minting)

1. **The Manage Placed card's caption** — same fraction, same class of defect, deliberately out of
   scope (sec.6), currently UNMEASURED because the card is skipped in the capture. Closing it needs
   the capture to build it (a `managePlaced` callback) first, then its own measured band; its longer
   copy will not fit the 50 px this ticket authored.
2. **The glyph-survival oracle rule** (sec.1i / sec.7) — `isTextTruncated` or
   `characterCount < text.Length` on a visible TMP. Every existing assert measures WHERE a rect is;
   nothing measures whether the text inside it survived, which is why seven truncated captions passed
   a green gate. `LayoutOracle.cs:17-20` requires it be SEEN RED first via a synthetic authored-defect
   canvas in `UiTouchClampRegression`.
