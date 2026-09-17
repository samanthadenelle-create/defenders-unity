'use strict';

// =============================================================================
// WO-1158 — the server quotes the price. These cases are the proof.
// -----------------------------------------------------------------------------
// Every refusal below exists because the alternative is a PAID-BUT-NOT-GRANTED
// purchase: /verify runs AFTER the transfer settles, so anything it refuses, it
// refuses with the money already gone. That is why "the oracle is down" has a
// test at all — inventing a price is not a lesser evil than refusing to sell.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const catalog = require('../api/_lib/purchase-catalog');
const { _test: verifyTest } = require('../api/purchases/verify');
const { _test: quoteTest } = require('../api/purchases/quote');

const wallet = 'Wallet111111111111111111111111111111111111';
const other = 'Attacker1111111111111111111111111111111111';
const recipient = 'Treasury11111111111111111111111111111111';
const recipientAta = 'TreasuryAta111111111111111111111111111111';
const devnetMint = '3BwWSAUZmyngXDSZiCawEnP7iLgY5ANNopBDz94AB77N';
const signature = 'S'.repeat(85);

// ─────────────────────────────────────────────────────────────────────────────
//  WO-1818 — TWO PRICING PATHS NOW EXIST, AND A CASE MUST NAME WHICH IT TESTS.
// ─────────────────────────────────────────────────────────────────────────────
// ⛔ THE RATE-DERIVED CASES BELOW WERE WRITTEN AGAINST `impulse-wood-medium` AND
// HAD TO MOVE, NOT RELAX. That SKU now carries `pricing.skrFlat` (packs.json), so
// it is priced by a CONSTANT and its rate assertions had become assertions about
// the wrong path — including "no rate means NO quote", which a flat SKU must now
// deliberately violate. Repointing them at a SKU that is genuinely still
// rate-derived keeps the fail-closed law proven rather than deleting it: the rule
// did not change, the SKU did.
//
// `monthly-wayfarer` is authored in battle_monthly.json, which
// tools/gen-sku-catalog.mjs does not copy, so it has no skrFlat and cannot
// silently become flat without that generator changing. The two named cases
// directly below assert BOTH halves of that premise, so if either SKU ever
// changes sides the suite says which one and why instead of drifting.
const FLAT_SKU = 'impulse-wood-large';       // packs.json: usd 4.99 -> skrFlat 300
const RATE_SKU = 'monthly-wayfarer';         // battle_monthly.json: NO skrFlat, still rate-derived
const RATE_SKU_USD = 4.99;                   // its authored anchor, asserted against USD_ANCHORS below
const DEVNET_DECIMALS = catalog.SKR_DECIMALS_BY_NETWORK.devnet;

test('the two pricing paths each still have a witness SKU — the premise of every case below', () => {
    assert.equal(catalog.usdAnchor(RATE_SKU), RATE_SKU_USD, 'the rate-path witness lost its anchor');
    assert.equal(catalog.isFlatSku(RATE_SKU), false,
        `${RATE_SKU} became flat: every rate-derived case below is now testing the wrong path`);
    assert.equal(catalog.isFlatSku(FLAT_SKU), true,
        `${FLAT_SKU} lost its skrFlat: the flat cases below are silently testing the rate path`);
});

function withEnv(vars, fn) {
    const old = { ...process.env };
    Object.assign(process.env, vars);
    try { return fn(); }
    finally {
        for (const key of Object.keys(process.env)) if (!(key in old)) delete process.env[key];
        Object.assign(process.env, old);
    }
}

const DEVNET_ENV = {
    SOLANA_DEVNET_PURCHASE_RECIPIENT: recipient,
    SOLANA_DEVNET_PURCHASE_RECIPIENT_ATA: recipientAta,
    SOLANA_DEVNET_SKR_MINT: devnetMint,
};

/** A persisted purchase_quotes row, as verify.js reads it back. */
function quoteRow(over = {}) {
    return Object.assign({
        quote_ref: 'a'.repeat(32),
        wallet, sku: 'impulse-wood-medium', network: 'devnet', currency: 'SKR',
        amount_base_units: '396000000000', decimals: 9,
        mint: devnetMint, recipient, recipient_ata: recipientAta,
        usd_anchor: '2.9900', usd_rate: '0.007559540000', rate_source: catalog.RATE_SOURCE,
        expires_at: new Date(Date.now() + 300_000).toISOString(),
        consumed_at: null, consumed_tx: null,
    }, over);
}

