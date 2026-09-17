'use strict';

// =============================================================================
// WO-1799 — the storewide sale. Owner 2026-09-16: "can we run a 30% deal?"
// -----------------------------------------------------------------------------
// Everything here is about ONE risk: a sale is a PRICE, and /verify runs AFTER
// the transfer settles. A sale that priced a quote the verifier will not accept
// is a purchase that fails with the money already gone — the same family as the
// 6-vs-9 decimals near-miss, arriving through the marketing door.
//
// So these cases prove, in order:
//   1. the knob resolves, clamps and expires (a sale you cannot stop is not a sale);
//   2. the combination with the shortfall discount is MAX, never a sum;
//   3. the discounted amount is EXACTLY what the verifier accepts, and the
//      full-price anchor is REFUSED;
//   4. the Google Play rail is excluded because its prices live in Play Console;
//   5. the sale bps never reaches a phone through the public tunables endpoint.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const sale = require('../api/_lib/store-sale');
const catalog = require('../api/_lib/purchase-catalog');
const tunables = require('../api/_lib/tunables');
const { _test: quoteTest } = require('../api/purchases/quote');
const { _test: verifyTest } = require('../api/purchases/verify');
const { _test: publicTunables } = require('../api/client-tunables');

const REPO = path.join(__dirname, '..');
const wallet = 'Wallet111111111111111111111111111111111111';
const recipient = 'Treasury11111111111111111111111111111111';
const recipientAta = 'TreasuryAta111111111111111111111111111111';
const devnetMint = '3BwWSAUZmyngXDSZiCawEnP7iLgY5ANNopBDz94AB77N';
const signature = 'S'.repeat(85);
const RATE = { usdPerSkr: 0.01, source: 'test' };

// ─────────────────────────────────────────────────────────────────────────────
//  WO-1818 — A SALE NOW HAS TWO PRICE BASES, SO A CASE MUST SAY WHICH IT MEANS.
// ─────────────────────────────────────────────────────────────────────────────
// ⛔ THESE CASES WERE WRITTEN AGAINST `impulse-wood-medium` AND HAD TO MOVE, NOT
// RELAX. That SKU now carries `pricing.skrFlat`, so its sale is applied to a flat
// SKR CONSTANT and the USD-anchor arithmetic asserted below stopped describing it.
// The sale law itself did not change — max-not-additive, ceil, 'sale' wording,
// verifier-reads-the-row — so the rate-path cases are repointed at a SKU that is
// still rate-derived, and the flat path gets its own case beside them.
//
// ⚠ 27 of the 29 packs[] rows are FLAT today, so the flat case is the one most
// buyers will actually meet. `monthly-wayfarer` (battle_monthly.json, not copied
// by tools/gen-sku-catalog.mjs) is the remaining rate-derived witness.
const RATE_SKU = 'monthly-wayfarer';    // usd 4.99, NO skrFlat
const RATE_SKU_USD = 4.99;
const FLAT_SKU = 'impulse-wood-large';  // usd 4.99 -> skrFlat 300

const DEVNET_ENV = {
    SOLANA_DEVNET_PURCHASE_RECIPIENT: recipient,
    SOLANA_DEVNET_PURCHASE_RECIPIENT_ATA: recipientAta,
    SOLANA_DEVNET_SKR_MINT: devnetMint,
};

function withEnv(vars, fn) {
    const old = { ...process.env };
    Object.assign(process.env, vars);
    try { return fn(); }
    finally {
        for (const key of Object.keys(process.env)) if (!(key in old)) delete process.env[key];
        Object.assign(process.env, old);
    }
}

/** A minimal finalized SPL transfer carrying `amount` base units. */
function transaction(amount, decimals = 9) {
    return {
        slot: 42, blockTime: Math.floor(Date.now() / 1000),
        meta: { err: null },
        transaction: { message: {
            accountKeys: [{ pubkey: wallet, signer: true, writable: true }],
            instructions: [{ program: 'spl-token', parsed: { type: 'transferChecked', info: {
                authority: wallet, destination: recipientAta, mint: devnetMint,
                source: 'SourceAta111111111111111111111111111111111',
                tokenAmount: { amount, decimals, uiAmount: 0, uiAmountString: amount },
            } } }],
        } },
    };
}

