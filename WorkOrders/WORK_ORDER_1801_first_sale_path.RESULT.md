# WORK ORDER 1801 — RESULT

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Branch:** dev (working tree; nothing committed by this lane)

---

## What changed

**1. The $1.99 first buy now completes the Barracks** — `builders-hour`
wood 600 → **700**, iron 300 → **360**, stone 300 (unchanged), name → **"Raise the Barracks"**,
tagline re-pointed at the goal, `_shelfNote` records the ruling + both numbers + why iron is capped.
SKU / tier / every price / badge / convenience untouched. `USD_ANCHORS` needed **no** change
(`'builders-hour': 1.99`, `api/_lib/purchase-catalog.js:99`, opened this session).

**2. A store door now exists at the moment of need** — on `BuildStructureInfoPanel` (the panel that
opens when a palette card is tapped and shows the build cost), a single gold row
`"Short 320 Iron - raise it now for $1.99"` appears when — and only when — the first-buy pack would
close **every** lane of the gap. It opens the store focused on that pack, hands the gap across, and
earns the server's `repair_shortfall` hint at the till.

**3. Two funnel events** — `shortfall_offer_shown` / `shortfall_offer_tapped`, one emit site each.

**4. A regression** — `[first-sale-path]`, six cases, zero cost/cap literals.

---

## Proofs (each measured this session)

**Data — binary patch, both copies, newline shape preserved.** ⚠ These files are **CRLF**, not LF —
measured before the edit, so the patch carried `\r\n` and the proof pins CR as well as LF:

```
Assets/Resources/Data/Canonical/packs.json:       bytes 52187 -> 53565 ; LF 930 (unchanged) ; CRLF 930 ; CR 930 ; no BOM
Assets/StreamingAssets/Data/Canonical/packs.json: bytes 52187 -> 53565 ; LF 930 (unchanged) ; CRLF 930 ; CR 930 ; no BOM
sha256 (identical across both copies): 6b61f0fbe8a5907c748eeef10e8f4925a383e84f2f30675f16517c82671ae08e
json.loads: OK on both
```

**Authored values re-read at source, not from any doc:**
- Barracks BUILD cost `wood 600 / iron 320` — `Assets/Resources/Data/Canonical/structures-catalog.json`,
  `barracks.repo.cost`. ⚠ The brief named `building-tiers.json`; that file holds the **upgrade
  ladder** (T1 already 1490 wood), not the build cost. Corrected, and the regression reads the
  catalog.
- Base bank cap `wood/iron/stone 2000` — `storage-caps.json` `baseCap`; crystals + coins **uncapped**
  by that file's own `_note`.
- `$1.99` rungs, parsed from the patched file: `builders-hour` 700w/360i/300st (+1 convenience),
  `impulse-wood-small` 1000w, `impulse-iron-small` **400i**, `impulse-stone-small` 1000st,
  `impulse-crystals-small` 250c.

**Server mirror regenerated** (never hand-edited): `node tools/gen-sku-catalog.mjs` →
`api/_lib/sku-catalog.generated.json` (29 packs, 52666 bytes).

**JS suite:** `node --test test/*.test.js` → **tests 761, pass 760, fail 0, todo 1**. The single
non-green is the pre-existing `heartbound-contract.test.js` todo (WO-1693 finding 2,
`recordVerifiedStake names no streak column`) — untouched by this lane. The two suites this ticket
could break are green: `purchases.quote.test.js` **32/32** (including *"no impulse rung is strictly
dominated"*) and `admin.skus.view.test.js` **23/23** (including the canonical-copy parity and the
LF/no-BOM check on the generated mirror).

**C# gates:**
```
python tools/gate_brace.py <7 files>  ->  GATE_BRACE_SUMMARY bad=0 of 7   (exit 0)
NUL scan (\x00) on the same 7 files   ->  0 in every file  (NUL_CLEAN)
raw brace counts balanced in all 7
every runtime string literal in the new files is 7-bit ASCII (checked outside comments)
```

**The gate pre-check the lead flagged, resolved:** iron 400 would have **strictly dominated**
`impulse-iron-small` (400 iron, same $1.99 anchor) and turned `purchases.quote.test.js` red. Fixed by
sizing the first buy's iron to **360** — still covering the 320-iron goal with headroom — rather than
weakening the test or moving another pack's price. `[iron-ceiling]` in the new suite pins the reason
in-tree.

