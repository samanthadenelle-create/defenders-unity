# WORK ORDER 1801 — The FIRST-SALE PATH

**Status:** IMPLEMENTED, NOT YET GATED

**Owner, 2026-09-16, verbatim:** *"yeah cause i just want a first sale you know"*

**Lane:** implementation. Number PRE-ASSIGNED by the lead — `CLI_LANES_WO_NUMBERS.md` was NOT touched.
**Parent spec:** `WorkOrders/WORK_ORDER_1798_packs_more_substantial_for_purchases.md` (P1 is implemented here).
**Gates:** no Unity gate, no commit, no board banner in this lane. `gate_brace` + NUL clean on every `.cs`;
JSON validity + newline proof on both `packs.json` copies; `node --test test/*.test.js` run (below).

---

## 0. The measurement this ticket is built on (the lead, production, 30 days)

`events_by_name`: `store_opened` **59** events by **3** ids; `pack_tapped` **54** by **2** ids, last
**2026-09-10**; `checkout_started` **9** by 2 ids; `checkout_failed` **3** by 1; `purchase_completed`
**3** by **ONE** id (the owner's own, Aug 25) — against **~25 distinct players/day**. `bundle_viewed`
fires per CARD BUILT (WO-1798 §0), so it is not a player action.

**So the funnel does not leak at the shelf. Almost nobody OPENS the store, and the two who tapped a
pack did not buy.** Two things follow, and they are the two halves of this ticket:

1. the cheapest rung must **complete something the player wants** (§1), and
2. the game must **offer it at the moment they want it** (§2) — which it did not, anywhere.

---

## 1. P1 — the $1.99 first buy completes its goal (DATA ONLY)

`builders-hour` (the `FIRST BUY` badge row, `$1.99`) granted **wood 600 / iron 300 / stone 300**.
The Barracks BUILD costs **wood 600 + iron 320** — read at source 2026-09-16 from
`Assets/Resources/Data/Canonical/structures-catalog.json` `barracks.repo.cost`. The pack was the
Barracks build **minus 20 iron**: shaped like a goal it could not finish.

**Now: wood 700 / iron 360 / stone 300** + the unchanged 1× temporary-builder (6 h).

| Lane | Grant | Goal cost | Base bank cap | Verdict |
|---|---|---|---|---|
| wood | 700 | 600 | 2000 | covers, fits |
| iron | 360 | 320 | 2000 | covers, fits |
| stone | 300 | 0 | 2000 | fits |

Caps read at source from `Assets/Resources/Data/Canonical/storage-caps.json` `baseCap`
(wood/iron/stone 2000 each; crystals + coins uncapped by that file's own `_note`). Fitting the
**bare** base cap matters because a paid grant **bypasses** the cap
(`EconomyService.GrantSpendablePurchased` → `BankGrantKind.PurchasedOrPromised`), so an oversized
first basket lands in full and then the buyer's ordinary faucet clamps to zero — the purchase would
punish the buyer.

**⚠ IRON IS 360, NOT the 400 the spec proposed, and the reason is a real constraint, not caution.**
`impulse-iron-small` grants **400 iron at the same $1.99 anchor**. At 400 the first buy would match
that rung's entire basket and add wood + stone — **strictly dominating** it, which
`test/purchases.quote.test.js` *"no impulse rung is strictly dominated by another purchasable pack at
the same USD anchor"* fails by name. 360 covers the 320-iron goal with headroom and leaves the iron
rung the better **iron** buy at that price, which is what a ladder is for. (Flagged by the lead as a
gate pre-check; resolved without weakening the test.)

**Copy.** `name` → **"Raise the Barracks"** — `name` is the only goal-copy slot a browsing player
reads (WO-1798 §1c: the shelf card renders `name` + `contents`, never `tagline`). Kept to **18
characters** deliberately: at 22 ("Raise the Barracks Now") it would have been the longest
visible-row name in the catalogue by five glyphs, and the card's name label has never been
captured at that width - 18 sits inside the band already proven by "Permanent Builder" (17).
`tagline` (the
spotlight line) → *"Enough timber, iron and stone to raise the Barracks tonight - and a second crew
for six hours."* ASCII only.

**Unchanged on purpose:** `sku` (a live save key), `tier`, `pricing` (all rails), `storeBadge`,
`convenience`, and `USD_ANCHORS` — `'builders-hour': 1.99` at `api/_lib/purchase-catalog.js:99`,
opened 2026-09-16 and **needs no change** (the price did not move).

Files: `Assets/Resources/Data/Canonical/packs.json` + its `Assets/StreamingAssets/...` twin
(byte-identical), and the generated server mirror `api/_lib/sku-catalog.generated.json`, regenerated
with `node tools/gen-sku-catalog.mjs` because `test/admin.skus.view.test.js` pins the copy equal to
the canonical file.

---

## 2. Is the store OFFERED at the moment of need? — IT WAS NOT. Findings first.

Read at source 2026-09-16, before writing anything:

1. **`PackStore.FocusShortfall(label, missing)` — the "open the store on the remedy" seam — had
   ZERO callers anywhere in the tree.** It is an instance method on a `DeNelle.Wallet` type, and the
   shortfall context lives in `DeNelle.Village`, which cannot reference that assembly. The seam
   existed and was unreachable.
2. **The upgrade panel's WO-1037 shortfall plate is INERT BY CONSTRUCTION and says
   *"Coming soon - tap to dismiss"*** (`BuildingUpgradePanelMvvm.BuildShortfallBand`) — while
   `FeatureFlags.RealmStorePurchase` now returns **`true`** (`Assets/_Modules/Core/FeatureFlags.cs:751`)
   and three real purchases have settled. The highest-intent moment the economy produces is a dead
   end. **NOT fixed here** (that surface is the upgrade lane's, and its own header says the routing
   must be a new, deliberate change) — recorded as the single cheapest remaining win.
3. **`ShortfallPackOffer` cannot resolve the first-buy pack and must not be widened to.** It rejects
   any pack with more than one economy key or any convenience item (WO-947 §12c guardrails 1 + 3);
   the first buy is deliberately a multi-lane basket + a crew charge. A separate resolver was written
   instead; `ShortfallPackOffer` is untouched.
4. **The 20% is a SERVER grant and the client cannot know it at show time.** `reasonHint
   'repair_shortfall'` earns **2000 bps, once per wallet per 7 days**
   (`api/purchases/quote.js:85-111, :343-361`), and the public LIST quote deliberately excludes it
   (`quote.js:308` — *"the till adds the shortfall if this player is owed one"*).
5. **`api/events/track.js` applies NO event-name allowlist** — it skips only a blank name — so the
   two new events land with **no server change**. (But `STORE_FUNNEL_EVENTS` in `api/admin/stats.js`
   is a fixed list, so they appear in `events_by_name`, not inside the `store_funnel` view. Not
   changed here: `api/` is WO-1799's lane this session.)

### 2b. The door that was built

**One door, one pack, in the build-mode side only** — `BuildStructureInfoPanel`, the panel that opens
when the player TAPS a palette card, shows the multi-resource build cost, and carries the
"Place Structure" button. That is where a player decides they want the Barracks and finds they cannot
pay. (`BuildPreviewModal` was rejected as the site: it has **no live caller** — only the capture
harness. `BuildFeedbackToast` is a non-tappable auto-dismissing toast.)

The line reads: **`"Short 320 Iron - raise it now for $1.99"`** — the measured gap, then the
**authored** price. It opens the store focused on the pack, with the gap handed across.

**⚠ DELIBERATE DEVIATION FROM THE BRIEF'S COPY, stated rather than quietly reinterpreted
(CLAUDE.md §11B):** the brief specified *"Raise it now - $1.99 (20% off today)"*. **The "(20% off
today)" is not printed.** Per finding 4 the client cannot prove a discount when the row is painted,
and the claim would be flatly **wrong** for any player who already spent their 7-day window. What is
implemented instead: the door passes the shortfall context to the till, the till applies whatever
discount the server owns, and the confirm line renders the **server's own** `discountLabel`. The
regression `[door-no-invented-discount]` now fails if a future edit re-introduces a percentage
client-side. If the owner wants the words on the door, the honest version needs the LIST quote to
carry a per-viewer discount — a server change, not a copy change.

**The no-nag rules, each a line of code in `FirstBuyDoorModel`:**

| Rule | Implementation |
|---|---|
| ONE pack | `FirstBuyOffer.Resolve()` reads the authored `FIRST BUY` badge. No SKU literal, no upsell. |
| Must close the gap **in every lane** | `FirstBuyOffer.Covers(...)` — all four lanes, or no door. |
| At most **once per session per structure** | static `HashSet<string>` by structure id, **not persisted** (same shape/reason as `BuildingUpgradePanelMvvm._dismissedOffers`). Marked when the row is actually PAINTED. |
| **Never during a wave** | `AmbientNPC.IsCombatActive` — the same combat authority the shops already close on (`DialogueCommandSink.ShopsClosedForCombat`). No second wave read; `WaveManager` untouched. |
| Only **before the first sale** | `FirstBuyOffer.HasPaidEntitlement()`, probing only rows with `pricing.usd > 0` so a redeemed `welcome-*` promo never counts as a purchase. |
| **Fail closed** | no purchase rail, no `PanelId.RealmStore` registrar in this artifact, no `EconomyService`, no pack, gap not covered, or ownership **unknown** ⇒ **no door**. |

---

## 3. Instrumentation

`shortfall_offer_shown` / `shortfall_offer_tapped` through `EventTracker.Track`, **exactly one emit
site each** (pinned by `[door-instrumented]`), with `{ sku, structure, resource, missing, door }`.
`shown` fires when the row is **painted**, never on a resolve that was thrown away — the
`bundle_viewed` mistake WO-1798 §0 named. Every show/hide and the tap also emit `[Flow:Store]` lines
carrying the **reason**, so a device log says which gate closed the door.

The store open is recorded under the existing funnel door name **`shortfall`** (already documented on
`StoreFocusRequest.RequestDoor`), so `store_opened_doors` gains a real value with no new vocabulary.

---

## 4. Regression — `FirstSalePathRegression` `[first-sale-path]`

`Assets/Editor/Regression/FirstSalePathRegression.cs`, wired into `DataRegression.RunAll`.
Markers `FIRST_SALE_PATH_OK` / `FIRST_SALE_PATH_FAIL`. **Not one cost or cap literal** — the Barracks
cost is read from `structures-catalog.json`, the caps from `storage-caps.json`, and the pack through
the same `FirstBuyOffer.Resolve()` the runtime door uses.

| Case | Pins |
|---|---|
| `[barracks-goal]` | the first-buy grant ≥ the goal's build cost in every priced lane |
| `[cap-fit]` | every granted lane ≤ the **base** bank cap (crystals/coins skipped — uncapped by design) |
| `[dominance]` | the first buy makes NO impulse rung pointless: it never grants >= a rung's whole basket in every data-derived lane while costing the same or less. Rung set, prices and lanes all read from `packs.json`; the JS suite's same-anchor rule remains the authority, this is a deliberate superset. (Rewritten after the hollow-pass scanner caught the first cut standing down on one named SKU - see the RESULT.) |
| `[door-no-nag]` | the session latch drives set → refuses → reset, and a resolve with no entry is silent |
| `[door-no-invented-discount]` | no `% off` / `20%` in the model's code |
| `[door-instrumented]` | exactly one emit site per event; both names still declared |

---

## 5. Files

**Data (both copies byte-identical, CRLF + newline count preserved, binary patch):**
- `Assets/Resources/Data/Canonical/packs.json`
- `Assets/StreamingAssets/Data/Canonical/packs.json`
- `api/_lib/sku-catalog.generated.json` (generated, never hand-edited)

**New code:**
- `Assets/_Modules/Commerce/FirstBuyOffer.cs`
- `Assets/_Modules/Village/BuildMode/FirstBuyDoorModel.cs`
- `Assets/Editor/Regression/FirstSalePathRegression.cs`

**Edited code:**
- `Assets/_Modules/Commerce/StoreFocusRequest.cs` — new `RequestShortfall` / `ConsumeShortfall` /
  `HasPendingShortfall` latch (same shape as the existing SKU + door latches).
- `Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs` — one hidden `Button` + the
  `RenderFirstBuyDoor` / `OnFirstBuyTapped` pair. The View paints and delegates; it reads no game
  state (the rule that failed `BuildPreviewModal` at the UI-MVVM oracle).
- `Assets/_Modules/Wallet/PackStore.cs` — **TWO methods only**, and neither is shelf-card rendering
  (WO-1800 owns those): **`OnEnable`** consumes the new cross-assembly shortfall latch into the
  existing `FocusShortfall`, before `LatchPiSpotlightOnOpen` / `TrackStoreOpened` which both read it;
  **`Purchase`** widens the `quoteReason` condition so the non-impulse first-buy pack also earns the
  `repair_shortfall` hint when a real gap opened the store. The literal
  `"_pendingShortfallMissing > 0"` that `StorePiSkinCurrencyRegression` lints for is preserved.
- `Assets/Editor/Regression/DataRegression.cs` — one registration line.

**NOT touched:** `SafeZoneRecovery.cs`, `WaveManager.cs`, `SmartEnemySpawner.cs`, `RemoteTunables.cs`,
`HeroAbilities.cs`, `Troops/**`, `SmartMobileCamera.cs`, `CLI_LANES_WO_NUMBERS.md`, any `api/*.js`
source, `ShortfallPackOffer.cs`, and PackStore's card builders.

---

## 6. ⛔ The risk that outranks everything in this ticket

**WO-1386 (owner, 2026-09-04) makes EVERY price wallet-gated on the Solana, Pi and Unknown channels**
— `PurchaseGate.RequiresWallet` returns true for any channel that is not Google Play, at any price
(read at source: `Assets/_Modules/Wallet/PurchaseGate.cs:150-158`). So on the Seeker build a **guest**
who taps this door is routed to the wallet-connect surface, not to a completable $1.99 checkout
(`PackStore.RouteGuestShortfallToWalletConnect`, which this door correctly arms). That routing is
honest, and it is still a door where there was none — but **if the first sale does not arrive, the
next thing to examine is that ruling, not this pack.** WO-1798 §0 reached the same conclusion from
the other direction. Owner ruling, not a lane decision.

---

## 7. Not proven by this lane

- **No Unity gate was run** (this lane does not hold Unity): `COMPILE_GATE_OK` and
  `REGRESSION_OK <n>/<n>` are **unproven**. `gate_brace` (the port of the gate's own scanner) and the
  NUL scan are clean on all 7 `.cs` files, which is not the same thing as compiling.
- **The door has never been seen on a device or in a capture.** Whether the row reads well inside the
  info panel at phone width is a felt/visual judgement and needs the owner's eyes (or a capture).
- **Whether the widened `quoteReason` actually earns 2000 bps in production** was not exercised
  against the live endpoint — the server-side eligibility (7-day window, per-wallet) is untested from
  here.
- **`ConvenienceRedeemer` behaviour is unchanged and unexamined** — the convenience list was not edited.
- **`FirstBuyOffer.HasPaidEntitlement` fails closed on a Google Play artifact**, where no local
  entitlement writer is registered (`PackGrantBridge.HasApplier` false by design). Consequence,
  stated rather than hidden: **the door does not appear on a Play build.** The shipping artifacts
  today are Seeker/dApp Store and Pi, both of which register the writer.
- The production counts in §0 are the lead's, read from the database this session; this lane did not
  re-query them.
