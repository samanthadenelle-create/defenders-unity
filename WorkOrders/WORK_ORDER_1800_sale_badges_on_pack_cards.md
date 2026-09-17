# WORK ORDER 1800 — Big sale signs on the pack cards: a "30% OFF" ribbon, a struck anchor, and the open-wave "flash"

**Status:** IMPLEMENTED
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** Night Market shelf presentation — `DeNelle.Wallet` (`PackStore.cs`, `StorePackCard.cs`, `PurchaseQuoteService.cs` DTO only) + `DeNelle.Editor` (one regression, one capture entry point). Touches NO `.unity`, NO scene builder, NO art, NO `api/`.
**Lane disjointness:** file-disjoint from the gate round in flight. Does NOT touch `SafeZoneRecovery.cs`, `WaveManager.cs`, `SmartEnemySpawner.cs`, `RemoteTunables.cs`, `HeroAbilities.cs`, anything under `Troops/`, `Hero/SmartMobileCamera.cs`, or `RaidGarrisonSpawner.cs`.
**Pairs with:** WO-1799 (the SERVER half, a sibling lane, in `api/`).

---

## 0. The owner's ask, verbatim

2026-09-16:

> "put big sales signs with x% off!!!! you know some flash"

and, the same day:

> "also make the packs pulse when the store opens."

This is the CLIENT half. The server half (the `saleBps` / `saleLabel` knob on the public shelf LIST
branch) is WO-1799.

---

## 1. ⚠ THE SERVER CONTRACT WAS NOT ON DISK WHEN THIS WAS WRITTEN — SAID PLAINLY (CLAUDE.md §11B)

`WorkOrders/WORK_ORDER_1799_storewide_sale_knob_30_percent.md` **did not exist** when this lane ran
(`ls WorkOrders/WORK_ORDER_1799*` returned nothing, 2026-09-16). So the field names are taken from
**two** sources, both read at source this session, and neither is a doc's summary:

| Field | Where it comes from | Status |
|---|---|---|
| `usdEffective`, `usdSaving`, `discountBps`, `discountLabel`, `usdAnchor` | **ALREADY ON THE WIRE** — `api/purchases/quote.js` `wireQuote()` (read 2026-09-16), and already parsed by `PurchaseQuoteService.PurchaseQuote` | proven present |
| `saleBps`, `saleLabel` | the lead's brief for WO-1799. **NOT found in `quote.js`** — `grep -n "saleBps" api/purchases/quote.js` returned nothing | **UNPROVEN** — added client-side against the named contract |
| `saleEndsAt` | **this lane's own optional addition.** No brief and no server mentions it | **UNPROVEN, and may never be sent** |

**This is why every reader is fail-closed rather than defensive-by-habit.** If WO-1799 ships a
different field name, the client draws **no badge at all** and the trace says why — it does not draw a
wrong one.

⛔ **`saleBps` IS NOT `discountBps`, AND CONFLATING THEM WOULD DOUBLE-COUNT THE MONEY.**
`discountBps` is the **per-player, per-quote** shortfall grant the server issues at the till
(`discountBpsForReason`, once per window, bound to a persisted `purchase_quotes` row). `saleBps` is the
**storewide** merchandising knob. One is an entitlement; the other is a price list. They are separate
fields, separate predicates (`IsDiscounted` vs `IsOnSale`) and separate UI (the confirm line vs the
ribbon).

---

## 2. What was built

### 2.1 Parse — `Assets/_Modules/Wallet/PurchaseQuoteService.cs` (DTO + pure formatters only)

Three new wire fields on `PurchaseQuote` (`saleBps`, `saleLabel`, `saleEndsAt`) and these pure
formatters beside the existing `IsDiscounted` / `UsdSavingLabel` pair:

- `IsOnSale` — an integer **strictly between 0 and 10000**. Mirrors `IsDiscounted`'s own test so the
  two axes fail closed by the same rule.
- `SaleBadgeText` — the server's `saleLabel` when it sent one; otherwise the **bps → percent unit
  conversion** (`3000` → `"30% OFF"`). **Empty whenever `IsOnSale` is false — there is no "0% OFF".**
- `HasStruckAnchor` / `SaleAnchorLabel` / `SaleEffectiveLabel` — a strike needs **both** dollar
  figures and a lower effective; the badge may show without the strike, never the reverse.
