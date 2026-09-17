# WORK ORDER 1798 — Make the packs substantial enough to buy

**Status:** SPEC - owner ruling required

**Owner, 2026-09-16, verbatim:** *"how do we make the packs better. we need to make them more substantial to get purchases"*
**Owner, same day, on the SKR rail (verbatim):** *"what if we drop the conversion and only do straight SKR for SKR build 100 200 300 500 9999"*

**Lane:** design proposal, read-only. No code, no data edited by this ticket. Every number below was opened at source 2026-09-16; the file:line is on the claim.

---

## 0. READ THIS BEFORE RULING ON CONTENTS — the funnel already exists, and it changes the question

The six-step store funnel is **shipped and already surfaced**. `STORE_FUNNEL_EVENTS` = `store_opened, bundle_viewed, pack_tapped, checkout_started, checkout_failed, purchase_completed` (`api/admin/stats.js:219-222`), rendered as `store_funnel` with fixed 7d/30d windows plus `checkout_failed_reasons` and `store_opened_doors` (`api/admin/stats.js:910-918`). Every step has exactly one emit site (`PackStore.cs:476, 530, 542`, plus `GooglePlayStorefront.cs:95-108` for the Play door).

Two consequences that fork this whole design:

1. **`bundle_viewed` is not a player action.** It fires once per CARD BUILT (`PackStore.cs:2302`, inside the card builder), not per tap. Nine rows are visible on the shelf (§1), so 1-13 store opens/day mechanically produces 12-78 `bundle_viewed`/day — the owner's measured range reconciles exactly. **The first genuinely player-driven step is `pack_tapped`** (`PackStore.cs:530`, emitted from `FocusPack` on a tap).
2. **⛔ THE LIKELIEST CAUSE IS THE TILL, NOT THE CONTENTS.** WO-1386 (owner 2026-09-04, quoted at `packs.json:375`) made *nothing* guest-buyable on the Solana rail — **including $1.99**. `wallet-required` is one of exactly four `checkout_failed` reasons (`PackStore.cs:560`). If that reason dominates the 7d window, then re-authoring pack contents cannot move revenue, because no player is reaching a completable checkout.

**ACCEPTANCE STEP 0 (do this before the owner rules on anything below):** paste the 7d `store_funnel.steps` counts + `checkout_failed_reasons` + `store_opened_doors` from `/api/admin/stats.js?view=economy` into this ticket. The design answer forks on **where the first zero is**. This lane cannot read the database.

---

## 1. Inventory of every pack as shipped

Source: `Assets/Resources/Data/Canonical/packs.json` (930 lines, 29 packs; the `Assets/StreamingAssets/Data/Canonical/packs.json` twin is byte-identical at 930 lines). Loader/schema: `Assets/_Modules/Commerce/PackCatalog.cs`. Shelf filter is the single `storeVisible` flag (`packs.json:17`).

### 1a. The nine rows a player can browse (`storeVisible: true`)

| SKU | line | USD | wood | iron | crystals | stone | coins | convenience | badge |
|---|---|---|---|---|---|---|---|---|---|
| `builders-hour` | :344 | 1.99 | 600 | 300 | — | 300 | — | 1x temporary-builder (6 h) | FIRST BUY |
| `impulse-wood-medium` | :569 | 2.99 | 3500 | — | — | — | — | — | — |
| `impulse-iron-medium` | :648 | 2.99 | — | 1200 | — | — | — | — | — |
| `impulse-stone-medium` | :728 | 2.99 | — | — | — | 3500 | — | — | — |
| `starters-hand` | :307 | 4.99 | 4000 | 2000 | 400 | 1500 | 600 | 2x lantern x2 runs | BEST START |
| `folks-thanks` | :85 | 9.99 | 9000 | 4500 | 900 | 3400 | 1400 | 2x lantern x5 runs | — |
| `permanent-builder` | :902 | 9.99 | — | — | — | — | — | permanent-builder (+1 crew) | — |
| `patron-of-elarion` "Resource Pack I" | :120 | 19.99 | 18500 | 9200 | 1850 | 7000 | 2800 | 3x lantern x5 runs | BEST VALUE |
| `founders-vow` "Resource Pack II" | :156 | 49.99 | 46000 | 23000 | 4600 | 17500 | 7000 | 3x lantern x12 runs | none (retired, :189) |

### 1b. The twenty rows a player cannot browse

