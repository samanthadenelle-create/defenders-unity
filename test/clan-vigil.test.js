// =============================================================================
// test/clan-vigil.test.js — WO-1852 (clan WO-9). The SKR Vigil read.
// -----------------------------------------------------------------------------
//     node --test test/clan-vigil.test.js
//
// ⭐ THE RPC IS A REAL LOCAL HTTP SERVER, NOT A STUBBED FUNCTION, and that is the
// whole point of the harness. Acceptance criterion 5 is "two calls within 60 seconds
// produce exactly one RPC round-trip per wallet" — a COUNT OF ROUND TRIPS. A stubbed
// `verifyStake` could not observe one; a server that records every JSON-RPC method
// and address it is asked for measures it directly. The same server is what proves
// the two-token-account sum and the degraded paths.
//
// The account fixtures are built from the Anchor IDL offsets recorded in
// api/_lib/skr-staking.js's header (StakeConfig 193 bytes, UserStake 169 bytes,
// discriminators from the IDL's `accounts` array). The DECODERS themselves are
// already unit-tested in test/skr-staking.test.js — this file tests the Vigil
// arithmetic, the caching, the degradation and the endpoint on top of them.
//
// Zero mainnet, zero database, zero Unity: a test that needs the chain is a test
// that fails on a plane (the rule test/skr-staking.test.js's header states).
// The live-chain proofs behind the DENOMINATOR decision are recorded in
// WorkOrders/WORK_ORDER_1852_skr_vigil_read.md's implementation record.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const SKR_PATH = path.join(REPO, 'api', '_lib', 'skr-staking.js');
const CLAN_LIB = path.join(REPO, 'api', '_lib', 'clan.js');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const CLAN_VIGIL = path.join(REPO, 'api', '_lib', 'clan-vigil.js');
const WALLET_AUTH = path.join(REPO, 'api', '_lib', 'wallet-auth.js');
const ROUTE_PATH = path.join(REPO, 'api', 'clan', 'vigil.js');

const skr = require(SKR_PATH);
const vigilLib = require(CLAN_VIGIL);
const { AuthCode } = require(WALLET_AUTH);
const { ClanCode } = require(CLAN_LIB);

// Two real, distinct base58 wallets (they only have to decode to 32 bytes).
const WALLET_A = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const WALLET_B = 'DLQtTaJKEU8yCxC2boPMdttJf3BWDNzFbJXGei8upiUZ';
const CLAN_ID = '3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e';

const SKR = 1000000n;                       // one whole SKR in base units (6 decimals)
const SHARE_PRICE = skr.SHARE_PRICE_SCALE;  // 1e9 => activeStaked === shares exactly

// ═══════════════════════════════════════════════════════════════════════════
// ACCOUNT FIXTURES — built from the IDL offsets, not from a byte dump
// ═══════════════════════════════════════════════════════════════════════════

function writeU128LE(buf, offset, value) {
    const v = BigInt(value);
    buf.writeBigUInt64LE(v & 0xffffffffffffffffn, offset);
    buf.writeBigUInt64LE(v >> 64n, offset + 8);
}

/** A 193-byte StakeConfig whose share_price makes shares and SKR interchangeable. */
function stakeConfigAccount({ sharePrice = SHARE_PRICE, cooldown = 172800n } = {}) {
    const b = Buffer.alloc(skr.STAKE_CONFIG_SIZE);
    Buffer.from([238, 151, 43, 3, 11, 151, 63, 176]).copy(b, 0);
    b.writeBigUInt64LE(1n, 105);                   // min_stake_amount
    b.writeBigUInt64LE(BigInt(cooldown), 113);     // cooldown_seconds
    writeU128LE(b, 121, 10n ** 18n);               // total_shares
    writeU128LE(b, 137, BigInt(sharePrice));       // share_price
    return b;
}

/** A 169-byte UserStake with the given active shares and pending unstake. */
function userStakeAccount({ shares = 0n, unstaking = 0n, unstakeTs = 0n } = {}) {
    const b = Buffer.alloc(skr.USER_STAKE_SIZE);
    Buffer.from([102, 53, 163, 107, 9, 138, 87, 153]).copy(b, 0);
    writeU128LE(b, 105, BigInt(shares));           // shares
    writeU128LE(b, 121, 0n);                       // cost_basis
    b.writeBigUInt64LE(BigInt(unstaking), 153);    // unstaking_amount
    b.writeBigInt64LE(BigInt(unstakeTs), 161);     // unstake_timestamp
    return b;
}

