# WORK ORDER 1662 — RESULT

**Status:** IMPLEMENTED - awaiting gate (no Unity run in this lane; the lead gates)
**Lane:** HUD-CHIP, isolated worktree `.claude/worktrees/agent-a879bf9887691bfa1`
**Base:** `a89603a7ad13639549ea8895c12fb351426227b2` — the lead's WO-1660 commit, ff-merged; the gold
-chip fix (`goldRt.sizeDelta = new Vector2(0f, RailChipHeightPx)`, `HudKitController.cs:3238`) and the
HUD capture wiring (`CaptureAdaptiveHud()`, `UICaptureLaunch.cs:639`) are both present at that HEAD.
**Date:** 2026-09-10

---

## 1. RCA — PROVEN AT SOURCE, NOT INFERRED

The two pins written to catch exactly this label were **GREEN on the cut build** because they measure
a font the card does not draw.

`ElarionUiKit.BuildObsidianButton` ends on `MedievalUiSkin.ApplyButton`
(`ElarionUiKitObsidian.cs:688`), and `ApplyButton` (`MedievalUiSkin.cs:86-91`) sets
`fontStyle |= Bold`, `characterSpacing = 2f` and `EnsureFont(FontRole.Title)`. The card's own slice
overrides size/colour/alignment afterwards and **never resets the role or the spacing**.
`MeasureLineWidthPx` sums **regular-weight advances with no spacing term**
(`ElarionUiKitObsidian.cs:2905-2938`). Both pins passed `FontRole.Body`.

**Computed offline this session** by summing glyph advances out of the committed font assets exactly
as `MeasureLineWidthPx` does (`m_PointSize 64`, `m_Scale 1`), for `"THE NIGHT MARKET"` at font 20
against the **226.7 px** label rect:

| model | width | verdict |
|---|---|---|
| `font_body` Alata Regular — **what the pins measured** | **177.6** | 78%, passes |
| `font_title` Merriweather Bold — **what is drawn** | **214.7** | 95% before anything else |
| + `characterSpacing 2` | ~221 | 97.5% |
| + TMP faux-bold (`font_title.asset:2968` carries `boldSpacing: 7`) | **~243, CONSISTENT WITH — not derived** | **over by ~17 px** |

~17 px is two glyphs plus the ellipsis — which is what the capture reports as
`draws 12 of 14 printable glyphs`. **The label rect derived from the authored fractions
(226.7 x 68.9) equals the capture's x and y bands to the tenth**, so the geometry half of this
arithmetic is a model of the render, not of itself.

⚠ **THE LAST ROW IS AN ORDER-OF-MAGNITUDE CHECK, NOT A DERIVATION.** Turning `boldSpacing: 7` into
px requires knowing how TMP scales it, which I did not read and do not assert — the same reason the
new pin uses one named SLACK instead of modelling the engine (§11B). **The proof does not rest on
it.** It rests on elimination: a Body render is 177.6 px in a 226.7 px rect and *cannot* cut, the
capture *did* cut, and the only other face in play is Title at 214.7 before weight and spacing.

This is the **weight-and-role half** of the same blind spot WO-1466 fixed the **case** half of, in
these same two cases, five days earlier.

---

## 2. THE FIX — three edits, two files

### A. `Assets/_Modules/HUD/Kit/HudKitController.cs`

| Line | Change |
|---|---|
| `:1191-1197` | New `private const float NightMarketLabelMaxPx = 26f;` — the ceiling, named once so the oracle parses it instead of carrying a copy. |
| `:1400` | `face.fontSize = 26f` → `face.fontSize = NightMarketLabelMaxPx`. |
| `:1401-1449` | The WO-1662 evidence block: the captured line, the four-row width table above, and the two-line budget. |
| `:1450` | **`face.textWrappingMode = NoWrap` + `overflowMode = Ellipsis` + `FitSingleLine(face, 20f, 26f)` → `ElarionUiKit.FitBlock(face, ElarionUiKit.FontHardFloor, NightMarketLabelMaxPx)`.** `FitBlock` sets Normal wrapping + Truncate itself and **clamps `minSize` at `FontHardFloor` structurally** (`ElarionUiKitObsidian.cs:3083`), so the floor stops being a hand-passed argument. |

