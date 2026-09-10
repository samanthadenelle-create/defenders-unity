# WO-1636 RESULT - glyph oracle first run, 68 truncated labels (sub-lane sections merged by the lead 2026-09-10)

## SUB-LANE NightMarket 2026-09-10

**Scope:** the `NightMarket_*` panel family only — 21 of the run's 60 printed findings.
**Base:** `dev` @ `3da5e5360` (worktree `D:\EoA\.claude\worktrees\agent-a8b7c36493769d74d`).
**Measurement source:** `Builds/wave3-capture2.glyph-findings.txt:44-64` (read at source this session).
**Files changed:** `Assets/_Modules/Wallet/PackStore.cs`, `Assets/_Modules/Wallet/NightMarketComposition.cs`.
**Not touched:** `Assets/Editor/UICaptureLaunch.cs`, `Assets/_Modules/Core/UI/LayoutOracle.cs`,
`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs`, any regression file. No gate, no commit, no Unity run.
**Font floors:** none lowered anywhere. `ElarionUi.FontFloorMobile` (30) and
`ElarionUiKit.FontHardFloor` (`ElarionUiKitObsidian.cs:3044`, 20) are untouched. One autosize
**CEILING** came down (32 → 30) and that is stated below with its arithmetic.

### The four causes behind the 21 findings

| # | site (as it is NOW) | findings | measured cause | change |
|---|---|---|---|---|
| 1 | `PackStore.cs:2561-2637` (`BuildSpotlight`, the bar ledger) + `PackStore.cs:2656-2708` (`BuildLedgerRow`) | **15** | the ladder was authored as `rowH = .055f` re-gapped `+.010f`, i.e. a **.045 fraction** of the spotlight column → **32.75 ref px** on every captured ~2.22-aspect surface. A 30 pt line box does not seat in 32.8 px, so TMP's Ellipsis overflow **culled the whole line**: every printed figure drew **ZERO** glyphs. | ladder authored in **reference px** (WO-1623/1628 shape: named constants, y-anchors collapsed onto the ladder's edge, **pivot set before the offsets**), spending a stated budget. |
| 2 | `PackStore.cs:1133-1264` (`BuildHeader`, `_balanceLabel` at `:1243`) | **4** | the walletless banner (`StoreStrings.WalletlessBrowsingBanner`, `StoreStrings.cs:57`) is 36 printable glyphs in a **625 x 84 px** band fitted `FitSingleLine` at `[30..30]` — no shrink range, so TMP ellipsised the tail. | `FitSingleLine` → **`FitBlock`**. The band is 84 px (`TopBarPx(100) x (.92 − .08)`) and two line boxes at the floor are 78 px, so the sentence wraps instead of losing its last word. **Ruling-adjacent — see the flag below.** |
| 3 | `PackStore.cs:1994-2050` (`BuildUtilityRow`) + the new `NightMarketLayout` px constants at `PackStore.cs:172-207` | **1** | caption band was **.20-.92 of the button** = 302 px at 1280x720; "MONTHLY LEDGER" needs ~326 px with its ellipsis. | icon gets a **px slot** (14 px in, 52 px wide) and the caption a **px inset** (74 px left, 8 px right) → **337 px** on the same 419 px button. |
| 4 | `PackStore.cs:1002-1043` (the landscape `CommerceCta` seat), `PackStore.cs:2828-2841` (the gutter), `PackStore.cs:4832-4857` (`SeatCtaLabelInPx` at `:4849`) | **1** | the landscape CTA seat was **.30-.50 of the bottom band** (375 px at 1280x720) → button 335 px → label 308 px; "CONNECT WALLET" needs ~332 px. Compounding it, `gutterFrac` divided the 24 px gutter by `_plan.CommerceWidthPx` — **the rail, not the seat it is applied to**. | seat **authored in px BETWEEN its two neighbours** — right edge a 24 px keep-out clear of the legal copy, width `CommerceMinPx` (408) back from there, never crossing the Close; gutter now derived from the **real seat width**; caption re-seated at an **8 px pad** → **344 px**. |

### The 21 findings, one line each — every one now cleared by the change named above