/** One jsonParsed SPL token account entry, as mainnet returned it (probe 2026-09-17). */
function tokenAccountEntry(owner, amountRaw, pubkey) {
    return {
        pubkey: pubkey,
        account: {
            lamports: 2039280,
            data: {
                program: 'spl-token',
                parsed: {
                    info: {
                        isNative: false,
                        mint: skr.SKR_MINT,
                        owner: owner,
                        state: 'initialized',
                        tokenAmount: {
                            amount: String(amountRaw),
                            decimals: 6,
                            uiAmount: Number(amountRaw) / 1e6,
                            uiAmountString: String(Number(amountRaw) / 1e6),
                        },
                    },
                    type: 'account',
                },
                space: 165,
            },
            owner: 'TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA',
            executable: false,
            space: 165,
        },
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// THE RECORDING RPC SERVER
// ═══════════════════════════════════════════════════════════════════════════

/**
 * @param {object} cfg
 *   cfg.stakes    { [wallet]: { shares, unstaking } | 'missing' }
 *   cfg.balances  { [wallet]: [amountRaw, ...] }   one entry per token account
 *   cfg.fail      'all' | 'balance' | 'stake' | false
 */
async function startRpc(cfg) {
    const calls = [];
    const conf = cfg || {};

    // Map every configured wallet's derived UserStake PDA back to the wallet, so the
    // server can answer getAccountInfo by address exactly as the chain does.
    const pdaToWallet = new Map();
    for (const w of Object.keys(conf.stakes || {})) {
        pdaToWallet.set(skr.deriveUserStakePda(w).address, w);
    }

    const server = http.createServer((req, res) => {
        let body = '';
        req.on('data', (c) => { body += c; });
        req.on('end', () => {
            let msg = {};
            try { msg = JSON.parse(body); } catch (_) { msg = {}; }
            const method = msg.method;
            const params = msg.params || [];
            calls.push({ method: method, address: typeof params[0] === 'string' ? params[0] : null });

            const send = (obj) => {
                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify(Object.assign({ jsonrpc: '2.0', id: msg.id }, obj)));
            };
            const die = () => { res.writeHead(500); res.end('rpc down'); };

            if (conf.fail === 'all') return die();

            if (method === 'getAccountInfo') {
                if (conf.fail === 'stake') return die();
                const addr = params[0];
                const encode = (buf) => send({
                    result: {
                        context: { slot: 447954298 },
                        value: {
                            data: [buf.toString('base64'), 'base64'],
                            owner: skr.STAKING_PROGRAM,
                            lamports: 1,
                            executable: false,
                        },
                    },
                });
                if (addr === skr.STAKE_CONFIG) return encode(stakeConfigAccount(conf.config));
                const wallet = pdaToWallet.get(addr);
                const spec = wallet ? conf.stakes[wallet] : 'missing';
                if (!wallet || spec === 'missing') {
                    // The proven "this wallet never staked" answer: a null value.
                    return send({ result: { context: { slot: 447954298 }, value: null } });
                }
                return encode(userStakeAccount(spec));
            }

            if (method === 'getTokenAccountsByOwner') {
                if (conf.fail === 'balance') return die();
                const owner = params[0];
                const amounts = (conf.balances || {})[owner] || [];
                return send({
                    result: {
                        context: { slot: 447954298, apiVersion: '4.3.0-alpha.2' },
                        value: amounts.map((a, i) =>
                            tokenAccountEntry(owner, a, 'tok' + i + owner.slice(0, 6))),
                    },
                });
            }

            return send({ error: { code: -32601, message: 'Method not found' } });
        });
    });

    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const url = 'http://127.0.0.1:' + server.address().port;
    return {
        url: url,
        calls: calls,
        count: (method, address) => calls.filter(
            (c) => c.method === method && (address == null || c.address === address)).length,
        close: () => new Promise((r) => server.close(r)),
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE ARITHMETIC — pure, no network
// ═══════════════════════════════════════════════════════════════════════════

test('vigilPercentPpm is exact above 2^53, where a double would lose bits', () => {
    // Total SKR supply read from mainnet 2026-09-17: 10,599,697,046.828418 SKR =
    // 1.0599697046828418e16 base units, already past Number.MAX_SAFE_INTEGER.
    const total = 10599697046828418n;
    assert.ok(total > BigInt(Number.MAX_SAFE_INTEGER),
        'fixture assumption: the real supply exceeds 2^53, so floats cannot be used');

    // ⚠ HONESTLY STATED: this PARTICULAR total happens to be exactly representable
    // as a double (it is even, and below 2^54), so it does round-trip. The hazard is
    // not that every large value is lossy — it is that above 2^53 only SOME are, and
    // which ones is not a property anybody should have to reason about per balance.
    // An odd base-unit total one step past 2^53 is the proof that the class is unsafe:
    const oddAbove2to53 = 9007199254740993n;   // 2^53 + 1, ~9,007,199,254.74 SKR
    assert.ok(oddAbove2to53 > BigInt(Number.MAX_SAFE_INTEGER));
    assert.notEqual(BigInt(Number(oddAbove2to53)), oddAbove2to53,
        'a base-unit total in SKR\'s live range that does NOT survive a double');
    // And the scaled-integer path is exact for it, where a float path is not.
    assert.equal(skr.vigilPercentPpm(oddAbove2to53, oddAbove2to53), 1000000n);

    // Exactly half is exactly 500000 ppm — computed in integers, so no rounding.
    assert.equal(skr.vigilPercentPpm(total / 2n, total), 500000n);
    assert.equal(skr.vigilPercentPpm(total, total), 1000000n);

    // An awkward ratio: the BigInt path truncates deterministically, and the answer
    // is checked against the arithmetic rather than against the implementation.
    const third = total / 3n;
    assert.equal(skr.vigilPercentPpm(third, total), (third * 1000000n) / total);
    assert.equal(skr.vigilPercentPpm(third, total), 333333n);

    // One base unit out of the whole supply is a real, nonzero position that rounds
    // to zero ppm — recorded so nobody later reads a 0 percent as "unreadable".
    assert.equal(skr.vigilPercentPpm(1n, total), 0n);
});

test('a zero denominator is 0, never NaN and never a throw', () => {
    assert.equal(skr.vigilPercentPpm(0n, 0n), 0n);
    assert.equal(skr.ppmToFraction(skr.vigilPercentPpm(0n, 0n)), 0);
    assert.equal(skr.ppmToFraction(null), 0);
    assert.ok(!Number.isNaN(skr.ppmToFraction(skr.vigilPercentPpm(5n, 0n))));
});

test('percent is clamped to 1.0 — a torn read across two slots cannot exceed wholly staked', () => {
    assert.equal(skr.vigilPercentPpm(200n, 100n), 1000000n);
    assert.equal(skr.ppmToFraction(skr.vigilPercentPpm(200n, 100n)), 1);
});

test('isFreshWithin is the ONE freshness rule and isCacheFresh delegates to it', () => {
    // The 300-second caller must behave byte-identically to its original inline form.
    assert.equal(skr.isCacheFresh(1000000, 1000000 + 299), true);
    assert.equal(skr.isCacheFresh(1000000, 1000000 + 300), false);
    assert.equal(skr.isCacheFresh(null, 1000000), false);
    // And the Vigil's 60-second window rides the same comparison.
    assert.equal(skr.VIGIL_CACHE_TTL_SECONDS, 60);
    assert.equal(skr.isFreshWithin(1000000, 1000000 + 59, skr.VIGIL_CACHE_TTL_SECONDS), true);
    assert.equal(skr.isFreshWithin(1000000, 1000000 + 60, skr.VIGIL_CACHE_TTL_SECONDS), false);
    assert.equal(skr.isFreshWithin(null, 1000000, 60), false);
});

test('getStakedPercentage takes a wallet and nothing that could be an amount', () => {
    // The same structural property test/skr-staking.test.js:233 asserts for verifyStake:
    // product rule 7 holds because there IS no parameter for a stake, tier or share count.
    assert.equal(skr.getStakedPercentage.length, 2);
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE BALANCE READ — the measured call shape
// ═══════════════════════════════════════════════════════════════════════════

test('readSkrBalance SUMS every token account, never just one', async () => {
    // ⛔ THE CASE THAT MAKES THIS NON-OPTIONAL, measured on mainnet 2026-09-17: owner
    // 4HQy82s9CHTv1GsYKnANHMiHfhcqesYkK6sB3RDSYyqw holds TWO SKR accounts, 26 SKR and
    // 5,019,397,722 SKR. Reading one of them is wrong by eight orders of magnitude.
    const rpc = await startRpc({ balances: { [WALLET_A]: [26n * SKR, 5019397722n * SKR] } });
    try {
        const r = await skr.readSkrBalance(rpc.url, WALLET_A);
        assert.equal(r.ok, true);
        assert.equal(r.accountCount, 2);
        assert.equal(r.balanceRaw, (26n + 5019397722n) * SKR);
        assert.equal(rpc.count('getTokenAccountsByOwner'), 1, 'one round trip, not one per account');
    } finally { await rpc.close(); }
});

test('an owner holding no SKR is a real zero, not a fault', async () => {
    // Proven shape: HTTP 200 with `value: []` (measured against mainnet for
    // 11111111111111111111111111111112).
    const rpc = await startRpc({ balances: {} });
    try {
        const r = await skr.readSkrBalance(rpc.url, WALLET_A);
        assert.equal(r.ok, true);
        assert.equal(r.balanceRaw, 0n);
        assert.equal(r.accountCount, 0);
    } finally { await rpc.close(); }
});

test('a dead RPC is rpc_unavailable, never a zero balance', async () => {
    const r = await skr.readSkrBalance('http://127.0.0.1:1/dead', WALLET_A);
    assert.equal(r.ok, false);
    assert.equal(r.reason, 'rpc_unavailable');
    assert.equal(r.balanceRaw, undefined, '⛔ a failed read must carry NO amount at all');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. getStakedPercentage — the denominator, the degradation, the cache
// ═══════════════════════════════════════════════════════════════════════════

test.beforeEach(() => skr._resetVigilCache());

test('TEST PLAN 1: 100 SKR staked, 100 liquid (200 total) => percent_staked 0.5', async () => {
    // ⭐ THE DENOMINATOR IS TOTAL HOLDINGS, NOT THE LIQUID BALANCE. The work order's
    // §1 formula reads `stakedRaw / balanceRaw`; its test plan says "stakes 100 SKR,
    // holds 200 total -> 0.5". Only total holdings satisfies that, and only total
    // holdings is defined for a real staker — five mainnet stakers read on 2026-09-17
    // all hold their stake in the program vault and ZERO in their own wallet.
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR } },
        balances: { [WALLET_A]: [100n * SKR] },
    });
    try {
        const r = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url });
        assert.equal(r.degraded, false);
        assert.equal(r.activeStakedRaw, 100n * SKR);
        assert.equal(r.liquidRaw, 100n * SKR);
        assert.equal(r.totalRaw, 200n * SKR);
        assert.equal(r.balanceRaw, r.totalRaw, "the spec's balanceRaw is an alias of totalRaw");
        assert.equal(r.percent, 0.5);
        assert.equal(r.verificationStatus, 'VERIFIED');
    } finally { await rpc.close(); }
});

test('a FULLY staked wallet (zero liquid) is 1.0 — the case that divides by zero in the spec', async () => {
    // This is the shape of every real staker measured on mainnet. Under the literal
    // reading of the work order's formula this input is a DIVIDE BY ZERO.
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 1140806412n * SKR } },
        balances: { [WALLET_A]: [] },
    });
    try {
        const r = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url });
        assert.equal(r.degraded, false);
        assert.equal(r.liquidRaw, 0n);
        assert.equal(r.percent, 1);
        assert.ok(!Number.isNaN(r.percent), 'never NaN');
        assert.ok(Number.isFinite(r.percent), 'never Infinity');
    } finally { await rpc.close(); }
});

