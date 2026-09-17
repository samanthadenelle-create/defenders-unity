# WORK ORDER 1800 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Date:** 2026-09-16
**Branch:** `dev` (working tree; this lane did NOT commit, gate, or touch the numbering banner — per brief)

---

## 1. What landed

The owner's storewide sale signs, client side. Five files, all inside this lane's silo:

| File | What |
|---|---|
| `Assets/_Modules/Wallet/PurchaseQuoteService.cs` | `saleBps` / `saleLabel` / `saleEndsAt` on `PurchaseQuote`, plus `IsOnSale`, `SaleBadgeText`, `HasStruckAnchor`, `SaleAnchorLabel`, `SaleEffectiveLabel`, `SaleEndsAtUtc`, `SaleCountdownLabel(nowUtc)`. Pure formatters; **no** price arithmetic, **no** write seam, **no** transport change. |
| `Assets/_Modules/Wallet/StorePackCard.cs` | `SaleBadge`/`SaleLine` on the model, `SaleRibbon`/`SaleRibbonLabel`/`SaleLineLabel`/`OnSale` on the handle, `BuildSaleRibbon`, the sale proof line, `SaleBlockPx`/`SaleExtraPx`, a 3-arg `CardHeight`, `EvaluatePulse`, `Strike`, and public `SaleRibbonFill`/`SaleRibbonInk`. |
| `Assets/_Modules/Wallet/PackStore.cs` | `SaleQuoteFor`/`SaleBadgeFor`/`SaleLineFor`, sale-aware row height, the effective price in **both** price lanes, the pulse list + `TickCardPulses` + `SeatCardPulsesAtRest`, the open-wave arm in `OnEnable`, three `FlowTrace.Step` lines (incl. `serverCopy=` / `endsAt=`, §2b). |
| `Assets/Editor/Regression/NightMarketUiRegression.cs` | `CheckStorewideSale` — 12 case groups over the **live types**, wired into `Run()`. |
| `Assets/Editor/UICaptureLaunch.cs` | `RunNightMarketSaleCaptureHeadless` (own marker `NIGHT_MARKET_SALE_CAPTURE_OK`), reflection injection + flag-gated teardown, and a measured ribbon count. |

WO + RESULT: `WorkOrders/WORK_ORDER_1800_sale_badges_on_pack_cards.md` (+ this file).

---

## 2. Proofs — what was actually run

```
$ python tools/gate_brace.py Assets/_Modules/Wallet/PackStore.cs \
      Assets/_Modules/Wallet/StorePackCard.cs \
      Assets/_Modules/Wallet/PurchaseQuoteService.cs \
      Assets/Editor/Regression/NightMarketUiRegression.cs \
      Assets/Editor/UICaptureLaunch.cs
GATE_BRACE_SUMMARY bad=0 of 5      (exit 0)
```

NUL scan (CLAUDE.md §1, WO-434), all five files: **`nul-clean`**.

Source facts re-read at source this session, each one load-bearing:

- **`FlowTrace.cs:308`** — the 4-arg `Measure(string, string, float, float)` overload exists with that
  exact signature; the tick uses it, not the 3-arg logging form.
- **`PackStore.Update`** — first statement was `if (!_purchaseInFlight) return;`. The tick is inserted
  **above** it; a tick below would never have run while browsing.
- **`PackStore.VariantFor(StoreBand.Basket)`** returns `LandscapeStandard` when `_utilityContent != null`,
  and **`BuildPill` is skipped for that variant** (`StorePackCard.cs:872`). The ribbon is therefore
  **not** routed through the pill path: `BuildSaleRibbon` is called unconditionally across variants at
  `:880`, i.e. the only `!= LandscapeStandard` check in `Build` (`:872`) does **not** gate it.
- **`SolanaPackPricing.UsdApprox`** resolves `PurchaseQuoteService.UsdAnchorFor` — i.e. the **anchor**.
  This is the §2.5 contradiction and it is closed.
