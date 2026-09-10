# WO-1669 RESULT — the SPOILS row now draws at the floor, and an oracle can see it

**Status:** IMPLEMENTED - awaiting gate + capture
**Lane:** STAGING-SPOILS (edit-only worktree — **no Unity run, no commit**, per the brief)
**Base:** `dev` @ `e4b5906a541c65fc12255a109b5dc3723e328488` (ff-merged into this worktree; `git log -1` confirmed)
**Date:** 2026-09-10

---

## 1. WHAT THE RULING ASKED, AND WHAT LANDED

Owner, 12:16, verbatim:

> **"the raid staging screen's SPOILS resource band is seated at FontLabel 40
> (RaidDeployScreen.cs:219) but drawn at 24 px (:890), under the 30 px floor — raise the draw
> to the floor."**

Landed exactly that, in three parts:

1. **The draw is at the floor** — `fontPx: 24f` is gone; the row is drawn at
   `RaidDeployScreen.SpoilsChipFontPx`, which **is** `ElarionUiKit.FontFloor`. The number 30 is
   **not typed anywhere in this diff**, once or twice.
2. **The row was given the band it needs to draw it in** — the CostRow's y-anchors went
   `0.10..0.90` -> `0.00..1.00`, named as `SpoilsRowAnchorMin/Max`.