test('unstaking SKR counts in the DENOMINATOR but never in the numerator', async () => {
    // The discriminating case, written down so the ruling is legible: 50 active,
    // 50 unstaking, 0 liquid => 0.5 with unstaking counted, 1.0 without.
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 50n * SKR, unstaking: 50n * SKR, unstakeTs: 1n } },
        balances: { [WALLET_A]: [] },
    });
    try {
        const r = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url });
        assert.equal(r.activeStakedRaw, 50n * SKR);
        assert.equal(r.unstakingRaw, 50n * SKR);
        assert.equal(r.totalRaw, 100n * SKR);
        assert.equal(r.percent, 0.5);
    } finally { await rpc.close(); }
});

test('TEST PLAN 2: a wallet with no stake at all is percent 0 and NOT degraded', async () => {
    // The distinction the whole module exists for: a REAL zero, reported as trustworthy.
    const rpc = await startRpc({
        stakes: { [WALLET_B]: 'missing' },
        balances: { [WALLET_B]: [500n * SKR] },
    });
    try {
        const r = await skr.getStakedPercentage(WALLET_B, { rpcUrl: rpc.url });
        assert.equal(r.verificationStatus, 'NO_STAKE');
        assert.equal(r.percent, 0);
        assert.equal(r.degraded, false, '⛔ a real zero is NOT degraded');
        assert.equal(r.activeStakedRaw, 0n);
        assert.equal(r.totalRaw, 500n * SKR);
    } finally { await rpc.close(); }
});

test('a wallet with NO SKR anywhere is 0, not NaN', async () => {
    const rpc = await startRpc({ stakes: { [WALLET_A]: 'missing' }, balances: {} });
    try {
        const r = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url });
        assert.equal(r.totalRaw, 0n);
        assert.equal(r.percent, 0);
        assert.equal(r.degraded, false);
    } finally { await rpc.close(); }
});

test('a dead RPC is degraded with NULL amounts — never a fabricated zero stake', async () => {
    const r = await skr.getStakedPercentage(WALLET_A, { rpcUrl: 'http://127.0.0.1:1/dead' });
    assert.equal(r.degraded, true);
    assert.equal(r.percent, 0, 'the wire still needs a number');
    assert.equal(r.totalRaw, null, '⛔ and the amounts stay NULL so the zero is not mistaken for real');
    assert.equal(r.activeStakedRaw, null);
    assert.equal(r.verificationStatus, 'RPC_UNAVAILABLE');
});

test('a readable stake with an UNREADABLE balance is still degraded', async () => {
    // You cannot state a fraction whose denominator you could not read, even though
    // the numerator came back cleanly.
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR } },
        balances: { [WALLET_A]: [100n * SKR] },
        fail: 'balance',
    });
    try {
        const r = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url });
        assert.equal(r.degraded, true);
        assert.equal(r.percent, 0);
        assert.equal(r.totalRaw, null);
        assert.match(r.errorCode, /^skr_balance_/);
        assert.equal(r.activeStakedRaw, 100n * SKR, 'the half we DID read is still reported');
    } finally { await rpc.close(); }
});

test('an unset RPC url is degraded, never a fallback to another network', async () => {
    const saved = process.env.SOLANA_MAINNET_RPC_URL;
    delete process.env.SOLANA_MAINNET_RPC_URL;
    try {
        const r = await skr.getStakedPercentage(WALLET_A);
        assert.equal(r.degraded, true);
        assert.equal(r.errorCode, 'rpc_url_unset');
        assert.equal(r.totalRaw, null);
    } finally {
        if (saved !== undefined) process.env.SOLANA_MAINNET_RPC_URL = saved;
    }
});