---

## Findings worth the lead's attention (read at source, 2026-09-16)

1. **`PackStore.FocusShortfall` had ZERO callers in the entire tree.** The "open the store on the
   remedy" seam existed and was unreachable from the assembly that owns the gap. This ticket gives it
   its first caller, through a new rail-neutral latch.
2. **⛔ The upgrade panel's shortfall plate says *"Coming soon - tap to dismiss"* while
   `FeatureFlags.RealmStorePurchase` returns `true`** (`FeatureFlags.cs:751`) and three purchases have
   settled. The single highest-intent moment in the economy is a dead end, and it is the cheapest
   remaining win in the whole first-sale problem. **Not fixed here** — that surface belongs to the
   upgrade lane and its own header requires the routing to be a deliberate, separate change.
3. **`api/events/track.js` has no event-name allowlist** (it skips only a blank name), so the new
   events need no server deploy. But `STORE_FUNNEL_EVENTS` in `api/admin/stats.js` is a fixed list,
   so they will show in `events_by_name`, **not** inside the `store_funnel` view. Adding them there is
   a two-line `api/` change this lane deliberately did not make (WO-1799 owns `api/` this session).
4. **⛔ WO-1386 outranks this ticket for revenue.** `PurchaseGate.RequiresWallet` returns true for
   every non-Google-Play channel at **every** price (`PurchaseGate.cs:150-158`), so on the Seeker a
   guest tapping this door lands on wallet-connect, not a completable $1.99 checkout. The door is
   still right, and it arms the existing guest route honestly — but if no first sale arrives, that
   ruling is the next thing to look at, not the pack contents.
5. **`ShortfallPackOffer` correctly rejects the first-buy pack** (multi-key + convenience, WO-947
   §12c) and was left alone; a separate, narrower resolver was written instead.

## Deliberate deviation from the brief, stated up front (CLAUDE.md §11B)

The brief asked for the door copy *"Raise it now - $1.99 (20% off today)"*. **The percentage is not
printed.** The 2000 bps shortfall discount is issued server-side at the till, once per wallet per 7
days, and the public LIST quote deliberately excludes it (`api/purchases/quote.js:308`), so the client
cannot know it when the row is painted — and the claim is simply **wrong** for a player who already
spent their window. The door prints the authored USD; the confirm line renders the server's own
`discountLabel` after the quote returns. `[door-no-invented-discount]` fails if a percentage is ever
composed client-side. If the owner wants the words on the door, the honest route is to carry a
per-viewer discount on the LIST quote — a server change.

## Late checks and hardenings (after the first pass, all proven by reading)

- **The spotlight DOES open on the first-buy pack, not on the impulse rung.** `PackStore.ResolveFocusSku`
  gives the `StoreFocusRequest.Consume()` SKU **first precedence** and only falls through to
  `ShortfallPackOffer.Resolve(...)` when no SKU was requested. My door sets both latches, so
  `builders-hour` wins and `impulse-iron-small` is never spotlighted. Read at source; **no third
  PackStore method was needed.** ⚠ Pre-existing and worth knowing: with a shortfall latched, the card
  loop also admits the MATCHING impulse rung onto the shelf, so the store can show two remedies (the
  first-buy spotlight plus an iron row). That is WO-1037 behaviour, unchanged by this ticket.
- **The registration line is FULLY QUALIFIED.** `DataRegression.cs` is `namespace DeNelle.Editor` and
  carries no `using DeNelle.Editor.Regression;`, so the bare call would have been CS0103. Fixed to
  `DeNelle.Editor.Regression.FirstSalePathRegression.Run(...)`.
- **The entitlement probe is now the LAST gate**, after the integer gap arithmetic: it walks every
  priced row and builds a `PackStoreVM` per probe, and paying that on every tap of every unaffordable
  card was needless.
- **`Tap()` re-checks `AmbientNPC.IsCombatActive`** before opening, because a wave can start between
  the row being painted and the finger landing; it refuses with the same sentence
  `DialogueCommandSink` already shows ("Shops closed during the assault!").