`GlyphBaseline` keys are quoted verbatim from `Assets/Editor/UICaptureLaunch.cs:6191-6234` (read
read-only in the lead's main tree; that file is NOT on `dev` and was NOT edited by this lane).

| # | GlyphBaseline entry | cause | cleared by |
|---|---|---|---|
| 1 | `NightMarket_800x360\|…/TopBar/Text\|32 of 36` | 2 | FitBlock, 84 px band ≥ 76 px |
| 2 | `NightMarket_800x360\|…/Spotlight/ledger-wood/Text\|ZERO of 5` | 1 | 39 px px-authored row band |
| 3 | `NightMarket_800x360\|…/Spotlight/ledger-iron/Text\|ZERO of 5` | 1 | idem |
| 4 | `NightMarket_800x360\|…/Spotlight/ledger-crystals/Text\|ZERO of 3` | 1 | idem |
| 5 | `NightMarket_800x360\|…/Spotlight/ledger-stone/Text\|ZERO of 5` | 1 | idem |
| 6 | `NightMarket_800x360\|…/Spotlight/ledger-coins/Text\|ZERO of 3` | 1 | idem |
| 7 | `NightMarket_915x412\|…/TopBar/Text\|32 of 36` | 2 | FitBlock |
| 8 | `NightMarket_915x412\|…/Spotlight/ledger-wood/Text\|ZERO of 5` | 1 | 39 px row band |
| 9 | `NightMarket_915x412\|…/Spotlight/ledger-iron/Text\|ZERO of 5` | 1 | idem |
| 10 | `NightMarket_915x412\|…/Spotlight/ledger-crystals/Text\|ZERO of 3` | 1 | idem |
| 11 | `NightMarket_915x412\|…/Spotlight/ledger-stone/Text\|ZERO of 5` | 1 | idem |
| 12 | `NightMarket_915x412\|…/Spotlight/ledger-coins/Text\|ZERO of 3` | 1 | idem |
| 13 | `NightMarket_1280x720\|…/TopBar/Text\|28 of 36` | 2 | FitBlock |
| 14 | `NightMarket_1280x720\|…/utility-row-MONTHLY LEDGER/ObsBtn_MONTHLY LEDGER/Label\|12 of 13` | 3 | caption 302 → 337 px |
| 15 | `NightMarket_1280x720\|…/BottomBand/CommerceCta/ObsBtn_Connect Wallet/Label\|12 of 13` | 4 | caption 308 → 344 px |
| 16 | `NightMarket_2670x1200\|…/TopBar/Text\|32 of 36` | 2 | FitBlock |
| 17 | `NightMarket_2670x1200\|…/Spotlight/ledger-wood/Text\|ZERO of 5` | 1 | 39 px row band |
| 18 | `NightMarket_2670x1200\|…/Spotlight/ledger-iron/Text\|ZERO of 5` | 1 | idem |
| 19 | `NightMarket_2670x1200\|…/Spotlight/ledger-crystals/Text\|ZERO of 3` | 1 | idem |
| 20 | `NightMarket_2670x1200\|…/Spotlight/ledger-stone/Text\|ZERO of 5` | 1 | idem |
| 21 | `NightMarket_2670x1200\|…/Spotlight/ledger-coins/Text\|ZERO of 3` | 1 | idem |

### The band change, in px, per label (WO-1636 acceptance §4)

* **Five ledger figures x three surfaces** — row band **32.75 px → 39 px**
  (`NightMarketComposition.LedgerRowBandPx` = `ceil(LedgerFigureMaxPt(30) x LedgerRowLineFactorCeiling(1.30))`),
  pitch **40.1 px (drifting) → 39 px (fixed)**. The ladder is 5.1 px SHORTER than before — the 7.35 px
  of dead re-gap moved inside the band where the line box needs it, and the row count is now a floor
  over real px rather than an accident of the fraction.
  The budget on the column the run actually measured (**727.8 px**, back-solved from its own
  32.75 px band over the retired .045 fraction — not a rounded 729, because the row count is a
  `floor` and a rounded numerator is how a five-row ladder quietly becomes four):
  `available = .395 x 727.8 = 287.5`, `rows = floor(287.5 / 39) = 7 → 5 drawn`, `ladder = 195`,
  `left = 92.5`, the sentence wants `LedgerComparisonGapPx(8) + LedgerComparisonReservePx(78) = 86`,
  **slack 6.5 px**. At 1280x720 (~848 px column) the slack is ~140 px.
* **TopBar banner x four surfaces** — band **unchanged at 625 x 84 px**; the FIT changed
  (NoWrap+Ellipsis → wrap+Truncate). Two line boxes at the floor = **78 px** ≤ 84, and a `FlowTrace.Warn`
  fires if that ever inverts.
* **MONTHLY LEDGER caption** — **302 px → 337 px** (+11.6%) on the 419 px button measured at
  1280x720. Requirement ~326 px. **Margin ~3.4%.**
* **CONNECT WALLET caption** — **308 px → 344 px** (+11.7%); the button itself **335 px → 360 px**
  (canon) because its seat is now **408 px** rather than 20% of the bottom band. Requirement ~332 px.
  **Margin ~3.6%.**
  Seat arithmetic at 1280x720 (band ≈ 1875 px): legal copy starts at `.51 x 1875 = 956`, the seat's
  right edge is pinned at `956 − 24 = 932`, its left at `932 − 408 = 524`, and the Close ends at
  `.15 x 1875 + 180 = 461` — **63 px** of clearance on the left, **24 px** on the right. On a 4:3
  tablet (band ≈ 1627) the same derivation yields a 358 px seat and a `FlowTrace.Warn`, where a
  fixed 408 px seat centred on the old .40 would have **overlapped the legal copy by ~25 px** —
  i.e. traded one glyph finding for a LayoutOracle Assert B overlap.

### The autosize ceiling that came down, and why it is not a floor change

`BuildLedgerRow` fitted the printed figure `FitSingleLine(…, FontFloorMobile, 32f)`. TMP computes its
line box at **fontSizeMax** while auto-sizing, so a `[30..32]` band asks for a **32 pt** line box even
on the surfaces where it renders at 30 — 2.5 px of band the ladder does not have. The ceiling is now
`NightMarketComposition.LedgerFigureMaxPt` = `ElarionUi.FontFloorMobile` (30). **The floor is
untouched**; only unused growth headroom was removed. Stated because WO-1636 §2 forbids the opposite.

### Instrumentation added (§12 — permanent, never stripped)

* `FlowTrace.Step("Store", "WO-1636 ledger ladder: …")` — prints the measured column height, the
  band, the row count x pitch and the comparison reserve on every spotlight build. This is what a
  future reader reads instead of re-deriving the budget.
* `FlowTrace.Warn` in `BuildLedgerRow` — measures the figure's **real** `preferredHeight` against the
  authored band and names `LedgerRowLineFactorCeiling` if the font face reads wider than 1.30.
* `FlowTrace.Step` + `FlowTrace.Warn` on the `CommerceCta` seat — prints the band width, the seat's
  two edges and both neighbours' edges, and warns when the three tenants are oversubscribed.
* `FlowTrace.Warn` in `BuildHeader` when the header band drops below two line boxes.
* `FlowTrace.Warn` when a ledger row or the comparison sentence is dropped for want of px — the
  short-column case is now a logged finding instead of an invisible row.
* `FlowTrace.Step` in `BuildHeader` naming the 84 px band against the 76 px two-line requirement.

### ⚠ Flags for the lead / the owner — named, not slipped through

1. **RULING-ADJACENT (needs the owner's eye, does not block).** WO-1334b's comment at
   `PackStore.cs:1180-1243` reads *"ONE LINE, TOP LEFT, ON ITS OWN READABLE GROUND"*. Its three named
   instructions are all intact — the rect (x .018-.315), the word "Balance", the plate — and the
   BALANCE sentence still renders on one line because it fits on one. Only the **walletless banner**,
   which that ruling never saw, now takes a second line rather than losing four characters. The band
   cannot be widened (the ruling pins it inside the left third and the wordmark art starts at x .25)
   and the copy is `StoreStrings.WalletlessBrowsingBanner`, probed by name in
   `NightMarketNoWalletRegression.cs:335` — re-voicing it is the owner's call. If she wants one line
   at any cost, her copy is the remaining lever.
2. **The two caption margins are ~3.4% / ~3.6% and only the next capture proves them.** Stated as
   arithmetic over the run's own measured rects, not as a claim that they pass.
3. **`GlyphAdvanceEm = 0.55f` (`NightMarketComposition.cs:189`) under-estimates ALL-CAPS bold by a
   lot.** Back-solved from finding `:58`: "CONNECT WALLET" fits 12 glyphs in 308 px at 30 pt, i.e.
   **~0.76 em**, not 0.55 — and that constant is what `SpotlightMinPx` and every breakpoint derive
   from. **Deliberately NOT changed by this lane** (it would re-derive `ThreeColumnMinBodyPx` and
   could flip 1280x720 into a different composition). Recorded as a finding for its own ticket.
4. **`CommerceMinPx` derives from the canon Buy control alone** and has never accounted for the
   utility-rail captions that share the rail. If finding #14 survives the next capture, that is the
   honest next lever — not the caption's floor.
5. **`LedgerLabelFrac`/`LedgerBarFrac`/`LedgerNumberFrac` (.26/.42/.30) already disagree with what
   `BuildLedgerRow` authors** (icon 0-.11, key .13-.66, figure .66-1.0), and the file's own comment
   says they must match. Pre-existing, width-axis, NOT this lane's 21 findings — left alone and
   flagged.
6. **The bottom band is genuinely oversubscribed on a 4:3 aspect, and the code now SAYS so instead of
   picking a victim.** Close(360) + a canon seat(408) + legal copy(.51-.995 = 789 px) = 1557 px into a
   1627 px band once the keep-outs are counted — it does not fit at 4:3, so the seat degrades to
   ~358 px and warns. 4:3 is in `NightMarketUiRegression.CompositionProbes` but NOT in the captured
   panel set, so **this is a stated finding, not a proven pass.** The structural answer is to lay all
   three tenants out in px across the band; that touches the Close and the legal copy and belongs to
   a layout ticket, not to a glyph-fit lane.
7. **The 1.30 line factor is derived from an ABSENCE and that is worth naming.** The run flagged no
   ledger row at 1280x720, where the retired fraction resolves to a 38.2 px band — so the shipped
   face's 30 pt line box is ≤ 38.2 px (factor ≤ 1.272). That is real evidence, but it is evidence
   about the face the CAPTURE used; a font swap re-opens it, which is exactly what the
   `BuildLedgerRow` warn exists to catch.
8. **Duplicate child name.** Both children of `ledger-<key>` are called `Text` (`MakeText`), so the
   oracle path `…/ledger-wood/Text` is ambiguous by name. Renaming would move the baseline keys, so
   it was deliberately **not** done. Noted for whoever next edits that list.

### What the next capture must show

* `UI_GLYPH_OK` with `baselined=` **reduced by exactly 21**, and **all 21 rows above removable** from
  `GlyphBaseline` (`UICaptureLaunch.cs:6191-6234`) — entries 1-21 in the table, i.e. every
  `NightMarket_*` row in that list. No NightMarket row survives.
* `UI_GLYPH_FAIL` on nothing new — in particular **no `Truncate/Normal` finding on the spotlight
  comparison sentence** (its band is now an explicit 2-line reserve) and **none on the TopBar text**
  (now a wrapping block).
* ⛔ **THE ABSENCES, WHICH ARE THE HALF `baselined= −21` CANNOT PROVE.** The oracle reads labels that
  EXIST; it cannot tell a fixed label from one that was never built, so a ledger row dropped for want
  of px would clear its baseline entry while the granted good vanished from the money screen. On the
  fresh log, all four NightMarket surfaces must show:
  * `[Flow:Store] WO-1636 ledger ladder: … 5 row(s) x 39px = 195px …` — **five**, not four;
  * **no** `granted good(s) are NOT drawn`;
  * **no** `the comparison sentence is DROPPED`;
  * **no** `LedgerRowLineFactorCeiling is too low` (that warn means 1.30 is short for the shipped
    face and the CEILING, never the floor, is what moves);
  * **no** `CommerceCta seat: the bottom band is …px, which leaves …` on any 16:9 / ~2.22 surface
    (it is expected only on a 4:3 aspect, which is not in the captured set);
  * **no** `BuildHeader: the header band is …px and two line boxes need …`.
* The PNGs opened: `NightMarket_800x360`, `NightMarket_915x412`, `NightMarket_1280x720`,
  `NightMarket_2670x1200`. The owner is colourblind and reads words — the five ledger figures must
  be **visible numbers**, the banner must end in "USD", and both buttons must read their full
  captions.

### ⛔ Compile-gate RED on the first hand-back, and what it was (Builds/wave3-compile6)

```
Assets\_Modules\Wallet\PackStore.cs(2562,45): error CS1061:
  'Transform' does not contain a definition for 'rect'
Assets\_Modules\Wallet\PackStore.cs(2563,34): same
```

**Cause, stated plainly: I asserted a type I never opened.** The ledger budget measures the spotlight
column with `_spotlightHost.rect.height` — but `_spotlightHost` is declared **`Transform`**
(`PackStore.cs:308`), not `RectTransform`, even though `NightMarketComposition.Compose` hands it a
`RectTransform`. Every OTHER host on this screen (`_screen`, `_topBar`, `_bodyHost`, `_bottomBand`,
`_marketHost`, `_commerceHost`, `_ctaHost`, `:296-302`) IS a `RectTransform`, and I generalised from
its neighbours instead of reading the one line that mattered — the exact CLAUDE.md §11B failure this
ticket's own findings are made of.

**Fix — `PackStore.cs:2561-2570`:** cast once with a null guard, the idiom the file already uses at
`SeatCloseInBottomBand` (`PackStore.cs:1323`, `close.transform as RectTransform`), and keep
`_plan.SpotlightHeightPx` as the honest fallback:

```csharp
var spotlightRt = _spotlightHost as RectTransform;
float columnPx = spotlightRt != null && spotlightRt.rect.height > 1f
    ? spotlightRt.rect.height
    : Mathf.Max(1f, _plan.SpotlightHeightPx);
```

**Swept for the same mistake across both files, receiver by receiver** — every other `.rect` /
`.rectTransform` / anchor-pivot-offset write this lane added has a receiver whose declared type is
already right: `_bottomBand` and `_ctaHost` are `RectTransform` (`:299`, `:302`); `textRect`,
`crt`, and the `SeatCtaLabelInPx` rect all come from `TMP_Text.rectTransform`; `BuildLedgerRow`'s
`rt` is `go.GetComponent<RectTransform>()`; `Region(...)` returns `RectTransform`. The other 18
`_spotlightHost` call sites all take a `Transform` parameter and are untouched. The gate reported
exactly two errors and both were this one line — nothing else in the patch relied on an implicit
`RectTransform`.

### Gate evidence for this lane (edit-only, as instructed)

```
GATE_BRACE_SUMMARY bad=0 of 2
Assets/_Modules/Wallet/PackStore.cs              no-NUL  316291 bytes
Assets/_Modules/Wallet/NightMarketComposition.cs no-NUL   35101 bytes
```

No Unity run, no compile gate, no regression run, no commit — those belong to the lead's single
batch gate over the combined tree.


## SUB-LANE RumorBoard 2026-09-10

**Scope:** the 21 `RumorBoard_*` + `RumorBoard_page2_*` findings (WO-1636 sec.3 lane A).
**Worktree:** `D:\EoA\.claude\worktrees\agent-a3019a35d333a1c8e`
**Base:** `3da5e5360` (fast-forwarded from `f5d39acd1`; `git status --short` clean before any edit)
**Stage:** IMPLEMENTED - **EDIT ONLY. No Unity run, no gate, no commit, no capture.** Every number
below is arithmetic over constants read at source this session, or a simulation over shipped data
files; nothing here is a claim about a run that did not happen.

---

### 1. The two causes, each named by the measurement rather than by a code read

The 21 findings are two defects, not twenty-one. Both were read off
`Builds/wave3-capture2.glyph-findings.txt:23-43` (the run's own `TEXT TRUNCATED` lines, with the
band rects in them), never inferred from the source.

| # | labels | the finding, verbatim from the run |
|---|---|---|
| A | **18** `.../Body/PosterHook` | `overflow=Ellipsis wrap=NoWrap`; worst case `"Carry the sealed ledger past the flooded stai..."` **27 of 62** printable glyphs, rect `x -214.1..237.1` = **451.2 ref px wide**, font 30 `[30..32]` (`:25`) |
| B | **3** `.../RewardRow/RewardChip_Word/Fill/Label` | `"A found item"` **4 of 10** printable glyphs, rect `x -448.4..-375.4` = **73.0 ref px wide**, font 24 `[24..32]` (`:24`) |

**The geometry model used below is verified against that run, not assumed.** Replaying
`RumorBoardLayoutRegression.Measure` (`:170-185`) over the View's own constants predicts the hook
label at **452.7 / 499.8 / 506.5** ref px for 1920x1080 / 2340x1080 / 2670x1200; the run measured
**451.2 / 498.4 / 505.1**. Agreement to 1.5 px at all three aspects is what licenses the rest.

#### Cause A - the hook's ONE-LINE budget, against a 72-character cut

- The band: `RumorBoardPanel.HookBandPx = 46f` (`:179` at HEAD) - "ONE FontMicro(32) line box (40)
  plus slack" - with `hookLabel.textWrappingMode = NoWrap` and
  `ElarionUiKit.FitSingleLine(hookLabel, FontFloorMobile, FontMicro)` (`:748` and `:752` at HEAD).
- The copy: `RumorBoardVM.HookMaxChars = 72` (`:180` at HEAD), a character count with no relation to
  any band.
- **The widest advance the run itself measured is 15.04 px per character** - `"Done: Clear 3 waves at
  the western gate."` drew 24 printable glyphs, which is 30 characters of that string, inside 451.2 px
  (`glyph-findings:23`). So ONE line of the NARROWEST hook band holds **~30 characters** against a
  **72**-character cut. `FitSingleLine` sets `Ellipsis` overflow (`ElarionUiKitObsidian.cs:3065`), so
  TMP ate the tail. Nothing was wrong with the layout pass or with the VM's word-boundary cut.
- **WIDTH CANNOT BE THE FIX HERE, and that is why this lane deviates from the brief - stated in
  advance, per CLAUDE.md sec.11B-B.** The lead's brief and WO-1636 sec.2 both prescribe
  "band in reference px through the kit (FitSingleLine / the WO-1623/1628 shape)" for the
  `Ellipsis/NoWrap` family. The hook band's width is `CardW * (1 - 2*CardSideFrac)` and `CardW` is one
  of three owner-approved poster columns (`Poster1XMin..Poster3XMax`, `:138-143`). Widening it means
  fewer than three posters. So this lane applies the WO-1623/1628 *method* - **author the band in
  reference px, sized from the measured requirement** - to the band's **HEIGHT** instead, which is the
  same shape lane C is told to use for the `Truncate/Normal` family. The label converts to a two-line
  block accordingly.

#### Cause B - the word chip asked the layout group for nothing

`MakeWordChip` set `le.preferredWidth = 0f` (`RumorBoardPanel.cs:1019` at HEAD) on a
`HorizontalLayoutGroup` child. The currency chips in the SAME row **do** claim a width:
`ElarionUiKit.CurrencyChipHandle.SyncPreferredWidth` (`ElarionUiKitObsidian.cs:836-845`) writes
`le.preferredWidth` from the amount's `GetPreferredValues`, and it runs on every `WriteAmount`, which
`MakeCurrencyChip`'s `handle.SetAmount(chip.Amount, animate: false)` (`RumorBoardPanel.cs:986` at HEAD)
triggers. The word chip was therefore the only child of that group asking for nothing, and it got
what was left - **73 px**, four glyphs of "A found item". The chip's own doc comment claimed it was
"sized from its label's MEASURED width"; the line under it said `0f`.

---

### 2. What changed, and the arithmetic that says it fits

**No font floor was lowered anywhere.** `ElarionUiKit.FontHardFloor` (`ElarionUiKitObsidian.cs:3044`)
and `ElarionUi.FontFloorMobile` (`ElarionUi.cs:123`) are untouched, and no call site passes a smaller
`minSize` than it did at HEAD.

#### `Assets/_Modules/Village/Hero/RumorBoardPanel.cs`

| line (as it is now) | change | why, in px |
|---|---|---|
| `:174-181` | **new** `public const float TitleFontMaxPx = 40f;` | the ceiling the View already handed `FitBlock` at HEAD (`FitBlock(titleLabel, FontFloorMobile, 40f)`), promoted to a const so the band and the regression budget against the size that DRAWS |
| `:183` | `TitleBandPx` **130 -> 104** | 2 x `TitleFontMaxPx`(40) line boxes = 100, + 4 slack. **This is where the hook's second line is funded from.** The old 130 was documented as "TWO FontBody(50) line boxes (2 x 62.5 = 125) plus slack" while the fit call capped the title at 40 - the band reserved **25 px for a size the title can never reach**, and `RumorBoardLayoutRegression` pinned it against `FontBody` for the same reason. Nothing the player sees gets smaller: the title's font ceiling is unchanged |
| `:194` | `HookBandPx` **46 -> 82** | 2 x `FontMicro`(32) line boxes = 80, + 2 slack (the WO-1628 "+2.4 px headroom" idiom) |
| `:209` | `AcceptBandPx` **120 -> `ElarionUiKit.MinTouchPx`** (112) | the floor READ from its one owner (the WO-1623 idiom). Spends the 8 px of margin this face carried over the touch floor; the mockup's 140 screen px is ~113 ref px at 2670x1200, so 112 is still the mockup's number to within a pixel |
| `:215` | `RewardBandPx` **60 -> `ChipHeightPx`** (52) | the band is authored AS the chip it must seat; `ChipHeightPx` (`:244`) is promoted `private -> public` so the regression can pin that |
| `:781` + `:785` | hook: `NoWrap` -> `TextWrappingModes.Normal`, `FitSingleLine` -> **`FitBlock`** | a two-line band with a no-wrap label is still one line; the mode has to move with the band |
| `:764` | title `FitBlock` max `40f` -> `TitleFontMaxPx` | removes the literal that disagreed with the band |
| `:1052-1069` | word chip: `le.preferredWidth = 0f` -> **measured** | `MeasureLineWidthPx(FontRole.Body, display, FontMicro) + 2 * ChipPadPx` (`:1064-1067`), with the `PageButtonWidthPx` fallback idiom (`:561-569`) when no font resolves. `minWidth` stays **0** on purpose - a crowded row must shrink these chips proportionally INSIDE the card, never overflow it |

**Derived constants move with them** (all `public const`, all read by the regression):
`HookTopPx` 230 -> **204**, `ReadTopPx` 288 -> **298**, `RewardBottomPx` 158 -> **150**,
`RuleBottomPx` 234 -> **218**, `PosterStackTopPx` 400 -> **410**, `PosterStackBottomPx` 236 -> **220**,
`PosterMinHeightPx` 660 -> **654**.

**`PosterYMin` / `PosterYMax` are deliberately NOT touched, and the reason is a measured constraint,
not caution.** Growing the poster band upward was the obvious way to fund the hook, and it FAILS
`Case3_HeadRow`'s gutter assert: `gutter = (HeadTopPx - CanonCtaHeight) - posterTop` must clear
`TypeTagOverhangPx + ClearancePx` = 22 px, and at 2670x1200 the gutter is only **30.2 px** today.
Every `PosterYMax` I tried (0.775 / 0.780 / 0.785 / 0.790) failed that assert at two of three aspects.
So the whole fix is funded from **inside** the stack, and the card, the columns, the head row and the
status band all keep their exact HEAD geometry.

**The stack, replayed at all three landscape capture aspects** (the same arithmetic as
`Case1_PosterStack` `:257-278`, `Case2_ZeroOverlap` and `Case3_HeadRow`'s gutter):

| aspect | CardH | PosterMinHeightPx | slack | card bottom (>= 46) | gutter (>= 22) | band overlap | band outside card |
|---|---|---|---|---|---|---|---|
| 1920x1080 | 738.7 | 654 | **+84.7** | 68.0 | 49.4 | none | none |
| 2340x1080 | 669.1 | 654 | **+15.1** | 61.6 | 32.4 | none | none |
| 2670x1200 | 660.3 | 654 | **+6.3** | 60.8 | 30.2 | none | none |

At HEAD the same table read `+78.7 / +9.1 / **+0.3**`. **The card ends up with MORE slack than it has
today at every aspect**, because the title band was returning 26 px that the hook only needed 36 of.

#### `Assets/_Modules/Village/Hero/RumorBoardVM.cs`

`HookMaxChars` **72 -> 52** (`:202`), with the derivation written into its doc.

**52 is simulated over the shipped copy, not chosen.** Greedy word-wrap at **30 characters per line**
(451.2 px / the 15.04 px-per-char worst measured advance) over **63** hook sources - every
`objectiveText` / `letter` / `description` in `Assets/Resources/Data/Canonical/quests.json` +
`daily-quests.json` - plus the five capture fixtures, through a Python port of `OneLineHook`
(`:341-367`):

| cap | strings that still need a THIRD line |
|---|---|
| 72 (HEAD) | **39** |
| 60 | 25 |
| 58 | 11 |
| 56 | 4 |
| 54 | 1 |
| **52** | **0** |
| 50 | 0 |

52 is the **largest** cap at which nothing overflows two lines. Longest resulting hook: **54**
characters (52 + the VM's `"..."`).

**The cut does not cascade to another screen.**
`grep -rn "OneLineHook\|\.HookFor(\|\.ObjectiveFor(" --include=*.cs Assets/` minus the three rumor-board
files returns **nothing** - no other surface consumes this hook, so no caption outside this board moves.

**THE COPY CUT IS REAL AND IT IS DECLARED** (the brief's "never shorten player copy without saying
so"): **48 of the 63** hooks lose words they kept at 72. Two things make it the right trade and both
are measured, not asserted: **39 of those 48 were being ellipsized past two lines at the old cap
anyway**, so most of what the cut removes is text no player could read; and the FULL letter is one tap
away behind "Read the letter >", which is what `RumorBoardPanel`'s own header calls the reason the
board never shows dense copy. Net for the player: the hook goes from **~30 readable characters ending
in a TMP ellipsis** to **up to 54 readable characters ending on a whole word**.

#### `Assets/Editor/Regression/RumorBoardLayoutRegression.cs` - re-pointed WITH the numbers

- `Case1_PosterStack`: `titleBand >= 2 * FontBody-line` -> `>= 2 * TitleFontMaxPx-line`, reading the
  View's new const (**the stale budget that funded this fix - it named a font the title never renders
  at**); `hookBand >= microLine` -> **`>= 2 * microLine`**, with the WO-1636 measurement in the
  failure text; **new** assert `rewardBand >= ChipHeightPx`.
- `Case4_SourceLaws`: **three new** one-token-revert guards - the hook back to `NoWrap`, the hook off
  `FitBlock`, and the word chip back to `le.preferredWidth = 0f`. The band budgets catch a band that
  shrinks; only these catch a band that keeps its height while the label refuses to use it.
- Header block: a WO-1636 note recording that the re-point IS the finding (a budget naming a number
  the drawing code does not use is duplicated state, and it fails silently toward "looks fine").

#### `Assets/Tests/EditMode/RumorBoardVMTests.cs`

`the_hook_is_one_line_and_never_ends_mid_word` -> `the_hook_is_one_sentence_and_never_ends_mid_word`,
with the reason in the body. **No assertion changed**: `:348` already reads
`RumorBoardVM.HookMaxChars + 3` off the const, and both literal expectations still hold at 52
("The shelves have begun to sing at dusk." = 39 chars; "Speak to Brom at the market." = 28 - both
under the new cap, so both still take the first-sentence path).

---

### 3. Hygiene (run in this worktree, this session)

```
python tools/gate_brace.py <the 4 files>   ->  GATE_BRACE_SUMMARY bad=0 of 4   (exit 0)
NUL bytes                                  ->  0 in all four
non-ASCII                                  ->  0 in RumorBoardPanel.cs and RumorBoardVM.cs
                                               (Case4_SourceLaws pins both files ASCII)
raw braces                                 ->  98/98, 46/46, 58/58, 68/68
```

### 4. Files changed (4)

```
Assets/_Modules/Village/Hero/RumorBoardPanel.cs
Assets/_Modules/Village/Hero/RumorBoardVM.cs
Assets/Editor/Regression/RumorBoardLayoutRegression.cs
Assets/Tests/EditMode/RumorBoardVMTests.cs
```

**Deliberately NOT touched:** `Assets/Editor/UICaptureLaunch.cs` (the `GlyphBaseline` list and Assert
C routing - WO-1630 owns them and the lead deletes the entries at the gate), `LayoutOracle.cs`,
`UiTouchClampRegression.cs`, every Night Market / Realm / Journey / Manage / HeroSelect file, and
every other lane's regression.

### 5. The 21 GlyphBaseline entries this lane expects to make removable

**These are EXPECTED, not proved.** This lane cannot run Unity, so no capture was taken. The lead
confirms by re-running the capture and reading `UI_GLYPH_OK` with `baselined=` down by exactly **21**
and `UI_GLYPH_FAIL` on nothing new - the marker on a fresh log, never an exit code.

| panel build | entries | which labels |
|---|---|---|
| `RumorBoard_1920x1080` | 4 | 3 x `PosterHook` + 1 x `RewardChip_Word` |
| `RumorBoard_2340x1080` | 4 | 3 x `PosterHook` + 1 x `RewardChip_Word` |
| `RumorBoard_2670x1200` | 4 | 3 x `PosterHook` + 1 x `RewardChip_Word` |
| `RumorBoard_page2_1920x1080` | 3 | 3 x `PosterHook` |
| `RumorBoard_page2_2340x1080` | 3 | 3 x `PosterHook` |
| `RumorBoard_page2_2670x1200` | 3 | 3 x `PosterHook` |
| **total** | **21** | 18 hooks + 3 word chips |

### 6. What this lane has NOT proved, and the cheapest way to close each

1. **That the hooks stop ellipsizing on a real capture.** The wrap simulation uses a uniform 15.04
   px/char - the WIDEST advance the run measured - not TMP's per-glyph advances. It is conservative by
   construction (real proportional text averages narrower), but it is a model. Closed by: a fresh
   `RunCaptureHeadless`, `UI_GLYPH_OK baselined=` down 21.
2. **That "A found item" now fits.** `MeasureLineWidthPx` is a build-time measurement, but the
   `HorizontalLayoutGroup`'s min/preferred/flexible allocation across a mixed row of currency and word
   chips is Unity's, and it was not run. Closed by the same capture, and by eyes on the poster PNGs -
   the owner is colourblind and reads words, so a glyph count is not the whole gate here.
3. **That the card still looks right.** Three bands moved. `PosterMinHeightPx` says it FITS at every
   aspect with more slack than HEAD, which is a budget, not a composition judgement. Closed by opening
   `RumorBoard_1920x1080.png` / `_2340x1080.png` / `_2670x1200.png`.
4. **That `RUMOR_BOARD_LAYOUT_OK` still comes up green.** The re-pointed asserts were verified by
   replaying their arithmetic in Python against the new constants, not by running the suite.


## SUB-LANE DeckCardPurpose 2026-09-10

**Scope:** the 16 `overflow=Truncate wrap=Normal` findings on the shared `DeckCardPurpose_*` label —
12 on `RealmWorkspace`, 4 on `JourneyWorkspace`. One authoring site, one fix.

**Status of this section:** EDIT ONLY — no Unity, no gate, no commit, no `**Status:**` flip on the WO.

### 1. The authoring site

`Assets/_Modules/HUD/PlayerDeckWorkspace.cs:348-384` — `BuildCard`, the single
`ElarionUiKit.Label(...)` that names itself `"DeckCardPurpose_" + spec.Title` (`:355`). It is the
whole family: `grep -rn "DeckCardPurpose" --include=*.cs Assets/` returns exactly that one line.

### 2. What the captured data proves (not inferred)

From `Builds/wave3-capture2.glyph-findings.txt:72-81` and `Builds/wave3-navcapture.glyph-findings.txt:1-6`,
the label was authored as the y band **0.26–0.52 of the card** — a share of a rect whose reference
height changes with the aspect. Resolved, per captured aspect (card height = measured band ÷ 0.26):

| aspect | card height | purpose band | band width | e.g. "Review non-expiring monthly progress" |
|---|---|---|---|---|
| 1920x1080 | 202.7 ref px | **52.7 px** | 329.6 px | 20 of 33 glyphs |
| 2340x1080 | 176.5 ref px | **45.9 px** | 365.3 px | 23 of 33 glyphs |
| 2670x1200 | 173.1 ref px | **45.0 px** | 370.4 px | 23 of 33 glyphs |

Every finding drops a whole SECOND LINE, never a clipped word. **Width was never implicated** — the
aspect that keeps the MOST glyphs also has the WIDEST band, so x stays a fraction and only y changes.

**Line count per string:** the oracle's own numbers give 20–24 glyphs on line 1 at font 30 in a
329.6–370.4 px band. The longest live purpose string is 33 printable glyphs ("Review non-expiring
monthly progress"); the rest are 21–30. So every string is **2 lines at every captured aspect** —
never 3, which is what makes a two-line band the correct target rather than a lucky one.

**The two-line requirement, measured at the font asset (not estimated):**
`Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset:66-73` declares `m_PointSize 64`,
`m_LineHeight 73.59375`, `m_AscentLine 57.9375`, `m_DescentLine -13.5625`. TMP stacks N lines as
`(N-1)·lineHeight + (ascent − descent)`, i.e. `(73.59375 + 71.5) / 64 = 2.26709` per point →
**68.01 px at font 30**, 77.08 px at 34.
**Corroborated independently:** WO-1628's step-1 probe *measured* `preferredHeight 47.6 px` for two
lines at fontSize 21 on all 21 of its lines; this model returns **47.61** for the same input. Two
derivations, one number. 45.0 px available vs 68.01 px required — that is the defect.

### 3. The fix — WO-1628's shape, verbatim

`Assets/_Modules/HUD/PlayerDeckWorkspace.cs`

- New constants at `:388-450`:
  - `PurposeTwoLineReqPx = 68.01f` — the measured requirement, with both derivations in its doc.
  - `PurposeBandPx = 70f` — the band HEIGHT in reference px (requirement + ~2 px headroom, the same
    margin WO-1628 left: 50 authored over 47.6 measured). **Never a fraction of the card.**
  - `PurposeTopFrac = 0.52f` — the band's existing TOP edge, which it still hangs from.
- The `Label` call passes `PurposeTopFrac, PurposeTopFrac` (both y anchors collapsed onto that edge).
  After it returns: `pivot = (.5f, 1f)` **before** `offsetMax = Vector2.zero` /
  `offsetMin = (0f, -PurposeBandPx)`. X stays a fraction (`TextPlateX0(...)..0.96`).
- A `FlowTrace.Step("Navigation", …)` per card names the band px, the top fraction and the 68.0 px
  requirement, so the next capture carries the numbers instead of needing this document. It fires on
  a **deck open**, once per card — `BuildCard` is not on a frame path, so the 3-arg form is right and
  no `Measure` scope is warranted.

**Font untouched.** `ElarionUiKit.FitBlock(purpose, ElarionUi.FontFloorMobile, 34f)` is byte-identical
— it must stay equal to Manage's call (`HudLabelFitRegression` case 6c). Nothing goes near
`ElarionUiKit.FontHardFloor`. **No player copy was shortened.** Nothing above the band moves: the
title (.55–.90), the medallion (x .055–.245) and the art are all where they were; the band grows into
the card's empty bottom margin.

Resolved bottom edge: **0.175 / 0.123 / 0.116** at the three captured aspects — inside the card with
23.5 / 21.7 / 20.1 ref px of margin to spare.

#### The check that nearly sent this the wrong way — recorded because it is the reusable part

The band's second line lands on art, so I measured whether the dark text plate reaches it, replicating
`HudLabelFitRegression.MeasurePlate` + `Contrast` exactly (PNG bytes, `l ≥ 90/255`,
0.2126/0.7152/0.0722, sRGB→linear) across all 15 faces in `Assets/Resources/UI/ElarionMedieval/cards/`.

**Measured in raw PNG fractions, the plate appears to fall off a cliff below y 0.14** (research 1.5:1,
raids 1.2:1, monthly-ledger 1.1:1). That reading is **WRONG**, and acting on it would have moved this
band onto the bottom edge for no reason. The art is not drawn 1:1: `PlayerDeckWorkspace.MeasureArtFit`
(`:585-678`) derives each sprite's opaque bounds and `BuildCard` (`:229-236`) seats that region onto
the button with `preserveAspect = false`, so a PNG y-fraction `p` renders at button
`(p − fy0)/(fy1 − fy0)`. Most faces carry a real packaging margin (`buildings` 0.100–0.923,
`monthly-ledger` 0.077–0.901; `raids`/`game-guide` go through the authored `OpaqueMargins` table at
0.088–0.929 / 0.102–0.925). The "cliff" was the cropped-away margin.

**Re-measured in BUTTON space,** contrast for `ParchmentDim` copy over the plate's x 0.49–0.96, worst
face per band:

| button y band | worst contrast | face |
|---|---|---|
| 0.26–0.52 | 5.9 : 1 | equipment |
| 0.20–0.26 | 10.2 : 1 | bag |
| 0.14–0.20 | 10.2 : 1 | bag |
| **0.10–0.14** | **10.1 : 1** | quests |
| 0.058–0.10 | 9.0 : 1 | buildings |
| 0.000–0.058 | 2.3 : 1 | defense-report (the card's frame edge) |

The band's lowest bottom is 0.116, so it never reaches the only band that dips. **The top-edge shape
briefed for this lane is correct as briefed** — no deviation was needed, and none was taken.

### 4. Regression: re-pointed, with the numbers

`Assets/Editor/Regression/HudLabelFitRegression.cs`

- `:1555` **`PlateY0 0.20f → 0.06f`** (case 9b/9c, the raids-locked art probe). 0.20 was the old
  purpose bottom (0.26) minus a 0.06 margin; the new bottom is 0.116 at the narrowest aspect, so the
  same margin gives 0.06. Left at 0.20 the case would have gone on checking contrast **where the live
  copy no longer lands** — green while covering nothing.
  **Measured before changing it,** with the replica described above, over `raids-locked.png`:

  | plate | ink (ceiling 0.006) | mean luminance | Gold | ParchmentDim | Parchment |
  |---|---|---|---|---|---|
  | 0.20–0.86 (old) | 0.0017 | 0.0051 | 9.05 | 10.21 | 15.91 |
  | **0.06–0.86 (new)** | **0.0014** | 0.0048 | **9.10** | **10.25** | **15.98** |

  The replica reproduces the suite's own documented baseline (`PlateInkCeiling` doc: *"The delivered
  face measures 0.0017"*) exactly, which is what makes it a check and not an opinion. The enlarged
  region is *cleaner*, so this re-point does not buy coverage with a relaxed assert.
- **A pre-existing hole recorded, not fixed:** `MeasurePlate` samples in PNG fractions while the
  label's anchors are BUTTON fractions, and §3 shows those frames differ for any face with a
  packaging margin. Case 9 happens to be safe because `raids-locked.png`'s alpha bbox is
  (3,7)–(1410,736) of 1416x742 → `fy0..fy1 = 0.008..0.991`, so its two frames coincide within 1%.
  A face like `raids.png` (0.088–0.929) would not. Written into the const's doc; out of this lane's
  scope to fix.
- `:849-856` — the stale sentence *"The deck card's purpose band is 0.26-0.52 of its plate - taller
  still"* corrected in place with the measured 45–53 px vs 68.0 px, the old text kept so it is not
  restored. The fit-call parity 6c asserts is unchanged in force.
- **6c / 6d string pins verified by hand:** `FitBlock(purpose, ElarionUi.FontFloorMobile, 34f)` and
  `(int)ElarionUi.FontMicro, TextAlignmentOptions.Center` are untouched; nothing between
  `available ? spec.Purpose` and `FitBlock(purpose,` contains `TextOverflowModes.Ellipsis` or
  `enableWordWrapping = false`. 6e's Hero slice starts at `List<Card> CardsFor(` (`:761`), well after
  every line I touched.
- No numeric copies of the new constants were written into the regression: it cites `PurposeTopFrac` /
  `PurposeBandPx` / `PurposeTwoLineReqPx` by name (CLAUDE.md §8 — the copy is the bug).
- No other regression pins these anchors: `grep -rn "0\.26f|0\.52f" Assets/Editor/Regression/` returns
  only unrelated colour/vector literals; `JourneyDeckSubtitleRegression`,
  `JourneyDeckTwoCardRegression`, `RaidsDiscoverabilityRegression` and `DeckReturnDoorRegression`
  carry no purpose-band geometry.

### 5. The 16 `GlyphBaseline` entries this sub-lane makes removable

All 16 are `DeckCardPurpose_*`, `overflow=Truncate wrap=Normal`, font 30 [30..34]. I did **not** edit
`UICaptureLaunch.cs` (out of lane).

| # | panel @ resolution | card | glyphs |
|---|---|---|---|
| 1 | RealmWorkspace_1920x1080 | The Night Market | 21 / 30 |
| 2 | RealmWorkspace_1920x1080 | Defense Report | 20 / 28 |
| 3 | RealmWorkspace_1920x1080 | Monthly Ledger | 20 / 33 |
| 4 | RealmWorkspace_1920x1080 | Game Guide | 21 / 28 |
| 5 | RealmWorkspace_2340x1080 | The Night Market | 23 / 30 |
| 6 | RealmWorkspace_2340x1080 | Defense Report | 23 / 28 |
| 7 | RealmWorkspace_2340x1080 | Monthly Ledger | 23 / 33 |
| 8 | RealmWorkspace_2340x1080 | Game Guide | 23 / 28 |
| 9 | RealmWorkspace_2670x1200 | The Night Market | 24 / 30 |
| 10 | RealmWorkspace_2670x1200 | Defense Report | 23 / 28 |
| 11 | RealmWorkspace_2670x1200 (nav capture) | Monthly Ledger | 23 / 33 |
| 12 | RealmWorkspace_2670x1200 (nav capture) | Game Guide | 23 / 28 |
| 13 | JourneyWorkspace_1920x1080 (nav capture) | Quests | 20 / 21 |
| 14 | JourneyWorkspace_1920x1080 (nav capture) | Raids | 18 / 25 |
| 15 | JourneyWorkspace_2340x1080 (nav capture) | Raids | 20 / 25 |
| 16 | JourneyWorkspace_2670x1200 (nav capture) | Raids | 20 / 25 |

A fresh capture should show `baselined=` down by exactly 16 with no new `TEXT TRUNCATED` on any
`DeckCardPurpose_*`.

### 6. Verification done in this lane, and what is NOT proven

Done: `python tools/gate_brace.py` on both files → `GATE_BRACE_SUMMARY bad=0 of 2`; NUL byte count 0
on both; CRLF/BOM byte style confirmed to match the tree on both; the PNG measurements above; every
string pin re-read at source.

**Not proven from this seat (no Unity, edit-only):** that the compile gate passes, that
`HudLabelFitRegression` cases 6c/6d/9b/9c go green with `PlateY0 = 0.06`, and that a fresh glyph
capture clears all 16. Those need the lead's single gate run and one fresh capture — worth a glance at
the `RealmWorkspace_2670x1200` PNG, the narrowest card, where the band's bottom sits at 0.116.

**Files changed (2):**
- `Assets/_Modules/HUD/PlayerDeckWorkspace.cs`
- `Assets/Editor/Regression/HudLabelFitRegression.cs`


## SUB-LANE Remainder 2026-09-10

**Base:** `dev` @ `3da5e5360` (worktree `D:\EoA\.claude\worktrees\agent-a671dfc8ae916a90e`, clean
before and after the ff-merge). **EDIT ONLY** - no Unity run, no gate, no commit, no capture.

### 0. What this sub-lane took, and what it did not

Sub-lanes A (`RumorBoard`), B (`NightMarket`) and C's `DeckCardPurpose_*` family were taken by other
seats. This lane took **everything else in sec.5's table**: 9 findings across 4 panel families.

| # | panel build(s) | label | measured | taken |
|---|---|---|---|---|
| 1-5 | `HeroSelect_1080x1920` | `Col_Signature/Label`, `Col_Skills/Label` x4 | 7/10, 7/11, 8/10, 8/13, 8/13 | **YES - fixed** |
| 6-7 | `ManageWorkspace_2340x1080`, `_2670x1200` | `ManageCard_ARMY/Label` | 11 of 14 (both) | **YES - partial, ruling needed** |
| 8 | `BuildMenuUpgradeTower_1920x1080` | `ObsBtn_Not enough resources/Label` | 16 of 18 | **YES - fixed** |
| 9 | `EndStateWaveClear_repairAll_1920x1080` | `SpoilCell2/SpoilRow/Label` | 17 of 19 | **NO - report only, see sec.4** |

⛔ **DECLINED, and it is NOT this lane's:**
`RealmWorkspace_1920x1080 | .../DeckCard_The Night Market/Label | 12 of 14` (the WO's lane C
"1 is a single-line label"). It is a **PlayerDeck** builder, and PlayerDeck files were forbidden to
this seat. It belongs with the `DeckCardPurpose_*` lane, which already owns that file.

### 1. HeroSelect - 5 findings - FIXED

**File:** `Assets/_Modules/Onboarding/HeroSelectController.cs`
**Source sites as they are NOW (post-edit line numbers):**
* `DetailColumns` - `:250-256` (the measured re-proportion; the reasoning block runs `:205-249`)
* `BuildSpecsPanel` builds the four columns at `:864-871`; the flagged labels are
  `sigName` (`:895`, the `Col_Signature/Label`) and the skill-row name (`BuildSkillRow` call at
  `:928`, the label itself at `:1036`)
* the blurb whose band was traded for width - `:881`
* `BuildPipRow` key lane + pip run - `:970-996`

**Why it truncated - measured, not inferred.** The findings fire at **portrait 1080x1920 ONLY**;
`HeroSelect_1920x1080` and `HeroSelect_2670x1200` are clean on the same run. **The well is 932.0
reference px wide at portrait**, and that number is DERIVED FROM THE ORACLE'S OWN RECTS rather than
assumed: `Col_Signature/Label` at `0.6628..0.7972` of the well back-solves to `151.9..277.0` against
the reported `151.8..277.1`, and the skills name lane at `0.820 + 0.28*0.175 .. 0.820 + 0.98*0.175`
back-solves to `343.9..458.0` against the reported `344..458.3`. Two independent labels agreeing to a
tenth of a pixel is the corroboration. Against that well the old split handed SIGNATURE 130.5 px and
SKILLS 163.1 px - and both columns carry **proper nouns from `HeroCatalog`** that cannot be wrapped,
hyphenated or shortened (they are mirrored verbatim from `abilities.json`, and
`HeroKitMirrorRegression` pins that mirror). So the band moved.

**Band changes, in reference px at 1080x1920 (well = 932):**

| column | fraction was -> is | column px was -> is | the label lane that mattered |
|---|---|---|---|
| LORE | 0.295 -> 0.262 | 275.0 -> 244.2 | blurb 264.0 -> 234.4 px wide, band 0.80 -> **0.84** so 246 -> 258 px tall |
| STATS | 0.320 -> 0.262 | 298.2 -> 244.2 | pip key 0.28 -> **0.30** of the column, 83.4 -> 73.3 px |
| SIGNATURE | 0.140 -> **0.210** | 130.5 -> 195.7 | name label **125.3 -> 187.9 px (+50%)** |
| PRIMARY SKILLS | 0.175 -> **0.232** | 163.1 -> 216.2 | name lane **114.3 -> 165.4 px (+45%)** (`badgeX1` 0.22->0.17, `nameX0` 0.28->0.215; the badge plate keeps its 32.4 px) |

The three inter-column gaps go `0.020 -> 0.010` and the outer margins `0.005 -> 0.002`; that is where
the last ~19 px came from. The columns stay disjoint by construction.

**Sized for the WORST copy in the catalog, not the captured one.** SIGNATURE is sized for
`"Sacred Mending"` (Cleric, `HeroCatalog.cs:190`), not the captured `"Shield Bash"`; SKILLS for
`"Warden's Grace"` / `"Storm of Arrows"`.

⛔ **NO FONT FLOOR WAS LOWERED.** The signature name still floors at 25 and the skill names at 20 -
the sizes the oracle measured them resolving to - and nothing was routed to a lower kit floor.

**The two columns that gave up width were checked for a NEW truncation, because acceptance forbids
one.** LORE's longest string is `hero.ranger.blurb` (**168 chars**, read out of
`Assets/Resources/Data/Canonical/en.json`); at 234.4 px and the 20 px floor it needs ~9 wrapped lines
= ~216 px against the 258 px band. STATS' longest pip key is `"ATTACK"` (~66 px at its 16 px
`FitLine` floor) against a 73.3 px lane - the lane was widened `0.28 -> 0.30` and the pip run
tightened (`pipX0` 0.34->0.35, `pipW` 0.115->0.112, `pipGap` 0.015->0.013) **specifically so this fix
would not buy two clean columns by opening a third truncation**. Pips are images and cannot truncate,
which is why they were the right thing to trade.

⚠ **The LORE and STATS numbers are ESTIMATES and are labelled as such in-code.** The oracle logs only
the labels it FAILS, so this run carries no rect for the blurb or the pip keys; their widths were
sized from a glyph-advance estimate **calibrated against the two rects above**. The re-capture is the
proof, not this file.

**Pins checked:** no regression references `DetailColumns`, `Col_Signature` or `Col_Skills`.
`HeroSelectCarouselRegression` pins the CAROUSEL lanes and the kit's `FitSingleLine(tt)` arming -
neither is touched. `ArtResourceRegression`, `FoundingReachabilityRegression`,
`StarterLoadoutRegression`, `UiMvvmConformanceRegression` reference the controller for other reasons.

### 2. BuildMenuUpgradeTower - 1 finding - FIXED

**File:** `Assets/_Modules/Village/Buildings/UI/BuildMenuLayout.cs`
**Source sites as they are NOW:**
* the two fractions - `InfoWidthFrac` `:129`, `CtaLeftFrac` `:131` (reasoning block `:101-128`)
* the CTA that reads them - `BuildMenu.BuildUpgradeActionBand`,
  `Assets/_Modules/Village/Buildings/UI/BuildMenu.cs:691-695`
* the caption itself - `BuildMenuVM.UpgradeCtaLabelFor`,
  `Assets/_Modules/Village/Buildings/UI/BuildMenuVM.cs:571-572` ->
  `BuildModeController.ShortfallMessage`, `Assets/_Modules/Village/BuildMode/BuildModeController.cs:3531-3562`
* the info lines that move with it - `BuildMenu.AddInfoLines`, `BuildMenu.cs:381-393`

⛔ **THE CAPTURED CAPTION IS NOT THE LONGEST ONE, SO IT IS NOT THE ONE TO SIZE FOR.** The label is
`ShortfallMessage`, whose branches are `"Not enough Wood (N)"`, `"Not enough Iron (N)"`,
`"Not enough Stone (N)"`, `"Not enough Crystals (N)"` and, last, the generic `"Not enough resources"`
the capture happened to land on. Sizing to the shot would have shipped a band that still cuts the
refusal a player is most likely to read.

**Change:** `CtaLeftFrac 0.62 -> 0.525`, `InfoWidthFrac 0.58 -> 0.505` (the 0.02 clear-air gap is
preserved). Widths are pure ratios of one band, so the measured **418.2 px at a 0.38 lane** scales
linearly to a 0.475 lane = **~523 px of label (+25%)**:

| caption | needs (est.) | headroom after |
|---|---|---|
| `"NOT ENOUGH RESOURCES"` (the finding) | ~454 px | **+15%** |
| `"NOT ENOUGH CRYSTALS (220)"` (worst real) | ~510 px | +2% |

**Pins checked:** `BuildMenuLayoutRegression:273-278` asserts ONLY `InfoWidthFrac <= CtaLeftFrac` and
`CtaLeftFrac < 1` - both hold (`0.505 <= 0.525 < 1`). No regression pins either literal value.

⚠ **Risk to watch on the re-capture, named rather than glossed:** the info lines are `FitBlock` at
`ElarionUi.FontFloorMobile` in a 56 px half-band, so they are effectively single-line. The longest
(`UpgradeStatLineFor`, `"Lvl 1 to 2:  dmg 10 to 15,  range 8m to 10m"`) is **estimated** ~13% inside
the narrowed lane, but it has no rect on this run. If it turns red, the width came out of the wrong
column and the CTA lane is the thing to re-derive - never the font floor.

### 3. ManageWorkspace ARMY card - 2 findings - PARTIAL, needs a ruling

**File:** `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs`
**Source sites as they are NOW:**
* the face label rect + `FitSingleLine(face, 30f, 40f)` - `:2294-2295` and `:2300`
  (reasoning block `:2274-2293`)
* the caption `"BUILD A BARRACKS"` - `RenderLauncherCards`, `:2176`
* the cell that constrains it - `cellW = Mathf.Min(width / 3f, height * HubCardAspect)`, `:2101`;
  `HubCardAspect = 132f / 169f`, `:503`

**Change made:** the face label's side inset `0.04/0.96 -> 0.02/0.98` (0.92 -> 0.96 of the card).
`288.4 / 0.92` back-solves the CELL to **313.5 px**, so this buys the face **+12.6 px**, to ~301 px.

⛔ **AND IT DOES NOT CLOSE THE FINDING. Saying so is the point.** A glyph-advance estimate calibrated
against that same rect puts `"BUILD A BARRACKS"` at **~324 px** at font 30 bold - still ~7% over.
**Both remaining levers are pinned, so this needs an owner ruling, not another nudge:**
1. **The WORDS** are pinned by `Assets/Editor/Regression/ManageApprovedLauncherRegression.cs:71-72`
   (WO-1406: the locked card's face IS the door).
2. **The CELL WIDTH** is pinned by `HubCardAspect` (132/169), measured off the owner's own device
   frame. `cellW = Min(width/3, height * HubCardAspect)` is **already taking the aspect branch here**
   (313.5 = 401 x 0.781), so widening the band cannot help - the cell is HEIGHT-clamped.

**The cheapest ruling on offer:** drop one word. `"BUILD BARRACKS"` measures **~293 px** and fits
inside the new lane with room. **NOT taken by this lane** - it is player-facing copy behind an owner
ruling, and taking it would also mean re-pointing WO-1406's pin.

**GlyphBaseline:** these two entries **STAY** until that ruling lands.

### 4. EndStateWaveClear_repairAll - 1 finding - REPORT ONLY, the fixture is the defect

**No edit made.** The fix is in `Assets/Editor/UICaptureLaunch.cs`, which this lane is forbidden to
touch and which WO-1636 reserves for `GlyphBaseline` deletions only.

**PROVEN, at source, this session - the capture fixture measures a RETIRED grammar:**

| | LABEL | AMOUNT | `Wide` |
|---|---|---|---|
| the fixture, `UICaptureLaunch.BuildWaveClearFixture`, **`:5452`** | `"North Gate"` | `"DESTROYED, looted 120"` | **unset (false)** |
| the LIVE producer, `EndStateVM.FromWaveClear`, **`:806-823`** | `"{e.Name} - {state}"` (the whole sentence) | `"Rebuild "/"Repair " + materials` | **`true` (`:822`)** |

The fixture's own docstring (`UICaptureLaunch.cs:5427-5429`) claims it *"mirrors EndStateVM.FromWaveClear's
own output"*. It does not, and it has not since the 2026-09-02 wide-row work. **Every live producer
that puts prose in `Amount` sets `Wide = true`** - verified by reading all 11 `new SpoilRowVM` sites
in `EndStateVM.cs` (`Wide = true` at `:780`, `:822`, `:1015`; every other row is a short noun plus a
short number). So the fixture is the ONLY place in the repo where a non-Wide row carries a sentence.

**Consequence:** the captured row goes down `BuildSpoilRow`'s **non-wide** branch - the fixed
`0.62/0.64` split at `EndStateView.cs:2216-2218` - which is precisely the pre-2026-09-02 defect shape
the wide-row solve was built to retire. Nothing that ships renders this.

⚠ **AND RE-POINTING THE FIXTURE WILL PROBABLY REVEAL A REAL TRUNCATION RATHER THAN REMOVE ONE.** The
live shape is a ~34-char sentence against a ~25-char cost in a ~522 px text region, and
`SolveWideRowFontPx` (`EndStateView.cs:2066-2098`) already clamps at `ElarionUiKit.FontFloor` and
**prints its own warning that a wide row may still ellipsise at the floor**. **Recommendation:
re-point the fixture FIRST, re-capture, and only then decide whether `EndStateView` needs anything.**
No `EndStateView` change should be made off this run's number.

**GlyphBaseline:** this entry **STAYS**.

### 5. GlyphBaseline entries that become removable

Line numbers are from the **main tree** `D:\EoA\Assets\Editor\UICaptureLaunch.cs` (the array is not
on `dev` at `3da5e5360`, so this lane could only read it, never edit it). The lead deletes these in
the same commit as the code.

**REMOVABLE - 6 entries (this lane's fixes):**

| main-tree line | entry |
|---|---|
| 6139 | `BuildMenuUpgradeTower_1920x1080\|ObsidianPanel/PanelContent/Zone_Body/ActionBand/ObsBtn_Not enough resources/Label\|16 of 18` |
| 6239 | `HeroSelect_1080x1920\|.../DetailsStrip/Col_Signature/Label\|7 of 10` |
| 6241 | `HeroSelect_1080x1920\|.../DetailsStrip/Col_Skills/Label\|7 of 11` |
| 6243 | `HeroSelect_1080x1920\|.../DetailsStrip/Col_Skills/Label\|8 of 10` |
| 6245 | `HeroSelect_1080x1920\|.../DetailsStrip/Col_Skills/Label\|8 of 13` |
| 6247 | `HeroSelect_1080x1920\|.../DetailsStrip/Col_Skills/Label\|8 of 13` |

**STAYING - 3 entries:**

| main-tree line | entry | why |
|---|---|---|
| 6290 | `ManageWorkspace_2340x1080\|.../ManageCard_ARMY/Label\|11 of 14` | sec.3 - still ~7% over; needs an owner ruling |
| 6293 | `ManageWorkspace_2670x1200\|.../ManageCard_ARMY/Label\|11 of 14` | same |
| 6236 | `EndStateWaveClear_repairAll_1920x1080\|.../SpoilCell2/SpoilRow/Label\|17 of 19` | sec.4 - the FIXTURE is the defect, and the fix is in `UICaptureLaunch.cs` |

⛔ **DELETE THEM ONLY AFTER A FRESH CAPTURE SHOWS `baselined=` DOWN BY EXACTLY 6 AND `UI_GLYPH_FAIL`
ON NOTHING NEW.** Acceptance item 2 says a fix that leaves its entry standing is not proved; the
inverse is just as true - an entry deleted ahead of the capture proves nothing at all.

### 6. Two findings raised BEYOND the 68, both report-only

1. **`HeroSelectController`'s local `FitLine`/`FitBlock` bypass the kit's readability floor entirely**
   (`:1241-1263`). Both set `fontSizeMin = Mathf.Clamp(t.fontSize * 0.5f, 8f, t.fontSize)` - **a
   local `8f` literal**, against `ElarionUiKit.FontHardFloor = 20` and `FontFloor = 30`
   (`ElarionUiKitObsidian.cs:3033`, `:3044`). Measured consequence on this very screen: every
   `ElarionUi.FontMicro` label in the details strip (the four `SectionHead`s, the pip keys, the
   Q/W/E/R badges) floors at **16 px - below the kit's own hard floor** - and the oracle cannot see
   it, because a label that shrinks to 16 and FITS draws all its glyphs. This is the exact
   "a local literal instead of the kit's floor" shape `BuildCollectionPlayerRegression:161-177`
   already lints for on another screen. **Deliberately NOT fixed here:** routing these through
   `ElarionUiKit.FitSingleLine` raises their floor 16 -> 20 and would push the pip key lane past
   what STATS can give, i.e. it would open new truncations inside a ticket whose acceptance forbids
   them. It wants its own ticket, sized with a capture.
2. **The EndState capture fixture is a stale mirror** - sec.4. Worth its own line on the board even
   after this ticket, because a fixture that drifts from its producer makes every future oracle run
   over that panel measure fiction.

### 7. Verification actually performed by this lane

* `git status --short` clean before, three modified files after - listed below, nothing else.
* `python tools/gate_brace.py` on all three: **`GATE_BRACE_SUMMARY bad=0 of 3`**, exit 0.
* NUL scan on all three: clean. Raw brace counts balanced (94/94, 425/425, 2/2).
* `git diff -U0` reviewed line by line: **only the 13 intended value lines changed**, everything else
  is comment.
* ⛔ **NOT performed, and not claimed:** no Unity run, no compile gate, no regression suite, no
  capture, no PNG opened. Every fit number in this section that is not a quotation from the oracle's
  own output is a **calibrated estimate**, and is marked as one where it appears. The re-capture is
  the proof.

### 8. Files changed

```
Assets/_Modules/Onboarding/HeroSelectController.cs
Assets/_Modules/Village/Buildings/UI/BuildMenuLayout.cs
Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs
```

