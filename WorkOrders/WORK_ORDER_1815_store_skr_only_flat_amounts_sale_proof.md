# WORK ORDER 1815 — The store goes SKR-ONLY at FLAT amounts, and the sale is proven by screenshot

**Status:** IMPLEMENTED

**Owner, 2026-09-16 ~21:25, verbatim:** *"change the store to SKR only and set to flat amounts"* /
*"and test with sales promos"* / *"so you can see screenshots of discounts"*
**Owner, earlier the same day, verbatim:** *"what if we drop the conversion and only do straight SKR
for SKR build 100 200 300 500 9999"* / *"if we just list the SKR does it feel more impulse less cost"* /
*"put big sales signs with x% off!!!! you know some flash"* / *"also make the packs pulse when the store
opens"* / *"can we run a 30% deal?"* / *"i just want a first sale"*

**Lane:** implementation. Number PRE-ASSIGNED by the lead; `CLI_LANES_WO_NUMBERS.md` NOT touched by this
lane. No Unity gate beyond the two headless capture entry points, no commit, no git.
**Parents:** `WORK_ORDER_1798_packs_more_substantial_for_purchases.md` §6 (the flat-SKR design),
`WORK_ORDER_1799_storewide_sale_knob_30_percent.md` (server `saleBps`),
`WORK_ORDER_1800_sale_badges_on_pack_cards.md` (client sale sign + pulse),
`WORK_ORDER_1801_first_sale_path.md` (the $1.99 rung's contents).
**Not mine:** `api/**` (spec'd in §6 for the lead), `RemoteTunables.cs`, `ArmyMuster*`,
`WallTools/*`, VFX, any `.unity` scene, `CLI_LANES_WO_NUMBERS.md`.

---

## 1. THE PRICE CHAIN, END TO END, READ AT SOURCE 2026-09-16

Every line number below was opened this session. Nothing here is copied from a doc.

### 1a. Authoring → catalog

| Step | File:line | What it does |
|---|---|---|
| authored rail prices | `Assets/Resources/Data/Canonical/packs.json` — `pricing.usd` / `.usdc` / `.sol` / `.skr` per pack (e.g. `:40`, `:68`, `:97`, `:133`, `:169`) | 29 packs, 930 lines. The `Assets/StreamingAssets/Data/Canonical/packs.json` twin is byte-identical and pinned so by `BuyGateAndPriceLadderRegression` `[dual-copy]` (`:203-217`) |
| the peg note | `packs.json:18` (`_authoringNotes.skrPeg`) | Records `skrPerUsd ~= 12.5` — **~4.6x below the live market** (WO-1798 §6a). Dead data (see `:1c`) |
| deserialize | `Assets/_Modules/Commerce/PackCatalog.cs:69-79` (`PackPricing`: `usd:72`, `usdc:74`, `sol:76`, `skr:78`), `PackDef.Pricing:277`, `UsdReference:325`, `PackCatalog.Packs:384` | Straight Newtonsoft deserialize into `PackDef`. Assembly `DeNelle.Commerce` (rail-neutral by design, `PackCatalog.cs:335`) |
| server mirror | `tools/gen-sku-catalog.mjs` → `api/_lib/sku-catalog.generated.json` | **VERBATIM copy**, no filtering (`gen-sku-catalog.mjs:28-31`). `test/admin.skus.view.test.js` asserts the copy parses EQUAL to canonical and goes RED on drift. **This is the seam that lets an authored field reach the server with ONE authoring surface.** |

### 1b. Server → quote (the price authority)

| Step | File:line | What it does |
|---|---|---|
| USD ladder | `api/_lib/purchase-catalog.js:83-119` (`USD_ANCHORS`), `usdAnchor():235-243` | 27 SKUs, $1.99–$49.99 |
| rate | `fetchSkrUsdRate():300`, used at `api/purchases/quote.js:273` | coingecko `seeker` `low_24h`, cached 120 s, **fail-closed → 503** |
| rounding | `quoteAmount():277-286` — `Math.ceil(usd / usdPerSkr)`, then integer `BigInt` base units | **Owner ruling 2026-08-23** (*"i think low over 24 is ok"*): 24h-low rate **and** ceil-to-a-whole-SKR, both favour us (`:241-262`) |
| quote body | `buildQuoteBody():365-395` | `amountBaseUnits`, `skrAmount`, `usdAnchor`, `usdEffective`, `usdSaving`, `discountBps/Label`, `rate`, `rateSource` |
| wire | `api/purchases/quote.js:122-158` (`wireQuote`), `:160-180` (`wirePinned`), LIST `:302-340`, QUOTE `:367-446` | `saleBps` is emitted ONLY when `discountReason === SALE_REASON` (`:150`, `:336`, `:446`) |

### 1c. Quote → shelf card (the client prices NOTHING)

| Step | File:line | What it does |
|---|---|---|
| transport | `Assets/_Modules/Wallet/PurchaseQuoteService.cs:499-587` `RefreshPricesAsync` (public LIST, `requireAuth:false` at `:515`, row-level Guard at `:550-567`); `:597-668` `RequestQuoteAsync` (the ONE binding quote; caches into the display map at `:659`) | ⛔ **Browsing does NOT authenticate (WO-1190)** — so the SKR figure is available **walletless**. That fact is load-bearing for §3 |
| the number | `PurchaseQuote.BaseUnits:169`, `UiAmount:179`, `MatchesBaseUnits:187`, `ExactSkrLabel:213`; `PurchaseQuoteService.SkrAmountFor():404-408` | `UiAmount = BaseUnits / 10^Decimals`. **The only printable price.** |
| rail label | `Assets/_Modules/Wallet/SolanaPackPricing.cs:40-76` `AmountFor` (canary exception `:72`, server figure `:73`), `AmountLabel:115-130` (`"{amount:0.######} SKR"` at `:126`, the WORDS `"Price unavailable"` at `:127`), `UsdApprox:106-112` | ⛔ `pricing.skr` is **dead data except the two canaries** (`:61-72`): a stale hand-typed figure, never rendered |
| card copy | `Assets/_Modules/Wallet/PackStore.cs:2454-2481` — `PriceMajor:2468` ← `StorePriceMajor:3727-3763`; `PriceMinor:2469` ← `StorePriceMinor:3765-3797`; `SaleLine:2481` ← `SaleLineFor:2655-2675` | Three price strings per card, all built here |
| view | `Assets/_Modules/Wallet/StorePackCard.cs:830-843` (`SaleLineLabel`, richText), `:880` (`drawRibbon`), `:1046-1086` (`BuildSaleRibbon`, fill `SaleRibbonFill:359` `#0A090C`, ink `SaleRibbonInk:361` `#F7F1E1`), `Strike():404`, `EvaluatePulse():389` | Pure view; every string arrives on the model |
| confirm line | `PackStore.cs:4472-4481` — `"you will send exactly {quote.ExactSkrLabel} ({quote.UsdApproxLabel}...) ... at ${rate} per SKR ({source})."` | ⚠ **This is player-facing UI copy, NOT a FlowTrace line.** It is the one live `"per SKR"` string in the store |

### 1d. saleBps → the badge

`quote.saleBps/saleLabel/saleEndsAt` (`PurchaseQuoteService.cs:124-128`) →
`IsOnSale:260` (fail-closed: `0 < bps < 10000`) → `SaleBadgeText:273-286` (server copy, else a
bps→percent **unit conversion**) → `PackStore.SaleQuoteFor:2605-2632` (suppressed on Google Play `:2611`,
Pi `:2613`, owned `:2615`, `anchorOnly` `:2616`, no display price `:2619`, pinned canary `:2620`, not a
usable sale `:2624`) → `SaleBadgeFor:2636` → `StorePackCard.BuildSaleRibbon:1046`.
The struck figure today is the **USD** anchor: `HasStruckAnchor:294`, `SaleAnchorLabel:300`,
`SaleEffectiveLabel:304`, composed at `PackStore.cs:2666-2667`.

### 1e. The pulse ALREADY EXISTS (WO-1800) — read before adding a second one

`_openPulseArmed` declared `PackStore.cs:440`, armed in `OnEnable` `:501`, ticked from `Update` `:825`
(above the `!_purchaseInFlight` early-return), enrolled per card `RegisterCardPulse:2685`, driven by
`TickCardPulses:2753` (open wave `:2775` then the sale loop `:2779`), seated at rest `:2813`, and the
wave is fired after a build at `:1814-1821`. Curve: `StorePackCard.EvaluatePulse:389`.
⛔ **This ticket adds NO pulse code.** §4's requirement is already met; the capture cannot prove motion
(a still is composed `Awake → EnsureBuilt → Render` and never ticks `Update`).

---

## 2. THE RULING THIS TICKET IMPLEMENTS, AND THE ONE LINE IT WILL NOT CROSS

**SKR-only, flat.** The shelf prints one figure, `"300 SKR"`. No `$`, no `~ $`, no `per SKR` rate copy.

⛔ **THE SERVER STAYS THE PRICE AUTHORITY. `skrFlat` IS AN AUTHORING FIELD, NOT A CLIENT PRICE.**
The whole of `PurchaseQuoteService.cs:6-31` exists because a client-resolved price and a server-checked
one *cannot both be right*, and `/verify` runs **after** the transfer settles — so the failure mode is
**paid-and-not-granted, triggered by a market move rather than a deploy**. Rendering an authored figure
as the price would rebuild that defect through a new door, the "flat" adjective notwithstanding.

So `pricing.skrFlat` has exactly four jobs, and pricing the charge is not one of them:

1. the **authoring source** the verbatim generator (`§1a`) mirrors to `api/_lib/sku-catalog.generated.json`,
   from which the server prices — **one authoring surface, no second table**;
2. the **struck anchor** on a sale card, and only when `quote.UiAmount < skrFlat` (fail-closed otherwise) —
   legitimate because a flat amount is a **constant**, not a market-derived figure;
3. the **ladder oracle** for the new regression;
4. the **stub** the headless capture injects so the PNG shows what the server will send;
5. **(added during implementation)** the shelf's **gap-filler** when the server served no figure for a row
   — `PackStore.SkrShelfLabel`, gated by `SkrFlatShelfPrices` and traced on every use. It is not an
   override (a served figure always wins) and it is not a charge; it exists because WO-1409's rule that a
   walletless shelf must still say a price is pinned by `NightMarketNoWalletRegression`, and under
   SKR-only the authored constant is the only honest figure left. The interim it creates is written up in
   the RESULT §7.1b rather than smoothed over.

⚠ **THEREFORE: the shelf only genuinely goes flat when the lead lands §6.** Until then the client prints
the server's rate-derived SKR — correctly, in SKR, with no dollars. The PNGs in §5 prove the *rendering*
against a stubbed server row, not the live server's arithmetic. Stated here so nobody reads this ticket
as "flat pricing shipped".

---

## 3. THE TIER → SKR LADDER (a PROPOSAL; the owner can re-rule any row)

Mapped by **USD anchor rung**, not by raw `tier`. Rationale: WO-1801 spent real design effort keeping
`builders-hour` from strictly dominating `impulse-iron-small` **at the same $1.99 anchor**
(`test/purchases.quote.test.js` *"no impulse rung is strictly dominated by another purchasable pack at
the same USD anchor"*). Packs sharing a USD anchor must therefore share an SKR rung, or that test's
premise silently changes meaning.

| USD anchor | SKR (flat) | Packs | ≈ USD at `low_24h` $0.01745041 (WO-1798 §6b, one call, already stale) |
|---|---|---|---|
| $1.99 | **100** | `builders-hour`, `impulse-wood-small`, `impulse-iron-small`, `impulse-stone-small`, `impulse-crystals-small` | ≈ $1.75 |
| $2.99 | **200** | `impulse-wood-medium`, `impulse-iron-medium`, `impulse-stone-medium`, `impulse-crystals-medium` | ≈ $3.49 |
| $4.99 | **300** | `starters-hand`, `hearth-spark`, `keepers-satchel`, `bloomtide-bundle`, `impulse-wood-large`, `impulse-iron-large`, `impulse-stone-large`, `impulse-crystals-large` | ≈ $5.24 |
| $9.99 | **500** | `folks-thanks`, `permanent-builder`, `frostfall-bundle`, `embergrove-bundle`, `hero-wardrobe-pack`, `realm-defender-bundle` | ≈ $8.73 |
| $19.99 | **1000** ⚠ | `patron-of-elarion`, `echo-patron-pack`, `builders-cache` | ≈ $17.45 |
| $49.99 | **9999** | `founders-vow` | ≈ $174.55 |

⚠ **THE 1000 RUNG IS THIS LANE'S ADDITION AND NEEDS THE OWNER'S WORD.** Her ladder has **five** rungs;
the shipped shelf has **six** USD bands. Folding $19.99 onto 500 would put it on the same rung as
$9.99 — where `patron-of-elarion` (18500 wood) strictly dominates `folks-thanks` (9000 wood) at an
identical price, which is the exact defect the domination test exists to catch. Folding it onto 9999
would price a $19.99 pack at ≈ $175. **1000 is the smallest honest sixth rung**; alternatives are
(a) retire `founders-vow` from the shelf and let $19.99 take 9999, or (b) re-anchor `patron-of-elarion`
to $9.99. Both are merchandising calls, not engineering ones.

⚠ **9999 SKR ≈ $175 at today's rate — 3.5x its $49.99 anchor.** That is the owner's own number and it is
authored as given; recording the consequence, not arguing it. And the standing risk from WO-1798 §6c.1
is unchanged: Seeker traded **$0.00542 and $0.05582 one day apart** this year, so a flat rung has been
worth $0.54 and $5.58 with no deploy in between. A flat ladder hands pricing to the token.

**Not priced** (no `pricing.usd`, promo-only, never quotable): `welcome-500`, `welcome-100`. They get no
`skrFlat`. The two canaries are **not authored in packs.json at all** (the server LIST injects the pinned
row, `api/purchases/quote.js:160-180`), so nothing here can touch a protocol constant.
`pricing.usd` **stays authored on every pack**: `PurchaseGate.RequiresWallet` derives the wallet rule
from it (`BuyGateAndPriceLadderRegression` `[threshold/*]` `:405-440`), and Google Play + Pi price
themselves off it (`StorePriceMajor:3730-3743`). It is no longer the *SKR* price; it is not dead.

---

## 4. WHAT CHANGES (client + data). Nothing is deleted.

**4a. `Assets/Resources/Data/Canonical/packs.json` + its StreamingAssets twin** — add
`"skrFlat": N` inside each priced `pricing` block (27 of 29 packs), and record the ruling in
`_authoringNotes`. Binary-safe patch; both copies byte-identical; CRLF shape proven (WO-1801 measured
CRLF 930 == LF 930 == CR 930). ⛔ `pricing.skr` is **left exactly as it is** — correcting a dead field
creates a second opinion about the same number (WO-1798 §6c.4).

**4b. `Assets/_Modules/Commerce/PackCatalog.cs`** — `[JsonProperty("skrFlat")] public double SkrFlat;`
on `PackPricing`, beside `skr`. A `double`, so `AuthoredFieldReaderRegression` (which scopes to
`public string`, `:395-420`) does not demand a reader.

**4c. `Assets/_Modules/Wallet/PurchaseQuoteService.cs`** — pure formatters over transported numbers:
`IsFlatPriced` (`!Pinned && !Rate.HasValue` — a **transported** fact, not arithmetic),
`SkrEffectiveLabel`, `HasStruckSkr(double authoredFlat)`, `SkrAnchorLabel(double authoredFlat)`.
No price arithmetic: a comparison and a format.

**4d. `Assets/_Modules/Wallet/PackStore.cs`** — four display edits:
- `StorePriceMajor`: the `WalletlessBrowsing` branch stops printing `pack.UsdReference` (a **dollar**
  figure on a shelf that charges SKR) and falls through to the SKR label. ⛔ The final
  `return pack != null ? pack.AmountLabel(_defaultCurrency) : string.Empty;` and the preceding
  `if (PiDisplay)` stay **byte-identical and in order** — `StorePiSkinCurrencyRegression.cs:311-315`
  pins that sequence verbatim.
- `StorePriceMinor`: on the Solana rail returns EMPTY under SKR-only. The USD code path is **kept and
  still compiled**, behind `static readonly bool ShowUsdAlongsideSkr = false`, and the pinned literal
  `return pack != null ? pack.UsdApprox() : string.Empty;` stays after `if (PiDisplay)`
  (`StorePiSkinCurrencyRegression.cs:316-320`). Pi and Google Play branches untouched.
- `SaleLineFor`: strikes the **SKR** anchor (`was <s>500 SKR</s>`) when `HasStruckSkr`, falling back to
  the existing USD strike only when `ShowUsdAlongsideSkr`. The countdown clause is unchanged.
- the confirm line (`:4597-4624`): a FLAT quote (`IsFlatPriced`) says *"A flat SKR price - no market rate
  is used."* instead of a fabricated `at $0.00000000 per SKR ()`; a rate-derived quote keeps the rate
  clause verbatim, because a rate that priced the charge must always be disclosed. ⚠ The `(~ $X)` fiat
  clause is dropped on **both** paths, because it is gated on `ShowUsdAlongsideSkr` — that is the SKR-only
  ruling applied at the till too, and it supersedes the 2026-08-23 "be transparent that price is approx"
  ruling at that step. The `rateLine` local and its pinned-canary branch survive.

**4e. NEW `Assets/Editor/Regression/StoreSkrFlatLadderRegression.cs`** + one registration line in
`Assets/Editor/Regression/DataRegression.cs`. Cases:
1. every priced pack authors `skrFlat` on the ladder `{100,200,300,500,1000,9999}`; `welcome-*` author none;
2. packs sharing a `pricing.usd` share a `skrFlat` (the domination premise);
3. the StreamingAssets twin authors the identical `skrFlat` for every sku;
4. the built card model carries **no `$`** and no `"per SKR"` in `PriceMajor`/`PriceMinor`/`SaleLine` on
   the Solana rail — asserted on the **model strings**, not on source text;
5. a 3000-bps quote yields `SaleBadgeText == "30% OFF"`, a struck SKR anchor and a discounted SKR figure,
   with the ceil rounding stated;
6. fail-closed: `skrFlat` absent, or `UiAmount >= skrFlat`, draws no strike;
7. anti-vacuity floor on the pack count.

**4f. `Assets/Editor/UICaptureLaunch.cs`** — a NEW entry point with its OWN marker
(`STORE_SKR_FLAT_CAPTURE_OK`), target list `{2670x1200}` only, **two** `RenderCanvasToPng` calls
(no-sale + `saleBps=3000`). It must not ride inside `RunNightMarketCaptureHeadless`: that marker judges
`count == NightMarketTargets.Length` **and** the `_geoCanvasesChecked` / `_touchPanelsChecked` counters,
all three of which increment per shot (WO-1800 RESULT §2 defect 2). `InjectSaleDisplayPrices` gains an
`amountBaseUnits` derived from `skrFlat` so the stub prints the ladder figure; the no-sale shot injects
the same rows with **no** `saleBps`.

---

## 5. ROUNDING RULE (stated, as the brief requires)

`effectiveSkr = ceil(skrFlat * (10000 - saleBps) / 10000)`, to a **whole SKR**, then
`amountBaseUnits = effectiveSkr * 10^decimals` in integer math.
Same direction as the shipped `quoteAmount` (`purchase-catalog.js:281`): **ceil favours us**, which is the
owner's 2026-08-23 ruling carried forward rather than re-decided. 30% off 300 SKR = `ceil(210) = 210`;
30% off 100 = `ceil(70) = 70`; 30% off 9999 = `ceil(6999.3) = 7000`.
⛔ The **client never performs this** — it prints `quote.UiAmount`. The rule is written here because the
server implements it (§6) and the capture stub must reproduce it exactly or the PNG lies.

---

## 6. THE `api/` CHANGE THE LEAD MUST MAKE (this lane did not touch `api/`)

Ordered. Step 1 is mandatory the moment `packs.json` changes, or `test/admin.skus.view.test.js` is RED.

1. **`node tools/gen-sku-catalog.mjs`** — regenerates `api/_lib/sku-catalog.generated.json` (verbatim,
   so `skrFlat` arrives with it). Never hand-edit that file.
2. **`api/_lib/purchase-catalog.js`** — read `skrFlat` from the generated catalog into a frozen
   `SKR_FLAT` map (**derived, not retyped** — a second hand-typed table is CLAUDE.md §2/§5's drift bug)
   and add `skrFlatFor(sku)`.
3. **`buildQuoteBody` (`:365-395`)** — when `skrFlatFor(sku) > 0`, price flat:
   `skr = Math.ceil(flat * (10000 - bps) / 10000)`,
   `amountBaseUnits = (BigInt(skr) * 10n ** BigInt(decimals)).toString()`,
   and return `rate: null`, `rateSource: 'flat-skr'`, `usdEffective: null`, `usdSaving: null`,
   `usdAnchor: usd` (kept — the wallet rule and the Play/Pi rails read it).
   ⚠ **The `!rate || !(rate.usdPerSkr > 0)` guard on `:368` must become conditional**: a flat SKU has to
   quote even when coingecko is unreachable. That is the *point* of flat — and today it would 503.
4. **`api/purchases/quote.js:273`** — `fetchSkrUsdRate()` and its worded 503 must be **skipped** when the
   requested SKU (or every LIST candidate) is flat. The existing refusal wording stays for rate-derived
   SKUs.
5. **Optional hardening:** also wire `skrAnchor: flat` on the quote, so the struck figure is transported
   rather than read from the client's own catalog copy. Not required — a flat amount is a constant the
   regression pins equal across both copies — but it removes the last place two files hold one number.
6. **Tests:** `test/purchases.quote.test.js` (32 cases today) needs flat cases: a flat SKU quotes with
   `rate: null`, quotes with the rate oracle **down**, and `ceil`s the sale; and the domination test's
   premise re-read now that the shelf's price is SKR.
7. **Deploy is NOT automatic** — `vercel.json` sets `git.deploymentEnabled:false` (WO-1799 RESULT step 0).

⛔ **No client rebuild is needed for the PRICE** (the shipped APK prints the server figure, WO-1798 §6a).
A rebuild IS needed for §4's **display** change — that is what makes it a client ticket at all.

---

## 7. Acceptance

- [ ] `python tools/gate_brace.py <every .cs touched>` exit 0; Python NUL scan clean on each.
- [ ] Both `packs.json` copies: `json.loads` OK, sha256 identical to each other, CRLF == LF == CR, no BOM.
- [ ] `STORE_SKR_FLAT_CAPTURE_OK 2/2` on a **fresh** UTF-16 log; both PNGs opened and described.
- [ ] Greyscale contrast measured on the sale sign from the PNG itself (the owner is colourblind).
- [ ] The new suite registered in `DataRegression.cs` and green under `REGRESSION_OK`.
- [ ] `StorePiSkinCurrencyRegression`, `NightMarketNoWalletRegression`, `NightMarketUiRegression`,
      `BuyGateAndPriceLadderRegression`, `StoreReturnToManageRegression` all still green.

## 8. What NOT to touch

`api/**` (§6 is the lead's), `pricing.skr` (dead, leave it), the two canaries, `PurchaseGate`'s USD
threshold, the Pi and Google Play branches of `StorePriceMajor`/`StorePriceMinor`,
`RunNightMarketCaptureHeadless` / `RunNightMarketSaleCaptureHeadless` and their markers,
any `.unity` scene, `CLI_LANES_WO_NUMBERS.md`, `RemoteTunables.cs`.