- **Known, pre-existing shape now true for one more SKU:** `_pendingShortfallLabel/Missing` are never
  cleared after a store open, so a LATER open (e.g. from the HUD) can still send `repair_shortfall`
  for the first-buy pack in the same session. The server owns eligibility and rate-limits it to once
  per wallet per 7 days, so nothing can be over-granted — but the hint is now reachable for a
  non-impulse SKU, which it was not before. Stated, not fixed (clearing it is a PackStore lifecycle
  change and belongs with whoever owns that file next).

## Hollow-pass fix (lead's 19:08 regression, `Builds/data-regression.log`)

`REGRESSION MARKER FAIL (2)` named two guards in `FirstSalePathRegression.cs` — `:311`
`[A-missing-dependency]` (`rung == null`) and `:320` `[B-says-skip]` (the anchor comparison). Both were
real: the case hung the whole dominance check on **one named SKU** and stood down in two places,
logging a SKIP to the reader while reporting a PASS to the caller. It would have gone quiet exactly
when the constraint became interesting — a renamed or repriced rung.

**Resolved under the three-way rule, by deleting the stand-downs rather than annotating them:**

- `[iron-ceiling]` is **retired and replaced by `[dominance]`**, and `CaseDoesNotDominateTheIronRung`
  is now `CaseDoesNotDominateAnImpulseRung`. The const `IronRungSku` is **gone** — no SKU name and no
  anchor is hardcoded anywhere in the case.
- The rung set (`impulse: true`), every price, and the **resource-key union** are derived from
  `packs.json` itself — the same way `test/purchases.quote.test.js` derives its keys, because a
  hand-kept lane list fails OPEN by checking one fewer key (how the food→stone rename slipped once).
- The rule is now **same-price-or-cheaper**, a deliberate superset of the JS suite's same-anchor rule
  (JS remains the authority on the shipped rule): the first buy must not grant ≥ a rung's whole basket
  in every lane while costing no more.
- **Fixture-absent ⇒ FAIL naming the path**, four times: unreadable `packs.json`, a parse error, no
  `packs[]` rows, no `impulse:true` rows (and the message says `ImpulsePackRegression` already
  requires the full family, so it is a data defect). No `PartialSkip` was used — nothing here is a
  missing HARNESS CAPABILITY; every dependency is authored data.
- **Content-absent ⇒ asserted through, never returned:** a rung cheaper than the first buy is logged
  as "not judged" and the loop continues, and a `judged == 0` outcome is **itself a named failure**
  ("every impulse rung is cheaper than the first buy, so nothing was judged"), so an all-out-of-scope
  ladder can never read as a pass.

**Swept the rest of the suite for the same shape:**
- `[barracks-goal]` and `[cap-fit]` had latent **arm-D vacuity** — every assertion sat inside
  `if (cost > 0)` / `if (granted > 0 && cap > 0)`, so an all-zero goal cost or a capless file would
  have asserted nothing and passed. Both now count what they will compare first and **FAIL by name**
  when that count is zero.
- The two `if (src == null) return;` guards are the scanner's documented **PRODUCER REPORTED IT**
  exoneration (`ReadSource` is handed `failures` and records the miss one statement earlier); a
  reason comment now sits at each site as well.
- `ReadFirstBuyGrant` / `ReadGoalCost` / `ReadBaseCaps` are value-returning private helpers (outside
  the scanner's verdict-method scope) and every failing path already records a failure by name.

Re-checked: `python tools/gate_brace.py` → `bad=0 of 1`, exit 0; NUL 0; braces 72/72; zero non-ASCII
outside comments. ⚠ **Unproven from here:** the scanner itself is Unity editor code, so that the two
findings are actually cleared is judged against its documented exonerations, not measured — the
lead's next `DataRegression` run on a fresh log is the proof.

## Unproven / owed

- **No Unity gate.** `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n>` are **unproven**; `gate_brace` +
  NUL clean is not the same as compiling. `DataRegression.RunAll` gains one suite, so the `<n>/<n>`
  count moves.
- **Never seen on a device or in a capture** — whether the row reads well in the info panel at phone
  width is a felt/visual judgement (owner's eyes or a `UI_CAPTURE_OK` PNG).
- **The widened `quoteReason` was not exercised against the live endpoint**, so "the first buy now
  earns the 20%" is a code path, not a measured outcome.
- **The door does not appear on a Google Play artifact** — `PackGrantBridge` has no applier there, so
  the entitlement probe fails closed by design. Seeker/dApp Store and Pi both register it.