test('ACCEPTANCE 5: two calls within 60 seconds are exactly ONE round trip per wallet', async () => {
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR } },
        balances: { [WALLET_A]: [100n * SKR] },
    });
    try {
        const first = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000000 });
        const balanceCalls = rpc.count('getTokenAccountsByOwner');
        const infoCalls = rpc.count('getAccountInfo');
        assert.equal(balanceCalls, 1);
        assert.equal(infoCalls, 2, 'StakeConfig + UserStake');

        const second = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000059 });
        assert.equal(rpc.count('getTokenAccountsByOwner'), balanceCalls,
            '⛔ a cached read must cost ZERO additional round trips');
        assert.equal(rpc.count('getAccountInfo'), infoCalls);
        assert.equal(second.fromCache, true);
        assert.equal(second.percent, first.percent);

        // 60 seconds is the boundary and it is EXCLUSIVE — the cache expires here.
        await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000060 });
        assert.equal(rpc.count('getTokenAccountsByOwner'), balanceCalls + 1);
    } finally { await rpc.close(); }
});

test('the cache is PER WALLET — one wallet cached does not answer for another', async () => {
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR }, [WALLET_B]: { shares: 300n * SKR } },
        balances: { [WALLET_A]: [100n * SKR], [WALLET_B]: [100n * SKR] },
    });
    try {
        const a = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000000 });
        const b = await skr.getStakedPercentage(WALLET_B, { rpcUrl: rpc.url, nowSeconds: 1000001 });
        assert.equal(a.percent, 0.5);
        assert.equal(b.percent, 0.75);
        assert.equal(rpc.count('getTokenAccountsByOwner'), 2);
    } finally { await rpc.close(); }
});

test('FAILURES are cached too — a dead RPC cannot be stampeded by a big roster', async () => {
    // Uncached failures would cost (members x 3) round trips on EVERY request during
    // an outage, turning someone else's outage into our own load test.
    const rpc = await startRpc({ fail: 'all' });
    try {
        const first = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000000 });
        assert.equal(first.degraded, true);
        const after = rpc.calls.length;
        const second = await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000030 });
        assert.equal(second.degraded, true);
        assert.equal(second.fromCache, true);
        assert.equal(rpc.calls.length, after, 'the second failing read spent no round trips');
    } finally { await rpc.close(); }
});

test('noCache bypasses the cache (the test seam itself is honest)', async () => {
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR } },
        balances: { [WALLET_A]: [100n * SKR] },
    });
    try {
        await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000000, noCache: true });
        await skr.getStakedPercentage(WALLET_A, { rpcUrl: rpc.url, nowSeconds: 1000000, noCache: true });
        assert.equal(rpc.count('getTokenAccountsByOwner'), 2);
    } finally { await rpc.close(); }
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE ROSTER AND THE AGGREGATE
// ═══════════════════════════════════════════════════════════════════════════

function recordingSql(route = []) {
    const calls = [];
    const fn = (strings, ...values) => {
        const text = Array.isArray(strings) ? strings.join(' ? ') : String(strings);
        calls.push({ text: text, values: values });
        for (const r of route) {
            if (!r.match.test(text)) continue;
            const hit = calls.filter(c => r.match.test(c.text)).length;
            if (typeof r.answer === 'function') {
                const a = r.answer(hit, values);
                if (a && a.throws) return Promise.reject(a.throws);
                return Promise.resolve(a && a.rows ? a.rows : []);
            }
            if (r.throws) return Promise.reject(r.throws);
            return Promise.resolve(r.rows || []);
        }
        return Promise.resolve([]);
    };
    fn.calls = calls;
    fn.matching = re => calls.filter(c => re.test(c.text));
    return fn;
}

const ROSTER_RE = /FROM\s+clan_members\s+m\s+LEFT\s+JOIN\s+wallet_identity/i;
const STAMP_RE = /UPDATE\s+wallet_identity[\s\S]*first_seen_staked_at\s*=\s*NOW\(\)/i;

function rosterRows(rows) {
    return rows.map(r => ({
        wallet: r.wallet,
        role: r.role || 'Member',
        first_seen_staked_at: r.stakedAt === undefined ? null : r.stakedAt,
        tenure_seconds: r.tenure || 0,
    }));
}