- **`StorePiSkinCurrencyRegression.cs:310-320`** pins the exact strings
  `return pack != null ? pack.AmountLabel(_defaultCurrency) : string.Empty;` and
  `return pack != null ? pack.UsdApprox() : string.Empty;` in order after `if (PiDisplay)` — **both
  still present, still in that order.**
- **`StoreReturnToManageRegression.cs:522`**'s forbidden-symbol sweep targets `BuildSlotOffer` in the
  **Village** assembly, not `PackStore` — unaffected.
- **`NightMarketUiRegression.CheckCard`**'s four `Require` strings
  (`float y = artH + TextGapPx`, `float priceLaneTop = cardH - (BottomPadPx + priceBlock)`,
  `budget >= CaptionBlockPx`, `BuildPill(card, Ascii(pill), cardH, artH)`) are **all still present
  verbatim.** The new `FontSaleRibbon = 38` clears the suite's generic font-floor sweep (floor 30).
- **`DataRegression.cs:1662`** — `NightMarketUiRegression.Run` is already registered, so the new cases
  run under `REGRESSION_OK` with no registration edit needed.

- **`NightMarketUiRegression.Code()`** (read at `:1200`) **deletes string literals**, not just
  comments. Confirmed by reading the body: the `"` branch consumes to the closing quote and `continue`s
  without appending. This is why the one assertion whose *subject* is two literals
  (`FlowTrace.Measure("Perf", "PackStore.SaleBadge", ...)`) runs on the **raw** source; on stripped
  source it could never match and would have gone red on its first run against working code.
- **`FrameBudgetMeasureRegression`** — `grep -n "PackStore"` returns **nothing**; PackStore is not one of
  its 27 listed frame-path sites, so it demands no `Measure` in `PackStore.Update` itself. No conflict.
- **`NightMarketRuntimeLayoutRegression` / `NightMarketSharedCardRegression`** construct
  `StorePackCardModel` by object initializer and hold `StorePackCardHandle` lists; added fields are
  source-compatible and default to empty ⇒ no ribbon, no height change, in both suites.

### Five defects caught during implementation and fixed before hand-back

1. **The text stack would have been drawn through the sale line.** The stack measures its budget down
   from `priceLaneTop`; the card grew by `SaleExtraPx` but the floor had not moved, so the contents
   block would have spent the sale line's own pixels — FIX 2's `268..330` overlap through a new door.
   Fixed with `stackFloor = hasSaleLine ? saleTop : priceLaneTop`.
2. **A second PNG per target would have turned `NIGHT_MARKET_CAPTURE_OK` RED.** That entry point judges
   itself on `count == NightMarketTargets.Length` **and** on `_geoCanvasesChecked` /
   `_touchPanelsChecked` equalling the same number — and all three increment per `RenderCanvasToPng`
   call. The sale capture is therefore a **separate entry point with its own marker**, not an extra shot
   inside the existing one.

3. **The ribbon's rotated corner left the card rect.** A `-9°` tilt about the `(0.5, 1)` pivot lifts the
   left end by `halfWidth · sin(9°)` ≈ **23 px** on a 500 px card against a **16 px** top offset. The
   tilt is removed; the badge is a bold rounded **tag**, which the brief explicitly allows and which is
   containable at every measured width by construction. Pinned by a regression case.