- `SaleEndsAtUtc` / `SaleCountdownLabel(DateTime nowUtc)` — `"ends in 2d 4h"` / `"4h 12m"` / `"9m"`,
  **empty** when absent, unparseable, or already past. `nowUtc` is a parameter so the oracle can pin it
  without waiting for a clock.

⛔ **NO PRICE ARITHMETIC WAS ADDED.** `usdEffective` remains the only number printable as a price. The
bps→percent step converts one **unit** (basis points) into another (percent); it never multiplies an
anchor. Deriving `anchor x (1 - saleBps/10000)` on the client is exactly the defect that file's header
exists to prevent, and it is not done here.

⛔ **NO WRITE SEAM WAS ADDED.** The headless capture fabricates its sale rows by **reflection** into
`_displayPrices` (§2.5) rather than through a new public setter, because a setter would live in the
shipped build forever so that an editor screenshot could be taken.

### 2.2 The badge — `Assets/_Modules/Wallet/StorePackCard.cs`

**Two elements with two different budgets, and keeping them apart is the whole of why this is safe:**

1. **THE RIBBON** (`BuildSaleRibbon`) is a pure **overlay** on the art well, top-**LEFT** — the mirror
   of where the state pill sits. It costs the vertical budget **nothing**, so no existing measurement
   moves. Near-black rounded **tag** (not rotated — §2.2b), parchment bold ink at font **38** (larger
   than the pill's 30 — it is the "big sign"). Plate height is **derived from its font**
   (`RibbonHeightPx = BlockPx(38,1) + 2*RibbonPadYPx`), never a literal: FIX 4's arithmetic records a
   44 px plate giving a 30 px font a 28 px box, which made TMP cull the label **whole**.
2. **THE SALE LINE** is text, so it gets a **BLOCK** — `SaleBlockPx` + `BlockGapPx` = `SaleExtraPx` —
   and the card's derived height grows by exactly that. It reads
   `was <s>$4.99</s> - ends in 2d 4h`, full width in the price row's own `0.06..0.94` band, one block
   above the price, `richText` explicitly **on**, `FitSingleLine` guarded so neither money figure can
   clip.

⛔ **THE TEXT STACK'S FLOOR MOVED WITH IT, AND THAT WAS THE BUG THAT WOULD HAVE SHIPPED.** The stack
measures its budget **down from the price lane**; growing the card without moving that floor would have
spent the sale line's own pixels and drawn the contents block straight through it — FIX 2's
`268..330` contents-over-price overlap, re-entered by a new door. Hence
`stackFloor = hasSaleLine ? saleTop : priceLaneTop`.

⛔ **THE BLOCK IS BOUGHT BY THE LINE, NOT BY THE RIBBON.** The server can legitimately send a sale bps
with no anchor and no end instant — a badge with nothing left to state — and a card that grew
`SaleExtraPx` for an empty string would carry a band of dead space under its price.

### 2.2b Two geometry facts that changed the design mid-implementation

Both were caught by arithmetic before any Unity ran, and both are recorded because the *reasoning* is
the durable part:

- ⛔ **THE TILT WAS REMOVED.** A `-9°` rotation about the `(0.5, 1)` pivot **lifts** the plate's left
  end by `halfWidth · sin(9°)` ≈ **23 px** on a 500 px card, against a top offset of only **16 px** —
  so the corner leaves the card rect, which the capture harness's containment audit is right to report.
  The brief allows either carrier (*"a diagonal ribbon **or** a bold rounded tag"*); the tag is the one
  containable at every measured card width **by construction**. The shape carriers that remain are the
  tag's weight, its corner radius, its font (38 vs 30) and its polarity.
- ⛔ **THE RIBBON AND THE PILL ARE NOW MUTUALLY EXCLUSIVE, BECAUSE THEIR BANDS OVERLAP.** Ribbon
  `0.02..0.62`, pill `0.26..0.96` — a **0.36-of-card-width** collision if both drew. Making them
  disjoint does not survive arithmetic: the pill starts at 0.26 because "BEST VALUE" measures 219 px
  bold at font 30 and needs 0.70 of the card (FIX 4's width budget), leaving the ribbon 0.02..0.24 —
  **46 px after padding** on the narrowest shipped card, for the loudest copy on the screen. Stacking
  them vertically fails too: the pill ends 60 px down and the **Compact** art well is only **101 px**,
  so a 54 px ribbon beneath it overhangs by 13 px.

  So the card draws **ONE top badge, ranked: STATE WORD > SALE RIBBON > MERCHANDISING BADGE.** A live
  discount outranks "BEST START"; the state word still outranks everything.

  ⚠ **Almost nothing is lost to this, and the reason is specific:** `SaleQuoteFor` already suppresses
  the sale for **owned** and **anchor-only** packs, so the only state words that can reach a sale card
  are **"Not yet"** — which the card *also* prints in full as its reason line, so no information goes
  missing — and **"Your gap"**. For those two the state wins and the big sign stands down; **the sale
  LINE still draws**, so the discount is stated in digits either way.

⛔ **THE RIBBON DOES NOT ROUTE THROUGH `BuildPill`, AND THAT IS LOAD-BEARING.** `BuildPill` is
**skipped** on `StorePackCardVariant.LandscapeStandard` — which is the variant
`PackStore.VariantFor(StoreBand.Basket)` returns for the shipped landscape shelf. A ribbon on the pill
path would have rendered **nowhere** on the very cards the owner is looking at, with every gate green.
`BuildSaleRibbon` is unconditional across variants, and the regression pins that it is not gated.

### 2.3 The "flash" — `StorePackCard.EvaluatePulse` + `PackStore.TickCardPulses`

Two beats, **one driver**, **one** `Measure` scope:

| Beat | Who | Shape |
|---|---|---|
| **OPEN WAVE** | every card, **once per open** | 1.0 → **1.05** → 1.0 over **0.6 s**, staggered **80 ms** per card in build order, so it sweeps left-to-right |
| **SALE LOOP** | **discounted cards only**, after their own open pulse | 1.0 → **1.06** → 1.0, **1.2 s** loop |

- The curve is **one pure static function** — `EvaluatePulse(t, duration, loop, peak)`, half a sine, so
  a card always starts and ends at its authored size and can never be left enlarged. It is pure
  precisely so the gate can pin it **without play mode**.
- ⛔ **`Time.unscaledTime`.** The store opens over a town that may be held at `timeScale 0`; a scaled
  pulse would freeze mid-beat and leave cards visibly enlarged with no way back.
- ⛔ **ONE DRIVER, NOT ONE COMPONENT PER CARD** — N MonoBehaviours is N frame-path sites and N sets of
  logs. This is one loop under the **4-arg** `FlowTrace.Measure("Perf", "PackStore.SaleBadge", 4f, 1f)`
  (signature read at `Assets/_Modules/Core/Diagnostics/FlowTrace.cs:308`). **No per-frame log exists in
  the tick** — the 3-arg form logs on every dispose and would evict the boot window out of the device
  logcat ring (memory: `logcat-ring-buffer-destroys-evidence`).
- ⛔ **THE TICK SITS ABOVE `Update`'s EARLY RETURN.** `PackStore.Update` returned immediately unless
  `_purchaseInFlight`, which is **false for the entire time the player is browsing**. A tick added
  below it would have compiled, gated green, and never moved a card. The regression pins the ordering.
- ⛔ **"ONCE PER OPEN" IS ARMED IN `OnEnable`, CONSUMED IN `Render`.** `Render()` is **not** "the store
  opened" — it re-runs on every returning price quote, every focus change and every walletless-banner
  repaint. Arming inside `Render` would have re-pulsed the whole shelf seconds after opening and again
  on each refresh. A render with the flag down calls `SeatCardPulsesAtRest()`.
- ⛔ **AN UNDISCOUNTED CARD LEAVES THE LIST** when its one pulse ends — that is "not looping" in code
  rather than in a comment. A shelf with no sale costs nothing after 0.6 s + the stagger.

### 2.4 Greyscale safety (memory: `owner-colorblind-delegate-visual-creative`)

The ribbon carries **four non-colour carriers** and colour is reinforcement only:

1. **Words** — "30% OFF" in digits.
2. **Shape and weight** — a bold rounded tag at font **38** against the pill's 30, so the two differ
   in mass as well as in copy.
3. **Position** — top-LEFT; and because the two badges are mutually exclusive (§2.2b), nothing else is
   ever in that band on the same card.
4. **Polarity** — the state pill is a **light** plate with **dark** ink; this is its exact inverse.

The strike-through is itself a shape carrier for "this is the OLD price" — a dimmer or differently
tinted anchor says nothing with the hue removed.

`SaleRibbonFill` / `SaleRibbonInk` are **public** so the regression computes the **WCAG relative-
luminance ratio** of the live values and fails under **4.5:1**, plus a polarity assertion. An oracle
that re-typed the hex could not fail when the value changed.

Touch/legibility: the ribbon is **information**, `raycastTarget = false` on both plate and label, so it
adds **no** interactive rect (WO-1060 Assert B) and cannot swallow the card's tap. It changes no
control's size, so `MinTouchPx = 112` is untouched. Ribbon font 38 and the sale line's 32 both clear
`ElarionUi.FontFloorMobile` (30), and the suite's generic `Font\w+` sweep re-checks that.

### 2.5 The price row contradiction this lane also had to close

⛔ **NOT IN THE BRIEF, AND IT WOULD HAVE SHIPPED A WRONG PRICE.** Read at source:

- `PackStore.StorePriceMajor`'s walletless branch returned `pack.UsdReference` — the **authored
  anchor**. So a walletless sale card would have printed **`$4.99` as its one large figure** while the
  line beneath it read `was $4.99`. That is not a layout defect; it is the pre-sale price shown **as**
  the price, under a sale sign.
- `StorePriceMinor`'s Solana branch returned `pack.UsdApprox()`, which resolves
  `PurchaseQuoteService.UsdAnchorFor` — the **anchor** again. It would have read `~ $4.99` beside a line
  striking that very figure out.

Both now prefer the server's `usdEffective` **when and only when** the pack is on sale
(`~ $3.49`, tilde kept — the dollars float because the rate does; the SKR is the exact figure). The
sale line therefore does **not** repeat the effective price: it is already the card's largest figure.

Both edits keep the exact source lines `StorePiSkinCurrencyRegression` pins in order
(`return pack != null ? pack.AmountLabel(_defaultCurrency) : string.Empty;` and
`return pack != null ? pack.UsdApprox() : string.Empty;`) — verified present and still after
`if (PiDisplay)`.

### 2.6 Suppression — the rails that price themselves

`SaleQuoteFor(pack, out why)` is the one resolver, and it returns null with a worded reason for:
**Google Play** (the card prints the Play provider's own localized string), **Pi** (the Pi quote),
**owned**, **anchor-only**, **pinned canary** (no USD anchor to take a percentage of), **no server
display row**, and **not `IsOnSale`**. Badging a Play or Pi price with a percentage taken off a
**Solana** LIST row would advertise a discount that rail never applies.

The sale reads off the **public, unauthenticated** LIST (WO-1190), so a **walletless** shelf is on sale
too — otherwise the sign would be bait.

### 2.7 Instrumentation

Three `FlowTrace.Step` lines, **one per render, never one per card**:

- `shelf sale badges: count=<n> bps=<n> label="<copy>"`
- `shelf sale badge hidden FAIL-CLOSED on <n> card(s); last reason: <why>`
- `shelf open pulse: cards=<n>`

A nine-pack shelf with no sale would otherwise print nine lines saying so.

---

## 3. The confirm line is UNCHANGED

`PackStore.cs` ~`:4404` still composes `quote.DiscountLabel` / `quote.UsdSavingLabel` /
`quote.UsdApproxLabel` exactly as before. **Not touched.** The storewide sale is a shelf sign; the
confirm step's copy is the per-quote discount's, on the other axis (§1).

---

## 4. Files touched

| File | Change |
|---|---|
| `Assets/_Modules/Wallet/PurchaseQuoteService.cs` | 3 wire fields + 6 pure formatters on the DTO. No transport, no service-method, no seam change. |
| `Assets/_Modules/Wallet/StorePackCard.cs` | `SaleBadge`/`SaleLine` on the model; ribbon + sale-line builders; `SaleBlockPx`/`SaleExtraPx`; 3-arg `CardHeight`; `EvaluatePulse`; `Strike`; public ribbon colours. |
| `Assets/_Modules/Wallet/PackStore.cs` | `SaleQuoteFor`/`SaleBadgeFor`/`SaleLineFor`; sale-aware row height; effective price in both price lanes; pulse list + `TickCardPulses` + `SeatCardPulsesAtRest`; open-wave arm in `OnEnable`; 3 trace lines. |
| `Assets/Editor/Regression/NightMarketUiRegression.cs` | `CheckStorewideSale` — 12 case groups (§5). |
| `Assets/Editor/UICaptureLaunch.cs` | `RunNightMarketSaleCaptureHeadless` + reflection injection/teardown + ribbon counter. |

**NOT touched:** any `.unity`, any scene builder, any art, `api/`, `CLI_LANES_WO_NUMBERS.md`, and every
file the in-flight gate round owns.

---

## 5. Acceptance criteria

- [x] No sale → **no badge, no strike, no growth**. Card is byte-identical to today.
- [x] `saleBps = 3000` → badge reads exactly **`30% OFF`**, anchor struck as `<s>$4.99</s>`.
- [x] An authored `saleLabel` **outranks** the bps conversion.
- [x] `saleBps` of **0, -1, 10000, 12000** → **hidden**. There is no "0% OFF".
- [x] An anchor with **no** effective price → **no strike** (never cross out the only figure).
- [x] Countdown: present → `ends in 2d 4h`; absent → empty; **expired → empty**.
- [x] Ribbon fill vs ink ≥ **4.5:1** WCAG, computed from the **live** colours, plus inverse polarity vs the pill.
- [x] `EvaluatePulse`: 1 at t=0, peak at midpoint, 1 at the end, **flat at 1 for 4 further periods** when `loop=false`; periodic when `loop=true`; 1 for a zero duration.
- [x] A sale card grows by **exactly** `SaleExtraPx` on **every** variant, and the 2-arg `CardHeight` answers **identically** to before.
- [x] The ribbon is **not** variant-gated like the pill, is **not rotated**, and never draws beside the pill.
- [x] `saleBps` 1..49 with no `saleLabel` → badge empty **and** sale line empty **and** no row growth (one predicate, §2.2b).
- [x] The pulse tick is **above** `Update`'s `!_purchaseInFlight` return, uses the **4-arg** `Measure`, and reads **unscaled** time.
- [ ] **OWNER JUDGEMENT — the look itself.** Whether the sign reads as "big" and the wave as "flash" is hers, from `Builds/ui-capture/NightMarket_Sale_*.png` or a device. This lane cannot assert it.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a **fresh** log — the lead's next gate round.

---

## 6. What this lane did NOT prove

- **No Unity ran.** No `COMPILE_GATE_OK`, no `REGRESSION_OK`, no PNG. Per the brief: gate in the next round.
- **The pulse is NOT proven by a still.** The capture path composes the panel by
  `Awake → EnsureBuilt → Render` and never ticks `Update`, so every card sits at its authored scale in
  both PNGs. The frames prove the **ribbon, the struck anchor, the countdown and the taller card**.
  The **motion** needs play mode or a device.
- **`saleBps` / `saleLabel` are not proven to be the names WO-1799 ships** (§1). If they differ, the
  shelf draws no badge and the fail-closed trace names the reason — a visible no-op, not a wrong sign.
- **Transient neighbour overlap during the wave, stated rather than hidden:** the pulse scales the card
  **root** inside a `HorizontalLayoutGroup` with 9 px spacing, so at peak 1.05 a ~500 px card
  transiently overlaps its neighbour by ~12 px per side. It is motion, it returns to exactly 1, and the
  card's own `LayoutElement` is unchanged — but it is a real visual fact for the owner's judgement, not
  a claim that nothing moves.

---

## 7. Provenance — every path opened this session (2026-09-16)

- `api/purchases/quote.js` — `wireQuote` (the field list), the LIST branch, `discountBpsForReason`.
- `Assets/_Modules/Wallet/PurchaseQuoteService.cs` — the DTO, `_displayPrices`, `DisplayPrice`, `IsSellable`, `UsdAnchorFor`.
- `Assets/_Modules/Wallet/PackStore.cs` — `Update`, `OnEnable`, `Render`, `BuildPackCard`, `VariantFor`, `StorePriceMajor/Minor`.
- `Assets/_Modules/Wallet/StorePackCard.cs` — the whole block budget, `Build`, `BuildPill`, `Ascii`, `NewText`.
- `Assets/_Modules/Wallet/SolanaPackPricing.cs` — `UsdApprox` (it resolves the **anchor**; §2.5).
- `Assets/_Modules/Core/Diagnostics/FlowTrace.cs:293-320` — the 4-arg `Measure` overload and why.
- `Assets/Editor/Regression/NightMarketUiRegression.cs`, `.../StorePiSkinCurrencyRegression.cs:310-320`, `.../StoreReturnToManageRegression.cs:510-535`, `.../DataRegression.cs:1662`.
- `Assets/Editor/UICaptureLaunch.cs` — `CaptureNightMarketStoreOnce`, `RenderCanvasToPng`, `RunNightMarketCaptureHeadless`.
- `Assets/Editor/DeNelle.Editor.asmdef` — confirms `DeNelle.Wallet` + `DeNelle.Commerce` are referenced.