test('the roster query LEFT JOINs and lets POSTGRES compute the tenure', async () => {
    const sql = recordingSql([{ match: ROSTER_RE, rows: rosterRows([
        { wallet: WALLET_A, stakedAt: 'T0', tenure: 3600 },
    ]) }]);
    const roster = await vigilLib.readClanVigilRoster(sql, CLAN_ID);

    assert.equal(roster.length, 1);
    assert.equal(roster[0].tenureSeconds, 3600);

    const q = sql.matching(ROSTER_RE)[0].text;
    assert.match(q, /LEFT\s+JOIN/i,
        '⛔ INNER JOIN would drop a member whose fail-open wallet_identity row is missing');
    assert.match(q, /EXTRACT\(EPOCH\s+FROM\s*\(\s*NOW\(\)\s*-\s*wi\.first_seen_staked_at/i,
        'tenure is measured by Postgres, never by Node — the work order says so by name');
    assert.match(q, /WHERE\s+m\.clan_id\s*=\s*\?/i, 'scoped to ONE clan, as a bound parameter');
    assert.match(q, /ORDER\s+BY/i, 'deterministic order, never an unordered pick');
});

test('a member who has NEVER been seen staked reads tenure 0, not NULL', async () => {
    const sql = recordingSql([{ match: ROSTER_RE, rows: rosterRows([
        { wallet: WALLET_A, stakedAt: null, tenure: 0 },
    ]) }]);
    const roster = await vigilLib.readClanVigilRoster(sql, CLAN_ID);
    assert.equal(roster[0].tenureSeconds, 0);
    assert.equal(roster[0].firstSeenStakedAt, null);
});

test('TEST PLAN 3: vigil_weight = 0.5 x tenure_A, with the unstaked member contributing 0',
    async () => {
        const rpc = await startRpc({
            stakes: { [WALLET_A]: { shares: 100n * SKR }, [WALLET_B]: 'missing' },
            balances: { [WALLET_A]: [100n * SKR], [WALLET_B]: [900n * SKR] },
        });
        const sql = recordingSql([{ match: ROSTER_RE, rows: rosterRows([
            { wallet: WALLET_A, stakedAt: 'T0', tenure: 7200 },
            { wallet: WALLET_B, stakedAt: null, tenure: 0 },
        ]) }]);
        try {
            const v = await vigilLib.readClanVigil(sql, CLAN_ID, { rpcUrl: rpc.url });

            assert.equal(v.memberCount, 2);
            assert.equal(v.degraded, false);
            assert.equal(v.members[0].percentStaked, 0.5);
            assert.equal(v.members[0].tenureSeconds, 7200);
            assert.equal(v.members[0].vigilContribution, 0.5 * 7200);
            assert.equal(v.members[1].percentStaked, 0, 'B stakes nothing');
            assert.equal(v.members[1].vigilContribution, 0);
            assert.equal(v.vigilWeight, 0.5 * 7200, 'the sum is A alone');

            // B holds 900 SKR and stakes none, so nothing stamps B's Vigil.
            assert.equal(sql.matching(STAMP_RE).length, 0);
        } finally { await rpc.close(); }
    });

test('a member observed staked for the FIRST time has their Vigil stamped, with tenure 0 now',
    async () => {
        const rpc = await startRpc({
            stakes: { [WALLET_A]: { shares: 100n * SKR } },
            balances: { [WALLET_A]: [100n * SKR] },
        });
        const sql = recordingSql([
            { match: ROSTER_RE, rows: rosterRows([{ wallet: WALLET_A, stakedAt: null, tenure: 0 }]) },
            { match: STAMP_RE, rows: [{ first_seen_staked_at: 'T-NOW' }] },
        ]);
        try {
            const v = await vigilLib.readClanVigil(sql, CLAN_ID, { rpcUrl: rpc.url });

            const stamps = sql.matching(STAMP_RE);
            assert.equal(stamps.length, 1, 'stamped exactly once');
            assert.match(stamps[0].text, /first_seen_staked_at\s+IS\s+NULL/i,
                '⛔ the WHERE clause is what makes the stamp idempotent and un-movable');
            assert.match(stamps[0].text, /=\s*NOW\(\)/i, 'Postgres clock, never Node');

            assert.equal(v.members[0].vigilBegan, true);
            assert.equal(v.members[0].tenureSeconds, 0, 'a tenure that begins now IS zero');
            assert.equal(v.members[0].vigilContribution, 0);
            assert.equal(v.vigilWeight, 0);
        } finally { await rpc.close(); }
    });

test('an ALREADY-stamped member is never re-stamped — tenure cannot be reset', async () => {
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR } },
        balances: { [WALLET_A]: [100n * SKR] },
    });
    const sql = recordingSql([
        { match: ROSTER_RE, rows: rosterRows([{ wallet: WALLET_A, stakedAt: 'T0', tenure: 86400 }]) },
        { match: STAMP_RE, rows: [{ first_seen_staked_at: 'SHOULD-NEVER-BE-READ' }] },
    ]);
    try {
        const v = await vigilLib.readClanVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
        assert.equal(sql.matching(STAMP_RE).length, 0, '⛔ no UPDATE is even attempted');
        assert.equal(v.members[0].vigilBegan, false);
        assert.equal(v.members[0].tenureSeconds, 86400);
    } finally { await rpc.close(); }
});

test('a member whose stake could NOT be read is degraded, contributes 0, and does not stamp',
    async () => {
        const rpc = await startRpc({ fail: 'all' });
        const sql = recordingSql([{ match: ROSTER_RE, rows: rosterRows([
            { wallet: WALLET_A, stakedAt: 'T0', tenure: 7200 },
        ]) }]);
        try {
            const v = await vigilLib.readClanVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
            assert.equal(v.degraded, true);
            assert.equal(v.members[0].degraded, true);
            assert.equal(v.members[0].percentStaked, 0);
            assert.equal(v.members[0].vigilContribution, 0);
            assert.equal(v.vigilWeight, 0);
            assert.equal(sql.matching(STAMP_RE).length, 0,
                '⛔ an OUTAGE must never be the thing that begins someone\'s tenure');
        } finally { await rpc.close(); }
    });

test('ONE degraded member does not degrade or lose the others', async () => {
    // WALLET_B is not configured on the server, so its UserStake resolves to a real
    // NO_STAKE; the roster still carries a third unknown wallet that fails to derive.
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR }, [WALLET_B]: 'missing' },
        balances: { [WALLET_A]: [100n * SKR] },
    });
    const sql = recordingSql([{ match: ROSTER_RE, rows: rosterRows([
        { wallet: WALLET_A, stakedAt: 'T0', tenure: 100 },
        { wallet: 'not-a-valid-base58-wallet!!', stakedAt: 'T0', tenure: 100 },
        { wallet: WALLET_B, stakedAt: 'T0', tenure: 100 },
    ]) }]);
    try {
        const v = await vigilLib.readClanVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
        assert.equal(v.memberCount, 3);
        assert.equal(v.members[0].percentStaked, 0.5);
        assert.equal(v.members[0].vigilContribution, 50);
        assert.equal(v.members[1].degraded, true, 'the undecodable wallet is flagged');
        assert.equal(v.members[2].degraded, false, 'a real NO_STAKE is trustworthy');
        assert.equal(v.vigilWeight, 50, 'A still contributes in full');
        assert.equal(v.degraded, true, 'the top-level flag summarises that SOMETHING was unreadable');
    } finally { await rpc.close(); }
});

test('an empty clan is weight 0 and spends no RPC at all', async () => {
    const rpc = await startRpc({});
    const sql = recordingSql([{ match: ROSTER_RE, rows: [] }]);
    try {
        const v = await vigilLib.readClanVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
        assert.deepEqual(v.members, []);
        assert.equal(v.vigilWeight, 0);
        assert.equal(v.degraded, false);
        assert.equal(rpc.calls.length, 0);
    } finally { await rpc.close(); }
});

test('concurrency is BOUNDED — an unbounded clan cannot fan out unbounded RPC', async () => {
    assert.equal(vigilLib.VIGIL_READ_CONCURRENCY, 5);

    // Measure it directly: count how many workers are in flight at the peak.
    let inFlight = 0;
    let peak = 0;
    const items = new Array(20).fill(0).map((_, i) => i);
    const out = await vigilLib.mapWithConcurrency(items, 5, async (item) => {
        inFlight++;
        peak = Math.max(peak, inFlight);
        await new Promise(r => setTimeout(r, 2));
        inFlight--;
        return item * 2;
    });
    assert.ok(peak <= 5, 'peak in-flight was ' + peak + ', over the limit');
    assert.ok(peak > 1, 'nothing ran in parallel at all — the limit is not a limit, it is a queue of 1');
    assert.deepEqual(out.slice(0, 3), [0, 2, 4], 'results stay in INPUT order');
});