4. **The ribbon and the state pill would have collided on the same card.** Ribbon `0.02..0.62` vs pill
   `0.26..0.96` — a 0.36-of-card-width overlap. Disjoint bands do not survive arithmetic (46 px of usable
   ribbon on the narrowest card) and neither does stacking (a 54 px ribbon under a 60 px pill inside
   Compact's **101 px** well). Resolved by a stated ranking — **state word > sale ribbon > merchandising
   badge** — so exactly one top badge draws. Three regression cases pin it.
5. **`saleBps` 1..49 with no `saleLabel` made the row and the card disagree.** `IsOnSale` is true but
   `SaleBadgeText` rounds to 0% and fails closed to empty, so no ribbon drew while `SaleLineFor` still
   returned `was <s>$4.99</s>` — the row reserved `SaleExtraPx` the card never spent. `SaleLineFor` now
   returns empty when the badge text is empty, which is what makes `rowHasSale` genuinely one predicate.

Also: an `out` variable was initially declared **inside** an object initializer — hoisted above it rather
than relying on out-variable scope rules in a file that takes money. And the per-target teardown of the
injected sale rows is **gated on the sale flag**, so the ordinary Night Market capture path gains no side
effect at all (an unconditional `Clear()` there could have wiped a real LIST response).

---

## 2b. Two RED cases from the 19:08 full-suite run (`Builds/data-regression.log`), both fixed

`REGRESSION_FAIL 9`; two were this lane's. **Neither was a defect in the shipped behaviour** — one was
a missing lint-visible reader, the other was **my own oracle reporting a false positive.**

### (1) `AUTHORED_FIELD_READER_FAIL` — `saleLabel` / `saleEndsAt` had no production reader

`AuthoredFieldReaderRegression`'s rule, read at source before touching anything: a production reader is
a `.cs` under `Assets/_Modules/` matching the regex **`\.<Member>`** (`:426`) — **a leading dot is
required**. Reads inside the declaring file count, but mine were **bare** (`SaleLabel.Trim()`,
`string.IsNullOrEmpty(SaleEndsAt)`), so the matcher could not see them. `SaleBps` escaped only because
it is an `int?` and the suite scopes to `public string` fields.

**Fixed by giving each a real reader that earns its place**, not by decorating the DTO: the shelf's sale
trace now reads `quote.SaleLabel` and `quote.SaleEndsAt` directly and reports
`serverCopy=yes|no (bps converted)` and `endsAt=<raw>`. That is genuinely diagnostic —
`SaleBadgeText` returns the **same shape** whether the server authored the copy or the client converted
the bps, so it cannot answer the first question anyone asks when a sale sign reads wrong; and the
countdown is a derived relative string, so only the raw instant separates a stale sale from a
mis-parsed timestamp.

**Verified** by re-implementing the suite's own strip-and-match in Python against the live tree:

```
SaleLabel  -> production readers: ['PackStore.cs']
SaleEndsAt -> production readers: ['PackStore.cs']
SaleBps    -> production readers: ['PackStore.cs']
```

### (2) `NIGHT_MARKET_UI_FAIL` "the sale ribbon is gated on the variant" — MY OWN ORACLE WAS WRONG

⛔ **The ribbon was never variant-gated. The assertion was.** I wrote it as a proximity match —
`variant != StorePackCardVariant.LandscapeStandard` within **200 characters** of `BuildSaleRibbon`, over
`Code(card)`. `Code()` **strips comments**, so the ~60 lines of block comment that legitimately sit
between the pill's guard and the ribbon's call collapsed to whitespace and the two tokens landed **~80
characters apart**. Reproduced exactly, the matched span being:

```
variant != StorePackCardVariant.LandscapeStandard)
        handle.StateLabel = BuildPill(card, Ascii(pill), cardH, artH);
        <~7 blank lines where the comments were>
        if (drawRibbon) BuildS
```

**The lesson, recorded in the file:** *distance between two tokens is not a control-flow fact* — least
of all in source whose comments have been deleted. This is the same family as the "oracle reports its
own staleness in the defect's voice" failure this suite's own header records, and the second time in
this lane a comment-blind/literal-blind `Code()` bit an assertion (the first was the `Measure` literal).

**Fixed by asserting the guard expression itself**: extract `bool drawRibbon = ([^;]*);` and fail only
if that expression mentions `variant`. Live value: `hasRibbon && !stateOutranksRibbon` → **no
`variant`, PASS.** The rotation case had the identical fragility and is now a whole-template invariant
(`localRotation`/`localEulerAngles` absent) — simpler, stronger, and unable to false-positive.

Re-ran all of `CheckStorewideSale`'s source-scanning cases against the live files:

```
OLD proximity regex matches: True   <- would have failed (reproduced)
NEW guard expr: 'hasRibbon && !stateOutranksRibbon' | mentions 'variant': False   PASS
badge suppression present: True | call guarded: True | rotation present: False    PASS
tick above !_purchaseInFlight guard: True | 4-arg Measure (raw source): True      PASS
Time.unscaledTime present: True                                                  PASS
```

⚠ **Still not compile-proven, and the DTO/curve/contrast cases are still un-run** — they are C# over
live types and cannot be executed from here. Their expected values were re-derived by hand
(`3000 → "30% OFF"`; `2026-09-18T16:00Z − 2026-09-16T12:00Z → "ends in 2d 4h"`; fill `#0A090C` vs ink
`#F7F1E1` ≈ **17.5:1** against the 4.5:1 bar). **The suite is the proof, not this arithmetic.**

---

## 3. ⛔ UNPROVEN — read this before believing anything above

1. **NO UNITY RAN.** There is **no** `COMPILE_GATE_OK`, **no** `REGRESSION_OK`, **no** PNG from this
   lane. The brief said to land in the next gate round. **The code is not proven to compile.**
   `gate_brace` + the NUL scan are byte checks, not a compiler.
2. **`saleBps` / `saleLabel` are NOT proven to be the names WO-1799 ships.**
   `WorkOrders/WORK_ORDER_1799*` did not exist on disk, and
   `grep -n "saleBps" api/purchases/quote.js` returned **nothing**. The names come from the lead's
   brief. If the server ships different ones, the client draws **no badge** and the fail-closed trace
   says why — a visible no-op, never a wrong sign. **A one-line DTO rename closes it.**
3. **`saleEndsAt` is this lane's own invention.** No server, no brief mentions it. The countdown is
   drawn absent when it is absent.
4. **THE LOOK IS THE OWNER'S JUDGEMENT AND THIS LANE CANNOT ASSERT IT.** Whether the sign reads as
   "big" and the wave as "flash" needs her eyes on
   `Builds/ui-capture/NightMarket_Sale_*.png` or a device. The regression proves **contrast,
   polarity, geometry and curve shape** — it cannot prove "some flash".
5. **THE PULSE IS NOT PROVEN BY EITHER PNG.** The capture composes the panel by
   `Awake → EnsureBuilt → Render` and never ticks `Update`, so every card sits at its authored scale in
   the stills. Motion needs play mode or a device.
6. **Transient neighbour overlap during the wave, disclosed not hidden:** the pulse scales the card
   **root** inside a `HorizontalLayoutGroup` with 9 px spacing, so at peak 1.05 a ~500 px card
   transiently overlaps its neighbour by ~12 px per side. It returns to exactly 1 and the
   `LayoutElement` is untouched — but it is a real visual fact for her judgement.
7. **The ranking in §2.2b is a DESIGN CALL THIS LANE MADE, not an owner ruling.** "A sale ribbon
   outranks the merchandising badge" is defensible (and the arithmetic forcing one-badge-per-card is
   proven), but *which* badge wins is a merchandising judgement. If she wants "BEST START" to survive
   beside a sale, the pill's width budget has to come down and that is its own ticket.
8. **The countdown is re-stamped every 30 s** inside the same `Measure` scope, not per frame. Under an
   hour remaining it therefore lags reality by up to 30 s. Deliberate, and stated.

---

## 4. For the lead

1. Gate the combined tree: `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a **fresh** log (judge the
   marker, never the exit code).
2. Then the two capture entry points, and **open the PNGs**:
   - `DeNelle.Editor.UICaptureLaunch.RunNightMarketCaptureHeadless` → `NightMarket_*.png` (must stay
     green and unchanged — nothing about the no-sale shelf moved).
   - `DeNelle.Editor.UICaptureLaunch.RunNightMarketSaleCaptureHeadless` → `NightMarket_Sale_*.png`
     (`NIGHT_MARKET_SALE_CAPTURE_OK`). **Desaturate it** — that is the owner's gate
     (`owner-colorblind-delegate-visual-creative`).
3. Commit paths: the five files above + this WO pair. Flip is already `IMPLEMENTED, NOT YET GATED`;
   move to DONE only with the markers in hand.
4. **One question for WO-1799's lane:** confirm the field names are `saleBps` / `saleLabel`, and say
   whether the server will ever send `saleEndsAt`. If it will not, the countdown clause is dead code
   and should be told so rather than left looking live.