- **9 shortfall-only impulse SKUs** (wood/iron small+large, stone small+large, crystals small/medium/large) — reachable only via `ShortfallPackOffer`; exactly three medium rungs were promoted to the shelf by owner ruling 2026-08-21 (`packs.json:597`).
- **9 hidden paid rows**: `hearth-spark` (:29, strictly dominated by `starters-hand` at the same $4.99, :55), `keepers-satchel` (:58, blocked on the `harvest_boost` redeemer), `frostfall-bundle` (:193), `embergrove-bundle` (:231), `bloomtide-bundle` (:269), `echo-patron-pack` (:378), `hero-wardrobe-pack` (:424), `realm-defender-bundle` (:462), `builders-cache` (:500). **Five of those nine are hidden for the same reason: `"vapor-dominant (cosmetic-led, art does not render)"`** (:228, :266, :304, :459, :497).
- **2 promo-only**: `welcome-500` (:857), `welcome-100` (:879).

### 1c. What the shelf card actually shows the player

The card model is built at `PackStore.cs:2313-2340`: `Name`, `Contents`, `ValueCaption`, `PriceMajor/Minor`, `Badge`, `StateWord`, `NotSellableReason`. **`Contents` is raw amounts** — `DescribeContents` (`PackStore.cs:3595-3646`) emits `"9,000 wood, 4,500 iron +3 more"`, two items then a `+N more` tail. **`tagline` is NOT on the card** — it renders only inside the spotlight (`PackStore.cs:2622`). ⚠ Several `_shelfNote`s in packs.json state *"PackStore renders name + tagline only"* (e.g. :117, :153, :190); that is **stale** — the card renders `name`, never `tagline`. **Therefore the one data-only home for goal copy that a browsing player reads is `name`.**

Prices per rail: `usd`/`usdc`/`sol` are authored per pack; **`skr` is dead data except for two canaries** (§6). There is no `pi` field — Pi is quoted live (`api/pi/quote.js:7-24`).

### 1d. The stage this has to serve, and whether a pack even fits the bank

| Fact | Value | Source |
|---|---|---|
| New-game wood / iron seed | **0 / 0** | `NestedTypes.cs:82, :84` |
| New-game gold seed | 200 | `NestedTypes.cs:110` |
| Town bank base cap, wood / iron / stone | **2000 each** | `storage-caps.json` `baseCap` |
| Crystals + coins | **UNCAPPED by design** | `storage-caps.json` `_note` |
| Container ladder (capacity AT level N) | 1000 / 2000 / 4000 / 8000 / 16000 / 32000 | `storage-caps.json` `levelCapacityMultipliers` + `_ladderNote` |
| Max wood ceiling, fully maxed Lumberyard | 2000 + 32000 = **34000** | same two rows |
| Barracks BUILD cost | wood **600** + iron **320** | `structures-catalog.json` `barracks.repo.cost` |
| Barracks upgrade ladder (wood) | T1 **1490** / T2 3260 / T3 6600 / T4 12330 / T5 18650 / T6 28350 | `building-tiers.json:37,39,41,43,44,45` |
| Lumberyard L1→L2 (doubles the wood ceiling) | wood **1200** + iron **480** | `structures-catalog.json` `lumberyard.repo.upgradeCost[0]` |
| Foundry L1→L2 / Silo L1→L2 | 1440w+720i / 1440w+360i | same, `foundry` / `silo` |
| Faucet, wood per hour | Lumbermill L1 **720**, L3 1646, L5 3960 | `ResourceBuildingProgression.cs:321-322` |
| Faucet, iron per hour | Forge L1 **432**, L3 1029, L5 2520 | `ResourceBuildingProgression.cs:329-331` |
| Faucet, stone per hour | Quarry L1 **936**, L3 2160, L5 5220 | `ResourceBuildingProgression.cs:311` |
| Build concurrency vs queue depth | `freeBuildSlots` **2** / `queueDepthPerLine` **5** | `BuildTimerConfig.cs:231, :269` |
| Pack temporary-builder window | 6 h (`packTemporaryBuilderSeconds`) | `BuildTimerConfig.cs:246` |
| Instant-finish price | 1 crystal/min, min 10, curve exp 0.75; max job 24 h | `BuildTimerConfig.cs:128, :131, :209, :59` |
| ⇒ one full 24 h skip | **1440 crystals** at the anchor | `BuildTimerConfig.cs:177-182` |