test('mapWithConcurrency turns a throwing worker into a value, never a lost roster', async () => {
    const out = await vigilLib.mapWithConcurrency([1, 2, 3], 2, async (n) => {
        if (n === 2) throw new Error('boom');
        return n;
    });
    assert.equal(out[0], 1);
    assert.ok(out[1] && out[1].__threw, 'the thrower is captured as a value');
    assert.equal(out[2], 3, 'and the work after it still completed');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. THE ENDPOINT
// ═══════════════════════════════════════════════════════════════════════════

const DRIVER_ID = require.resolve('@neondatabase/serverless');

function fakeRes() {
    const out = { statusCode: null, body: null, headers: {}, ended: false };
    const res = {
        setHeader(k, v) { out.headers[String(k).toLowerCase()] = v; return res; },
        status(code) { out.statusCode = code; return res; },
        json(obj) { out.body = obj; return res; },
        send() { return res; },
        end() { out.ended = true; return res; },
    };
    res.out = out;
    return res;
}

const FRESH = [ROUTE_PATH, CLAN_HTTP, CLAN_LIB, CLAN_VIGIL];

function loadRoute(sqlFn) {
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = { neon() { return sqlFn; } };
    for (const p of FRESH) delete require.cache[require.resolve(p)];
    const handler = require(ROUTE_PATH);
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    for (const p of FRESH) delete require.cache[require.resolve(p)];
    return handler;
}

async function callRoute(opts = {}) {
    const sqlFn = opts.sql || recordingSql();
    const headers = Object.assign({ 'x-session': 'a'.repeat(44) }, opts.headers || {});
    const req = { method: opts.method || 'GET', headers: headers, query: opts.query || {} };
    const prevUrl = process.env.DATABASE_URL;
    const prevRpc = process.env.SOLANA_MAINNET_RPC_URL;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    if (opts.rpcUrl) process.env.SOLANA_MAINNET_RPC_URL = opts.rpcUrl;
    const handler = loadRoute(sqlFn);
    const res = fakeRes();
    try {
        await handler(req, res);
    } finally {
        if (prevUrl === undefined) delete process.env.DATABASE_URL;
        else process.env.DATABASE_URL = prevUrl;
        if (prevRpc === undefined) delete process.env.SOLANA_MAINNET_RPC_URL;
        else process.env.SOLANA_MAINNET_RPC_URL = prevRpc;
    }
    return { out: res.out, sql: sqlFn };
}

const MEMBERSHIP_RE = /FROM\s+clan_members\s+m\s*\n?\s*JOIN\s+clans/i;

/** A route-level sql that authenticates the wallet rail and answers the two reads. */
function authedSql(extra = []) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET_A, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i,
          rows: [{ first_seen_at: 'T0', last_seen_at: 'T0', first_seen_staked_at: 'T0' }] },
        ...extra,
        { match: /FROM\s+clan_members\s+m[\s\S]*JOIN\s+clans/i, rows: [{
            clan_id: CLAN_ID, code: 'ABCD12', name: 'Ember Wardens', tag: 'EMBR',
            join_policy: 'invite', created_at: 'T0', role: 'Leader', joined_at: 'T0',
            member_count: 2,
        }] },
    ]);
}

test('GET /api/clan/vigil returns the work order\'s field names exactly', async () => {
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR }, [WALLET_B]: 'missing' },
        balances: { [WALLET_A]: [100n * SKR], [WALLET_B]: [] },
    });
    try {
        const { out } = await callRoute({
            rpcUrl: rpc.url,
            query: { playerId: WALLET_A },
            sql: authedSql([{ match: ROSTER_RE, rows: rosterRows([
                { wallet: WALLET_A, stakedAt: 'T0', tenure: 7200 },
                { wallet: WALLET_B, stakedAt: null, tenure: 0 },
            ]) }]),
        });

        assert.equal(out.statusCode, 200);
        assert.equal(out.body.ok, true);
        // The work order names these fields; a client branches on these strings.
        assert.equal(typeof out.body.vigil_weight, 'number');
        assert.equal(out.body.member_count, 2);
        assert.deepEqual(Object.keys(out.body.members[0]).sort(),
            ['degraded', 'percent_staked', 'role', 'tenure_seconds', 'vigil_contribution', 'wallet'].sort());
        assert.equal(out.body.members[0].percent_staked, 0.5);
        assert.equal(out.body.members[0].tenure_seconds, 7200);
        assert.equal(out.body.members[0].vigil_contribution, 3600);
        assert.equal(out.body.vigil_weight, 3600);
        assert.equal(out.body.degraded, false);
    } finally { await rpc.close(); }
});

test('ACCEPTANCE 4: an RPC failure is a 200 with degraded, NEVER a 500', async () => {
    const rpc = await startRpc({ fail: 'all' });
    try {
        const { out } = await callRoute({
            rpcUrl: rpc.url,
            query: { playerId: WALLET_A },
            sql: authedSql([{ match: ROSTER_RE, rows: rosterRows([
                { wallet: WALLET_A, stakedAt: 'T0', tenure: 7200 },
            ]) }]),
        });
        assert.equal(out.statusCode, 200, '⛔ the chain is a dependency we report ON, never fail WITH');
        assert.equal(out.body.ok, true);
        assert.equal(out.body.degraded, true);
        assert.equal(out.body.members[0].degraded, true);
        assert.equal(out.body.members[0].percent_staked, 0);
        assert.equal(out.body.members[0].tenure_seconds, 7200, 'the tenure is still known and still told');
    } finally { await rpc.close(); }
});

test('a caller in NO clan is 404 CLAN_NOT_IN_CLAN, and spends no RPC', async () => {
    const rpc = await startRpc({});
    try {
        const sql = recordingSql([
            { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET_A, revoked: false, expired: false }] },
            { match: /INSERT\s+INTO\s+wallet_identity/i,
              rows: [{ first_seen_at: 'T0', last_seen_at: 'T0', first_seen_staked_at: 'T0' }] },
            { match: /FROM\s+clan_members\s+m[\s\S]*JOIN\s+clans/i, rows: [] },
        ]);
        const { out } = await callRoute({ rpcUrl: rpc.url, query: { playerId: WALLET_A }, sql });
        assert.equal(out.statusCode, 404);
        assert.equal(out.body.error, ClanCode.NOT_IN_CLAN);
        assert.equal(out.body.error, 'CLAN_NOT_IN_CLAN', 'the EXISTING code, not a new one');
        assert.equal(sql.matching(ROSTER_RE).length, 0, 'no roster read for a caller with no clan');
        assert.equal(rpc.calls.length, 0);
    } finally { await rpc.close(); }
});

test('an unauthenticated request is refused in the SAME shape as every other clan route', async () => {
    const { out } = await callRoute({ query: { playerId: WALLET_A }, headers: { 'x-session': '' } });
    assert.equal(out.statusCode, 401);
    assert.ok(out.body.code, 'carries a stable machine code');
    assert.ok(out.body.ref, 'and a ref, exactly like quietFail everywhere else');
    assert.equal(out.body.ok, false);
});

test('a POST is refused — this route is a read', async () => {
    const { out } = await callRoute({ method: 'POST', query: { playerId: WALLET_A } });
    assert.equal(out.statusCode, 400);
    assert.equal(out.body.code, AuthCode.METHOD_NOT_ALLOWED);
});