**Why two lines and not a wider plate:** band **68.9 px** tall; two Title lines at the **26 px
ceiling** need `2 x 26 x 1.2570 = 65.4 px` (`font_title` `m_LineHeight 80.448 / m_PointSize 64`), and
the widest wrapped line `"THE NIGHT"` measures **154.5 px raw** in the 226.7 px rect — **~30% margin**,
which absorbs the bold + spacing terms whose exact size is *not* asserted anywhere. A one-line
widening would have had to be sized off a number I cannot measure headlessly. **The title also gets
BIGGER** — 26 instead of the bottomed-out 20 — which serves WO-1384's own owner ruling
(*"needs to be the shining gem ... it should stand out"*). No floor lowered, no string shortened.

**The one-line alternative was SIZED, then rejected on the number — not on preference.** The
coordinator sanctioned either. To seat ~243 px plus margin, `NightMarketLabelPlateX0` would have to
fall to roughly `0.02`, i.e. the dark plate spans essentially the whole 320 px card and covers the
illustration it sits on — and that target is itself computed from the one quantity I cannot measure
headlessly (the bold term), so the widening would have been sized off an estimate. The two-line form
needs no such estimate.

⛔ **Not touched:** `MedievalUiSkin`, `BuildObsidianButton`, `MeasureLineWidthPx`,
`NightMarketLabelPlateX0`, the card's px size, the canon `storeWordmark`, the `GlyphBaseline` array.
**Verified by token count over the `BuildNightMarketCard..OpenNightMarket` slice** — every 12a-12e pin
string is in its required state (`"NightMarketCardRing"` 1, `"NightMarketCardComet"` 1,
`"NightMarketCardAura"` 1, `RadialGlowSprite` 1, `cimg.sprite = auraSprite` 1, `AddComponent<Mask>()`
1, `ApplyRounded(cardImage, NightMarketCornerRadiusPx)` 1, `ElarionUi.Gold` 12,
`"NightMarketCardFrame"` 0, `ParticleSystem` 0, `TextWrappingModes.NoWrap` **0**,
`ElarionUiKit.FitSingleLine(face,` **0**, `ElarionUiKit.FitBlock(face,` **1**).

⚠ **One trap found and closed while writing the comment:** a source-text oracle cannot tell prose from
code. The first draft of the evidence block quoted `TextWrappingModes.NoWrap` and
`FitSingleLine(face, 20f, 26f)` verbatim while explaining what they were replaced BY — which would
have red 12e's own new pin from inside a comment. The block now deliberately refuses to spell either
token, and says why (`HudKitController.cs:1438-1444`).

### B. `Assets/Editor/Regression/HudLabelFitRegression.cs` — both pins re-pointed onto the drawn face

| Line | Change |
|---|---|
| `:1826-1858` | The WO-1662 header: the four-row table, and `SkinnedFaceWidthSlack = 1.15f`. |
| `:1860-1862` | `SkinnedFaceRole = ElarionUiKit.FontRole.Title` — the role `MedievalUiSkin.ApplyButton:91` installs. Named once; both cases read it. |
| `:1864-1904` | `TryWrapLines` — greedy word-wrap measured through the kit at the slack, returning `false` (a **stated skip**, never a silent pass) when the font is not measurable headlessly. |
| `:1906-1998` | `CheckNightMarketTitleFit` — **the single shared measurement**, consumed by 11c and 12e. |
| `:2063-2078` | 11c's own copy of the measurement **deleted** (the dead legacy method was removed outright, not parked). |
| `:2183-2204` | 12e re-pointed: requires `FitBlock`, **forbids** `TextWrappingModes.NoWrap`, then calls the shared check. |
| `:1815-1821`, `:2101-2104`, `:2110-2111` | The two case headers and their RED-one-liner recipes corrected in the same change (§15). |

**Two-form model, deliberately, exactly like WO-1660's 7h:**
- `FitBlock` in the slice → wrap at the parsed ceiling; assert `lines <= 2`, every line inside
  `plateW`, and `2 x ceiling x lineFactor <= bandH` (the line factor is **read off the Title font
  asset**, `faceInfo.lineHeight / faceInfo.pointSize`, never typed).
- `FitSingleLine` in the slice (the pre-fix shape, kept so a **revert reds instead of skipping**) →
  whole string at `FontHardFloor` x slack.
- **Neither → hard FAIL.** An oracle that cannot tell which shape it is modelling must not report a
  verdict in either direction; modelling the wrong thing silently is this ticket's entire root cause.

**Why the slack is load-bearing rather than cosmetic:** Title-regular alone is `214.7 < 226.7`, so a
re-point onto the right *role* without a weight term would have gone **straight to green and never
been seen fail**. With `x1.15`, HEAD measures `246.9 > 226.7` and FAILS. The slack covers bold +
character spacing **as one named allowance** — how TMP scales either internally is deliberately **not**
asserted (§11B). Precedent: `RumorBoardPanel.PageButtonBoldSlack = 1.10f` (`RumorBoardPanel.cs:126-128`)
already concedes the weight half on Body advances.