**Do paid resources fit? They bypass the cap, and that is the tail nobody has priced.** `GrantSpendablePurchased` → `GrantPurchased` → `BankGrantKind.PurchasedOrPromised` (`EconomyService.cs:564, :397-398`), and `GrantInternal` only clamps when `TownBankCapacity.IsClampable(kind)` (`EconomyService.cs:425, :433-441`). So the advertised figure lands in full — **and then the player's FAUCET clamps to zero** until they spend back under cap, because ordinary income *is* clampable on the same lines.

| Pack | wood granted | vs 2000 base cap | vs 34000 maxed ceiling |
|---|---|---|---|
| `builders-hour` 600 | 0.3x | **fits** | fits |
| `impulse-wood-medium` 3500 | 1.75x | overflows until Lumberyard L2 (4000) | fits |
| `starters-hand` 4000 | 2.0x | overflows | fits |
| `folks-thanks` 9000 | 4.5x | overflows | fits |
| `patron-of-elarion` 18500 | 9.3x | overflows | fits |
| `founders-vow` 46000 | **23x** | overflows | **⛔ overflows even a MAXED town** |

⛔ **`founders-vow` at $49.99 is the worst-designed row on the shelf**: 46000 wood cannot be held by any legal save, the buyer's wood faucet goes dead the moment it lands, and WO-1165 §4 already found this rung is *worse* value per dollar than the $19.99 beneath it (`packs.json:189`, 1962 vs 1968 goods/$). Its badge was correctly removed rather than replaced. It has nothing left that makes it worth $49.99.

⛔ **And the $1.99 FIRST BUY misses its one nameable goal by 20 iron.** `builders-hour` grants wood 600 / iron 300 (`packs.json:363-365`); the Barracks costs wood 600 / iron **320**. It is *exactly* the Barracks build minus 20 iron — the pack cannot complete the one thing its basket is obviously shaped like.

---

## 2. Against the CoC value ladder (tie-breaker memory `design-tiebreaker-what-would-coc-do`)

| CoC pattern | Our shelf | Verdict |
|---|---|---|
| The $4.99 starter completes a NAMED goal in one tap ("finishes your second builder") | every row advertises raw tonnage; `DescribeContents` prints `"9,000 wood, 4,500 iron +3 more"` (`PackStore.cs:3595`) | **MISSING — this is the headline gap** |
| Storage first: a pack raises your CEILING before it fills your wallet | no pack touches capacity; six of nine rows overflow the base cap (§1d) | **MISSING, and actively inverted** |
| First pack = best value; big packs = prestige, not tonnage | ladder is a straight resource multiple; `founders-vow` is literally 23x the holdable ceiling | **INVERTED — the whale rung is just more tonnage, which the pricing memory forbids** |
| Time compression is the paid good | present and healthy: `permanent-builder` ($9.99, concurrency), `builders-hour` (6 h crew), crystals→instant-finish | **OK, and under-merchandised** |
| Cosmetics carry the prestige | five rows hidden because `"art does not render"`; `cosmetics.json` rows carry only `previewColor` (no art path) | **BLOCKED on assets, not on design** |

Standing constraints this proposal obeys: whale tier = permanence + prestige, **never a better army** (memory `solana-store-early-access-pack-pricing`); cost-basket separation — regular = wood+iron, magical = crystals (memory `cost-basket-separation-ruling`), and the PURCHASE boundary is legally distinct from the COST boundary (`packs.json:15`, WO-947 §12 amendment), so a multi-resource *basket* pack is legal while a multi-resource *impulse* pack is forbidden; entitlement SKUs are live save keys and are never renamed without `legacySkus` (`packs.json:21`).

---

## 3. Five proposed redesigns

On-card copy goes in **`name`** (the only goal-copy slot a browsing player reads, §1c); the sentence goes in `tagline`, which the spotlight renders. Every basket below is sized to **complete a named goal and still fit the ceiling at the stage it targets**.

### P1 — `builders-hour` re-pointed: **"Raise the Barracks"** — $1.99
`wood 700, iron 400, stone 300` + 1x temporary-builder (6 h).
**Goal:** builds the Barracks outright (600w + 320i) with slack, and the 6 h crew means it goes up beside whatever is already building (`freeBuildSlots` 2 → 3 for the window).
**Fit:** 700 < 2000, 400 < 2000, 300 < 2000 — **fits the bare base cap on a brand-new save.** Earn-equivalent 600w+320i ≈ 0.8 h Lumbermill L1 + 0.7 h Forge L1.
**Card:** `"Raise the Barracks"` / tagline *"The muster yard, up tonight — and a second crew for six hours."*
**Cost:** data-only in packs.json (amount edit; SKU, price and `USD_ANCHORS` row unchanged).

