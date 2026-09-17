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
        const built = catalog.buildQuoteBody('devnet', 'impulse-wood-medium',
            { usdPerSkr: 0.00755954, source: catalog.RATE_SOURCE });
        assert.deepEqual(built, {
            sku: 'impulse-wood-medium', network: 'devnet', currency: 'SKR',
            amountBaseUnits: '396000000000', skrAmount: 396, decimals: 9,
            mint: devnetMint, recipient, recipientAta,
            usdAnchor: 2.99, usdEffective: 2.99, usdSaving: null,
            // WO-1799: the reason that priced the body travels with it, so the caller
            // persists the same string it labelled with. Null on an undiscounted quote.
            discountBps: null, discountLabel: null, discountReason: null,
            rate: 0.00755954, rateSource: catalog.RATE_SOURCE,
        });
    });
});

test('the server applies a 20% discount and ships the same effective USD that priced SKR', () => {
    withEnv(DEVNET_ENV, () => {
        const regular = catalog.buildQuoteBody('devnet', 'impulse-wood-medium',
            { usdPerSkr: 0.01, source: 'test' });
        const discounted = catalog.buildQuoteBody('devnet', 'impulse-wood-medium',
            { usdPerSkr: 0.01, source: 'test' }, 2000);
        assert.equal(regular.amountBaseUnits, '299000000000');
        assert.equal(discounted.amountBaseUnits, '240000000000');
        assert.equal(discounted.usdAnchor, 2.99, 'the authored anchor remains auditable');
        assert.equal(regular.usdEffective, regular.usdAnchor,
            'an undiscounted quote keeps the plain server price');
        assert.equal(regular.usdSaving, null, 'an undiscounted quote announces no sale');
        assert.equal(discounted.usdEffective, 2.392,
            'the effective display price is the exact server input to quoteAmount');
        assert.ok(Math.abs(discounted.usdSaving - 0.598) < 1e-12,
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
            const built = catalog.buildQuoteBody('devnet', 'impulse-wood-medium',
                { usdPerSkr: 0.01, source: 'test' }, bad);
            assert.equal(built.amountBaseUnits, '299000000000', `bad bps ${bad}`);
            assert.equal(built.discountBps, null, `bad bps ${bad}`);
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

test('no rate means NO quote — never a stale or catalog price', () => {
    withEnv(DEVNET_ENV, () => {
        assert.equal(catalog.buildQuoteBody('devnet', 'impulse-wood-medium', null), null);
        assert.equal(catalog.buildQuoteBody('devnet', 'impulse-wood-medium',
            { usdPerSkr: 0, source: 'x' }), null);
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