---

## 3. RED RUN — exact revert for the lead

```
git checkout a89603a7ad13639549ea8895c12fb351426227b2 -- Assets/_Modules/HUD/Kit/HudKitController.cs
```
(leave `HudLabelFitRegression.cs` in place — it is the oracle doing the measuring).

Expected on that tree:
1. **`UI_GLYPH_FAIL x6 NEW over 106 panels`** with the captured `[glyph-oracle] TEXT TRUNCATED ...
   NightMarketCardLabelPlate/Label ("THE NIGHT MARKET") draws 12 of 14 printable glyphs` — unchanged,
   at all three aspects.
2. **`HudLabelFitRegression` RED under BOTH tags** — `[night-market-standout]` and
   `[night-market-aurora]` — via the single-line path: `246.9 ref px ... but the label plate is only
   226.7 px wide`. 12e additionally reds on the missing `FitBlock`.

Re-apply → the glyph line is gone and both tags go green through the two-line path
(widest line 154.5 x 1.15 = 177.7 in 226.7; height 65.4 of 68.9).

---

## 4. WHAT I HAVE **NOT** PROVEN (§11B)

- **No Unity ran in this lane.** Every number above is a source read or an offline computation over
  the committed font assets. `COMPILE_GATE_OK`, `REGRESSION_OK`, `UI_GLYPH_OK` are the lead's.
- **That `FontRole.Title` is what TMP finally RESOLVED** is proven as far as the source *request*
  (`MedievalUiSkin.cs:91`) plus elimination — a Body render measures 177.6 in a 226.7 rect and could
  not cut. `EnsureFont` can fall through if an asset is absent. The PNG is the direct proof.
- **The HEIGHT half can legitimately go unmeasured, and the log says which.** `ElarionUiKit.FontFor`
  runs a numeral-legibility gate and can return null on a healthy tree
  (`ElarionUiKitObsidian.cs:2938-2975`). On the lead's log the green run must carry the note
  `... 2 lines need 65.4 of 68.9 px`; if instead it carries `the HEIGHT half of the two-line budget
  is UNMEASURED this run, not proven`, then only the WIDTH half was proven and the PNG owes the rest.
  Marker-absence discipline applies to notes too.
- **Where autosize settles.** Two lines at 26 leave 3.5 px in the 68.9 band; TMP's block bounds may
  settle at 24-25. Still far above the 20 px hard floor — within contract either way — but the number
  comes from the PNG.
- **The device-side `TextFitGuard` interplay.** `FitBlock` arms the same guard
  (`ElarionUiKitObsidian.cs:3090`); its behaviour on the new two-line band is unmeasured until a
  logcat. ⚠ Worth watching because the main tree carries another lane's uncommitted
  `FitGuardRelaxAllowlistRegression.cs` edit, and that allowlist is keyed by label path.
- **The two pins now BOTH report the same failure text under their own tags.** That is deliberate
  (each case owns its verdict), but it means a single defect will print twice.

---

## 5. RELATED, DELIBERATELY NOT DONE

- **The deck-card twin.** `WORK_ORDER_1636_...RESULT.md:1109` holds
  `RealmWorkspace_1920x1080|.../DeckCard_The Night Market/Label|12 of 14` as **accepted debt** in the
  `GlyphBaseline` array — same string, same count, same shape, **different producer**
  (`PlayerDeckWorkspace`). Cross-linked in WO-1662 §7. The diagnosis in §1 transfers; the file does
  not. Not touched here, and the baseline row is left exactly as it is.
- **The systemic sweep.** `grep -c "MeasureLineWidthPx(ElarionUiKit.FontRole.Body"
  Assets/Editor/Regression/HudLabelFitRegression.cs` returns **12**. Every one of those measuring a
  face built through `BuildObsidianButton` is measuring Alata Regular against a Merriweather Bold
  render — **+21% on this string before bold**. Named as a follow-up in WO-1662 §8; it wants its own
  ticket and probably a kit helper rather than a thirteenth copy of a slack constant.

---

## 6. FILE + GATE HYGIENE

- `python tools/gate_brace.py` over both files: `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0.
- Raw brace parity: `HudKitController.cs` 412/412, `HudLabelFitRegression.cs` 216/216.
- NUL scan: 0 in both. Line endings uniformly CRLF in both (checked byte-wise; the repo convention).
- No `.unity`, `.asmdef`, `.json` or font asset touched. No new `System.Reflection`.
- No commit, no push, no Unity — per the lane brief.