### P2 — NEW `store-double` : **"Double Your Wood Store"** — $2.99
`wood 1300, iron 550`.
**Goal:** pays the Lumberyard L1→L2 upgrade (1200w + 480i) in one tap, which takes the wood ceiling **2000 → 4000**. This is the pack that *fixes* the overflow problem instead of causing it, and it is the CoC "storage first" rung we do not have.
**Fit:** 1300 < 2000 and 550 < 2000 — fits pre-upgrade, by construction.
**Card:** `"Double Your Wood Store"` / *"The Lumberyard goes up a level. Twice the wood before it spills."*
**Cost:** new SKU → packs.json row **plus** a `USD_ANCHORS` row (`api/_lib/purchase-catalog.js:83-119`, mirror-law tested). No new code path.

### P3 — NEW `long-shift` : **"Skip Tonight's Build"** (the time pack) — $4.99
`crystals 1500` + 2x temporary-builder (6 h each).
**Goal:** 1440 crystals is **exactly one full 24 h instant-finish** at the authored anchor (`BuildTimerConfig.cs:177-182`), so the pack's promise is literally its arithmetic; the 1500 leaves slack for the rounding curve.
**Fit:** crystals are **uncapped by design** (`storage-caps.json` `_note`) — this pack can never overflow anything, at any stage. That makes it the safest rung on the shelf.
**Card:** `"Skip Tonight's Build"` / *"One whole day off the clock, and two extra crews' worth of evenings."*
**Cost:** data-only row + `USD_ANCHORS`. ⚠ **Needs one check:** whether `ConvenienceRedeemer` honours `count: 2` on `temporary-builder` as two sequential 6 h windows. `packs.json:13` says a second grant inside a running window is *deferred, never burned*, which implies yes — **not proven by this lane.**

### P4 — `founders-vow` re-pointed: **"Patron's Charter"** — $49.99, founderOnly
Replace the 46000/23000 tonnage dump with: `permanent-builder` (+1 concurrent crew, permanent), the three storage-container L1→L2 baskets as resources (`wood 4200, iron 1600` — the sum of lumberyard 1200+480, foundry 1440+720, silo 1440+360), `crystals 6000`.
**Goal:** the whale rung stops being a wallet and becomes **permanence + headroom** — one crew forever, and a town that can *hold* what it earns. Per the pricing memory this is the only legal shape for the top rung, and it is not a better army: concurrency and capacity are convenience, never combat power.
**Fit:** 4200 wood exceeds the 2000 base cap but a $49.99 buyer is not on a bare save; at Lumberyard L2 (4000+2000=6000) it fits. **Crystals uncapped.** Nothing here can exceed 34000.
**Cost:** data-only amounts + convenience list. ⚠ A *title* or *named banner* is the natural third component and is **deliberately omitted** — `packs.json:189` records exactly this trap: the previous badge promised players would be "named on the Heart" with **no implementation anywhere**. Build the thing first, author the badge second.

### P5 — NEW `mage-wayfarer` : the Mage cosmetic pack — $4.99 — **NOT data-only, do not promise in the window**
The hackathon video is built around the **Mage** (`WorkOrders/WORK_ORDER_1776_mage_kit_vfx_lightning_on_arcane_bolt.md:13`).
**Described by silhouette and motion, not colour** (owner is colourblind): a **taller hooded silhouette** — high collar, long trailing hem that lifts and settles on each cast — plus **one slow mote that orbits the staff hand** and briefly accelerates outward on the cast beat. No hue is load-bearing; the read is shape + movement, legible in greyscale.
⛔ **Cost is CODE, not data.** `cosmetics.json` has 37 items and **every one carries only `previewColor`, no art path** — the three mage rows included (`hero-mage-embergrove`, `hero-mage-wanderer`, `cosmetic.embergrove-bundle.hero-outfit`). `CosmeticDef` has `meshPath` (`CosmeticCatalog.cs:62`) but **no VFX/trail field at all** (`CosmeticCatalog.cs:32-66`). So the silhouette half needs a mesh asset + a `meshPath` row; the orbiting-mote half needs a **new schema field and applier code**. This is exactly why five cosmetic packs are already hidden as `"art does not render"`. **Rank last; unblocking it unblocks five existing hidden SKUs too, which is the real prize.**