test('ACCEPTANCE 6: no wallet address appears in ANY refusal body, at any depth', async () => {
    // The endpoint renders wallets to a PROVEN clan member (that is the roster). It must
    // never leak one into an error body, which is the surface an unauthorised caller sees.
    const cases = [
        await callRoute({ query: { playerId: WALLET_A }, headers: { 'x-session': '' } }),
        await callRoute({ method: 'POST', query: { playerId: WALLET_A } }),
        await callRoute({ query: {}, headers: { 'x-session': 'a'.repeat(44) } }),
    ];
    for (const c of cases) {
        assert.ok(c.out.statusCode >= 400, 'fixture assumption: these are all refusals');
        const json = JSON.stringify(c.out.body || {});
        assert.ok(!json.includes(WALLET_A),
            'a wallet address reached a refusal body: ' + json);
        assert.ok(!json.includes(WALLET_B), 'a wallet address reached a refusal body: ' + json);
    }
});

test('a DATABASE failure is the one 500 — and it carries no wallet either', async () => {
    const sql = recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET_A, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i,
          rows: [{ first_seen_at: 'T0', last_seen_at: 'T0', first_seen_staked_at: 'T0' }] },
        { match: /FROM\s+clan_members\s+m[\s\S]*JOIN\s+clans/i, throws: new Error('relation missing') },
    ]);
    const { out } = await callRoute({ query: { playerId: WALLET_A }, sql });
    assert.equal(out.statusCode, 500);
    assert.equal(out.body.code, AuthCode.SERVER_ERROR);
    assert.ok(!JSON.stringify(out.body).includes(WALLET_A));
});

test('the route spends NO clan rate budget — a read cannot cost you the ability to leave', async () => {
    const rpc = await startRpc({ stakes: { [WALLET_A]: 'missing' }, balances: {} });
    try {
        const { sql } = await callRoute({
            rpcUrl: rpc.url,
            query: { playerId: WALLET_A },
            sql: authedSql([{ match: ROSTER_RE, rows: rosterRows([
                { wallet: WALLET_A, stakedAt: null, tenure: 0 },
            ]) }]),
        });
        assert.equal(sql.matching(/clan_rate_limit/i).length, 0,
            'beginClanRequest was called with THREE arguments, so no budget is spent');
    } finally { await rpc.close(); }
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. THE AUTH-PATH STAMP (wallet-auth.touchWalletIdentity)
// ═══════════════════════════════════════════════════════════════════════════

test('touchWalletIdentity still RETURNS the two WO-1844 columns and never throws', async () => {
    const auth = require(WALLET_AUTH);
    const sql = recordingSql([{ match: /INSERT\s+INTO\s+wallet_identity/i,
        rows: [{ first_seen_at: 'T0', last_seen_at: 'T1', first_seen_staked_at: 'T2' }] }]);
    const r = await auth.touchWalletIdentity(sql, WALLET_A);
    assert.equal(r.ok, true);
    assert.equal(r.firstSeenAt, 'T0');
    assert.equal(r.lastSeenAt, 'T1');
    // Already stamped => no probe, no UPDATE. This early return is what bounds the cost.
    assert.equal(sql.matching(STAMP_RE).length, 0);
});

test('the UPSERT still names last_seen_at ALONE — first_seen_at must survive every visit', async () => {
    const auth = require(WALLET_AUTH);
    const sql = recordingSql([{ match: /INSERT\s+INTO\s+wallet_identity/i,
        rows: [{ first_seen_at: 'T0', last_seen_at: 'T1', first_seen_staked_at: 'T2' }] }]);
    await auth.touchWalletIdentity(sql, WALLET_A);
    const q = sql.matching(/INSERT\s+INTO\s+wallet_identity/i)[0].text;
    assert.match(q, /ON\s+CONFLICT\s*\(\s*wallet\s*\)\s*DO\s+UPDATE\s+SET\s*\n?\s*last_seen_at\s*=\s*NOW\(\)/i,
        '⛔ WO-1844 invariant: the conflict clause sets last_seen_at and NOTHING else');
    assert.match(q, /RETURNING[^;]*first_seen_staked_at/i,
        'WO-1852 reads the column in the SAME round trip rather than adding a SELECT');
});

test('a missing wallet_identity table is still fail-open (never a 500 on the auth path)', async () => {
    const auth = require(WALLET_AUTH);
    const sql = recordingSql([{ match: /INSERT\s+INTO\s+wallet_identity/i,
        throws: new Error('relation "wallet_identity" does not exist') }]);
    const r = await auth.touchWalletIdentity(sql, WALLET_A);
    assert.equal(r.ok, true);
    assert.equal(r.degraded, true);
});

test('an UNSTAKED wallet is never stamped — first_seen_staked_at stays NULL', async () => {
    // ACCEPTANCE 1. WALLET_A has no UserStake account and holds no SKR.
    const auth = require(WALLET_AUTH);
    const rpc = await startRpc({ stakes: { [WALLET_A]: 'missing' }, balances: {} });
    const prev = process.env.SOLANA_MAINNET_RPC_URL;
    process.env.SOLANA_MAINNET_RPC_URL = rpc.url;
    skr._resetVigilCache();
    try {
        const sql = recordingSql([{ match: /INSERT\s+INTO\s+wallet_identity/i,
            rows: [{ first_seen_at: 'T0', last_seen_at: 'T1', first_seen_staked_at: null }] }]);
        const r = await auth.touchWalletIdentity(sql, WALLET_A);
        assert.equal(r.ok, true);
        assert.equal(r.stakedStamped, false);
        assert.equal(sql.matching(STAMP_RE).length, 0, '⛔ no UPDATE for a wallet with no stake');
    } finally {
        if (prev === undefined) delete process.env.SOLANA_MAINNET_RPC_URL;
        else process.env.SOLANA_MAINNET_RPC_URL = prev;
        await rpc.close();
    }
});

test('ACCEPTANCE 2: a STAKED wallet is stamped on first observation, with Postgres NOW()', async () => {
    const auth = require(WALLET_AUTH);
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR } },
        balances: { [WALLET_A]: [100n * SKR] },
    });
    const prev = process.env.SOLANA_MAINNET_RPC_URL;
    process.env.SOLANA_MAINNET_RPC_URL = rpc.url;
    skr._resetVigilCache();
    try {
        const sql = recordingSql([
            { match: /INSERT\s+INTO\s+wallet_identity/i,
              rows: [{ first_seen_at: 'T0', last_seen_at: 'T1', first_seen_staked_at: null }] },
            { match: STAMP_RE, rows: [{ first_seen_staked_at: 'T-NOW' }] },
        ]);
        const r = await auth.touchWalletIdentity(sql, WALLET_A);
        assert.equal(r.stakedStamped, true);
        assert.equal(r.firstSeenStakedAt, 'T-NOW');

        const stamps = sql.matching(STAMP_RE);
        assert.equal(stamps.length, 1);
        assert.match(stamps[0].text, /first_seen_staked_at\s*=\s*NOW\(\)/i,
            'Postgres time, not Node time — the work order names this to avoid clock skew');
        assert.match(stamps[0].text, /WHERE\s+wallet\s*=\s*\?\s*AND\s+first_seen_staked_at\s+IS\s+NULL/i,
            '⛔ idempotent by its WHERE clause: a tenure already begun can never be moved');
    } finally {
        if (prev === undefined) delete process.env.SOLANA_MAINNET_RPC_URL;
        else process.env.SOLANA_MAINNET_RPC_URL = prev;
        await rpc.close();
    }
});