async function readChain(result, contract) {
    const previous = global.fetch;
    global.fetch = async () => ({ ok: true, json: async () => ({ jsonrpc: '2.0', result }) });
    try { return await verifyTest.readFinalizedTransfer('https://rpc.invalid', signature, wallet, contract); }
    finally { global.fetch = previous; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  1. THE KNOB — on, off, clamped, expiring
// ─────────────────────────────────────────────────────────────────────────────

test('the resting state of the knob table is NO SALE', () => {
    // ⛔ An EMPTY table is the documented resting state of client_tunables. If the
    // absence of a row meant anything other than "full price", every deploy would
    // be one missing row away from giving the store away.
    assert.equal(sale.resolveSale({}, Date.now()).bps, 0);
    assert.equal(sale.resolveSale(undefined, Date.now()).bps, 0);
    assert.equal(sale.resolveSale({ 'store.saleBps': '0' }, Date.now()).bps, 0);
});

test('the owner 30% deal reads as 3000 bps and a "30% off" label', () => {
    const live = sale.resolveSale({ 'store.saleBps': '3000' }, Date.now());
    assert.equal(live.bps, 3000);
    assert.equal(sale.saleLabel(3000), '30% off');
    // The label is DERIVED, not stored: the rail is int-only, so there is no row a
    // typo could land a wrong percentage in beside a right one.
    assert.equal(sale.saleLabel(2500), '25% off');
});

test('the sale is CLAMPED at both ends — a fat thumb cannot give the store away', () => {
    assert.equal(sale.resolveSale({ 'store.saleBps': '30000' }, Date.now()).bps, sale.SALE_MAX_BPS);
    assert.equal(sale.SALE_MAX_BPS, 7000, 'the ceiling is 70% off');
    // A NEGATIVE must never become a surcharge, and junk must never become a sale.
    for (const junk of ['-3000', 'abc', '', '   ', null, undefined, {}]) {
        assert.equal(sale.resolveSale({ 'store.saleBps': junk }, Date.now()).bps, 0,
            'junk bps ' + String(junk));
    }
});

test('an end time in the PAST ends the sale without anybody being awake', () => {
    const nowMs = 1_800_000_000_000;
    const endedMin = Math.floor((nowMs - 60_000) / 60_000);
    const futureMin = Math.floor((nowMs + 3_600_000) / 60_000);
    const ended = sale.resolveSale(
        { 'store.saleBps': '3000', 'store.saleEndsAtEpochMin': String(endedMin) }, nowMs);
    assert.equal(ended.bps, 0, 'an expired window charges the ordinary price');
    assert.equal(ended.expired, true);
    const running = sale.resolveSale(
        { 'store.saleBps': '3000', 'store.saleEndsAtEpochMin': String(futureMin) }, nowMs);
    assert.equal(running.bps, 3000);
    assert.equal(running.expired, false);
    assert.equal(running.endsAtMs, futureMin * 60_000);
});

test('the end time is epoch MINUTES because the rail refuses epoch SECONDS', () => {
    // ⛔ THE REASON THE UNIT IS ODD, PROVEN RATHER THAN ASSERTED IN A COMMENT.
    // normalizeValue accepts at most nine digits, and epoch seconds is ten — so a
    // seconds value is REFUSED AT WRITE TIME, which would look like the operator
    // mistyping a key. Minutes fits, and also fits a C# int.
    const nowSec = 1_800_000_000;
    assert.equal(tunables.normalizeValue('store.saleEndsAtEpochMin', String(nowSec)), null,
        'epoch seconds must be refused, which is why the key names minutes');
    assert.equal(tunables.normalizeValue('store.saleEndsAtEpochMin', String(Math.floor(nowSec / 60))),
        '30000000');
    assert.equal(tunables.normalizeValue('store.saleBps', '3000'), '3000');
    assert.equal(tunables.normalizeValue('store.saleBps', 'thirty'), null);
});

// ─────────────────────────────────────────────────────────────────────────────
//  2. THE COMBINATION RULE — MAX, NEVER ADDITIVE
// ─────────────────────────────────────────────────────────────────────────────

test('the sale REPLACES the shortfall discount when larger — never adds to it', () => {
    const shortfall = quoteTest.SHORTFALL_DISCOUNT_BPS;      // read, never retyped
    const server = quoteTest.SHORTFALL_REASON_SERVER;
    assert.equal(shortfall, 2000, 'the shortfall figure moved; re-read this case');

    const both = sale.resolveDiscount(3000, shortfall, server);
    assert.equal(both.bps, 3000, '3000 + 2000 = 5000 would be a pack nobody authored');
    assert.equal(both.reason, sale.SALE_REASON);

    // The SMALLER sale loses to an eligible shortfall, and the reason follows the
    // number — a row that says 'sale' under a shortfall figure is an unreadable audit.
    const shortfallWins = sale.resolveDiscount(1000, shortfall, server);
    assert.equal(shortfallWins.bps, shortfall);
    assert.equal(shortfallWins.reason, server);

    // A TIE goes to the sale, which leaves the player's once-per-7-days shortfall
    // window UNSPENT. Recording a sale as a shortfall would burn an entitlement
    // the player never used.
    const tie = sale.resolveDiscount(2000, shortfall, server);
    assert.equal(tie.reason, sale.SALE_REASON);

    // Neither: no discount at all, and null rather than 0 — a formatter would
    // happily print "0% off".
    assert.deepEqual(sale.resolveDiscount(0, null, server), { bps: null, reason: null });
    assert.deepEqual(sale.resolveDiscount(0, 0, server), { bps: null, reason: null });

    // Sale alone, shortfall alone.
    assert.deepEqual(sale.resolveDiscount(3000, null, server),
        { bps: 3000, reason: sale.SALE_REASON });
    assert.deepEqual(sale.resolveDiscount(0, shortfall, server),
        { bps: shortfall, reason: server });
});

test('a SALE quote does not spend the shortfall window, in SQL and not only in prose', () => {
    // ⛔ THE BLIND SPOT THIS CASE EXISTS FOR. Both shortfall predicates keyed on
    // `discount_bps IS NOT NULL` alone. The moment a sale persists a discount_bps,
    // every buyer during the sale would read as "discounted recently" and be denied
    // a genuine shortfall discount for SEVEN DAYS AFTER the sale ended — a silent
    // punishment for having bought during a promotion.
    const source = fs.readFileSync(path.join(REPO, 'api/purchases/quote.js'), 'utf8');
    const windowChecks = source.match(/discount_bps IS NOT NULL/g) || [];
    assert.equal(windowChecks.length, 2, 'the two shortfall window predicates');
    const reasonScopes = source.match(/AND discount_reason = \$\{SHORTFALL_REASON_SERVER\}/g) || [];
    assert.equal(reasonScopes.length, 2,
        'BOTH window predicates must be scoped to the shortfall reason, or a sale ' +
        'silently consumes every buyer\'s once-per-seven-days discount');
});

test('a storewide sale is NOT rate-limited — the second buyer pays the sale price too', () => {
    // The serializable once-per-window transaction is the SHORTFALL's authority. A
    // sale routed through it would discount the first pack of the sale and nothing
    // after it, which is not a sale.
    const source = fs.readFileSync(path.join(REPO, 'api/purchases/quote.js'), 'utf8');
    assert.match(source, /const gateOnWindow = discountReason === SHORTFALL_REASON_SERVER;/);
    assert.match(source, /if \(discountBps != null && gateOnWindow\) \{/,
        'the window gate must apply to a shortfall only');
    // And a shortfall that loses the race falls back to the STOREFRONT, not to full
    // price. ⚠ WO-1833 renamed the function this line pins: the fallback now routes
    // through resolveEffectiveDiscount, which composes the promo-overrides-sale
    // precedence with this file's max-vs-shortfall law. The LAW being pinned is
    // unchanged — a lost race must never land the player at full price — and the promo
    // argument is load-bearing in it: a player holding a 30% promo who also asked for
    // a shortfall discount must not pay full freight because they lost a race.
    assert.match(source,
        /resolved = resolveEffectiveDiscount\(promoBps, sale\.bps, null, SHORTFALL_REASON_SERVER\);/);
    // The ungated INSERT must persist whatever the body carries, not a hardcoded NULL.
    assert.match(source, /\$\{built\.discountBps\}, \$\{built\.discountReason\}/,
        'the ordinary insert must carry a sale, or a sale quote persists at full price');
});

// ─────────────────────────────────────────────────────────────────────────────
//  3. THE PRICED QUOTE, AND WHAT THE VERIFIER ACCEPTS
// ─────────────────────────────────────────────────────────────────────────────

test('a 30% sale prices a RATE-DERIVED quote off the USD anchor and labels it "30% off"', () => {
    withEnv(DEVNET_ENV, () => {
        const full = catalog.buildQuoteBody('devnet', RATE_SKU, RATE);
        const sold = catalog.buildQuoteBody('devnet', RATE_SKU, RATE,
            3000, sale.SALE_REASON);
        assert.equal(full.amountBaseUnits, '499000000000', '$4.99 at $0.01/SKR = 499 SKR');
        assert.equal(sold.amountBaseUnits, '350000000000', '$3.493 ceil()s to 350 SKR');
        assert.equal(sold.usdAnchor, RATE_SKU_USD, 'the authored anchor stays auditable');
        assert.ok(Math.abs(sold.usdEffective - 3.493) < 1e-9);
        assert.ok(Math.abs(sold.usdSaving - 1.497) < 1e-9);
        assert.equal(sold.discountBps, 3000);
        assert.equal(sold.discountReason, sale.SALE_REASON);
        assert.equal(sold.discountLabel, '30% off',
            'a sale buyer must never be told they received a "shortfall discount"');
        // The shortfall keeps its own wording on the SAME code path.
        const shortfall = catalog.buildQuoteBody('devnet', RATE_SKU, RATE,
            2000, quoteTest.SHORTFALL_REASON_SERVER);
        assert.equal(shortfall.discountLabel, '20% shortfall discount');
    });
});

test('a 30% sale on a FLAT SKU discounts the SKR constant, with no USD in the answer', () => {
    // ⛔ WO-1818, AND THIS IS THE PATH 27 OF 29 PACKS TAKE. The discount is applied
    // to the authored SKR amount and ceil()ed to a whole SKR — the same rounding
    // direction as the rate path, so the shelf figure and the confirm figure cannot
    // land a token apart. No USD appears in the answer at all.
    withEnv(DEVNET_ENV, () => {
        const flat = catalog.skrFlatFor(FLAT_SKU);
        assert.ok(flat > 0, `${FLAT_SKU} lost its skrFlat: this case is testing the rate path`);
        const full = catalog.buildQuoteBody('devnet', FLAT_SKU, RATE);
        const sold = catalog.buildQuoteBody('devnet', FLAT_SKU, RATE, 3000, sale.SALE_REASON);
        assert.equal(full.skrAmount, flat, 'the undiscounted flat price is the authored constant');
        assert.equal(sold.skrAmount, Math.ceil(flat * 0.7));
        assert.equal(sold.discountBps, 3000);
        assert.equal(sold.discountReason, sale.SALE_REASON);
        assert.equal(sold.discountLabel, '30% off');
        assert.equal(sold.usdEffective, null, 'a flat sale must not publish a second, USD price');
        assert.equal(sold.usdSaving, null);
        assert.equal(sold.usdAnchor, catalog.usdAnchor(FLAT_SKU), 'the band stays auditable');
        // ⚠ A LIVE RATE WAS PASSED IN AND MUST HAVE BEEN IGNORED. This is the
        // assertion that fails if the flat branch ever falls back to the oracle.
        assert.equal(sold.rate, null);
        assert.equal(sold.rateSource, catalog.FLAT_RATE_SOURCE);
        // And the sale must survive the oracle being gone entirely.
        const offline = catalog.buildQuoteBody('devnet', FLAT_SKU, null, 3000, sale.SALE_REASON);
        assert.equal(offline.skrAmount, sold.skrAmount);
        assert.equal(offline.amountBaseUnits, sold.amountBaseUnits);
        const wired = quoteTest.wireQuote(offline, { quoteId: 'q-flat' });
        assert.equal(wired.saleBps, 3000, 'a flat pack must still advertise the sale');
        assert.equal(wired.saleLabel, '30% off');
    });
});

test('the VERIFIER accepts the DISCOUNTED amount and REFUSES the full-price anchor', async () => {
    // ⭐ THE ACCEPTANCE CRITERION THAT MATTERS MOST. /verify checks the chain against
    // the quote ROW's own amount_base_units (contractFromQuoteRow), never the USD
    // anchor — so a sale needs no change there at all. This proves it rather than
    // assuming it, because being wrong here means a paid player with no entitlement.
    const sold = withEnv(DEVNET_ENV, () =>
        catalog.buildQuoteBody('devnet', RATE_SKU, RATE, 3000, sale.SALE_REASON));
    const row = {
        quote_ref: 'a'.repeat(32), wallet, sku: RATE_SKU, network: 'devnet',
        currency: 'SKR', amount_base_units: sold.amountBaseUnits, decimals: 9,
        mint: devnetMint, recipient, recipient_ata: recipientAta,
        usd_anchor: '4.9900', usd_rate: '0.010000000000', rate_source: 'test',
        discount_bps: 3000, discount_reason: sale.SALE_REASON,
        expires_at: new Date(Date.now() + 300_000).toISOString(),
        consumed_at: null, consumed_tx: null,
    };
    const contract = catalog.contractFromQuoteRow(row);
    assert.equal(contract.amountBaseUnits, '350000000000');
    assert.equal((await readChain(transaction('350000000000'), contract)).state, 'verified',
        'the sale price the server quoted must be the sale price the server accepts');
    // Paying the FULL price against a sale quote is still a mismatch: it is not the
    // contract we issued, and overpaying is not a lesser evil than underpaying.
    assert.equal((await readChain(transaction('499000000000'), contract)).reason,
        'transfer_contract_mismatch');

    // And the pure row-usability checks are indifferent to the discount columns.
    assert.deepEqual(verifyTest.evaluateQuoteRow(row, wallet, RATE_SKU,
        'devnet', signature), { ok: true });

    // ── THE SAME PROOF ON THE FLAT PATH (WO-1818) ────────────────────────────
    // ⭐ The point being proven is that /verify needed NO change for flat pricing:
    // it accepts whatever amount the quote ROW carries. A flat sale row is just a
    // different number in the same column, and its usd_rate is 0 with
    // rate_source='flat-skr' because the column is NOT NULL and no rate exists.
    const flatSold = withEnv(DEVNET_ENV, () =>
        catalog.buildQuoteBody('devnet', FLAT_SKU, null, 3000, sale.SALE_REASON));
    const flatRow = Object.assign({}, row, { sku: FLAT_SKU,
        amount_base_units: flatSold.amountBaseUnits, usd_anchor: '4.9900',
        usd_rate: '0.000000000000', rate_source: catalog.FLAT_RATE_SOURCE });
    const flatContract = catalog.contractFromQuoteRow(flatRow);
    assert.equal(flatContract.amountBaseUnits, flatSold.amountBaseUnits);
    assert.equal((await readChain(transaction(flatSold.amountBaseUnits), flatContract)).state,
        'verified', 'the flat sale price the server quoted must be the one it accepts');
    // The UNDISCOUNTED flat amount is now the wrong amount, and must be refused.
    const flatFull = withEnv(DEVNET_ENV, () => catalog.buildQuoteBody('devnet', FLAT_SKU, null));
    assert.equal((await readChain(transaction(flatFull.amountBaseUnits), flatContract)).reason,
        'transfer_contract_mismatch');
    // ⚠ And the stored 0 is never echoed outward as a real rate.
    assert.equal(verifyTest.ledgerRate(flatRow), null);
    assert.equal(verifyTest.ledgerRate(row), '0.010000000000');
});

test('a sale never produces a free, negative or nonsense quote', () => {
    withEnv(DEVNET_ENV, () => {
        // 10000 bps would be a free pack; the clamp stops it long before here, and
        // buildQuoteBody refuses it a second time. Defence in depth on the money path.
        for (const bad of [10_000, 20_000, -1, 0, NaN, '3000']) {
            const built = catalog.buildQuoteBody('devnet', RATE_SKU, RATE,
                bad, sale.SALE_REASON);
            assert.equal(built.amountBaseUnits, '499000000000', 'bad bps ' + bad);
            assert.equal(built.discountBps, null, 'bad bps ' + bad);
            assert.equal(built.discountReason, null, 'bad bps ' + bad);
            // WO-1818: and the flat path refuses it too — a junk bps must never give
            // away a pack whose price is a constant either.
            const flat = catalog.buildQuoteBody('devnet', FLAT_SKU, null, bad, sale.SALE_REASON);
            assert.equal(flat.skrAmount, catalog.skrFlatFor(FLAT_SKU), 'bad bps ' + bad + ' (flat)');
            assert.equal(flat.discountBps, null, 'bad bps ' + bad + ' (flat)');
            assert.equal(flat.discountReason, null, 'bad bps ' + bad + ' (flat)');
        }
        assert.equal(sale.clampSaleBps(10_000), sale.SALE_MAX_BPS,
            'the clamp lands below the free-pack boundary, not on it');
    });
});

test('the canaries are never on sale — their amount is a protocol constant', () => {
    const pinned = quoteTest.wirePinned({
        sku: catalog.DEVNET_CANARY_SKU, network: 'devnet', currency: 'SKR',
        amountBaseUnits: 25_000_000_000, decimals: 9, mint: devnetMint,
        recipient, recipientAta,
    });
    assert.equal(pinned.saleBps, null);
    assert.equal(pinned.saleLabel, null);
    assert.equal(pinned.pinned, true);
    assert.equal(pinned.amountBaseUnits, '25000000000',
        'a discount on a proof-of-rail would refuse the very proof it exists to be');
});

// ─────────────────────────────────────────────────────────────────────────────
//  4. THE WIRE — what a shelf and a confirm line can read
// ─────────────────────────────────────────────────────────────────────────────

test('the wire carries saleBps/saleLabel only when the discount IS a sale', () => {
    withEnv(DEVNET_ENV, () => {
        const sold = catalog.buildQuoteBody('devnet', RATE_SKU, RATE,
            3000, sale.SALE_REASON);
        const wired = quoteTest.wireQuote(sold, { quoteId: 'q1' });
        assert.equal(wired.saleBps, 3000);
        assert.equal(wired.saleLabel, '30% off');
        assert.equal(wired.usdEffective, sold.usdEffective);
        assert.equal(wired.usdSaving, sold.usdSaving);
        assert.equal(wired.amountBaseUnits, '350000000000');

        // A SHORTFALL is a discount and NOT a sale: a shelf banner must not announce
        // one player's private apology as a storewide promotion.
        const shortfall = catalog.buildQuoteBody('devnet', RATE_SKU, RATE,
            2000, quoteTest.SHORTFALL_REASON_SERVER);
        const wiredShortfall = quoteTest.wireQuote(shortfall, { quoteId: 'q2' });
        assert.equal(wiredShortfall.saleBps, null);
        assert.equal(wiredShortfall.saleLabel, null);
        assert.equal(wiredShortfall.discountBps, 2000, 'the discount itself still ships');

        // An undiscounted row says null, never 0.
        const plain = quoteTest.wireQuote(
            catalog.buildQuoteBody('devnet', RATE_SKU, RATE), { quoteId: 'q3' });
        assert.equal(plain.saleBps, null);
    });
});

test('the wire field NAMES match the client that reads them, letter for letter', () => {
    // ⛔ TWO LANES, ONE CONTRACT, AND A TYPO HERE IS SILENT. The client half of this
    // feature was written in parallel (WO-1800) and parses these three by name with
    // Newtonsoft — an unmatched JsonProperty does not throw, it simply leaves the
    // field null, so a misspelling ships as "the sale never appeared" with no error
    // anywhere. Pinned by NAME rather than trusted.
    const client = fs.readFileSync(
        path.join(REPO, 'Assets/_Modules/Wallet/PurchaseQuoteService.cs'), 'utf8');
    const server = fs.readFileSync(path.join(REPO, 'api/purchases/quote.js'), 'utf8');
    for (const field of ['saleBps', 'saleLabel', 'saleEndsAt']) {
        assert.match(client, new RegExp('JsonProperty\\("' + field + '"\\)'),
            'the client does not parse ' + field);
        assert.match(server, new RegExp('\\b' + field + ':'),
            'the server does not send ' + field);
    }
    // And the older discount pair is untouched — the sale is ADDITIVE to that wire,
    // never a rename of it. A rename would blank the confirm line on every installed
    // build until the next APK.
    assert.match(client, /JsonProperty\("discountBps"\)/);
    assert.match(client, /JsonProperty\("discountLabel"\)/);
});

test('the sale is felt at the CONFIRM line on TODAY\'S installed build', () => {
    // ⚠ THE HONEST SPLIT, READ AT SOURCE 2026-09-16, BECAUSE THE OWNER WILL LOOK AT
    // A SHELF FIRST. PackStore.StorePriceMajor / StorePriceMinor draw a card from
    // AUTHORED client data (pack.AmountLabel / pack.UsdReference / pack.UsdApprox) or
    // from the Pi/Play provider — the server LIST feeds only IsSellable and its
    // worded reason. The CONFIRM line does render DiscountLabel + UsdSavingLabel, so
    // that is where a sale shows up WITHOUT a new build. The shelf ribbon and the
    // countdown are WO-1800's client work and need an APK.
    const store = fs.readFileSync(path.join(REPO, 'Assets/_Modules/Wallet/PackStore.cs'), 'utf8');
    assert.match(store, /quote\.DiscountLabel/, 'the confirm line renders the server label');
    assert.match(store, /quote\.UsdSavingLabel/, 'the confirm line renders the server saving');
    assert.match(store, /pack\.AmountLabel\(_defaultCurrency\)/,
        'the card price still comes from authored pack data, not from the list row');
});

// ─────────────────────────────────────────────────────────────────────────────
//  5. THE RAILS THE SALE DOES AND DOES NOT REACH
// ─────────────────────────────────────────────────────────────────────────────

test('the sale reaches every SERVER-PRICED rail: SKR and Pi, through one helper', () => {
    for (const rel of ['api/purchases/quote.js', 'api/pi/quote.js']) {
        const src = fs.readFileSync(path.join(REPO, rel), 'utf8');
        assert.match(src, /require\('\.\.\/_lib\/store-sale'\)/,
            rel + ' must read the sale through the shared helper, never its own copy');
        assert.match(src, /readStoreSale\(sql\)/, rel + ' must read the live sale');
    }
    // ⛔ ONE READER OF THE KNOB TABLE. store-sale.js goes through readTunables, the
    // same function api/client-tunables.js serves from. A second reader is how two
    // halves of one system end up disagreeing about whether a sale is running.
    const helper = fs.readFileSync(path.join(REPO, 'api/_lib/store-sale.js'), 'utf8');
    assert.match(helper, /require\('\.\/tunables'\)/);
    assert.doesNotMatch(helper, /\b(SELECT|INSERT|UPDATE|neon\()\b/,
        'the sale helper issues no query of its own');
});

test('GOOGLE PLAY IS EXCLUDED, because Play Console owns that price', () => {
    // ⛔ NOT AN OVERSIGHT AND NOT FIXABLE HERE. A Play purchase is priced by the
    // in-app product in Play Console and charged by Google; the server never issues
    // an amount for it, so there is no number a sale could discount. Running the
    // owner's 30% deal on Play means editing the Play Console prices (or publishing
    // a Play sale), which is a store-console action, not a knob.
    //
    // Proven by ABSENCE: the Play rail carries no USD anchor and no quote amount.
    for (const rel of ['api/purchases/google-play-verify.js', 'api/_lib/google-play-purchases.js']) {
        const src = fs.readFileSync(path.join(REPO, rel), 'utf8');
        assert.doesNotMatch(src, /buildQuoteBody|usdAnchor|amount_base_units/,
            rel + ' prices nothing server-side, so a server sale cannot reach it');
        assert.doesNotMatch(src, /store-sale/, rel + ' must not pretend to apply a sale');
    }
    // And the client says the same thing: the Play card's price is the provider's.
    const store = fs.readFileSync(path.join(REPO, 'Assets/_Modules/Wallet/PackStore.cs'), 'utf8');
    assert.match(store, /Channel == PaymentChannel\.GooglePlay/);
});

test('there is no USDC or SOL purchase rail for a sale to reach', () => {
    // Recorded so a future seat does not go looking for one. The quoted currency is
    // SKR on the Solana rail and PI on the Pi rail; nothing else issues a quote.
    const src = fs.readFileSync(path.join(REPO, 'api/_lib/purchase-catalog.js'), 'utf8');
    assert.match(src, /currency: 'SKR'/);
    assert.doesNotMatch(src, /currency: 'USDC'|currency: 'SOL'/);
});

// ─────────────────────────────────────────────────────────────────────────────
//  6. THE SALE PERCENTAGE NEVER REACHES A PHONE
// ─────────────────────────────────────────────────────────────────────────────

test('the public tunables endpoint WITHHOLDS every serverOnly key', () => {
    // ⛔ A sale bps on a device is a SECOND authority about money on hardware we do
    // not control. It is withheld by the SERVER rather than merely "not read by the
    // client": what a client chooses to read is not a boundary.
    assert.ok(publicTunables.SERVER_ONLY_KEYS.has('store.saleBps'));
    assert.ok(publicTunables.SERVER_ONLY_KEYS.has('store.saleEndsAtEpochMin'));
    const served = publicTunables.publicValues({
        'store.saleBps': '3000',
        'store.saleEndsAtEpochMin': '30000000',
        'combat.drainReturnPct': '60',
    });
    assert.deepEqual(served, { 'combat.drainReturnPct': '60' },
        'a price must never appear in a public, unauthenticated payload');
});

test('the withheld set is DERIVED from the allowlist, never a second hand-typed list', () => {
    const expected = new Set(tunables.TUNABLE_KEYS
        .filter((s) => s.serverOnly === true).map((s) => s.key));
    assert.deepEqual([...publicTunables.SERVER_ONLY_KEYS].sort(), [...expected].sort());
    assert.equal(expected.size, 2, 'the two WO-1799 sale rows are the only serverOnly keys today');
    // The day a third serverOnly knob lands it is withheld automatically. A copy here
    // would have shipped its value to every phone — CLAUDE.md sections 2, 5 and 16.
    const src = fs.readFileSync(path.join(REPO, 'api/client-tunables.js'), 'utf8');
    assert.match(src, /TUNABLE_KEYS\.filter\(\(s\) => s && s\.serverOnly === true\)/);
    assert.doesNotMatch(src, /'store\.saleBps'/, 'no hand-typed key name in the exclusion set');
});

test('the sale keys stay OUTSIDE the TUNABLE_KEYS literal the Unity oracle pins', () => {
    // ⛔ THE PIN THAT KEEPS A UNITY GATE GREEN FROM A JAVASCRIPT-ONLY LANE.
    // Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs `Case4_KeyDomain`
    // reads THIS FILE from disk, matches `TUNABLE_KEYS = [ ... ];` with a regex, and
    // asserts the keys inside that literal are EXACTLY its pinned ExpectedDefaults —
    // reporting anything extra as an "UNPINNED key". A serverOnly key has no build
    // entry BY DESIGN, so writing it inside that literal REDS the Unity regression,
    // and this lane is forbidden to touch Assets/ to fix it.
    //
    // So the rows are APPENDED after the literal, and this case re-implements the C#
    // regex to prove it. If a future seat moves them back inside, this goes red HERE,
    // in two seconds, instead of at a 20-minute batchmode gate.
    const src = fs.readFileSync(path.join(REPO, 'api/_lib/tunables.js'), 'utf8');
    const region = src.match(/TUNABLE_KEYS\s*=\s*\[([\s\S]*?)\]\s*;/);
    assert.ok(region, 'the C# oracle would fail to find the TUNABLE_KEYS array at all');
    const literalKeys = [...region[1].matchAll(/key\s*:\s*'([^']+)'/g)].map((m) => m[1]);
    assert.deepEqual(literalKeys.filter((k) => k.startsWith('store.')), [],
        'a serverOnly key inside the literal is an "UNPINNED key" to the Unity oracle');
    // ...and it is still in the RUNTIME allowlist, which is the whole point.
    assert.ok(tunables.TUNABLE_KEYS.some((s) => s.key === 'store.saleBps' && s.serverOnly === true));
    assert.equal(tunables.TUNABLE_KEYS.length, literalKeys.length + 2);
});

test('an appended row without the marker THROWS at module load, not silently', () => {
    // The append block is a back door if it is unguarded: a CLIENT knob placed there
    // would escape the Unity key-domain oracle AND be withheld from every phone by
    // api/client-tunables.js — two failures at once, neither with a symptom.
    const src = fs.readFileSync(path.join(REPO, 'api/_lib/tunables.js'), 'utf8');
    assert.match(src, /SERVER_ONLY_TUNABLE_KEYS/);
    assert.match(src, /if \(!spec \|\| spec\.serverOnly !== true\) \{[\s\S]*?throw new Error/,
        'the append block must refuse a row that is not marked serverOnly');
});

test('FULFIL and RECONCILE never re-derive a price, so a discounted payment settles', () => {
    // ⛔ THE FAILURE THIS FORBIDS: a settle path that recomputed the amount from
    // `usd_anchor` would compute FULL price against a payment made at the sale price
    // and refuse a purchase whose money has already moved. Proven by ABSENCE on both.
    for (const rel of ['api/purchases/fulfill.js', 'api/purchases/reconcile.js']) {
        const src = fs.readFileSync(path.join(REPO, rel), 'utf8');
        assert.doesNotMatch(src, /usd_anchor|usd_rate|USD_ANCHORS|buildQuoteBody|usdAnchor\(/,
            rel + ' must never re-derive an amount from the USD anchor');
    }
    // reconcile reports the amount the ENTITLEMENT row recorded, which verify wrote
    // from the quote's own contract — so it is already the discounted figure.
    const reconcile = fs.readFileSync(path.join(REPO, 'api/purchases/reconcile.js'), 'utf8');
    assert.match(reconcile, /expected_lamports/);
});

test('the sale knobs get NO Command Center card, and the money boundary still holds', () => {
    const manifest = require('../api/_lib/tunable-manifest');
    for (const area of manifest.build().areas) {
        for (const knob of area.knobs) {
            assert.doesNotMatch(knob.key, /^store\./,
                'a price lever must never appear on the balance page — its own ' +
                'OUT_OF_SCOPE_NOTICE says prices are never editable there');
        }
    }
    assert.equal(manifest.PRESENTATION['store.saleBps'], undefined);
    // ...and the three-way join still agrees, which is what the spec marker buys.
    assert.deepEqual(manifest.mismatches(), []);
});

test('the operator CLI can set and clear the sale, and refuses a typo', () => {
    // The write rail is tools/client-tunables.mjs -> isKnownKey/normalizeValue. This
    // is the command in the RESULT's operator recipe, proven at the validator.
    assert.equal(tunables.isKnownKey('store.saleBps'), true);
    assert.equal(tunables.isKnownKey('store.saleEndsAtEpochMin'), true);
    assert.equal(tunables.isKnownKey('store.salebps'), false, 'keys are case-exact');
    assert.equal(tunables.isKnownKey('store.saleLabel'), false,
        'there is deliberately NO label row: the rail stores no strings');
    assert.equal(tunables.specFor('store.saleBps').kind, 'int');
});

// ═════════════════════════════════════════════════════════════════════════════
//  WO-1833 — A PROMO CODE THAT CARRIES A DISCOUNT (SPOTLIGHT30)
// -----------------------------------------------------------------------------
// Owner: "if i set up promocode spotlight30 as a promo code can you make it make
// everything 30% off?", duration "flat 12:00 - 11:59 CST", stacking "personal
// promo should override global i would think not stack".
//
// THREE rulings are pinned below, and each one has already been got wrong once in
// the life of this ticket:
//   1. the window is a FIXED CALENDAR EVENT, never redemption + 48h;
//   2. a promo OVERRIDES the sale — it is NOT max(), so a 3000 promo beats a 5000
//      sale, deliberately;
//   3. the quote must say WHICH source discounted it, and must NOT light up the
//      storefront sale badge for a personal discount.
// ═════════════════════════════════════════════════════════════════════════════

const promoDiscount = require('../api/_lib/promo-discount');

const HOUR = 3_600_000;

/** A promo_codes row as the redeem/quote paths read it. */
function discountRow(over) {
    return Object.assign({
        code: 'SPOTLIGHT30',
        discount_bps: 3000,
        discount_starts_at: new Date(Date.parse('2026-09-18T17:00:00.000Z')).toISOString(),
        discount_ends_at: new Date(Date.parse('2026-09-20T04:59:00.000Z')).toISOString(),
    }, over || {});
}

const INSIDE = Date.parse('2026-09-19T12:00:00.000Z');
const BEFORE = Date.parse('2026-09-18T10:00:00.000Z');
const AFTER = Date.parse('2026-09-21T00:00:00.000Z');

test('⛔ the discount window is a FIXED calendar event, NOT redemption + 48 hours', () => {
    // THE CORRECTION THAT COST A REDESIGN. The first reading was a rolling per-player
    // window (expires_at = redeemed_at + 48h). The owner means one promotional event
    // with one shared end time, so a LATE redeemer gets WHAT IS LEFT.
    const row = discountRow();
    const early = promoDiscount.pickActiveDiscount([row], INSIDE);
    const late = promoDiscount.pickActiveDiscount([row], Date.parse('2026-09-20T04:00:00.000Z'));
    assert.equal(early.endsAtMs, late.endsAtMs,
        'two players redeeming 16 hours apart must share ONE end time - a per-player ' +
        'window is the thing this ruling forbids');
    assert.equal(late.endsAtMs, Date.parse('2026-09-20T04:59:00.000Z'));
    // The late redeemer has UNDER AN HOUR left, not a fresh 48.
    assert.ok(late.endsAtMs - Date.parse('2026-09-20T04:00:00.000Z') < HOUR);

    // ⛔ And no source file may compute an expiry from a redemption time. A grep,
    // deliberately, because the defect is a SHAPE (`+ 48h`) rather than a value.
    for (const rel of ['api/_lib/promo-discount.js', 'api/promo/redeem.js', 'api/purchases/quote.js']) {
        const src = fs.readFileSync(path.join(REPO, rel), 'utf8')
            .split('\n').filter(l => !/^\s*(\/\/|\*|--)/.test(l)).join('\n');
        assert.doesNotMatch(src, /INTERVAL '.*hour/i,
            rel + ' adds an interval to a redemption time - the window is authored, not computed');
        assert.doesNotMatch(src, /discount_duration_hours/,
            rel + ' still carries the rolling-window column the ruling replaced');
    }
});

test('the window gate refuses BEFORE it opens and AFTER it closes, with EXPIRED, unconsumed', () => {
    const row = discountRow();
    assert.deepEqual(promoDiscount.resolveRedeemWindow(row, INSIDE),
        { ok: true, error: null, spec: promoDiscount.readDiscountSpec(row) });
    assert.equal(promoDiscount.resolveRedeemWindow(row, BEFORE).error, 'EXPIRED',
        'not-yet-open must reuse the EXISTING error key - a new one cannot reach a published APK');
    assert.equal(promoDiscount.resolveRedeemWindow(row, AFTER).error, 'EXPIRED');

    // A discount with NO end is refused rather than treated as forever: a personal
    // discount no operator can stop is worse than one that never started.
    assert.equal(promoDiscount.resolveRedeemWindow(
        discountRow({ discount_ends_at: null }), INSIDE).error, 'EXPIRED');

    // ⚠ AN ORDINARY GRANT CODE IS UNTOUCHED, and so is a row from a database that has
    // not run migration 0028 (the KEYS ARE ABSENT, not zero). Both must read as "no
    // discount", never as a 0% discount some formatter prints as "0% off".
    for (const plain of [{ code: 'LINK01', discount_bps: null }, { code: 'LINK01' }]) {
        const gate = promoDiscount.resolveRedeemWindow(plain, INSIDE);
        assert.equal(gate.ok, true);
        assert.equal(gate.spec, null);
    }
});

test('a SECOND discount code while one is active: both stand, the LARGER bps is used', () => {
    // The stated default (WO-1833 D2). Nothing is replaced, extended or refused -
    // each redemption is its own ledger row and each code owns its own fixed window.
    const small = discountRow({ code: 'SPOTLIGHT30', discount_bps: 3000 });
    const big = discountRow({ code: 'BIGGER50', discount_bps: 5000 });
    assert.equal(promoDiscount.pickActiveDiscount([small, big], INSIDE).code, 'BIGGER50');
    assert.equal(promoDiscount.pickActiveDiscount([big, small], INSIDE).code, 'BIGGER50');

    // When the better one's window closes the other takes over BY ITSELF - no write,
    // no sweep, no cleanup job. That is the whole case for deriving this from the
    // ledger instead of storing it.
    const bigEndsEarly = discountRow({ code: 'BIGGER50', discount_bps: 5000,
        discount_ends_at: new Date(INSIDE - HOUR).toISOString() });
    assert.equal(promoDiscount.pickActiveDiscount([small, bigEndsEarly], INSIDE).code, 'SPOTLIGHT30');
    assert.equal(promoDiscount.pickActiveDiscount([bigEndsEarly], INSIDE), null,
        'an expired promo simply stops matching - it is never deleted');

    // The ceiling is the SALE's ceiling, clamped on the read. One ceiling, two names.
    assert.equal(promoDiscount.pickActiveDiscount(
        [discountRow({ discount_bps: 30_000 })], INSIDE).bps, sale.SALE_MAX_BPS,
        'a fat-thumbed 30000 must not hand the store away');
});

test('⛔ a personal promo OVERRIDES the storewide sale — it is NOT max()', () => {
    // Owner ruling 2026-09-17: "personal promo should override global i would think
    // not stack". ⚠ THIS SUPERSEDED an earlier best-of/max() default, so the case
    // that matters most is the one where the SALE IS LARGER and still loses.
    assert.equal(sale.DISCOUNT_PRECEDENCE, 'promo-overrides-sale',
        'the precedence constant is the one place this is decided');

    const biggerSale = sale.resolveStorefrontDiscount(3000, 5000);
    assert.equal(biggerSale.bps, 3000,
        'a 5000 bps sale must LOSE to a 3000 bps promo - max() would return 5000 and ' +
        'that is the ruling this case exists to pin');
    assert.equal(biggerSale.reason, sale.PROMO_REASON);

    // No promo -> the sale applies exactly as it did before WO-1833.
    assert.deepEqual(sale.resolveStorefrontDiscount(null, 3000),
        { bps: 3000, reason: sale.SALE_REASON });
    assert.deepEqual(sale.resolveStorefrontDiscount(0, 0),
        { bps: 0, reason: sale.SALE_REASON });

    // ⛔ NOT ADDITIVE EITHER, in case a future edit reaches for the other wrong answer.
    assert.notEqual(sale.resolveStorefrontDiscount(3000, 5000).bps, 8000);
});

test('the full three-way resolution: promo beats the sale, the shortfall is still MAX', () => {
    const server = quoteTest.SHORTFALL_REASON_SERVER;
    const shortfall = quoteTest.SHORTFALL_DISCOUNT_BPS;      // read, never retyped

    // 1. Promo present: it wins over a LARGER sale, and the reason follows the number
    //    so the persisted audit row does not call a personal discount a storewide sale.
    const promoWins = sale.resolveEffectiveDiscount(3000, 5000, null, server);
    assert.deepEqual(promoWins, { bps: 3000, reason: sale.PROMO_REASON });

    // 2. No promo: WO-1799's behaviour, byte for byte.
    assert.deepEqual(sale.resolveEffectiveDiscount(null, 3000, shortfall, server),
        sale.resolveDiscount(3000, shortfall, server));
    assert.deepEqual(sale.resolveEffectiveDiscount(null, null, null, server),
        { bps: null, reason: null });

    // 3. ⚠ A LARGER SHORTFALL STILL WINS OVER THE PROMO, and that is DELIBERATE, not
    //    an oversight: the owner ruled on promo versus the GLOBAL SALE. The
    //    max-not-additive shortfall law predates this ticket and exists so a player is
    //    never charged MORE for holding an entitlement. One line to change if ruled.
    const fatShortfall = sale.resolveEffectiveDiscount(1000, 0, shortfall, server);
    assert.equal(fatShortfall.bps, shortfall);
    assert.equal(fatShortfall.reason, server);

    // 4. A promo AT OR ABOVE the shortfall keeps the player's 7-day window unspent,
    //    the same way a sale does.
    assert.equal(sale.resolveEffectiveDiscount(shortfall, 0, shortfall, server).reason,
        sale.PROMO_REASON);
});

test('the WIRE names the discount SOURCE and does not fake a storefront sale', () => {
    withEnv(DEVNET_ENV, () => {
        const promoPriced = catalog.buildQuoteBody('devnet', FLAT_SKU, null, 3000, sale.PROMO_REASON);
        const promoEnds = '2026-09-20T04:59:00.000Z';
        const wired = quoteTest.wireQuote(promoPriced, { quoteId: 'q-promo' },
            '2026-10-01T00:00:00.000Z', promoEnds);

        assert.equal(wired.discountBps, 3000, 'the price is discounted');
        assert.equal(wired.discountSource, sale.PROMO_REASON, 'the client must be able to say "promo active"');
        assert.equal(wired.promoEndsAt, promoEnds);
        // ⛔ THE HALF THAT WOULD MISLEAD A PLAYER: a personal discount must NOT light up
        // the storewide sale badge, and a storewide countdown is the WRONG end time for
        // a personal window.
        assert.equal(wired.saleBps, null);
        assert.equal(wired.saleLabel, null);
        assert.equal(wired.saleEndsAt, null);

        // A genuine SALE still reports itself as one, and carries no promoEndsAt.
        const salePriced = catalog.buildQuoteBody('devnet', FLAT_SKU, null, 3000, sale.SALE_REASON);
        const wiredSale = quoteTest.wireQuote(salePriced, { quoteId: 'q-sale' },
            '2026-10-01T00:00:00.000Z', promoEnds);
        assert.equal(wiredSale.discountSource, sale.SALE_REASON);
        assert.equal(wiredSale.saleBps, 3000);
        assert.equal(wiredSale.promoEndsAt, null);

        // No discount at all: source null, never the string 'null' and never a 0.
        const plain = catalog.buildQuoteBody('devnet', FLAT_SKU, null, null, null);
        assert.equal(quoteTest.wireQuote(plain, {}).discountSource, null);
        assert.equal(quoteTest.wireQuote(plain, {}).promoEndsAt, null);

        // A canary is never promo-discounted either.
        const pinned = quoteTest.wirePinned(catalog.purchaseContract('devnet',
            catalog.pinnedSkus('devnet')[0]));
        assert.equal(pinned.discountSource, null);
        assert.equal(pinned.promoEndsAt, null);
    });
});

test('the VERIFIER accepts a PROMO-priced quote with no change of its own', async () => {
    // ⭐ The acceptance criterion that costs real money if it is wrong. /verify prices
    // from the quote ROW (contractFromQuoteRow), so a promo row is just a different
    // number in the same column - and `discount_reason = 'promo'` needs no migration
    // because the column is bare TEXT with no CHECK (api/schema.sql, read at source).
    const priced = withEnv(DEVNET_ENV, () =>
        catalog.buildQuoteBody('devnet', RATE_SKU, RATE, 3000, sale.PROMO_REASON));
    const row = {
        quote_ref: 'b'.repeat(32), wallet, sku: RATE_SKU, network: 'devnet',
        currency: 'SKR', amount_base_units: priced.amountBaseUnits, decimals: 9,
        mint: devnetMint, recipient, recipient_ata: recipientAta,
        usd_anchor: '4.9900', usd_rate: '0.010000000000', rate_source: 'test',
        discount_bps: 3000, discount_reason: sale.PROMO_REASON,
        expires_at: new Date(Date.now() + 300_000).toISOString(),
        consumed_at: null, consumed_tx: null,
    };
    const contract = catalog.contractFromQuoteRow(row);
    assert.equal(contract.amountBaseUnits, '350000000000',
        'a 30% promo on a 4.99 anchor at 0.01 usd/SKR is the same arithmetic as a 30% sale');
    assert.equal((await readChain(transaction('350000000000'), contract)).state, 'verified');
    assert.equal((await readChain(transaction('499000000000'), contract)).reason,
        'transfer_contract_mismatch', 'overpaying a promo quote is still not the contract issued');
    assert.deepEqual(verifyTest.evaluateQuoteRow(row, wallet, RATE_SKU, 'devnet', signature),
        { ok: true }, 'the row-usability checks are indifferent to WHICH discount priced it');
});

test('the active discount is DERIVED from the ledger — there is no player_discounts table', () => {
    // WO-1833 D1. A second table would be duplicated state on the money path AND a
    // second write, which on the Neon HTTP driver is a second TRANSACTION - so a crash
    // between claim and upsert would grant the code and lose the discount, with no
    // un-burn. This pins the decision, because "add a table" is the obvious wrong turn.
    const schema = fs.readFileSync(path.join(REPO, 'api/schema.sql'), 'utf8');
    assert.doesNotMatch(schema, /CREATE TABLE IF NOT EXISTS player_discounts/,
        'the ledger JOIN already holds this fact');

    const lib = fs.readFileSync(path.join(REPO, 'api/_lib/promo-discount.js'), 'utf8');
    assert.match(lib, /FROM promo_redemptions AS pr/, 'the read is the ledger JOIN');
    assert.match(lib, /pc\.discount_ends_at > NOW\(\)/,
        'expiry is judged AT READ TIME, which is why no cleanup job exists');

    // ⛔ AND THE REDEEM PATH WRITES NOTHING EXTRA. One atomic claim, still.
    const redeem = fs.readFileSync(path.join(REPO, 'api/promo/redeem.js'), 'utf8');
    assert.doesNotMatch(redeem, /INSERT INTO player_discounts|UPDATE player_discounts/,
        'a second write after the atomic claim is the failure D1 exists to avoid');

    // The applyable migration exists and matches what schema.sql describes.
    const mig = path.join(REPO, 'api/migrations/20260917_0028_promo_discount_codes.sql');
    assert.ok(fs.existsSync(mig), 'api/schema.sql is a DESCRIPTION; this is the copy that runs');
    const body = fs.readFileSync(mig, 'utf8');
    for (const col of ['discount_bps INTEGER', 'discount_starts_at TIMESTAMPTZ', 'discount_ends_at TIMESTAMPTZ']) {
        const alter = 'ALTER TABLE promo_codes ADD COLUMN IF NOT EXISTS ' + col + ';';
        assert.ok(body.includes(alter), 'the migration must carry: ' + alter);
        assert.ok(schema.includes(alter),
            'api/schema.sql must describe the same column - the two may never disagree');
    }
    // ⚠ NOT in the CREATE TABLE body: tools/schema-parity.mjs parses only those, so a
    // column declared there but unapplied reads as DRIFT and BLOCKS EVERY DEPLOY.
    const createBody = schema.slice(schema.indexOf('CREATE TABLE IF NOT EXISTS promo_codes'));
    assert.doesNotMatch(createBody.slice(0, createBody.indexOf('\n);')), /discount_bps/,
        'declaring it inside CREATE TABLE before it is applied blocks every deploy');
});

test('the authoring path can create a DISCOUNT-ONLY code, and refuses a half-authored window', () => {
    const ops = require('../api/_lib/ops');
    const now = Date.parse('2026-09-17T12:00:00.000Z');
    const START = '2026-09-18T12:00:00-05:00';
    const END = '2026-09-19T23:59:00-05:00';

    // ⛔ THE SHAPE THE OWNER ASKED FOR: no crystals, no coins, 30% off. Before WO-1833
    // this threw REWARD_EMPTY, so the feature could not be authored at all.
    const draft = ops.validatePromoDraft({ code: 'spotlight30', discountBps: '3000',
        discountStartsAt: START, discountEndsAt: END, message: '30% off everything' }, now);
    assert.equal(draft.code, 'SPOTLIGHT30');
    assert.equal(draft.rewardCrystals, 0);
    assert.equal(draft.rewardCoins, 0);
    assert.equal(draft.discountBps, 3000);
    assert.equal(draft.discountEndsAt, new Date(Date.parse(END)).toISOString());

    // A grant AND a discount on one code is legal - the columns are independent.
    assert.equal(ops.validatePromoDraft({ code: 'BOTH1', rewardCrystals: '100',
        discountBps: '1000', discountEndsAt: END }, now).discountBps, 1000);

    // ⛔ THE TIME-ZONE TRAP, AND IT IS THE ONE THAT WOULD HAVE SHIPPED SILENTLY. The
    // owner authors this window in CST. A bare wall clock is parsed as UTC on Vercel,
    // landing a public campaign 5-6 hours out, with a perfectly valid timestamp stored.
    assert.throws(() => ops.validatePromoDraft({ code: 'NOOFF1', discountBps: '3000',
        discountEndsAt: '2026-09-19 23:59' }, now), (e) => e.code === 'WINDOW_NEEDS_OFFSET');

    // Half-authored windows are refused at AUTHORING time, not discovered by a player.
    assert.throws(() => ops.validatePromoDraft({ code: 'NOEND1', discountBps: '3000' }, now),
        (e) => e.code === 'DISCOUNT_WINDOW_INCOMPLETE');
    assert.throws(() => ops.validatePromoDraft({ code: 'NOBPS1', discountEndsAt: END }, now),
        (e) => e.code === 'DISCOUNT_WINDOW_WITHOUT_DISCOUNT');
    assert.throws(() => ops.validatePromoDraft({ code: 'BACK1', discountBps: '3000',
        discountStartsAt: END, discountEndsAt: START }, now),
        (e) => e.code === 'DISCOUNT_WINDOW_BACKWARDS');
    assert.throws(() => ops.validatePromoDraft({ code: 'PAST1', discountBps: '3000',
        discountEndsAt: '2026-09-01T00:00:00-05:00' }, now),
        (e) => e.code === 'DISCOUNT_WINDOW_IN_THE_PAST');
    // A code that still grants nothing is still refused.
    assert.throws(() => ops.validatePromoDraft({ code: 'EMPTY1' }, now),
        (e) => e.code === 'REWARD_EMPTY');
    // The authoring ceiling is the SALE's ceiling, not a second number.
    assert.throws(() => ops.validatePromoDraft({ code: 'HUGE1', discountBps: '9000',
        discountEndsAt: END }, now), (e) => e.code === 'VALUE_TOO_LARGE');
});