---

## 4. Instrumentation to prove conversion — it already exists; read it

| Step | Exists? | Emit site |
|---|---|---|
| `store_opened {door}` | ✅ | `PackStore.cs:512-528` (one site) + `GooglePlayStorefront.cs:95-108` |
| `bundle_viewed` | ✅ but **per card built, not per action** | `PackStore.cs:2302` |
| `pack_tapped {sku, section, priceUsd}` | ✅ | `PackStore.cs:530-538` |
| `checkout_started {sku, channel, rail}` | ✅ fires **before** the gates, on purpose | `PackStore.cs:542-556` |
| `checkout_failed {reason}` | ✅ four reasons, incl. `wallet-required` | `PackStore.cs:558-560` |
| `purchase_completed` | ✅ | `PackStore.cs:4065, :4608` |

Reporting surface: `?view=economy` → `store_funnel` (7d/30d, zero-filled, never omitted) + `checkout_failed_reasons` + `store_opened_doors` (`api/admin/stats.js:857-918`). Server-verified truth is a **separate** view by design — `?view=purchases`, sourced from `purchase_entitlements`/`purchase_quotes`, never from `analytics_events` (`api/admin/stats.js:925-938`).

**The only gap worth a ticket:** `bundle_viewed` counts impressions of *cards we built*, so it can never drop below `store_opened × visible rows`. If the owner wants a true view→tap rate, the honest denominator is `pack_tapped / store_opened`, not `/ bundle_viewed`. **No new event is needed.**

---

## 5. Ranked by implementation cost; what fits the two-week video window

| # | Change | Cost | Two-week window? |
|---|---|---|---|
| 1 | **P1** amounts + `name`/`tagline` on `builders-hour` | packs.json only (existing SKU, price and anchor untouched) — needs an **APK**, because packs.json ships in `Assets/Resources` | ✅ ride the next build |
| 2 | Re-word the `name` on all nine visible rows to goal copy | packs.json only; same APK | ✅ same build as #1 |
| 3 | **P4** re-point `founders-vow` (amounts + convenience list, SKU and price unchanged) | packs.json only; same APK | ✅ same build |
| 4 | **P2**, **P3** new SKUs | packs.json + a `USD_ANCHORS` row each (`purchase-catalog.js:83-119`) + mirror tests; APK **and** a server deploy | ✅ if minted with #1-#3 |
| 5 | Fix the guest/wallet till if step 0 shows `wallet-required` dominating | policy ruling (WO-1386) + code | ⚠ owner ruling first; **highest revenue leverage of anything here** |
| 6 | **P5** Mage cosmetic | mesh asset + `meshPath`, **plus** a new schema field + applier code for the motion | ❌ not in the window |

⛔ **"Data-only" never means "build-free" here.** packs.json ships inside the APK via `Assets/Resources`, so every row above needs a build; only §6's SKR number is genuinely server-side.

---

## 6. The owner's flat-SKR proposal — *"only do straight SKR ... 100 200 300 500 9999"*

### 6a. What the SKR rail does today
- **The server prices; the client does no arithmetic.** `api/purchases/quote.js` has two modes: `LIST` (public, unauthenticated, binds nothing — exists *so the card can print an exact SKR figure without the client doing arithmetic*, `quote.js:16-47`) and `QUOTE` (one binding, single-use, expiring row in `purchase_quotes`, the artefact `/verify` checks the chain against).
- **The rate** is fetched from `https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&ids=seeker`, and the field used is **`low_24h`** (`purchase-catalog.js:136-137, :320`), cached 120 s, 8 s timeout, **fail-closed**: rate unavailable → 503 with the worded refusal *"We could not read a live SKR price just now, so we will not quote one. Nothing has been charged."* (`quote.js:72-75`). Rounding is `ceil(usd / usdPerSkr)` and it deliberately favours us (`purchase-catalog.js:241-253`).
- **⛔ WHAT THE CARD SHOWS IS THE SERVER QUOTE, NOT `packs.json`.** `SolanaPackPricing.AmountFor(CurrencyKind.Skr)` returns `PurchaseQuoteService.SkrAmountFor(sku)` and **returns 0 rather than falling back to `pricing.skr`**, because the authored figure is *"a stale hand-typed figure from before the SKR rail existed at a real rate"* (`SolanaPackPricing.cs:56-72`). 0 renders as the words **"Price unavailable"**, and `WalletService.Pay` refuses ≤ 0. The **two canaries are the sole exception** and keep their authored number, pinned by exact equality (`SolanaPackPricing.cs:72, :88-90`). The confirm line shows **two numbers, with the tilde on the DOLLARS** — the SKR is exact, the USD floats (`PackStore.cs:2321-2327`, owner ruling 2026-08-23).
- The stale peg is visible in the data: `packs.json:18` authors `skrPerUsd ≈ 12.5` (25/36/60/120/240/600 SKR). **Live is ~57 SKR/USD (§6b) — the authored table is ~4.6x low and would undercharge if anything ever honoured it.** Nothing does, except the canaries.