test('an RPC OUTAGE never begins a tenure', async () => {
    const auth = require(WALLET_AUTH);
    const rpc = await startRpc({ fail: 'all' });
    const prev = process.env.SOLANA_MAINNET_RPC_URL;
    process.env.SOLANA_MAINNET_RPC_URL = rpc.url;
    skr._resetVigilCache();
    try {
        const sql = recordingSql([{ match: /INSERT\s+INTO\s+wallet_identity/i,
            rows: [{ first_seen_at: 'T0', last_seen_at: 'T1', first_seen_staked_at: null }] }]);
        const r = await auth.touchWalletIdentity(sql, WALLET_A);
        assert.equal(r.ok, true, 'and the auth still succeeds');
        assert.equal(r.stakedStamped, false);
        assert.equal(sql.matching(STAMP_RE).length, 0);
    } finally {
        if (prev === undefined) delete process.env.SOLANA_MAINNET_RPC_URL;
        else process.env.SOLANA_MAINNET_RPC_URL = prev;
        await rpc.close();
    }
});

test('VIGIL_STAMP_ON_AUTH=false spends NO RPC on the auth path at all', async () => {
    const auth = require(WALLET_AUTH);
    const rpc = await startRpc({
        stakes: { [WALLET_A]: { shares: 100n * SKR } },
        balances: { [WALLET_A]: [100n * SKR] },
    });
    const prevRpc = process.env.SOLANA_MAINNET_RPC_URL;
    const prevFlag = process.env.VIGIL_STAMP_ON_AUTH;
    process.env.SOLANA_MAINNET_RPC_URL = rpc.url;
    process.env.VIGIL_STAMP_ON_AUTH = 'false';
    skr._resetVigilCache();
    try {
        assert.equal(auth.vigilStampOnAuthEnabled(), false);
        const sql = recordingSql([{ match: /INSERT\s+INTO\s+wallet_identity/i,
            rows: [{ first_seen_at: 'T0', last_seen_at: 'T1', first_seen_staked_at: null }] }]);
        const r = await auth.touchWalletIdentity(sql, WALLET_A);
        assert.equal(r.ok, true);
        assert.equal(rpc.calls.length, 0, 'the switch is a real switch, not a filter after the cost');
        assert.equal(sql.matching(STAMP_RE).length, 0);
    } finally {
        if (prevRpc === undefined) delete process.env.SOLANA_MAINNET_RPC_URL;
        else process.env.SOLANA_MAINNET_RPC_URL = prevRpc;
        if (prevFlag === undefined) delete process.env.VIGIL_STAMP_ON_AUTH;
        else process.env.VIGIL_STAMP_ON_AUTH = prevFlag;
        await rpc.close();
    }
});

test('the auth-path switch DEFAULTS ON, per the work order', () => {
    const auth = require(WALLET_AUTH);
    const prev = process.env.VIGIL_STAMP_ON_AUTH;
    delete process.env.VIGIL_STAMP_ON_AUTH;
    try {
        assert.equal(auth.vigilStampOnAuthEnabled(), true);
        process.env.VIGIL_STAMP_ON_AUTH = 'off';
        assert.equal(auth.vigilStampOnAuthEnabled(), false);
        process.env.VIGIL_STAMP_ON_AUTH = '0';
        assert.equal(auth.vigilStampOnAuthEnabled(), false);
        process.env.VIGIL_STAMP_ON_AUTH = 'true';
        assert.equal(auth.vigilStampOnAuthEnabled(), true);
    } finally {
        if (prev === undefined) delete process.env.VIGIL_STAMP_ON_AUTH;
        else process.env.VIGIL_STAMP_ON_AUTH = prev;
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 7. THE STANDING INVARIANTS THIS TICKET MUST NOT HAVE BROKEN
// ═══════════════════════════════════════════════════════════════════════════

test('WO-1851\'s clan.js is NOT touched by this lane', () => {
    // The brief: do not touch WO-1851's files again. Asserted structurally — the Vigil
    // library is a separate module and clan.js exports no Vigil symbol.
    const clan = require(CLAN_LIB);
    assert.equal(clan.readClanVigil, undefined);
    assert.equal(clan.readClanVigilRoster, undefined);
    assert.ok(typeof vigilLib.readClanVigil === 'function', 'it lives in clan-vigil.js instead');
});

test('the leaderboard still renders NO wallet — WO-1850\'s property is untouched', () => {
    const fs = require('node:fs');
    const src = fs.readFileSync(path.join(REPO, 'api', 'clan', 'leaderboard.js'), 'utf8');
    assert.ok(!/clan-vigil/.test(src),
        'the leaderboard does not import the Vigil — swapping its metric is a later ticket');
});

test('SKR_MINT is the mainnet mint, and this file does not carry a second copy of it', () => {
    // ⛔ VERIFIED AT SOURCE, TWICE, before any balance-read code was written:
    //    api/_lib/skr-staking.js:105  SKR_MINT           = SKRbvo6Gf7...NPGZhW3
    //    api/_lib/purchase-catalog.js:31 MAINNET_SKR_MINT = SKRbvo6Gf7...NPGZhW3
    // and confirmed live: getTokenSupply returned decimals 6 at slot 447954072.
    assert.equal(skr.SKR_MINT, 'SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3');
    assert.equal(skr.SKR_DECIMALS, 6);
    const catalog = require(path.join(REPO, 'api', '_lib', 'purchase-catalog.js'));
    assert.equal(catalog.MAINNET_SKR_MINT, skr.SKR_MINT,
        'the two existing copies agree; the Vigil adds no third');
    const fs = require('node:fs');
    const src = fs.readFileSync(CLAN_VIGIL, 'utf8');
    assert.ok(!/SKRbvo6/.test(src), 'clan-vigil.js reads the mint from skr-staking, never a literal');
});