function transaction({ amount = '396000000000', decimals = 9, blockTime = Math.floor(Date.now() / 1000) } = {}) {
    return {
        slot: 42, blockTime,
        meta: { err: null },
        transaction: { message: {
            accountKeys: [{ pubkey: wallet, signer: true, writable: true }],
            instructions: [{ program: 'spl-token', parsed: { type: 'transferChecked', info: {
                authority: wallet, destination: recipientAta, mint: devnetMint,
                source: 'SourceAta111111111111111111111111111111111',
                tokenAmount: { amount, decimals, uiAmount: 396, uiAmountString: '396' },
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
//  The USD ladder is the ONE authored number, and it must mirror the client
// ─────────────────────────────────────────────────────────────────────────────
// ⛔ THE MIRROR HAS TWO CLIENT SOURCE FILES, NOT ONE — WO-1165 §2, 2026-08-24.
// This case read packs.json alone, and its deepEqual made that omission ACTIVELY
// HOSTILE: the two Monthly Ledger cards were authored in battle_monthly.json with
// real `pricing.usd` and 30 days of grants each, and any attempt to give them a
// server anchor would have FAILED this test for being "extra". So the one check
// that exists to catch a missing price was the thing keeping two SKUs unbuyable.
// Every canonical file that authors a sellable `pricing.usd` belongs below.
const CANONICAL_SKU_SOURCES = [
    { file: 'packs.json', list: doc => doc.packs },
    { file: 'battle_monthly.json', list: doc => doc.monthlyCards },
];

function canonicalDir(root) {
    return path.join(__dirname, '..', 'Assets', root, 'Data', 'Canonical');
}

/** Every sellable client SKU, as {sku, usd, file}, read off the canonical mirror. */
function canonicalSellableSkus() {
    const rows = [];
    for (const source of CANONICAL_SKU_SOURCES) {
        // The twins must be byte-identical before either is trusted as canon.
        const streamText = fs.readFileSync(path.join(canonicalDir('StreamingAssets'), source.file), 'utf8');
        const resourceText = fs.readFileSync(path.join(canonicalDir('Resources'), source.file), 'utf8');
        assert.equal(resourceText, streamText, `${source.file}: canonical mirrors differ`);
        const list = source.list(JSON.parse(streamText));
        assert.ok(Array.isArray(list) && list.length, `${source.file}: no sellable rows found`);
        for (const row of list) {
            // Promo-only packs intentionally live in the same canonical file so
            // ApplyPackContents can grant them. Pricing, not current presentation
            // visibility, distinguishes the paid catalog: several paid bundles
            // are deliberately hidden pending their release window.
            if (!row.pricing || typeof row.pricing.usd !== 'number') continue;
            rows.push({ sku: row.sku, usd: row.pricing && row.pricing.usd, file: source.file });
        }
    }
    return rows;
}

test('server USD anchors mirror EVERY canonical client price file exactly', () => {
    const rows = canonicalSellableSkus();
    const clientSkus = rows.map(r => r.sku).sort();
    const serverSkus = Object.keys(catalog.USD_ANCHORS).sort();
    assert.deepEqual(serverSkus, clientSkus,
        'the server ladder and the canonical client files list different SKUs');
    for (const row of rows) {
        assert.equal(typeof row.usd, 'number', `${row.sku} (${row.file}) has no canonical USD price`);
        assert.equal(catalog.USD_ANCHORS[row.sku], row.usd,
            `${row.sku} (${row.file}): server USD anchor differs from the client's`);
    }
});

test('hidden Welcome Pack SKUs can be granted but can never be priced or quoted', () => {
    const packs = JSON.parse(fs.readFileSync(
        path.join(canonicalDir('StreamingAssets'), 'packs.json'), 'utf8')).packs;
    for (const sku of ['welcome-500', 'welcome-100']) {
        const pack = packs.find(row => row.sku === sku);
        assert.ok(pack, `${sku} is missing`);
        assert.equal(pack.storeVisible, false);
        assert.equal(pack.pricing, undefined);
        assert.equal(catalog.usdAnchor(sku), null);
        assert.equal(catalog.quotableSkus().includes(sku), false);
    }
});

test('no impulse rung is strictly dominated by another purchasable pack at the same USD anchor', () => {
    const packs = JSON.parse(fs.readFileSync(
        path.join(canonicalDir('StreamingAssets'), 'packs.json'), 'utf8')).packs;
    const purchasable = packs.filter(pack => pack && pack.storeVisible !== false &&
        pack.pricing && typeof pack.pricing.usd === 'number');
    const grant = pack => pack.contents && pack.contents.economy || {};
    // Derive the resource-key surface from canonical grants. A hand-maintained
    // list failed open during food -> stone by simply checking one fewer key.
    const keys = [...new Set(packs.flatMap(pack => Object.keys(grant(pack))))].sort();
    const dominates = (candidate, impulse) => {
        const a = grant(candidate), b = grant(impulse);
        return keys.every(key => Number(a[key] || 0) >= Number(b[key] || 0)) &&
            keys.some(key => Number(a[key] || 0) > Number(b[key] || 0));
    };

    for (const impulse of packs.filter(pack => pack && pack.impulse === true)) {
        const dominator = purchasable.find(candidate => candidate.sku !== impulse.sku &&
            candidate.pricing.usd === impulse.pricing.usd && dominates(candidate, impulse));
        assert.equal(dominator, undefined,
            `${impulse.sku} is strictly dominated by ${dominator && dominator.sku} at $${impulse.pricing.usd}`);
    }
});

test('the renamed stone impulse family is mirrored and grants stone only', () => {
    const packs = JSON.parse(fs.readFileSync(
        path.join(canonicalDir('StreamingAssets'), 'packs.json'), 'utf8')).packs;
    const expected = [
        ['impulse-stone-small', 'impulse-food-small', 1.99, 1000],
        ['impulse-stone-medium', 'impulse-food-medium', 2.99, 3500],
        ['impulse-stone-large', 'impulse-food-large', 4.99, 8000],
    ];
    for (const [sku, legacySku, usd, amount] of expected) {
        const pack = packs.find(row => row.sku === sku);
        assert.ok(pack, `${sku}: renamed client SKU missing`);
        assert.deepEqual(pack.legacySkus, [legacySku], `${sku}: retired paid key must resolve forever`);
        assert.equal(catalog.USD_ANCHORS[sku], usd, `${sku}: server anchor missing`);
        assert.equal(pack.impulseResource, 'stone', `${sku}: shortfall route still names food`);
        assert.deepEqual(pack.contents.economy, { stone: amount },
            `${sku}: paid grant must deliver exactly the advertised stone`);
    }
    assert.equal(packs.some(row => /^impulse-food-/.test(row.sku)), false,
        'retired food SKU survived in the client catalog');
    assert.equal(Object.keys(catalog.USD_ANCHORS).some(sku => /^impulse-food-/.test(sku)), false,
        'retired food SKU survived in the server price authority');
});

// A named case for the two SKUs the mirror was blind to, so a future edit that
// drops them fails on a line that says WHY, not just "different SKUs".
test('the Monthly Ledger cards are quotable — 60 authored reward days need a price', () => {
    const cards = JSON.parse(fs.readFileSync(
        path.join(canonicalDir('StreamingAssets'), 'battle_monthly.json'), 'utf8')).monthlyCards;
    assert.deepEqual(cards.map(c => c.sku), ['monthly-wayfarer', 'monthly-keeper']);
    for (const card of cards) {
        assert.equal(catalog.usdAnchor(card.sku), card.pricing.usd,
            `${card.sku}: no server anchor means usdAnchor() -> null -> no quote -> unbuyable`);
        assert.ok(catalog.quotableSkus('devnet').includes(card.sku), `${card.sku} is not quotable on devnet`);
        assert.ok(catalog.quotableSkus('mainnet-beta').includes(card.sku), `${card.sku} is not quotable on mainnet`);
        assert.equal(catalog.isPinnedSku('devnet', card.sku), false, `${card.sku} must not be a pinned canary`);
        assert.equal(catalog.isPinnedSku('mainnet-beta', card.sku), false, `${card.sku} must not be a pinned canary`);
        assert.equal(card.durationDays, 30, `${card.sku}: the 30-claim pool is the cap-safe drip (WO-1165 §3)`);
        assert.equal(card.dailyTable.length, 30, `${card.sku}: authored day count drifted from the pool size`);
    }
});

// ─────────────────────────────────────────────────────────────────────────────
//  The rounding rule — a PRICING DECISION, pinned so a change is deliberate
// ─────────────────────────────────────────────────────────────────────────────
test('the rounding rule is ceil-to-whole-SKR and it favours us, exactly as before', () => {
    // $2.99 at $0.00755954/SKR is 395.53 SKR exactly. The rule charges 396.
    const q = catalog.quoteAmount(2.99, 0.00755954, 9);
    assert.equal(q.skr, 396);
    assert.equal(q.amountBaseUnits, '396000000000');
    assert.ok(q.skr >= 2.99 / 0.00755954, 'the rule must never charge LESS than spot');
    assert.ok(q.skr - 2.99 / 0.00755954 < 1, 'and never more than one whole SKR above it');
    // Exactly-divisible prices are not rounded up a whole token for nothing.
    assert.equal(catalog.quoteAmount(1, 0.5, 6).skr, 2);
});

test('base units are integer math, never a float multiply', () => {
    // 0.1 * 1e9 in float is 100000000.00000001 — BigInt is why this is exact.
    assert.equal(catalog.quoteAmount(1.99, 0.01, 9).amountBaseUnits, '199000000000');
    assert.equal(catalog.quoteAmount(49.99, 0.00001, 6).amountBaseUnits, '4999000000000');
});

test('a nonsense rate or price yields NO quote, never a zero-priced one', () => {
    for (const bad of [0, -1, NaN, Infinity, null, undefined, '3'])
        assert.equal(catalog.quoteAmount(2.99, bad, 9), null, `rate ${bad} must not price a pack`);
    for (const bad of [0, -1, NaN, null])
        assert.equal(catalog.quoteAmount(bad, 0.01, 9), null, `usd ${bad} must not price a pack`);
    assert.equal(catalog.quoteAmount(2.99, 0.01, 99), null, 'absurd decimals must not price a pack');
});

// ─────────────────────────────────────────────────────────────────────────────
//  Decimals come from the mint, per network — never from a sibling network
// ─────────────────────────────────────────────────────────────────────────────
test('SKR decimals are 9 on our devnet test mint and 6 on mainnet', () => {
    assert.deepEqual(catalog.SKR_DECIMALS_BY_NETWORK, { 'devnet': 9, 'mainnet-beta': 6 });
    withEnv(DEVNET_ENV, () => {
        assert.equal(catalog.purchaseRail('devnet').decimals, 9);
        assert.equal(catalog.purchaseRail('devnet').mint, devnetMint);
    });
    withEnv({ MAINNET_CANARY_ENABLED: 'true',
              SOLANA_MAINNET_PURCHASE_RECIPIENT: recipient,
              SOLANA_MAINNET_PURCHASE_RECIPIENT_ATA: recipientAta }, () => {
        assert.equal(catalog.purchaseRail('mainnet-beta').decimals, 6);
        assert.equal(catalog.purchaseRail('mainnet-beta').mint, catalog.MAINNET_SKR_MINT);
    });
});

// ─────────────────────────────────────────────────────────────────────────────
//  The canaries are a protocol constant and stay OUT of the quote path
// ─────────────────────────────────────────────────────────────────────────────
test('the two canary SKUs keep their fixed amounts and are never quoted', () => {
    assert.equal(catalog.isPinnedSku('devnet', catalog.DEVNET_CANARY_SKU), true);
    assert.equal(catalog.isPinnedSku('mainnet-beta', catalog.MAINNET_CANARY_SKU), true);
    assert.equal(catalog.DEVNET_PACKS['hearth-spark'].amountBaseUnits, 25_000_000_000);
    assert.equal(catalog.DEVNET_PACKS['hearth-spark'].decimals, 9);
    assert.equal(catalog.MAINNET_PACKS['mainnet-wood-canary'].amountBaseUnits, 1_000_000);
    assert.equal(catalog.MAINNET_PACKS['mainnet-wood-canary'].decimals, 6);
    assert.ok(!catalog.quotableSkus('devnet').includes('hearth-spark'),
        'the devnet canary must never be repriced from a market rate');
    assert.ok(catalog.quotableSkus('devnet').includes('impulse-wood-medium'));
    withEnv(DEVNET_ENV, () => {
        const pinned = quoteTest.wirePinned(catalog.purchaseContract('devnet', 'hearth-spark'));
        assert.equal(pinned.usdEffective, null, 'a pinned proof-of-rail has no effective USD price');
        assert.equal(pinned.usdSaving, null, 'a pinned proof-of-rail is not a sale');
    });
});

// ─────────────────────────────────────────────────────────────────────────────
//  QUOTE ISSUED
// ─────────────────────────────────────────────────────────────────────────────
test('a quote is issued with the exact amount, the rate and the rate source', () => {
    withEnv(DEVNET_ENV, () => {
        const built = catalog.buildQuoteBody('devnet', RATE_SKU,
            { usdPerSkr: 0.00755954, source: catalog.RATE_SOURCE });
        assert.deepEqual(built, {
            sku: RATE_SKU, network: 'devnet', currency: 'SKR',
            amountBaseUnits: '661000000000', skrAmount: 661, decimals: 9,
            mint: devnetMint, recipient, recipientAta,
            usdAnchor: RATE_SKU_USD, usdEffective: RATE_SKU_USD, usdSaving: null,
            // WO-1799: the reason that priced the body travels with it, so the caller
            // persists the same string it labelled with. Null on an undiscounted quote.
            discountBps: null, discountLabel: null, discountReason: null,
            rate: 0.00755954, rateSource: catalog.RATE_SOURCE,
            // WO-1818: the value the quote ROW takes. Identical to `rate` on this path —
            // it diverges only for a flat SKU, where the NOT NULL column takes 0.
            usdRateForRow: 0.00755954,
        });
        // ceil-to-a-whole-SKR, restated independently of the literal above.
        assert.equal(built.skrAmount, Math.ceil(RATE_SKU_USD / 0.00755954));
    });
});

test('the server applies a 20% discount and ships the same effective USD that priced SKR', () => {
    withEnv(DEVNET_ENV, () => {
        const regular = catalog.buildQuoteBody('devnet', RATE_SKU,
            { usdPerSkr: 0.01, source: 'test' });
        const discounted = catalog.buildQuoteBody('devnet', RATE_SKU,
            { usdPerSkr: 0.01, source: 'test' }, 2000);
        assert.equal(regular.amountBaseUnits, '499000000000');
        assert.equal(discounted.amountBaseUnits, '400000000000');
        assert.equal(discounted.usdAnchor, RATE_SKU_USD, 'the authored anchor remains auditable');
        assert.equal(regular.usdEffective, regular.usdAnchor,
            'an undiscounted quote keeps the plain server price');
        assert.equal(regular.usdSaving, null, 'an undiscounted quote announces no sale');
        assert.ok(Math.abs(discounted.usdEffective - 3.992) < 1e-12,
            'the effective display price is the exact server input to quoteAmount');
        assert.ok(Math.abs(discounted.usdSaving - 0.998) < 1e-12,
            'the server, not the client, computes the dollar saving');
        assert.equal(discounted.discountBps, 2000);
        assert.equal(discounted.discountLabel, '20% shortfall discount');
        const wired = quoteTest.wireQuote(discounted, { quoteId: 'q1' });
        assert.equal(wired.usdEffective, discounted.usdEffective,
            'the endpoint must not discard the server-effective display price');
        assert.equal(wired.usdSaving, discounted.usdSaving,
            'the endpoint must not discard the server-computed saving');

        // RE-POINTED, NEVER DELETED (WO-1198). The old assertion banned a second
        // display figure. The stricter replacement requires the server figure and
        // fails if client code ever derives price or binds payment to USD/rate.
        const quoteClient = fs.readFileSync(path.join(__dirname, '..', 'Assets', '_Modules',
            'Wallet', 'PurchaseQuoteService.cs'), 'utf8');
        const storeClient = fs.readFileSync(path.join(__dirname, '..', 'Assets', '_Modules',
            'Wallet', 'PackStore.cs'), 'utf8');
        assert.match(quoteClient, /JsonProperty\("usdEffective"\)/);
        assert.match(quoteClient, /JsonProperty\("usdSaving"\)/);
        assert.match(quoteClient, /long\.TryParse\(AmountBaseUnits/,
            'the binding client amount must still originate in amountBaseUnits');
        assert.doesNotMatch(quoteClient, /UsdAnchor(?:\.Value)?\s*\*|UiAmount\s*\*\s*Rate|Rate(?:\.Value)?\s*\*\s*UiAmount/,
            'the client must never derive an effective USD price');
        assert.match(storeClient, /quote\.ExactSkrLabel/,
            'purchase confirmation must state the base-unit-derived token amount');
    });
});

test('invalid discount basis points never create a free or negative quote', () => {
    withEnv(DEVNET_ENV, () => {
        for (const bad of [0, -1, 10_000, 20_000, NaN, null, '2000']) {
            const built = catalog.buildQuoteBody('devnet', RATE_SKU,
                { usdPerSkr: 0.01, source: 'test' }, bad);
            assert.equal(built.amountBaseUnits, '499000000000', `bad bps ${bad}`);
            assert.equal(built.discountBps, null, `bad bps ${bad}`);
            // WO-1818: the same refusal on the FLAT path, so junk bps cannot hand a
            // flat pack away either. The amount must be the authored constant.
            const flat = catalog.buildQuoteBody('devnet', FLAT_SKU, null, bad);
            assert.equal(flat.skrAmount, catalog.skrFlatFor(FLAT_SKU), `bad bps ${bad} (flat)`);
            assert.equal(flat.discountBps, null, `bad bps ${bad} (flat)`);
        }
    });
});

test('shortfall discount issuance is server-owned and rate-limited to seven days', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', 'api', 'purchases', 'quote.js'), 'utf8');
    assert.match(source, /SHORTFALL_DISCOUNT_BPS\s*=\s*2000/);
    assert.match(source, /DISCOUNT_WINDOW_DAYS\s*=\s*7/);
    assert.match(source, /discount_bps IS NOT NULL/);
    assert.match(source, /WHERE NOT EXISTS/,
        'the INSERT must re-check eligibility rather than trusting only a pre-read');
    assert.match(source, /isolationLevel:\s*'Serializable'/,
        'simultaneous empty-window reads must not both commit discounted rows');
    assert.match(source, /discount_reason/);
    assert.match(source, /reasonHint:\s*reasonHint/,
        'the client hint is logged for audit');
    assert.match(source, /SHORTFALL_REASON_SERVER/,
        'the persisted reason must be the server label, not the client string');
});

test('a forged or replayed reason cannot obtain a second discount inside the window', () => {
    assert.equal(quoteTest.discountBpsForReason('repair_shortfall', false), 2000,
        'the first eligible shortfall hint receives the ruled discount');
    assert.equal(quoteTest.discountBpsForReason('repair_shortfall', true), null,
        'the same freely forged hint receives no second discount inside seven days');
    assert.equal(quoteTest.discountBpsForReason('anything_else', false), null,
        'an unrelated client string never selects discount policy');
    assert.equal(quoteTest.discountBpsForReason('repair_shortfall', undefined), null,
        'unknown eligibility fails closed');
});

test('an unsold SKU is not quotable at any rate', () => {
    withEnv(DEVNET_ENV, () => {
        assert.equal(catalog.usdAnchor('free-money'), null);
        assert.equal(catalog.buildQuoteBody('devnet', 'free-money',
            { usdPerSkr: 0.01, source: 'x' }), null);
    });
});

// ─────────────────────────────────────────────────────────────────────────────
//  ORACLE DOWN — refuse, never invent
// ─────────────────────────────────────────────────────────────────────────────
async function rateWith(fetchImpl) {
    const previous = global.fetch;
    catalog._resetRateCache();
    global.fetch = fetchImpl;
    try { return await catalog.fetchSkrUsdRate(); }
    finally { global.fetch = previous; catalog._resetRateCache(); }
}

test('the oracle fails CLOSED: unreachable, non-200, empty and junk all yield no rate', async () => {
    assert.equal(await rateWith(async () => { throw new Error('ENOTFOUND'); }), null);
    assert.equal(await rateWith(async () => ({ ok: false, json: async () => [] })), null);
    assert.equal(await rateWith(async () => ({ ok: true, json: async () => [] })), null);
    assert.equal(await rateWith(async () => ({ ok: true, json: async () => [{ low_24h: 0 }] })), null);
    assert.equal(await rateWith(async () => ({ ok: true, json: async () => [{ low_24h: -3 }] })), null);
    assert.equal(await rateWith(async () => ({ ok: true, json: async () => ({ nope: true }) })), null);
});

test('no rate means NO quote for a RATE-DERIVED SKU — never a stale or catalog price', () => {
    // ⛔ THE LAW IS UNCHANGED AND STILL ABSOLUTE FOR ANYTHING PRICED OFF THE
    // ORACLE. WO-1818 did not soften it: it made the guard CONDITIONAL, and only
    // for a SKU whose amount is an authored constant (see the flat cases below).
    // A SKU whose price depends on a rate we could not read must still refuse to
    // sell, because /verify runs after settlement and an invented price is a
    // paid-but-not-granted purchase.
    withEnv(DEVNET_ENV, () => {
        assert.equal(catalog.buildQuoteBody('devnet', RATE_SKU, null), null);
        assert.equal(catalog.buildQuoteBody('devnet', RATE_SKU,
            { usdPerSkr: 0, source: 'x' }), null);
        assert.equal(catalog.buildQuoteBody('devnet', RATE_SKU,
            { usdPerSkr: -1, source: 'x' }), null);
        assert.equal(catalog.buildQuoteBody('devnet', RATE_SKU, {}), null);
    });
});

test('the rate is cached server-side, not fetched per request', async () => {
    const previous = global.fetch;
    catalog._resetRateCache();
    let calls = 0;
    global.fetch = async () => { calls += 1; return { ok: true, json: async () => [{ low_24h: 0.0075 }] }; };
    try {
        const a = await catalog.fetchSkrUsdRate();
        const b = await catalog.fetchSkrUsdRate();
        assert.equal(a.usdPerSkr, 0.0075);
        assert.equal(b.usdPerSkr, 0.0075);
        assert.equal(a.source, catalog.RATE_SOURCE, 'which source backed the quote must be recorded');
        assert.equal(calls, 1, 'a second quote in the cache window must not hit the market again');
    } finally { global.fetch = previous; catalog._resetRateCache(); }
});

// ─────────────────────────────────────────────────────────────────────────────
//  QUOTE REUSED — single-use, or one good rate is replayed forever
// ─────────────────────────────────────────────────────────────────────────────
test('a quote already spent on another payment is REFUSED', () => {
    const row = quoteRow({ consumed_tx: 'D'.repeat(85), consumed_at: new Date().toISOString() });
    assert.deepEqual(verifyTest.evaluateQuoteRow(row, wallet, 'impulse-wood-medium', 'devnet', signature),
        { ok: false, code: 'quote_already_used' });
});

test('the SAME signature re-verifying its OWN quote is an idempotent retry, not a reuse', () => {
    const row = quoteRow({ consumed_tx: signature, consumed_at: new Date().toISOString() });
    assert.deepEqual(verifyTest.evaluateQuoteRow(row, wallet, 'impulse-wood-medium', 'devnet', signature),
        { ok: true });
});

test('a quote belonging to another wallet, SKU or network is REFUSED', () => {
    const row = quoteRow();
    for (const [w, s, n] of [[other, 'impulse-wood-medium', 'devnet'],
                             [wallet, 'founders-vow', 'devnet'],
                             [wallet, 'impulse-wood-medium', 'mainnet-beta']])
        assert.deepEqual(verifyTest.evaluateQuoteRow(row, w, s, n, signature),
            { ok: false, code: 'quote_not_yours' });
});

test('an unknown or unusable quote is REFUSED, never treated as a zero price', () => {
    assert.deepEqual(verifyTest.evaluateQuoteRow(null, wallet, 'impulse-wood-medium', 'devnet', signature),
        { ok: false, code: 'quote_unknown' });
    assert.deepEqual(verifyTest.evaluateQuoteRow(quoteRow({ amount_base_units: '0' }),
        wallet, 'impulse-wood-medium', 'devnet', signature), { ok: false, code: 'quote_unknown' });
});

test('a real pack cannot be verified without a well-formed quote id', () => {
    for (const bad of ['', 'not-a-quote', 'A'.repeat(32), 'a'.repeat(31)])
        assert.equal(verifyTest.QUOTE_REF_RE.test(bad), false, `${bad} must not pass as a quote id`);
    assert.equal(verifyTest.QUOTE_REF_RE.test('a'.repeat(32)), true);
});

// ─────────────────────────────────────────────────────────────────────────────
//  QUOTE EXPIRED
// ─────────────────────────────────────────────────────────────────────────────
test('a quote paid AFTER it expired (beyond the settlement grace) is REFUSED', () => {
    const expiresAt = Date.now();
    const row = quoteRow({ expires_at: new Date(expiresAt).toISOString() });
    const late = expiresAt + (catalog.QUOTE_SETTLEMENT_GRACE_SECONDS + 60) * 1000;
    assert.deepEqual(verifyTest.evaluatePaidQuote(row, late, Date.now()),
        { ok: false, code: 'quote_expired' });
});

test('a quote paid in time still verifies even when /verify runs long afterwards', () => {
    // The player paid one second before expiry; the chain took an hour to
    // finalize and we are only looking now. blockTime is the honest clock.
    const expiresAt = Date.now() - 3_600_000;
    const row = quoteRow({ expires_at: new Date(expiresAt).toISOString() });
    assert.deepEqual(verifyTest.evaluatePaidQuote(row, expiresAt - 1000, Date.now()), { ok: true });
});

test('slow wallet approval inside the settlement grace is not punished', () => {
    const expiresAt = Date.now();
    const row = quoteRow({ expires_at: new Date(expiresAt).toISOString() });
    const justLate = expiresAt + (catalog.QUOTE_SETTLEMENT_GRACE_SECONDS - 5) * 1000;
    assert.deepEqual(verifyTest.evaluatePaidQuote(row, justLate, Date.now()), { ok: true });
});

test('quotes expire at all — an unexpiring quote is a free option on a volatile asset', () => {
    assert.ok(catalog.QUOTE_TTL_SECONDS >= 120 && catalog.QUOTE_TTL_SECONDS <= 300,
        'the ruled window is 2-5 minutes');
    assert.equal(catalog.quoteOfferable(Date.now() - 1, Date.now()), false);
    assert.equal(catalog.quoteOfferable(Date.now() + 10_000, Date.now()), true);
});

// ─────────────────────────────────────────────────────────────────────────────
//  AMOUNT TAMPERED
// ─────────────────────────────────────────────────────────────────────────────
test('the verified contract is built from the QUOTE ROW, so the body cannot carry a price', () => {
    const contract = catalog.contractFromQuoteRow(quoteRow());
    assert.deepEqual(contract, {
        network: 'devnet', sku: 'impulse-wood-medium', currency: 'SKR',
        amountBaseUnits: '396000000000', decimals: 9,
        mint: devnetMint, recipient, recipientAta,
    });
    // There is deliberately no amount/rate/decimals input to this function other
    // than the persisted row: a client has nowhere to put a number of its own.
    assert.equal(catalog.contractFromQuoteRow.length, 1);
});

test('transferring a DIFFERENT amount than quoted is REFUSED', async () => {
    const contract = catalog.contractFromQuoteRow(quoteRow());
    assert.equal((await readChain(transaction(), contract)).state, 'verified');
    // One base unit short, and a whole token short: both are a mismatch.
    assert.equal((await readChain(transaction({ amount: '395999999999' }), contract)).reason,
        'transfer_contract_mismatch');
    assert.equal((await readChain(transaction({ amount: '395000000000' }), contract)).reason,
        'transfer_contract_mismatch');
    // Overpaying is ALSO a mismatch: it is not the contract we issued.
    assert.equal((await readChain(transaction({ amount: '400000000000' }), contract)).reason,
        'transfer_contract_mismatch');
    // The 6-vs-9 door: right digits, wrong scale.
    assert.equal((await readChain(transaction({ decimals: 6 }), contract)).reason,
        'transfer_contract_mismatch');
});

test('blockTime is captured so expiry can be judged at the moment of payment', async () => {
    const contract = catalog.contractFromQuoteRow(quoteRow());
    const chain = await readChain(transaction({ blockTime: 1_800_000_000 }), contract);
    assert.equal(chain.state, 'verified');
    assert.equal(chain.blockTimeMs, 1_800_000_000_000);
});

// ─────────────────────────────────────────────────────────────────────────────
//  Every refusal is WORDED
// ─────────────────────────────────────────────────────────────────────────────
test('every quote refusal carries a player-readable reason', () => {
    for (const code of ['quote_required', 'quote_unknown', 'quote_not_yours',
                        'quote_already_used', 'quote_expired']) {
        const message = verifyTest.QUOTE_MESSAGES[code];
        assert.equal(typeof message, 'string', `${code} has no worded reason`);
        assert.ok(message.length > 30, `${code}'s reason is too terse to help anyone`);
    }
    assert.match(verifyTest.QUOTE_MESSAGES.quote_expired, /do not pay again/i,
        'a refusal after the money moved must say so');
});

// ─────────────────────────────────────────────────────────────────────────────
//  WO-1818 — THE FLAT SKR LADDER. The shelf and the till quote ONE number.
// ─────────────────────────────────────────────────────────────────────────────
// ⛔ THE DEFECT THESE CASES PIN: WO-1815 authored `pricing.skrFlat` so a pack's
// SKR amount is a CONSTANT, and the shelf drew 300 SKR from it — while the server
// still quoted `ceil(usd / coingecko_low_24h)` and the confirm screen said 431.
// One purchase, two prices, and the second one is the one that gets charged.
//
// ⚠ EVERY ASSERTION BELOW READS ITS EXPECTATION OUT OF THE AUTHORED CATALOG, not
// out of a number typed here. A test that hardcoded 300 would go green against a
// server that had silently stopped reading the authoring file at all.
test('the flat SKR ladder is DERIVED from the generated catalog, never retyped', () => {
    const generated = require('../api/_lib/sku-catalog.generated.json');
    const authored = {};
    for (const pack of generated.packs) {
        const flat = pack.pricing && pack.pricing.skrFlat;
        if (Number.isSafeInteger(flat) && flat > 0) authored[pack.sku] = flat;
    }
    // ⛔ A SECOND HAND-TYPED TABLE IN purchase-catalog.js IS THE FAILURE THIS
    // CATCHES (CLAUDE.md §2/§5): it would pass on the day it was written and drift
    // the first time packs.json was re-authored without it.
    assert.deepEqual(catalog.SKR_FLAT, authored,
        'SKR_FLAT must equal the generated catalog exactly — it is a derivation, not a copy');
    assert.ok(Object.keys(authored).length >= 20,
        'the flat ladder is empty or nearly so: the generator copy is stale');
    assert.equal(catalog.skrFlatFor(FLAT_SKU), authored[FLAT_SKU]);
    assert.equal(catalog.isFlatSku(FLAT_SKU), true);
});

test('a NON-flat SKU keeps the rate-derived path, unchanged', () => {
    // ⚠ Not hypothetical: the Monthly Ledger cards live in battle_monthly.json,
    // which the SKU generator does not copy, so they carry no skrFlat and must
    // still be priced off the oracle. They are the live regression witness for
    // "SKUs without skrFlat keep today's path".
    assert.equal(catalog.skrFlatFor(RATE_SKU), null);
    assert.equal(catalog.isFlatSku(RATE_SKU), false);
    withEnv(DEVNET_ENV, () => {
        const rate = { usdPerSkr: 0.007559540000, source: catalog.RATE_SOURCE };
        const built = catalog.buildQuoteBody('devnet', RATE_SKU, rate);
        assert.ok(built, 'a rate-derived SKU must still quote');
        // The pre-WO-1818 arithmetic, asserted independently of the code under test.
        const expected = Math.ceil(catalog.usdAnchor(RATE_SKU) / rate.usdPerSkr);
        assert.equal(built.skrAmount, expected);
        assert.equal(built.rate, rate.usdPerSkr, 'the rate that priced it must still be reported');
        assert.equal(built.rateSource, catalog.RATE_SOURCE);
        assert.equal(built.usdEffective, catalog.usdAnchor(RATE_SKU));
        assert.equal(built.usdRateForRow, rate.usdPerSkr, 'the row still records the real rate');
        // And it still FAILS CLOSED with no rate — this is the half that must not move.
        assert.equal(catalog.buildQuoteBody('devnet', RATE_SKU, null), null);
    });
});

test('a FLAT SKU quotes the authored amount exactly, with no sale', () => {
    withEnv(DEVNET_ENV, () => {
        const flat = catalog.skrFlatFor(FLAT_SKU);
        const built = catalog.buildQuoteBody('devnet', FLAT_SKU,
            { usdPerSkr: 0.007559540000, source: catalog.RATE_SOURCE });
        assert.ok(built);
        assert.equal(built.skrAmount, flat, 'the shelf figure IS the charged figure');
        assert.equal(built.amountBaseUnits,
            (BigInt(flat) * (10n ** BigInt(DEVNET_DECIMALS))).toString(),
            'base units must be the flat amount scaled by THIS network decimals, nothing else');
        assert.equal(built.decimals, DEVNET_DECIMALS);
        // ⛔ A LIVE RATE WAS AVAILABLE AND WAS NOT USED. If the flat branch ever
        // falls through to the oracle this goes red instead of quietly charging
        // 431 SKR for a 300-SKR pack again.
        assert.equal(built.rate, null, 'a flat quote reports NO rate');
        assert.equal(built.rateSource, catalog.FLAT_RATE_SOURCE);
        assert.equal(built.usdEffective, null, 'a second USD figure beside a flat price is two prices');
        assert.equal(built.usdSaving, null);
        // usdAnchor SURVIVES: PurchaseGate.RequiresWallet and the Play/Pi rails read it.
        assert.equal(built.usdAnchor, catalog.usdAnchor(FLAT_SKU));
        // The row must satisfy purchase_quotes.usd_rate NOT NULL (api/schema.sql:1366).
        assert.equal(built.usdRateForRow, catalog.FLAT_ROW_RATE);
        assert.equal(built.usdRateForRow == null, false,
            'a null here would make every flat INSERT throw and 500 the whole till');
    });
});

test('a FLAT SKU quotes with the rate oracle DOWN — that is the point of flat', async () => {
    // Prove the oracle really is dead first, the same way the fail-closed case does.
    assert.equal(await rateWith(async () => { throw new Error('ENOTFOUND'); }), null);
    withEnv(DEVNET_ENV, () => {
        const flat = catalog.skrFlatFor(FLAT_SKU);
        for (const deadRate of [null, undefined, { usdPerSkr: 0, source: 'x' }]) {
            const built = catalog.buildQuoteBody('devnet', FLAT_SKU, deadRate);
            assert.ok(built, 'a constant price must not become unbuyable because a third party is down');
            assert.equal(built.skrAmount, flat);
            assert.equal(built.rate, null);
            assert.equal(built.rateSource, catalog.FLAT_RATE_SOURCE);
        }
    });
});

test('a sale on a FLAT SKU CEILs to a whole SKR and keeps its badge fields', () => {
    withEnv(DEVNET_ENV, () => {
        const flat = catalog.skrFlatFor(FLAT_SKU);        // 300 as authored today
        const built = catalog.buildQuoteBody('devnet', FLAT_SKU, null, 3000, 'sale');
        assert.ok(built, 'a sale must not need the oracle either');
        assert.equal(built.skrAmount, Math.ceil(flat * 7000 / 10_000));
        assert.equal(built.amountBaseUnits,
            (BigInt(built.skrAmount) * (10n ** BigInt(DEVNET_DECIMALS))).toString());
        // ⛔ THE BADGE FIELDS MUST SURVIVE FLATNESS. wireQuote() derives
        // saleBps/saleLabel/saleEndsAt from these three, so dropping them would
        // silently un-advertise a running sale on 27 of 29 packs.
        assert.equal(built.discountBps, 3000);
        assert.equal(built.discountReason, 'sale');
        assert.equal(built.discountLabel, catalog.discountLabelFor(3000, 'sale'));
        const wired = quoteTest.wireQuote(built, { quoteId: null, expiresAt: null },
            '2026-09-30T00:00:00.000Z');
        assert.equal(wired.saleBps, 3000);
        assert.equal(wired.saleLabel, '30% off');
        assert.equal(wired.saleEndsAt, '2026-09-30T00:00:00.000Z');
        assert.equal(wired.rate, null);
        assert.equal(wired.rateSource, catalog.FLAT_RATE_SOURCE);
        assert.equal(wired.skrAmount, built.skrAmount);
    });
});

test('the flat sale rounding is CEIL at every rung, so shelf and till never differ by a SKR', () => {
    // Integer arithmetic, checked against the independent float formula. 9999 at
    // 3000 bps is the case that actually has a fraction (6999.3 -> 7000).
    for (const flat of [100, 200, 300, 500, 1000, 9999]) {
        for (const bps of [0, 1000, 2000, 3000, 7000]) {
            const got = catalog.quoteFlatAmount(flat, bps, 6);
            const expected = bps > 0 ? Math.ceil(flat * (10_000 - bps) / 10_000) : flat;
            assert.equal(got.skr, expected, `flat ${flat} at ${bps}bps`);
            assert.equal(got.amountBaseUnits, (BigInt(expected) * 1_000_000n).toString());
        }
    }
    // A shortfall discount is just another bps on the same path — no second rule.
    assert.equal(catalog.quoteFlatAmount(9999, quoteTest.SHORTFALL_DISCOUNT_BPS, 6).skr,
        Math.ceil(9999 * 0.8));
    // Junk in, nothing out. Never a rounded guess at a money amount.
    for (const bad of [0, -1, 1.5, null, undefined, NaN])
        assert.equal(catalog.quoteFlatAmount(bad, 0, 6), null, `${bad} must not price a pack`);
    assert.equal(catalog.quoteFlatAmount(300, 10_000, 6).skr, 300,
        'an out-of-range bps is ignored, never applied as a 100% discount');
});

test('an all-flat shelf would send a NULL envelope rate the shipped client cannot parse', () => {
    // ⛔ THIS CASE IS A TRIPWIRE, NOT A BEHAVIOUR TEST, AND IT GUARDS A DEVICE-WIDE
    // CRASH. The LIST envelope now reports `rate: null` when NO oracle was consulted
    // (api/purchases/quote.js, the `mode: 'list'` response). The shipped client
    // declares that envelope field as a NON-NULLABLE double —
    // Assets/_Modules/Wallet/PurchaseQuoteService.cs `ListEnvelope.Rate` /
    // `ListResponse.Rate` — and Newtonsoft throws converting null to System.Double,
    // which fails the WHOLE envelope. Not one row: the entire shelf, on every device.
    //
    // ⚠ IT CANNOT FIRE TODAY, and that is exactly why it is written down. The shelf
    // is not all-flat (monthly-wayfarer / monthly-keeper carry no skrFlat), so a real
    // rate is still fetched and the envelope still carries a number. The day someone
    // authors skrFlat for those two cards — a change made entirely in Assets/, by a
    // seat with no reason to open api/ — the envelope goes null and the store dies
    // silently. So: the moment the shelf becomes all-flat, this test goes RED and
    // names the client field that must become `double?` first.
    const client = fs.readFileSync(path.join(__dirname, '..', 'Assets', '_Modules',
        'Wallet', 'PurchaseQuoteService.cs'), 'utf8');
    const envelopeAcceptsNull = /\[JsonProperty\("rate"\)\]\s*public\s+double\?\s+Rate;[\s\S]{0,400}?List<Newtonsoft\.Json\.Linq\.JObject>\s+Prices;/
        .test(client);
    const shelfIsAllFlat = ['devnet', 'mainnet-beta'].every(
        net => catalog.quotableSkus(net).every(catalog.isFlatSku));
    assert.ok(envelopeAcceptsNull || !shelfIsAllFlat,
        'the shelf is now ALL FLAT, so the LIST envelope sends rate: null — change ' +
        'ListEnvelope.Rate and ListResponse.Rate in PurchaseQuoteService.cs to double? ' +
        'and ship a client BEFORE deploying this, or every device gets an empty store');
    // The per-row fields were already nullable before this ticket and must stay so:
    // 27 of 29 rows now carry rate/usdEffective/usdSaving as null for every player,
    // where previously only the owner-only canary rows did.
    for (const field of ['rate', 'usdEffective', 'usdSaving']) {
        const re = new RegExp('\\[JsonProperty\\("' + field + '"\\)\\]\\s*public\\s+(\\w+\\??)\\s');
        const match = client.match(re);
        assert.ok(match, `PurchaseQuoteService.cs no longer parses ${field} at all`);
        assert.match(match[1], /\?$/,
            `PurchaseQuote.${field} must stay nullable: a flat quote sends null for every buyer`);
    }
});

test('the verifier reads the PERSISTED amount, so flat needs no arithmetic there', () => {
    // ⛔ The proof for WO-1818 item 3: nothing in verify.js recomputes an expected
    // amount from a rate. contractFromQuoteRow() takes ONE argument — the row.
    const flat = catalog.skrFlatFor(FLAT_SKU);
    const amount = (BigInt(flat) * (10n ** BigInt(DEVNET_DECIMALS))).toString();
    const row = quoteRow({ sku: FLAT_SKU, amount_base_units: amount,
        usd_rate: '0.000000000000', rate_source: catalog.FLAT_RATE_SOURCE });
    const contract = catalog.contractFromQuoteRow(row);
    assert.equal(contract.amountBaseUnits, amount,
        'the accepted amount is the one the server persisted, flat or derived');
    assert.deepEqual(verifyTest.evaluateQuoteRow(row, wallet, FLAT_SKU, 'devnet', signature),
        { ok: true });
    // ⚠ AND THE STORED 0 IS NEVER REPORTED AS A RATE. purchase_quotes.usd_rate is
    // NOT NULL so a flat row stores 0; echoing that outward would tell the ledger
    // SKR traded at $0. One helper normalises it, in one place.
    assert.equal(verifyTest.ledgerRate(row), null);
    assert.equal(verifyTest.ledgerRate(quoteRow()), '0.007559540000',
        'a real rate must still be reported verbatim');
    assert.equal(verifyTest.ledgerRate(null), null);
});