### 6b. The flat-SKR design, priced at today's rate
A server table of fixed SKR per SKU, no rate lookup on the SKR rail; the USD anchor stays for USDC / SOL / Play / Pi.

**Rate fetched once for this ticket, 2026-09-16 (same coingecko id `seeker` that `quote.js` uses):** `low_24h` = **$0.01745041** (the field the code reads), `current_price` = $0.01866522, `last_updated` `2026-09-16T21:48:20Z`.

| Rung | USD at `low_24h` | Nearest current anchor | Suggested identity (per the pricing memory) |
|---|---|---|---|
| **100 SKR** | **≈ $1.75** | $1.99 `builders-hour` | the first-buy micro (P1) |
| **200 SKR** | **≈ $3.49** | $2.99 impulse mediums | the goal packs (P2) |
| **300 SKR** | **≈ $5.24** | $4.99 `starters-hand` / P3 | the starter + the time pack |
| **500 SKR** | **≈ $8.73** | $9.99 `permanent-builder` | permanence: +1 crew forever |
| **9999 SKR** | **≈ $174.55** | *above* the $49.99 ceiling | **the prestige rung** — permanence + headroom + identity, per the $99+ prestige band the pricing memory records as planned. ⛔ **Never resources and never a better army.** Shape it like P4, scaled: the permanent crew, the full storage-ceiling headroom, and a named identity **only once a title system exists** (`packs.json:189` is the record of promising one that did not). |

The ladder is well-formed: 1.75 / 3.49 / 5.24 / 8.73 / 174.55 keeps roughly the current rung spacing and puts a real gap before the prestige tier, which is what a prestige tier is for.

### 6c. Risks
1. **⛔ VOLATILITY IS THE WHOLE RISK, AND THE SAME PAYLOAD SIZES IT.** Seeker's ATH this year is **$0.055818** (2026-01-21) and its ATL **$0.00542271** (2026-01-20) — *one day apart*. A fixed **100 SKR** pack has therefore been worth **$5.58 and $0.54** inside a single year, a **10x swing**, with no deploy in between. A fixed-SKR shelf means the token, not us, sets the price; today's `price_change_percentage_24h` alone is **+6.0%**. The current design exists precisely to stop client and server holding two opinions about a moving number (`quote.js:9-14`).
2. **Rate still needed for the approx line.** The confirm/card pair shows SKR *and* `~ $X` (`PackStore.cs:2321-2327`). Flat SKR removes the rate from the **charge**, not from the **display** — either the `~ $` line keeps a rate lookup (and can still 503), or the owner rules it dropped, which removes the transparency she herself asked for on 2026-08-23.
3. **Verification path is fine IF the quote pins the amount.** `/verify` checks the settled transfer against the persisted quote's `amountBaseUnits`, so a quote carrying a *fixed* number verifies exactly as a rate-derived one does — **no change to the verify law**. ⛔ **But do NOT implement flat SKR as exact-equality pinning like the canaries**: that route requires `IsServerPinnedSku` on the **client** (`SolanaPackPricing.cs:88-90`) to learn the new SKUs, and its own comment warns that a server-pinned SKU missing from that list is *"a silent paid-but-not-granted bug that only fires when the market crosses the price."* Route flat SKUs through the **normal quote path with a pinned amount** and the client needs no change at all.
4. **Number source ⇒ build requirement.** The shipped APK prints the **server** figure (§6a), so a flat table is **server-only: no APK needed for the price**. `packs.json`'s `skr` field would remain dead; it should be left alone rather than "corrected", since correcting it creates a second opinion about the same number.
5. **Store policy:** grepped `docs/SOLANA_STORE_LISTING.md` and `docs/SOLANA_STORE_READINESS_2026-08-06.md` — **no documented Solana Mobile / dApp Store rule about SKR-priced goods was found.** The only SKR pricing item there is the historical 1-SKR test-pricing hazard (`SOLANA_STORE_READINESS_2026-08-06.md:85-87`). Also note SKR is Solana Mobile's **governance token, not ours** — a payment rail we convert out through, never a balance we hold (memory `skr-is-solana-mobile-governance-token`).