3. **An oracle now judges both** — new case `[spoils-floor]`, which fails in both halves under
   mutation M10 (HEAD's own values typed back into the new constants). See §3 for why "red on HEAD"
   would be an untrue way to say that.

---

## 2. THE MEASUREMENTS THIS IS BUILT ON — every one read at source this session

| Fact | Where it was read | Value |
|---|---|---|
| The sub-floor draw | `RaidDeployScreen.cs:890-892` (HEAD) | `fontPx: 24f`, a bare literal |
| The seat | `RaidDeployScreen.cs:219` (HEAD) | `ElarionUi.FontLabel` |
| `FontLabel` | `Assets/_Modules/Core/UI/ElarionUi.cs:114` | `40` |
| The floor | `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3033` | `FontFloor = 30f` |
| The band | `RaidDeployScreen.cs:177` | `0.394..0.492` = 0.098 of the body |
| The body floor | `RaidDeployScreen.cs:154` (`MinBodyFracOfPanel`) | `0.473` of the panel = **411 ref px** on 2670x1200 |
| => band height | 0.098 x 411 | **40.3 ref px** |
| The line demand | `RaidSelectionScreen.cs:248` — `(fontPt + 1) * 1.18 + 2` | `NeedPx(30)` = **38.58 ref px** |
| => the band was ALREADY fine | 40.3 >= 38.58 | **the band was never the defect** |
| The row's old share | `RaidDeployScreen.cs:891` — `0.90 - 0.10` | 0.80 x 40.3 = **32.2 ref px** — under 38.58 |
| The cell height at 30 | `CostFormat.cs:154` — `max(24, fontPx + 4)` | **34 px**, centred by the group |

**No font-asset `lineHeight` citation is in this RESULT, and that is deliberate.** The brief said
to re-seat the band from the asset's lineHeight *if 24 -> 30 no longer seats*. **It seats**: the
arithmetic above shows the band clears the floor's line by 1.7 ref px with the band untouched, so
re-seating it would have been a change made against the evidence rather than from it. What did not
fit was the row's **share** of the band, and that is what moved.

---

## 3. THE RED PROOF — stated honestly, half by half

**`[spoils-floor]`, new in `RaidDeployLayoutRegression`, fails in BOTH halves under mutation M10.**

⚠ **It is NOT "red on HEAD", and this RESULT will not say so.** HEAD has neither
`SpoilsChipFontPx` nor `SpoilsRowAnchorMin/Max`, so against HEAD the case does not go red — it does
not **compile**. The reproducible red is the named mutation **M10: type HEAD's own values back into
those two constants** (`24f`, and `0.10`/`0.90`). Then:

* **Half 1** `RaidDeployScreen.SpoilsChipFontPx >= ElarionUiKit.FontFloor` — HEAD's value is the
  literal `24f`. `24 < 30` -> FAIL.
* **Half 2** `(SpoilsRowAnchorMax.y - SpoilsRowAnchorMin.y) * bandPx >= NeedPx(round(drawPx))` —
  HEAD's `0.10..0.90` gives 0.80 x 40.3 = 32.2 px against 38.58 -> FAIL.

Both are **arithmetic over values read at source**, not a run: this lane fires no Unity (§7).

Both halves read **live constants** off `RaidDeployScreen` and the **live band table**
(`BandsFor("spoils")`), never the source text — the regex-over-the-file approach this suite's own
banner (`:16-23`) exists to have retired.

**Why neither existing oracle could ever have caught this — the finding, not an excuse:**

* `[seat]` next door judges the **band** against `NeedPx(FontFloor)`, and the band passed every
  run at 40.3 px while the row inside it drew at 24. **A band that seats a floor-height line says
  nothing about the size handed to `CostRow`.**
* `CostRowFitRegression`'s SPOILS cases measure the **wrap of the word**. `SealPrefixCell` is
  size-agnostic by construction, so they are green at 24 and green at 30 — honest about the word,
  blind to the size.

So the sub-floor number lived as a bare literal at a call site with **no oracle over it at all**,
which is why WO-1640 could fix the word on that exact row and leave the size standing.

**⚠ WHAT THE RE-POINT ALONE PROVES: NOTHING, and this RESULT will not pretend otherwise.**
Re-pointing `CostRowFitRegression.SpoilsPrefixFontPx` at the screen's constant cannot go red —
neither the green case nor its RED companion (which narrows to `min(heuristic, measured - 4)`, a
condition rather than a pixel value) depends on the size. The re-point makes the fixture's own
doc-comment — *"at the fontPx RaidDeployScreen passes"* — **true again**. It is a drift fix, not a
pin. `[spoils-floor]` is the pin.

**⚠ AND WHAT `[spoils-floor]` DOES NOT CLAIM.** The `[seat]` law is about TMP's **Ellipsis**
overflow culling a whole line, which is what `FitSingleLine` labels get. The cost-row cells are not
fitted: `AddCostText` (`CostFormat.cs:133-155`) leaves TMP's **default `Overflow`** live, and the
`HorizontalLayoutGroup` (`childControlHeight = true, childForceExpandHeight = false`,
`CostFormat.cs:104`) clamps each cell to its preferred height and centres it. **This row was never
going to render blank** — it was going to render small, and then, once raised, to bleed past its
own plate. Half 2 is **bleed containment**, and it is written that way in the code so the next seat
does not inherit a false premise.

---

## 4. THE DIFF — four files, 194 insertions / 6 deletions

| File | Change |
|---|---|
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` | `+54/-3`. New named surface at `:219-225` — `public const float SpoilsChipFontPx = ElarionUiKit.FontFloor;` + `SpoilsRowAnchorMin/Max`, with the ruling and the arithmetic recorded above them. `BuildSpoilsChips` (`:938-942`) passes those three and no literal. |
| `Assets/Editor/Regression/RaidDeployLayoutRegression.cs` | `+120/-1`. New `CaseSpoilsFloor` -> `[spoils-floor]`, registered in `RunAll`'s case list beside `[vm-spoils-chips]`; banner gains **M10** and **R4**; the OK reason string now states the floor it proved. |
| `Assets/Editor/Regression/CostRowFitRegression.cs` | `+19/-2`. `SpoilsPrefixFontPx` reads `RaidDeployScreen.SpoilsChipFontPx` instead of copying `24f` (`+ using DeNelle.Village.Hero`); the WO-1640 banner paragraph is marked as history with the reason it does not go stale. |
| `Assets/_Modules/Core/UI/CostFormat.cs` | `+6/-1`, **comment only, zero code**. `"Pinned by ... [fit-SPOILS-24]"` re-pointed — that tag string no longer exists, since the tag is built from the constant and re-derives itself as `[fit-SPOILS-30]`. |

**`CostFormat`'s cell math needed no size parameter.** `AddCostText` already carries the size
through as `metricScale = fontPx / 13f` (`:152`), and `SealPrefixCell` measures the string at
whatever size it is handed — so the brief's conditional permission to touch `CostFormat` was not
taken up beyond the one stale comment.

---

## 5. NOT TOUCHED — checked, not assumed

* **The band table and band constants.** 40.3 px already clears 38.58. Re-seating would have moved
  `spoils` toward neighbours 4.9 ref px away for no measured gain, and `[disjoint]` reds on that.
  The seat row stays `ElarionUi.FontLabel`: `NeedsPx` clamps to the floor either way, and a
  **sub-floor draw** — the half that actually mattered — is now impossible by oracle.
* **WO-1640's `SealPrefixCell`** and everything else Item A landed. Size-agnostic; it is what keeps
  the word whole at 30.
* **WO-1640's toast work** (Item B, the outmatch confirm) — different band, not in this diff.
* **WO-1542's two-tap ruling** — no input, gating or CTA touched on this screen.
* **The 22 px icon cells in `CostRow`** (`CostFormat.cs:120-125`) — shared kit geometry used by
  every cost row in the game. At a 30 px number they will read proportionally smaller than they did
  at 24. **OWED and stated, not hidden**: a separate ticket if the owner's eye picks it up on the
  next frame. A player-facing lane does not smuggle a shared-kit geometry change in behind a font
  ruling.
* **`DataRegression.cs`** — the suite is already wired at `:623`, so `[spoils-floor]` is a LIVE pin
  the moment this lands. No registration line was needed or written.
* **`CLI_LANES_WO_NUMBERS.md`** — number pre-assigned by the lead; banner untouched.

---

## 6. GATE EVIDENCE FROM THIS LANE

* `python tools/gate_brace.py <the 4 files>` -> **`GATE_BRACE_SUMMARY bad=0 of 4`**, exit 0.
* NUL scan (`b'\x00' in bytes`) -> **no NUL** in any of the four. Raw brace counts balanced:
  `RaidDeployScreen 54/54`, `RaidDeployLayoutRegression 114/114`, `CostRowFitRegression 45/45`,
  `CostFormat 21/21`.
* Assembly check: `Assets/Editor/Regression/DeNelle.EditorRegression.asmdef:9` already references
  `DeNelle.Village`, so `CostRowFitRegression`'s new `using DeNelle.Village.Hero` is legal — it is
  the same reference `RaidDeployLayoutRegression` has always used. Both new `const` initialisers
  are const-to-const across assemblies, which C# bakes at compile time.

---

## 7. WHAT IS STILL OWED — the lead's step, not this lane's

1. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a **fresh** log, judged by the MARKER.
   Expect `[spoils-floor]` in the `RAID_DEPLOY_LAYOUT_OK` log line and the tag re-derived as
   `[fit-SPOILS-30]` in `COST_ROW_FIT_OK`.
2. `GlyphCoverageRegression` still `GLYPH_COVERAGE_OK`. The row's **characters are unchanged** —
   this is a size change, not a copy change — so a red there would mean something else moved.
3. **Re-capture and OPEN all three frames**: `Builds/ui-capture/RaidDeploy_1920x1080.png`,
   `RaidDeploy_2340x1080.png`, `RaidDeploy_2670x1200.png`. The SPOILS row must be whole on one
   line at 30, reading at the same weight as `SCOUT REPORT` beneath it, and must not touch `RECON`
   above or `SCOUT REPORT` below. **The acceptance is the opened PNG, never the regenerated file.**
4. Commit (sole committer, explicit paths) with the `**Status:**` flip and `BOARD.html`.