### 6d. Implementation shape, cost, and the recommendation
**Server-only** if done as pinned-amount quotes: a fixed SKR table beside `USD_ANCHORS` in `api/_lib/purchase-catalog.js`, `buildQuoteBody` skipping `fetchSkrUsdRate` for those SKUs, plus mirror/refusal tests. **No APK, no client change** (that is the design's main attraction). It becomes **needs-APK** only if the approx-dollar line changes shape or if exact-equality pinning is chosen (risk 3). Whether it can share WO-1799's sale-knob mechanics is **not assessable here — no WO-1799 file exists on disk** (highest 179x present is `WORK_ORDER_1796_ad_revenue_visibility_ilrd_to_dashboard.md`).

**Recommendation (owner rules):** the ladder is good and the rungs are well spaced — **but adopt it as a DISPLAY/CHARGE ladder derived from USD, not as fixed SKR**, i.e. keep the USD anchor authoritative and quote `ceil(usd/rate)` exactly as today, while re-anchoring the USD rungs so the *typical* SKR figures land near 100/200/300/500/9999. That gives her the clean round numbers she is after on the card most of the time, without handing pricing control to a token that moved 10x in two days (risk 1). If she wants genuinely fixed SKR anyway, do it server-side via pinned-amount quotes (never canary-style equality) and accept that the real-money price of every pack now floats with the market.

---

## 7. Owner questions

1. **Step 0 first:** shall the lead paste the 7d funnel + `checkout_failed_reasons` before you rule on contents? If `wallet-required` dominates, the WO-1386 guest rule — not pack contents — is the revenue blocker. **Do you want that rule revisited for the $1.99/$2.99 rungs?**
2. **P1/P2/P3/P4 — approve, amend, or drop?** P4 rewrites what a $49.99 buyer receives; the current row grants 46000 wood that **no legal save can hold**.
3. **Goal copy in `name`:** OK to rename shelf rows to the goal (`"Raise the Barracks"`, `"Double Your Wood Store"`)? `name` is display-only — `sku` is the save key and is untouched.
4. **P5 Mage cosmetic:** is the silhouette+motion described in §3 right, and do you want the mesh authored (which also unblocks five hidden cosmetic packs), or should P5 wait?
5. **Flat SKR:** fixed table, or the USD-anchored version of your ladder (§6d recommendation)? And does the `~ $X` approx line stay?
6. **9999 SKR rung (~$175):** confirm it is permanence + headroom + identity only. Any named title must be **built before it is advertised** (`packs.json:189`).

---

## Unverified / could not be proven by this lane

- **The live funnel counts and `checkout_failed_reasons`.** No DB access from here; the WO-1386 hypothesis in §0 is a *hypothesis*, sized from code, not measured.
- Whether `ConvenienceRedeemer` honours `count > 1` on `temporary-builder` as sequential windows (P3). `packs.json:13` implies yes; not read at the redeemer.
- Whether the owner's live production funnel view is `?view=economy` on the deployed `api/admin/stats.js` at the same revision read here.
- No troop-training cost was read, so no claim is made about an "army stage" resource need beyond the Barracks build/upgrade ladder.
- WO-1461 (raid cache) is cited only as *existing*; its mechanics were not read.
- Rate figures in §6b are one coingecko call at 2026-09-16 21:48Z. They will be wrong tomorrow — that is the point of §6c.1.
- ⚠ **Stale copy found, flagged not fixed:** `packs.json:15` cites a Barracks ladder of `costWood 500..16000` (live is **1490..28350**, `building-tiers.json:37-45`) and repeats the superseded `$5` ceiling memory; the `_shelfNote`s at :117, :153, :190 claim the card renders `tagline` (it does not, §1c). None were edited by this read-only lane.
