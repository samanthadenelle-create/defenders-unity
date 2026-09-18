// =============================================================================
// test/clan-vault-genesis.test.js — WO-1854 (clan WO-11). The Collective Vigil:
// Squads multisig vault + Seeker Genesis Token.
// -----------------------------------------------------------------------------
//     node --test test/clan-vault-genesis.test.js
//
// ⭐ THE RPC IS A REAL LOCAL HTTP SERVER, NOT A STUBBED FUNCTION, and the fixtures it
// serves are TRANSCRIBED FROM MAINNET, not invented. That combination is what makes the
// offline suite meaningful:
//   * the Genesis Token mint fixture carries the five extensions the REAL SGT mint
//     5H4VRJ378TMhhKK91fyAEN84LhnuFaooD93ojzkWW1Kp actually carries, read on 2026-09-17;
//   * the Squads Multisig fixture is 231 BYTES OF REAL MAINNET ACCOUNT DATA, base64 as
//     served, from JEJJhPFUoTzFsAQAoFYUEQDmFUPJjCifHfLgn3V9nYJq — so the decode is proven
//     against the chain rather than against a buffer this file wrote to suit itself;
//   * `getTokenAccountsByOwnerV2`'s object-shaped `value` and its non-null last-page
//     `paginationKey` are reproduced as MEASURED, because those are the two traps a
//     V1-shaped reader falls into and a friendlier fixture would hide both.
//
// Zero mainnet at test time, zero database, zero Unity — the rule
// test/skr-staking.test.js's header states: a test that needs the chain is a test that
// fails on a plane. The live proofs are recorded in WORK_ORDER_1854's implementation
// record; the fixtures here are their offline residue.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const GT_PATH = path.join(REPO, 'api', '_lib', 'genesis-token.js');
const VAULTS_PATH = path.join(REPO, 'api', '_lib', 'clan-vaults.js');
const SKR_PATH = path.join(REPO, 'api', '_lib', 'skr-staking.js');
const CLAN_LIB = path.join(REPO, 'api', '_lib', 'clan.js');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const CLAN_VIGIL = path.join(REPO, 'api', '_lib', 'clan-vigil.js');
const WALLET_AUTH = path.join(REPO, 'api', '_lib', 'wallet-auth.js');
const VIGIL_ROUTE = path.join(REPO, 'api', 'clan', 'vigil.js');
const REGISTER_ROUTE = path.join(REPO, 'api', 'clan', 'vault', 'register.js');

const gt = require(GT_PATH);
const vaults = require(VAULTS_PATH);
const skr = require(SKR_PATH);
const auth = require(WALLET_AUTH);
const vigilLib = require(CLAN_VIGIL);
const { AuthCode } = auth;
const { ClanCode } = require(CLAN_LIB);

// ── Real mainnet identities, used as the fixtures' subjects ───────────────────
// The SGT holder and its mint, both read off the real mint transaction
// 2ZxxArSSkEmrmMogj27n… on 2026-09-17 (see api/_lib/genesis-token.js's header).
const SEEKER_A = 'Bq1ntjWoEX19WbkxoTwpXPiRtDFbnE3MjbLnZcx3ZyNA';
const SGT_MINT_A = '5H4VRJ378TMhhKK91fyAEN84LhnuFaooD93ojzkWW1Kp';
// A second wallet and a second (invented but well-formed) per-device mint. Per-device is
// the proven shape, so two Seekers means two mints — not two holders of one mint.
const SEEKER_B = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const SGT_MINT_B = '8V1BmioKt28HerjpmbBsQPnYQjGATm2iXkJNmDWGVMyM';
// A wallet with no Seeker at all, and a junk Token-2022 mint it does hold.
const PLAIN_C = 'DLQtTaJKEU8yCxC2boPMdttJf3BWDNzFbJXGei8upiUZ';
const JUNK_MINT = '5BxcwkSrbZs4RT1we2xABuJ9XDyqT1pfehZVGaZ5qgFF';

const CLAN_ID = '3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e';
const SKR_UNIT = 1000000n;

// ── THE REAL MAINNET MULTISIG ────────────────────────────────────────────────
// getAccountInfo(JEJJhPFUoTzFsAQAoFYUEQDmFUPJjCifHfLgn3V9nYJq, base64), 231 bytes, owner
// SQDS4ep65T869zMMBKyuUq6aD6EgTu8psMjkvj52pCf. Captured 2026-09-17. Decodes to
// threshold 2 / 3 members with masks 7, 2, 7.
const REAL_MULTISIG_ADDRESS = 'JEJJhPFUoTzFsAQAoFYUEQDmFUPJjCifHfLgn3V9nYJq';
const REAL_MULTISIG_B64 =
      '4HR5ukShT+w/zGoDuEdN/paXPXpEs9wppqAhsuXs1GsGwboL05mXowAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
    + 'AAAAAgAAAAAAAQAAAAAAAAAAAAAAAAAAAAH24hM21hnbhewDltWRRY8ir3LTUdC5diIfzXpYuffDQPwDAAAAIWVBx10U'
    + '9lPUyGXbY80MV4Q5KI4xJUEzY35w7i2IHioHYUKfS2jz3kJ0PgCq6STFi4eqFg1I95JUnHT/FfEcrDoCZBBHdfi0hcgH'
    + '1gytSKv4oHGB8NZ/rQb/950QOE3miowH';
// Measured, not derived by this test: getVaultPda({multisigPda, index: 0}).
const REAL_VAULT_0 = 'DUQCftnDJJc4DoqyjPiZETPn9o4EBhwAGrUJoniQgg12';
const SQUADS_PROGRAM = 'SQDS4ep65T869zMMBKyuUq6aD6EgTu8psMjkvj52pCf';

// ═══════════════════════════════════════════════════════════════════════════
// FIXTURES — shaped exactly as mainnet answered
// ═══════════════════════════════════════════════════════════════════════════

/** One Token-2022 token-account entry, jsonParsed, as V1 and V2 both nest it. */
function token2022Account(owner, mint, amount, pubkey) {
    return {
        pubkey: pubkey,
        account: {
            lamports: 2039280,
            data: {
                program: 'spl-token-2022',
                parsed: {
                    info: {
                        isNative: false, mint: mint, owner: owner,
                        // The real SGT ATA is FROZEN after the mint transfer — transcribed
                        // rather than normalised to 'initialized', so nothing in the reader
                        // may quietly depend on the friendlier value.
                        state: 'frozen',
                        tokenAmount: {
                            amount: String(amount), decimals: 0,
                            uiAmount: Number(amount), uiAmountString: String(amount),
                        },
                    },
                    type: 'account',
                },
                space: 170,
            },
            owner: gt.TOKEN_2022_PROGRAM_ID,
            executable: false,
        },
    };
}

/**
 * A Genesis Token MINT account, carrying the five extensions the real one carries.
 * `overrides` lets a test break exactly one leg of the three-part check.
 */
function sgtMintAccount(mint, overrides) {
    const o = overrides || {};
    const authority = o.mintAuthority !== undefined ? o.mintAuthority : gt.SGT_MINT_AUTHORITY;
    const metadataAddress = o.metadataAddress !== undefined ? o.metadataAddress : gt.SGT_GROUP_ADDRESS;
    const group = o.group !== undefined ? o.group : gt.SGT_GROUP_ADDRESS;
    const extensions = [];
    if (!o.dropMetadataPointer) {
        extensions.push({ extension: 'metadataPointer',
            state: { authority: gt.SGT_MINT_AUTHORITY, metadataAddress: metadataAddress } });
    }
    extensions.push({ extension: 'permanentDelegate', state: { delegate: gt.SGT_MINT_AUTHORITY } });
    extensions.push({ extension: 'mintCloseAuthority', state: { closeAuthority: gt.SGT_MINT_AUTHORITY } });
    extensions.push({ extension: 'groupMemberPointer',
        state: { authority: gt.SGT_MINT_AUTHORITY, memberAddress: mint } });
    if (!o.dropGroupMember) {
        extensions.push({ extension: 'tokenGroupMember',
            state: { group: group, memberNumber: o.memberNumber || 96167, mint: mint } });
    }
    return {
        program: 'spl-token-2022',
        parsed: {
            type: 'mint',
            info: {
                decimals: 0, supply: '1', isInitialized: true,
                mintAuthority: authority, freezeAuthority: gt.SGT_MINT_AUTHORITY,
                extensions: extensions,
            },
        },
        space: 400,
    };
}

/** A plain Token-2022 mint nobody official ever touched. */
function junkMintAccount(mint) {
    return {
        program: 'spl-token-2022',
        parsed: {
            type: 'mint',
            info: {
                decimals: 6, supply: '1000', isInitialized: true,
                mintAuthority: PLAIN_C, freezeAuthority: null, extensions: [],
            },
        },
        space: 182,
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// THE RECORDING RPC SERVER
// ═══════════════════════════════════════════════════════════════════════════

/**
 * @param {object} cfg
 *   cfg.holdings   { [wallet]: [ {mint, amount} ] }  Token-2022 accounts per owner
 *   cfg.mints      { [mint]: <mint account>|'missing'|'spl' }
 *   cfg.multisigs  { [address]: base64 | 'missing' | 'wrongOwner' }
 *   cfg.stakes     { [address]: { shares } | 'missing' }   SKR UserStake, per skr-staking
 *   cfg.balances   { [address]: [amountRaw] }              SKR SPL accounts
 *   cfg.noV2       true => answer -32601 to getTokenAccountsByOwnerV2 (a non-Helius plan)
 *   cfg.pageLimit  force a small V2 page size so pagination is exercised
 *   cfg.fail       'all' | 'tokens' | false
 */
async function startRpc(cfg) {
    const conf = cfg || {};
    const calls = [];

    const stakePdaToOwner = new Map();
    for (const a of Object.keys(conf.stakes || {})) {
        stakePdaToOwner.set(skr.deriveUserStakePda(a).address, a);
    }

    const server = http.createServer((req, res) => {
        let raw = '';
        req.on('data', (c) => { raw += c; });
        req.on('end', () => {
            let msg = {};
            try { msg = JSON.parse(raw); } catch (_) { msg = {}; }
            const method = msg.method;
            const params = msg.params || [];
            const config = params[2] || {};
            calls.push({ method: method, address: typeof params[0] === 'string' ? params[0] : null,
                filter: params[1] || null, config: config });

            const send = (obj) => {
                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify(Object.assign({ jsonrpc: '2.0', id: msg.id }, obj)));
            };
            const die = () => { res.writeHead(500); res.end('rpc down'); };

            if (conf.fail === 'all') return die();

            // ── Token-2022 enumeration, both methods ────────────────────────
            if (method === 'getTokenAccountsByOwnerV2' || method === 'getTokenAccountsByOwner') {
                const filter = params[1] || {};
                // An SKR balance read (mint filter) belongs to skr-staking, not here.
                if (filter.mint) {
                    const amounts = (conf.balances || {})[params[0]] || [];
                    return send({ result: { context: { slot: 447962077 },
                        value: amounts.map((a, i) => splTokenEntry(params[0], a, 'skr' + i)) } });
                }
                if (conf.fail === 'tokens') return die();
                if (method === 'getTokenAccountsByOwnerV2' && conf.noV2) {
                    // ⭐ THE EXACT SHAPE MEASURED FROM A REAL UNKNOWN METHOD: HTTP 200 with
                    // a JSON-RPC error whose code is -32601. It is NOT an HTTP error, which
                    // is why the reader has to inspect the code rather than the status.
                    return send({ error: { code: -32601, message: 'Method not found' } });
                }
                const all = ((conf.holdings || {})[params[0]] || []).map(
                    (h, i) => token2022Account(params[0], h.mint, h.amount, 'ata' + i + h.mint.slice(0, 4)));

                if (method === 'getTokenAccountsByOwner') {
                    // V1: value IS the array.
                    return send({ result: { context: { slot: 447962077 }, value: all } });
                }
                // V2: value is an OBJECT, and its paginationKey is non-null even on the
                // last page — both measured on 2026-09-17.
                const limit = conf.pageLimit || Number(config.limit) || gt.V2_PAGE_LIMIT;
                const start = config.paginationKey ? Number(config.paginationKey) : 0;
                const page = all.slice(start, start + limit);
                return send({ result: { context: { slot: 447962077, apiVersion: '4.3.0-alpha.2' },
                    value: { accounts: page, count: page.length,
                             paginationKey: String(start + page.length) } } });
            }

            if (method === 'getAccountInfo') {
                const addr = params[0];

                // A mint?
                const mintSpec = (conf.mints || {})[addr];
                if (mintSpec === 'missing') return send({ result: { context: { slot: 1 }, value: null } });
                if (mintSpec === 'spl') {
                    return send({ result: { context: { slot: 1 }, value: {
                        lamports: 1, owner: 'TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA',
                        data: { program: 'spl-token', parsed: { type: 'mint', info: {} } },
                        executable: false } } });
                }
                if (mintSpec) {
                    return send({ result: { context: { slot: 1 }, value: {
                        lamports: 1, owner: gt.TOKEN_2022_PROGRAM_ID,
                        data: mintSpec, executable: false } } });
                }

                // A Squads multisig?
                const msSpec = (conf.multisigs || {})[addr];
                if (msSpec === 'missing') return send({ result: { context: { slot: 1 }, value: null } });
                if (msSpec === 'wrongOwner') {
                    return send({ result: { context: { slot: 1 }, value: {
                        lamports: 1, owner: '11111111111111111111111111111111',
                        data: ['', 'base64'], executable: false } } });
                }
                if (typeof msSpec === 'string') {
                    return send({ result: { context: { slot: 1 }, value: {
                        lamports: 1, owner: SQUADS_PROGRAM,
                        data: [msSpec, 'base64'], executable: false } } });
                }

                // SKR staking accounts (skr-staking's own fixtures).
                if (addr === skr.STAKE_CONFIG) {
                    return send({ result: { context: { slot: 447962077 }, value: {
                        lamports: 1, owner: skr.STAKING_PROGRAM,
                        data: [stakeConfigAccount().toString('base64'), 'base64'],
                        executable: false } } });
                }
                const owner = stakePdaToOwner.get(addr);
                const spec = owner ? conf.stakes[owner] : 'missing';
                if (!owner || spec === 'missing') {
                    return send({ result: { context: { slot: 447962077 }, value: null } });
                }
                return send({ result: { context: { slot: 447962077 }, value: {
                    lamports: 1, owner: skr.STAKING_PROGRAM,
                    data: [userStakeAccount(spec).toString('base64'), 'base64'],
                    executable: false } } });
            }

            return send({ error: { code: -32601, message: 'Method not found' } });
        });
    });

    await new Promise((r) => server.listen(0, '127.0.0.1', r));
    return {
        url: 'http://127.0.0.1:' + server.address().port,
        calls: calls,
        count: (m, a) => calls.filter((c) => c.method === m && (a == null || c.address === a)).length,
        close: () => new Promise((r) => server.close(r)),
    };
}

// skr-staking fixtures, byte-identical to test/clan-vigil.test.js's (the IDL offsets).
function writeU128LE(buf, offset, value) {
    const v = BigInt(value);
    buf.writeBigUInt64LE(v & 0xffffffffffffffffn, offset);
    buf.writeBigUInt64LE(v >> 64n, offset + 8);
}
function stakeConfigAccount() {
    const b = Buffer.alloc(skr.STAKE_CONFIG_SIZE);
    Buffer.from([238, 151, 43, 3, 11, 151, 63, 176]).copy(b, 0);
    b.writeBigUInt64LE(1n, 105);
    b.writeBigUInt64LE(172800n, 113);
    writeU128LE(b, 121, 10n ** 18n);
    writeU128LE(b, 137, skr.SHARE_PRICE_SCALE);   // share_price 1e9 => shares === SKR
    return b;
}
function userStakeAccount({ shares = 0n, unstaking = 0n } = {}) {
    const b = Buffer.alloc(skr.USER_STAKE_SIZE);
    Buffer.from([102, 53, 163, 107, 9, 138, 87, 153]).copy(b, 0);
    writeU128LE(b, 105, BigInt(shares));
    writeU128LE(b, 121, 0n);
    b.writeBigUInt64LE(BigInt(unstaking), 153);
    b.writeBigInt64LE(0n, 161);
    return b;
}
function splTokenEntry(owner, amountRaw, pubkey) {
    return {
        pubkey: pubkey,
        account: {
            lamports: 2039280,
            data: { program: 'spl-token', parsed: { type: 'account', info: {
                isNative: false, mint: skr.SKR_MINT, owner: owner, state: 'initialized',
                tokenAmount: { amount: String(amountRaw), decimals: 6,
                    uiAmount: 0, uiAmountString: String(amountRaw) },
            } }, space: 165 },
            owner: 'TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA',
            executable: false,
        },
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// A RECORDING, ROUTABLE FAKE `sql` — same shape test/clan-vigil.test.js uses
// ═══════════════════════════════════════════════════════════════════════════

function recordingSql(route = []) {
    const calls = [];
    const fn = (strings, ...values) => {
        const text = Array.isArray(strings) ? strings.join(' ? ') : String(strings);
        calls.push({ text: text, values: values });
        for (const r of route) {
            if (!r.match.test(text)) continue;
            const hit = calls.filter((c) => r.match.test(c.text)).length;
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
    fn.matching = (re) => calls.filter((c) => re.test(c.text));
    return fn;
}

const SGT_WRITE_RE = /INSERT\s+INTO\s+wallet_identity[\s\S]*sgt_mint/i;
const SGT_REUSE_RE = /SELECT\s+wallet\s+FROM\s+wallet_identity[\s\S]*sgt_mint\s*=/i;
const VAULT_ROW_RE = /FROM\s+clan_vaults\s+v/i;
const VAULT_INSERT_RE = /INSERT\s+INTO\s+clan_vaults/i;
const VAULT_STAMP_RE = /UPDATE\s+clan_vaults[\s\S]*first_seen_staked_at\s*=\s*NOW\(\)/i;
const ROSTER_RE = /FROM\s+clan_members\s+m\s+LEFT\s+JOIN\s+wallet_identity/i;

/** The db a happy SGT binding needs: the write lands, nobody else holds the mint. */
function sgtSql(extra = []) {
    return recordingSql([
        { match: SGT_WRITE_RE, rows: [{ sgt_mint: 'X', sgt_verified_at: 'T1' }] },
        { match: SGT_REUSE_RE, rows: [] },
        ...extra,
    ]);
}

function uniqueViolation() {
    const e = new Error('duplicate key value violates unique constraint "wallet_identity_sgt_mint_unique"');
    e.code = '23505';
    return e;
}

test.beforeEach(() => { gt._resetSgtCache(); skr._resetVigilCache(); });

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE ADDRESSES — the correction that changes the implementation
// ═══════════════════════════════════════════════════════════════════════════

test('the SGT constant is the MINT AUTHORITY, and it is not used as a mint filter', () => {
    // ⛔ WORK_ORDER_1854:6 calls GT2zuHVa… the "Genesis Token mint address". It is the MINT
    // AUTHORITY: getAccountInfo on it returned owner 11111111111111111111111111111111
    // (System Program), space 0 — a plain keypair account, not a mint. A `{mint:}` filter
    // pointed at it matches nothing forever, silently. The NAME is the guard against that
    // mistake coming back, so the name is what this test pins.
    assert.equal(gt.SGT_MINT_AUTHORITY, 'GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4');
    assert.equal(gt.SGT_GROUP_ADDRESS, 'GT22s89nU4iWFkNXj1Bw6uYhJJWDRPpShHt4Bk8f99Te');
    assert.ok(!Object.keys(gt).includes('SGT_MINT'),
        '⛔ no export may be called SGT_MINT — that one word is the whole bug');
    // Token-2022, not the classic program: an SGT is invisible to a Tokenkeg-filtered read.
    assert.equal(gt.TOKEN_2022_PROGRAM_ID, 'TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb');
});

test('findSgtMint filters by the TOKEN-2022 PROGRAM, never by a mint', async () => {
    const rpc = await startRpc({
        holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
        mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
    });
    try {
        const r = await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url });
        assert.equal(r.ok, true);
        const listing = rpc.calls.find((c) => c.method === 'getTokenAccountsByOwnerV2');
        assert.equal(listing.filter.programId, gt.TOKEN_2022_PROGRAM_ID);
        assert.equal(listing.filter.mint, undefined,
            '⛔ a mint filter here is the bug the work order shipped with');
    } finally { await rpc.close(); }
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE TWO V2 TRAPS, AND THE FALLBACK
// ═══════════════════════════════════════════════════════════════════════════

test('normalizeAccounts is the ONE seam for V2\'s object and V1\'s array', () => {
    // V2, as measured: { accounts, paginationKey, count }.
    assert.deepEqual(gt.normalizeAccounts({ accounts: ['a'], paginationKey: 'k', count: 1 }),
        { accounts: ['a'], paginationKey: 'k' });
    // V1: the array itself.
    assert.deepEqual(gt.normalizeAccounts(['a', 'b']), { accounts: ['a', 'b'], paginationKey: null });
    // ⛔ NEITHER SHAPE IS NULL, NOT []. An absent value is not an empty one: reporting
    // "unparseable" as "holds nothing" turns an outage into a fabricated non-Seeker.
    assert.equal(gt.normalizeAccounts(null), null);
    assert.equal(gt.normalizeAccounts({ nope: 1 }), null);
    assert.equal(gt.normalizeAccounts('a string'), null);
});

/** N junk Token-2022 holdings, distinct by mint. Only distinctness matters. */
function bulkHoldings(n) {
    const out = [];
    for (let i = 0; i < n; i++) {
        out.push({ mint: 'Bulk' + String(i).padStart(6, '0') + 'aaaaaaaaaaaaaaaaaaaaaaaa', amount: 1 });
    }
    return out;
}

test('a FULL page is followed, and the loop really does collect every page', async () => {
    // 1001 accounts at the reader's own 1000-per-page limit: page one comes back FULL, so
    // the loop must continue, and page two is short, so it must stop. Nothing is faked
    // about the page size — the fixture slices by the limit the reader actually sent.
    const rpc = await startRpc({ holdings: { [SEEKER_A]: bulkHoldings(gt.V2_PAGE_LIMIT + 1) } });
    try {
        const listing = await gt.readToken2022Accounts(rpc.url, SEEKER_A);
        assert.equal(listing.ok, true);
        assert.equal(listing.via, 'v2');
        assert.equal(listing.accounts.length, gt.V2_PAGE_LIMIT + 1, 'every page was collected');
        assert.equal(listing.pages, 2);
        assert.equal(listing.truncated, false);
    } finally { await rpc.close(); }
});

test('an ordinary wallet costs exactly ONE round trip, DESPITE a non-null paginationKey',
    async () => {
        // ⭐ THIS IS THE MEASURED TRAP, AND IT IS THE ONE THAT COSTS REAL MONEY. On
        // 2026-09-17, paging an owner with exactly two accounts at limit 1 returned a
        // NON-NULL paginationKey on page 1 AND on page 2 — only a third page came back
        // empty. So a `while (paginationKey)` loop spends a wasted round trip on EVERY
        // wallet, forever, on a billed key. The fixture reproduces that faithfully (it
        // always returns a key), and the assertion is that the reader stops anyway.
        const rpc = await startRpc({
            holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
            mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
        });
        try {
            const listing = await gt.readToken2022Accounts(rpc.url, SEEKER_A);
            assert.equal(listing.accounts.length, 1);
            assert.equal(listing.pages, 1);
            const served = rpc.calls.find((c) => c.method === 'getTokenAccountsByOwnerV2');
            assert.ok(served, 'V2 answered');
            assert.equal(rpc.count('getTokenAccountsByOwnerV2'), 1,
                '⛔ ONE round trip: a short page is the last page, whatever the key says');
        } finally { await rpc.close(); }
    });

test('a wallet too large to page is reported TRUNCATED, never as a complete listing',
    async () => {
        const rpc = await startRpc({
            holdings: { [SEEKER_A]: bulkHoldings(gt.V2_PAGE_LIMIT * gt.MAX_TOKEN_PAGES + 1) },
        });
        try {
            const listing = await gt.readToken2022Accounts(rpc.url, SEEKER_A);
            assert.equal(listing.ok, true);
            assert.equal(listing.pages, gt.MAX_TOKEN_PAGES, 'it stopped at the cap');
            assert.equal(listing.truncated, true, 'and it SAYS the list is partial');
        } finally { await rpc.close(); }
    });

test('a plan WITHOUT getTokenAccountsByOwnerV2 falls back to the standard method', async () => {
    // The work order's third VERIFY item. The provisioned Helius plan DOES support V2
    // (measured: HTTP 200 with real accounts), so this path is insurance rather than
    // today's behaviour — insurance that is tested, because an unsupported method silently
    // making every Seeker look like a non-Seeker is the failure mode.
    const rpc = await startRpc({
        noV2: true,
        holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
        mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
    });
    try {
        const r = await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url });
        assert.equal(r.ok, true, 'the fallback produced the SAME answer');
        assert.equal(r.mint, SGT_MINT_A);
        assert.equal(r.via, 'v1', 'and it says which method answered');
        assert.equal(rpc.count('getTokenAccountsByOwner'), 1);
    } finally { await rpc.close(); }
});

test('a DEAD endpoint does NOT fall back — one dead endpoint is dead for both methods',
    async () => {
        const r = await gt.readToken2022Accounts('http://127.0.0.1:1/dead', SEEKER_A);
        assert.equal(r.ok, false);
        assert.equal(r.reason, 'rpc_unavailable');
        // Retrying the other method would double the load on something already failing.
    });

test('rpcCall preserves the JSON-RPC code, which skr-staking\'s deliberately does not', async () => {
    // The ONE reason this file has its own transport: the whole fallback decision turns on
    // -32601 surviving to the caller, and skr-staking's rpcCall collapses every error into
    // rpc_unavailable. The contract difference is asserted so a future "de-duplication"
    // cannot quietly reunite them.
    const rpc = await startRpc({ noV2: true });
    try {
        const r = await gt.rpcCall(rpc.url, 'getTokenAccountsByOwnerV2', ['x', {}, {}]);
        assert.equal(r.ok, false);
        assert.equal(r.reason, 'method_not_found');
        assert.equal(r.rpcCode, -32601);
    } finally { await rpc.close(); }
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. classifyMint — the forgery defence
// ═══════════════════════════════════════════════════════════════════════════

test('all THREE legs are required; the mint authority alone is a forgery vector', () => {
    const good = sgtMintAccount(SGT_MINT_A).parsed.info;
    assert.equal(gt.classifyMint(good).sgt, true);
    assert.equal(gt.classifyMint(good).memberNumber, 96167);

    // ⛔ InitializeMint takes the mint authority as a PLAIN PUBKEY ARGUMENT — it is not a
    // signer. So anyone can create a Token-2022 mint that NAMES GT2zuHVa… as its authority.
    // The token-group membership is the leg only Solana Mobile could have written (its
    // initialization requires the group's update authority to sign), so dropping it must
    // refuse even though the authority reads perfectly right.
    const forged = sgtMintAccount(SGT_MINT_A, { dropGroupMember: true }).parsed.info;
    assert.equal(forged.mintAuthority, gt.SGT_MINT_AUTHORITY, 'the forgery names the real authority');
    assert.equal(gt.classifyMint(forged).sgt, false);
    assert.equal(gt.classifyMint(forged).reason, 'group_member_mismatch');

    const wrongGroup = sgtMintAccount(SGT_MINT_A, { group: PLAIN_C }).parsed.info;
    assert.equal(gt.classifyMint(wrongGroup).reason, 'group_member_mismatch');

    const wrongMeta = sgtMintAccount(SGT_MINT_A, { metadataAddress: PLAIN_C }).parsed.info;
    assert.equal(gt.classifyMint(wrongMeta).reason, 'metadata_pointer_mismatch');

    const noPointer = sgtMintAccount(SGT_MINT_A, { dropMetadataPointer: true }).parsed.info;
    assert.equal(gt.classifyMint(noPointer).reason, 'metadata_pointer_mismatch');

    const wrongAuthority = sgtMintAccount(SGT_MINT_A, { mintAuthority: PLAIN_C }).parsed.info;
    assert.equal(gt.classifyMint(wrongAuthority).reason, 'wrong_mint_authority');

    assert.equal(gt.classifyMint(null).sgt, false);
    assert.equal(gt.classifyMint(undefined).reason, 'mint_unparseable');
});

test('a ZERO-BALANCE account for a real SGT mint does not prove the device', async () => {
    // The residue of a transfer AWAY from this wallet: the ATA survives, the token does not.
    // Counting it would keep proving a Seeker the wallet gave up. The official recipe says
    // "skip empty token accounts"; this is that, tested.
    const rpc = await startRpc({
        holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 0 }] },
        mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
    });
    try {
        const r = await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url });
        assert.equal(r.ok, false);
        assert.equal(r.reason, 'no_sgt');
        assert.equal(rpc.count('getAccountInfo', SGT_MINT_A), 0,
            'and it did not even spend a round trip probing the mint');
    } finally { await rpc.close(); }
});

test('a Token-2022 junk token is not an SGT, and the wallet still reads no_sgt', async () => {
    const rpc = await startRpc({
        holdings: { [PLAIN_C]: [{ mint: JUNK_MINT, amount: 5 }] },
        mints: { [JUNK_MINT]: junkMintAccount(JUNK_MINT) },
    });
    try {
        const r = await gt.findSgtMint(PLAIN_C, { rpcUrl: rpc.url });
        assert.equal(r.ok, false);
        assert.equal(r.reason, 'no_sgt', 'we LOOKED and there is none — a real answer');
    } finally { await rpc.close(); }
});

test('no_sgt and rpc_unavailable NEVER collapse into each other', async () => {
    // Reporting an outage as "your phone is not a Seeker" is the fabricated-zero mistake
    // skr-staking exists to prevent, wearing a different hat.
    const dead = await gt.findSgtMint(SEEKER_A, { rpcUrl: 'http://127.0.0.1:1/dead' });
    assert.equal(dead.ok, false);
    assert.equal(dead.reason, 'rpc_unavailable');
    assert.notEqual(dead.reason, 'no_sgt');

    gt._resetSgtCache();
    // ⛔ THE ENV IS CLEARED RATHER THAN ASSUMED EMPTY. With no rpcUrl the module falls back
    //    to HELIUS_RPC_URL / SOLANA_MAINNET_RPC_URL; a runner with .env.local sourced would
    //    otherwise send this test at the REAL, BILLED Helius endpoint.
    const prevH = process.env.HELIUS_RPC_URL;
    const prevM = process.env.SOLANA_MAINNET_RPC_URL;
    delete process.env.HELIUS_RPC_URL;
    delete process.env.SOLANA_MAINNET_RPC_URL;
    try {
        assert.equal(gt.sgtRpcUrl(), null, 'fixture precondition: no endpoint is reachable');
        const unset = await gt.findSgtMint(SEEKER_A, { rpcUrl: null, nowSeconds: 1 });
        assert.equal(unset.ok, false);
        assert.equal(unset.reason, 'rpc_url_unset');
        assert.notEqual(unset.reason, 'no_sgt');
    } finally {
        if (prevH === undefined) delete process.env.HELIUS_RPC_URL;
        else process.env.HELIUS_RPC_URL = prevH;
        if (prevM === undefined) delete process.env.SOLANA_MAINNET_RPC_URL;
        else process.env.SOLANA_MAINNET_RPC_URL = prevM;
    }
});

test('two SGTs in one wallet pick DETERMINISTICALLY, so a retry cannot rebind', async () => {
    // Solana Mobile notes the token can move between one owner's OWN accounts on a Seed
    // Vault account change, so a wallet transiently holding two is not absurd. An arbitrary
    // pick would let a retry bind a different mint — and a different mint is, to the
    // uniqueness index, a different device.
    const rpc = await startRpc({
        holdings: { [SEEKER_A]: [
            { mint: SGT_MINT_A, amount: 1 }, { mint: SGT_MINT_B, amount: 1 },
        ] },
        mints: {
            [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A),
            [SGT_MINT_B]: sgtMintAccount(SGT_MINT_B, { memberNumber: 4242 }),
        },
    });
    try {
        const first = await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url, noCache: true });
        const second = await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url, noCache: true });
        assert.equal(first.ok, true);
        assert.equal(first.mint, second.mint, 'the same wallet binds the same mint twice');
        assert.deepEqual(first.allMints, [SGT_MINT_A, SGT_MINT_B].sort(),
            'and both candidates are reported, so the choice is visible');
        assert.equal(first.mint, [SGT_MINT_A, SGT_MINT_B].sort()[0]);
    } finally { await rpc.close(); }
});

test('the mint-probe fan-out is CAPPED, and a capped read is not no_sgt', async () => {
    // Anyone can mint themselves thousands of worthless Token-2022 tokens and send one of
    // each to a wallet they nominate as a signer. Uncapped, one register call fans out one
    // getAccountInfo per mint on a shared, billed key.
    const many = [];
    const mints = {};
    for (let i = 0; i < gt.MAX_MINT_PROBES + 5; i++) {
        // Well-formed distinct base58-ish mints; only their distinctness matters here.
        const m = 'Mnt' + String(i).padStart(2, '0') + 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
        many.push({ mint: m, amount: 1 });
        mints[m] = junkMintAccount(m);
    }
    const rpc = await startRpc({ holdings: { [PLAIN_C]: many }, mints: mints });
    try {
        const r = await gt.findSgtMint(PLAIN_C, { rpcUrl: rpc.url });
        assert.equal(r.ok, false);
        assert.equal(r.reason, 'too_many_mints',
            '⛔ NOT no_sgt — we did not finish looking, and saying otherwise would be a lie');
        assert.equal(rpc.count('getAccountInfo'), 0, 'and it probed nothing at all');
    } finally { await rpc.close(); }
});

test('the 60-second cache is the Vigil\'s own freshness rule, and failures cache too', async () => {
    const rpc = await startRpc({
        holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
        mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
    });
    try {
        assert.equal(gt.SGT_CACHE_TTL_SECONDS, skr.VIGIL_CACHE_TTL_SECONDS,
            'one window, reusing skr-staking\'s isFreshWithin rather than a second rule');
        await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url, nowSeconds: 1000000 });
        const after = rpc.calls.length;
        const hit = await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url, nowSeconds: 1000059 });
        assert.equal(hit.fromCache, true);
        assert.equal(rpc.calls.length, after, 'the second read spent no round trips');
        await gt.findSgtMint(SEEKER_A, { rpcUrl: rpc.url, nowSeconds: 1000060 });
        assert.ok(rpc.calls.length > after, 'and 60 seconds later it is stale');
    } finally { await rpc.close(); }
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. verifyGenesisToken — the binding and its uniqueness
// ═══════════════════════════════════════════════════════════════════════════

test('TEST PLAN 1: a wallet holding a valid SGT verifies ok', async () => {
    const rpc = await startRpc({
        holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
        mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
    });
    const sql = sgtSql();
    try {
        const r = await auth.verifyGenesisToken(sql, SEEKER_A, { rpcUrl: rpc.url });
        assert.equal(r.ok, true);
        assert.equal(r.mint, SGT_MINT_A);
        assert.equal(r.memberNumber, 96167);

        const write = sql.matching(SGT_WRITE_RE)[0];
        assert.ok(write, 'the binding was persisted');
        // ⛔ INSERT … ON CONFLICT, not UPDATE. A vault signer may have NO wallet_identity
        // row (only the wallet rail writes that table), and an UPDATE would move zero rows
        // — leaving the reuse check nothing to compare against and letting a SECOND clan
        // bind the same device.
        assert.match(write.text, /INSERT\s+INTO\s+wallet_identity/i);
        assert.match(write.text, /ON\s+CONFLICT\s*\(\s*wallet\s*\)\s*DO\s+UPDATE/i);
        assert.match(write.text, /sgt_verified_at\s*=\s*NOW\(\)/i,
            'stamped by POSTGRES, never by a serverless clock');

        // ⛔ THE WRITE COMES FIRST. THIS IS THE ONE PROPERTY MIGRATION 0036 EXISTS FOR, and
        //    without pinning it a refactor to read-then-write passes every other test in this
        //    file. Attempt the insert, let the unique index refuse it, and only THEN read to
        //    label the refusal — consumeNonce's exact order. Read first and written second,
        //    two wallets verifying the SAME mint in the same instant both see "nobody has it"
        //    and both succeed, which is the whole Sybil hole.
        const writeAt = sql.calls.findIndex((c) => SGT_WRITE_RE.test(c.text));
        const readAt = sql.calls.findIndex((c) => SGT_REUSE_RE.test(c.text));
        assert.ok(writeAt >= 0, 'the write happened');
        assert.ok(readAt >= 0, 'the classifying read happened (the unmigrated-deploy fallback)');
        assert.ok(writeAt < readAt,
            '⛔ the SELECT ran BEFORE the write — that is read-then-write, and it is raceable');
    } finally { await rpc.close(); }
});

test('TEST PLAN 3: a wallet with no SGT is no_sgt, and nothing is written', async () => {
    const rpc = await startRpc({ holdings: { [PLAIN_C]: [] } });
    const sql = sgtSql();
    try {
        const r = await auth.verifyGenesisToken(sql, PLAIN_C, { rpcUrl: rpc.url });
        assert.equal(r.ok, false);
        assert.equal(r.reason, 'no_sgt');
        assert.equal(sql.matching(SGT_WRITE_RE).length, 0, 'no binding for a wallet with none');
    } finally { await rpc.close(); }
});

test('TEST PLAN 2: the SAME MINT on a second wallet is sgt_reused — decided by the DATABASE',
    async () => {
        // The transfer case: wallet A recorded the mint, the token moves to wallet B, B
        // verifies. The partial unique index (migration 0036) makes the write a 23505, and
        // THAT is the refusal — a read-then-write would let two wallets verifying the same
        // mint at the same instant both succeed, which is the Sybil shape the index closes.
        const rpc = await startRpc({
            holdings: { [SEEKER_B]: [{ mint: SGT_MINT_A, amount: 1 }] },
            mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
        });
        const sql = recordingSql([
            { match: SGT_WRITE_RE, throws: uniqueViolation() },
            { match: SGT_REUSE_RE, rows: [{ wallet: SEEKER_A }] },
        ]);
        try {
            const r = await auth.verifyGenesisToken(sql, SEEKER_B, { rpcUrl: rpc.url });
            assert.equal(r.ok, false);
            assert.equal(r.reason, 'sgt_reused');
            // The SELECT ran only to LABEL the refusal — consumeNonce's exact division of
            // labour — and it names who actually holds the binding.
            assert.equal(r.detail.otherWallet, SEEKER_A);
            assert.equal(r.detail.mint, SGT_MINT_A);
        } finally { await rpc.close(); }
    });

test('sgt_reused is ALSO caught on a deployment where migration 0036 is not applied',
    async () => {
        // Without the index the write cannot fail, so the reuse has to be found after the
        // fact. A missing migration must degrade the RACE-PROOFING, never the RULE — deploy
        // order has burned this repo before (see renewSession's note on signed_at).
        const rpc = await startRpc({
            holdings: { [SEEKER_B]: [{ mint: SGT_MINT_A, amount: 1 }] },
            mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
        });
        const sql = recordingSql([
            { match: SGT_WRITE_RE, rows: [{ sgt_mint: SGT_MINT_A, sgt_verified_at: 'T1' }] },
            { match: SGT_REUSE_RE, rows: [{ wallet: SEEKER_A }] },
        ]);
        try {
            const r = await auth.verifyGenesisToken(sql, SEEKER_B, { rpcUrl: rpc.url });
            assert.equal(r.ok, false);
            assert.equal(r.reason, 'sgt_reused');
        } finally { await rpc.close(); }
    });

test('verifyGenesisToken NEVER THROWS, and is not an authentication step', async () => {
    // A garbage address is a refusal, not an exception.
    const bad = await auth.verifyGenesisToken(recordingSql(), 'not-a-wallet');
    assert.equal(bad.ok, false);
    assert.equal(bad.reason, 'bad_wallet');

    // A database that rejects everything is `write_failed`, never a throw out of the door.
    const rpc = await startRpc({
        holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
        mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
    });
    try {
        const sql = recordingSql([{ match: SGT_WRITE_RE, throws: new Error('db on fire') }]);
        const r = await auth.verifyGenesisToken(sql, SEEKER_A, { rpcUrl: rpc.url });
        assert.equal(r.ok, false);
        assert.equal(r.reason, 'write_failed');
    } finally { await rpc.close(); }

    // Its signature takes (sql, wallet, opts) and carries no parameter through which a
    // caller could supply a mint or an answer — the structural rule skr-staking states.
    assert.equal(auth.verifyGenesisToken.length, 3);
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. THE SQUADS READ — against real mainnet bytes
// ═══════════════════════════════════════════════════════════════════════════

test('a REAL mainnet Multisig account decodes to its real threshold and members', async () => {
    const rpc = await startRpc({ multisigs: { [REAL_MULTISIG_ADDRESS]: REAL_MULTISIG_B64 } });
    try {
        const ms = await vaults.readMultisig(rpc.url, REAL_MULTISIG_ADDRESS);
        assert.equal(ms.ok, true);
        assert.equal(ms.size, 231, 'the byte length mainnet served');
        assert.equal(ms.threshold, 2);
        assert.deepEqual(ms.members, [
            '3FN1Mofvnkg3dmFtrbNFr3uV4EyTPxEw1ekqR9YF8At1',
            '7YfU8TRgLbuaYnajv7cJkbas6zqixkMX2YC4YLD8TbPw',
            '7jcAhEkH977M18wkrNwRZHDyaBymahqs1fQ2AzF22dk7',
        ]);
        // Squads permissions are a bitmask (1 Initiate / 2 Vote / 4 Execute). This real
        // multisig's masks are 7, 2, 7 — so a member who can vote but not execute is a
        // REAL configuration, which is why "all members are signers" is recorded as a
        // first-pass default rather than an obvious truth.
        assert.deepEqual(ms.memberMasks, [7, 2, 7]);
        assert.equal(ms.configAuthority, '11111111111111111111111111111111',
            'UNCONTROLLED: no single key can swap this vault\'s members');
    } finally { await rpc.close(); }
});

test('the vault PDA derivation matches the address mainnet reported', () => {
    // Measured on 2026-09-17 via the SDK's own getVaultPda; pinned here so a future change
    // to the derivation path cannot silently point a clan at a different treasury.
    assert.equal(vaults.deriveVaultAddress(REAL_MULTISIG_ADDRESS, 0), REAL_VAULT_0);
    assert.notEqual(vaults.deriveVaultAddress(REAL_MULTISIG_ADDRESS, 1), REAL_VAULT_0,
        'a different index is a different treasury');
    assert.equal(vaults.deriveVaultAddress(REAL_MULTISIG_ADDRESS, 999), null,
        'outside Squads\' own 0..255 range there is no vault');
});

test('an address that is not a Squads multisig is refused with its own code, never guessed',
    async () => {
        // ⛔ A VAULT ADDRESS CANNOT BE REVERSED TO ITS MULTISIG — that is what a one-way PDA
        // means — and a vault PDA carries no data, so it can never answer "who are the
        // signers". Passing one has to be a named refusal, not a fallback.
        const rpc = await startRpc({
            multisigs: {
                [REAL_VAULT_0]: 'missing',          // the vault PDA itself: no account
                [PLAIN_C]: 'wrongOwner',            // a real account, wrong program
                [SEEKER_A]: 'bm90IGEgbXVsdGlzaWc=', // Squads-owned but undecodable
            },
        });
        try {
            const a = await vaults.readMultisig(rpc.url, REAL_VAULT_0);
            assert.equal(a.code, vaults.VaultCode.NOT_A_MULTISIG);
            assert.equal(a.reason, 'account_not_found');

            const b = await vaults.readMultisig(rpc.url, PLAIN_C);
            assert.equal(b.code, vaults.VaultCode.NOT_A_MULTISIG);
            assert.equal(b.reason, 'wrong_program_owner');

            const c = await vaults.readMultisig(rpc.url, SEEKER_A);
            assert.equal(c.code, vaults.VaultCode.NOT_A_MULTISIG);
            assert.equal(c.reason, 'discriminator_or_decode');
        } finally { await rpc.close(); }
    });

test('a DIFFERENT Squads account type at a valid address is refused — the DISCRIMINATOR, not luck',
    async () => {
        // ⭐ THIS CASE FOUND A REAL DEFECT AND IS KEPT AS ITS REGRESSION.
        //    A Proposal account is owned by the SAME program, so readMultisig's owner check
        //    passes, and it is well-formed, so "undecodable junk" says nothing about it. The
        //    original code trusted `Multisig.deserialize` to reject a wrong discriminator —
        //    and it DOES NOT. Run against this exact fixture it returned ok:true with
        //    threshold 0 and an empty member vector: beet's generated reader treats the 8
        //    bytes as just another field and never compares them.
        //    Squads owns several account types (Proposal, VaultTransaction, SpendingLimit,
        //    ProgramConfig), so without an explicit check any of them at the supplied address
        //    would decode as a "multisig" whose threshold and signer list are whatever those
        //    bytes happen to mean. clan-vaults.readMultisig now compares the discriminator
        //    itself, the way skr-staking.requireDiscriminator does.
        const squads = require('@sqds/multisig');
        const proposal = Buffer.alloc(200);
        Buffer.from(squads.accounts.proposalDiscriminator).copy(proposal, 0);
        assert.notDeepEqual(
            Array.from(proposal.subarray(0, 8)),
            Array.from(squads.accounts.multisigDiscriminator),
            'fixture assumption: the two discriminators differ');

        const rpc = await startRpc({
            multisigs: { [REAL_MULTISIG_ADDRESS]: proposal.toString('base64') },
        });
        try {
            const r = await vaults.readMultisig(rpc.url, REAL_MULTISIG_ADDRESS);
            assert.equal(r.ok, false);
            assert.equal(r.code, vaults.VaultCode.NOT_A_MULTISIG);
            assert.equal(r.reason, 'discriminator_or_decode',
                'refused by the discriminator, not by a downstream missing-field check');
        } finally { await rpc.close(); }
    });

test('an UNREADABLE chain is CLAN_VAULT_UNREADABLE, distinct from NOT_A_MULTISIG', async () => {
    const r = await vaults.readMultisig('http://127.0.0.1:1/dead', REAL_MULTISIG_ADDRESS);
    assert.equal(r.ok, false);
    assert.equal(r.code, vaults.VaultCode.UNREADABLE);
    assert.notEqual(r.code, vaults.VaultCode.NOT_A_MULTISIG,
        '"check your address" and "try again later" are different instructions');
});

test('normalizeVaultAddress and normalizeVaultIndex refuse before spending a round trip', () => {
    assert.equal(vaults.normalizeVaultAddress('nope').code, vaults.VaultCode.BAD_ADDRESS);
    assert.equal(vaults.normalizeVaultAddress('').code, vaults.VaultCode.BAD_ADDRESS);
    assert.equal(vaults.normalizeVaultAddress(null).code, vaults.VaultCode.BAD_ADDRESS);
    assert.equal(vaults.normalizeVaultAddress(' ' + REAL_MULTISIG_ADDRESS + ' ').value,
        REAL_MULTISIG_ADDRESS, 'trimmed, as every other clan normaliser trims');

    assert.equal(vaults.normalizeVaultIndex(undefined).value, 0, 'absent means vault 0');
    assert.equal(vaults.normalizeVaultIndex(3).value, 3);
    assert.equal(vaults.normalizeVaultIndex(255).value, 255);
    assert.equal(vaults.normalizeVaultIndex(256).code, vaults.VaultCode.BAD_VAULT_INDEX);
    assert.equal(vaults.normalizeVaultIndex(-1).code, vaults.VaultCode.BAD_VAULT_INDEX);
    assert.equal(vaults.normalizeVaultIndex(1.5).code, vaults.VaultCode.BAD_VAULT_INDEX);
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. REGISTRATION — the signer sweep
// ═══════════════════════════════════════════════════════════════════════════

/** A real Squads address this file uses as the registration subject (2-of-2 on mainnet). */
const TWO_OF_TWO = 'JEJdqYLAAT8kirhwiWYNKXDPAp2U7xT5mtVqqegpb4Dm';

/**
 * A Multisig account whose MEMBERS ARE THE WALLETS THIS FILE CONTROLS — built with the
 * SDK'S OWN SERIALIZER, not with a hand-written byte layout and not by stubbing
 * readMultisig out.
 *
 * ⭐ WHY THIS SHAPE AND NOT A STUB. A stub on `vaults.readMultisig` would not even be
 * CALLED: registerClanVault holds a direct lexical reference to the function, not a
 * property lookup on module.exports, so reassigning the export changes nothing — a
 * monkey-patch that silently does not apply is worse than no test. And a hand-built buffer
 * would only prove this file can reproduce its own guess. `Multisig.fromArgs(...).
 * serialize()` uses the same beet layout `deserialize` reads, so a round trip through the
 * REAL RPC path exercises the discriminator, the owner check and the member vector exactly
 * as mainnet does. The mainnet 231-byte fixture above proves the layout is the chain's;
 * this proves the reader handles arbitrary member sets.
 */
function multisigB64(members, threshold, over) {
    const squads = require('@sqds/multisig');
    const { PublicKey } = require('@solana/web3.js');
    const o = over || {};
    const ms = squads.accounts.Multisig.fromArgs({
        createKey: new PublicKey('11111111111111111111111111111112'),
        configAuthority: new PublicKey(o.configAuthority || '11111111111111111111111111111111'),
        threshold: threshold,
        timeLock: o.timeLock || 0,
        transactionIndex: 0,
        staleTransactionIndex: 0,
        rentCollector: null,
        bump: 255,
        members: members.map((k) => ({
            key: new PublicKey(k), permissions: { mask: o.mask != null ? o.mask : 7 },
        })),
    });
    return ms.serialize()[0].toString('base64');
}

test('TEST PLAN 4 / ACCEPTANCE 4: a signer with no SGT is a 403 that NAMES that signer',
    async () => {
        const rpc = await startRpc({
            multisigs: { [TWO_OF_TWO]: multisigB64([SEEKER_A, PLAIN_C], 2) },
            holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }], [PLAIN_C]: [] },
            mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
        });
        const sql = sgtSql([{ match: VAULT_INSERT_RE, rows: [{ verified_at: 'T1' }] }]);
        try {
            const r = await vaults.registerClanVault(
                sql, CLAN_ID, TWO_OF_TWO, 0, { rpcUrl: rpc.url });
            assert.equal(r.ok, false);
            assert.equal(r.status, 403);
            assert.equal(r.code, vaults.VaultCode.SIGNER_NO_SGT);
            assert.equal(r.wallet, PLAIN_C, 'the work order requires the wallet be named');
            // ⛔ AND NO ROW WAS WRITTEN. The sweep runs before the insert.
            assert.equal(sql.matching(VAULT_INSERT_RE).length, 0);
        } finally { await rpc.close(); }
    });

test('a signer whose device is already bound elsewhere gets its OWN code, not "no token"',
    async () => {
        // Telling a Leader "no token" when the truth is "that phone is already registered"
        // sends them hunting a device sitting in their hand. Two problems, two fixes.
        const rpc = await startRpc({
            multisigs: { [TWO_OF_TWO]: multisigB64([SEEKER_A, SEEKER_B], 2) },
            holdings: {
                [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }],
                [SEEKER_B]: [{ mint: SGT_MINT_A, amount: 1 }],   // the same device's mint
            },
            mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
        });
        const sql = recordingSql([
            { match: SGT_WRITE_RE, answer: (hit) => (hit === 1
                ? { rows: [{ sgt_mint: SGT_MINT_A, sgt_verified_at: 'T1' }] }
                : { throws: uniqueViolation() }) },
            { match: SGT_REUSE_RE, answer: (hit) => (hit === 1
                ? { rows: [] } : { rows: [{ wallet: SEEKER_A }] }) },
        ]);
        try {
            const r = await vaults.registerClanVault(
                sql, CLAN_ID, TWO_OF_TWO, 0, { rpcUrl: rpc.url });
            assert.equal(r.ok, false);
            assert.equal(r.code, vaults.VaultCode.SIGNER_SGT_REUSED);
            assert.equal(r.status, 403);
            assert.equal(r.wallet, SEEKER_B);
        } finally { await rpc.close(); }
    });

test('a signer we could NOT LOOK AT is 503, never a 403', async () => {
    // ⛔ Refusing a real Seeker owner because our provider blinked is the fabricated-zero
    // mistake wearing a 403. "We could not check" is not "you do not qualify".
    const rpc = await startRpc({
        multisigs: { [TWO_OF_TWO]: multisigB64([SEEKER_A, SEEKER_B], 2) },
        holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
        mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
        fail: 'tokens',
    });
    const sql = sgtSql();
    try {
        const r = await vaults.registerClanVault(
            sql, CLAN_ID, TWO_OF_TWO, 0, { rpcUrl: rpc.url });
        assert.equal(r.ok, false);
        assert.equal(r.code, vaults.VaultCode.SIGNER_UNVERIFIABLE);
        assert.equal(r.status, 503);
        assert.notEqual(r.status, 403);
        assert.equal(sql.matching(VAULT_INSERT_RE).length, 0);
    } finally { await rpc.close(); }
});

test('TEST PLAN 3 / ACCEPTANCE 5: a valid vault registers and WRITES the clan_vaults row',
    async () => {
        const rpc = await startRpc({
            multisigs: { [TWO_OF_TWO]: multisigB64([SEEKER_A, SEEKER_B], 2) },
            holdings: {
                [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }],
                [SEEKER_B]: [{ mint: SGT_MINT_B, amount: 1 }],
            },
            mints: {
                [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A),
                [SGT_MINT_B]: sgtMintAccount(SGT_MINT_B, { memberNumber: 4242 }),
            },
        });
        const sql = sgtSql([{ match: VAULT_INSERT_RE, rows: [{
            clan_id: CLAN_ID, vault_address: 'V', multisig_address: TWO_OF_TWO,
            vault_index: 0, signer_wallets: [SEEKER_A, SEEKER_B], threshold: 2,
            verified_at: 'T1',
        }] }]);
        try {
            const r = await vaults.registerClanVault(
                sql, CLAN_ID, TWO_OF_TWO, 0, { rpcUrl: rpc.url });
            assert.equal(r.ok, true);
            assert.equal(r.vault.threshold, 2);
            assert.equal(r.vault.multisigAddress, TWO_OF_TWO);
            assert.equal(r.vault.vaultIndex, 0);
            assert.deepEqual(r.vault.signerWallets, [SEEKER_A, SEEKER_B]);
            assert.equal(r.signers.length, 2);
            assert.deepEqual(r.signers.map((s) => s.mint).sort(), [SGT_MINT_A, SGT_MINT_B].sort());
            // The config authority is SURFACED, because a CONTROLLED multisig can have its
            // members swapped by one key — a real caveat on the word "collective".
            assert.equal(r.vault.configAuthority, '11111111111111111111111111111111');

            // ⛔ THE VAULT ADDRESS STORED IS THE DERIVED PDA, NOT THE ADDRESS SUPPLIED.
            // The supplied address is the MULTISIG; the vault is where SKR would sit.
            assert.equal(r.vault.vaultAddress, vaults.deriveVaultAddress(TWO_OF_TWO, 0));
            assert.notEqual(r.vault.vaultAddress, TWO_OF_TWO);

            const insert = sql.matching(VAULT_INSERT_RE)[0];
            assert.ok(insert);
            assert.match(insert.text, /signer_wallets/);
            assert.match(insert.text, /multisig_address/);
            // signer_wallets is an array of PLAIN STRINGS, which is what makes the
            // hardware_backed count a jsonb_array_elements_text join rather than a scan.
            const jsonArg = insert.values.find(
                (v) => typeof v === 'string' && v.startsWith('['));
            assert.deepEqual(JSON.parse(jsonArg), [SEEKER_A, SEEKER_B]);
            // ⛔ AND EVERY ELEMENT IS A PLAIN STRING, WHICH IS A LOAD-BEARING CONTRACT THE
            //    MIGRATION'S CHECK CANNOT EXPRESS (it only requires `jsonb_typeof = 'array'`).
            //    readClanVaultRow derives hardware_backed with
            //    `jsonb_array_elements_text(signer_wallets) JOIN wallet_identity ON wi.wallet =
            //    s.w`. Store objects like {wallet, mask} and that join matches NOTHING — every
            //    clan silently reads hardware_backed:false, forever, with no error anywhere.
            //    Exactly the class of silent failure FLAG 1 was about, so it is pinned here.
            assert.ok(JSON.parse(jsonArg).every((s) => typeof s === 'string'),
                'signer_wallets must be an array of plain address STRINGS, never of objects');
        } finally { await rpc.close(); }
    });

test('a second registration is a 409, from the DATABASE\'s uniqueness and not a pre-read',
    async () => {
        const rpc = await startRpc({
            multisigs: { [TWO_OF_TWO]: multisigB64([SEEKER_A], 1) },
            holdings: { [SEEKER_A]: [{ mint: SGT_MINT_A, amount: 1 }] },
            mints: { [SGT_MINT_A]: sgtMintAccount(SGT_MINT_A) },
        });
        const dup = new Error('duplicate key value violates unique constraint "clan_vaults_pkey"');
        dup.code = '23505';
        const sql = sgtSql([{ match: VAULT_INSERT_RE, throws: dup }]);
        try {
            const r = await vaults.registerClanVault(
                sql, CLAN_ID, TWO_OF_TWO, 0, { rpcUrl: rpc.url });
            assert.equal(r.ok, false);
            assert.equal(r.status, 409);
            assert.equal(r.code, vaults.VaultCode.ALREADY_REGISTERED);
        } finally { await rpc.close(); }
    });

/** N valid, distinct pubkeys. Built from real 32-byte buffers, so PublicKey accepts them. */
function bulkSigners(n) {
    const { PublicKey } = require('@solana/web3.js');
    const out = [];
    for (let i = 0; i < n; i++) {
        const b = Buffer.alloc(32, 0);
        b.writeUInt16BE(i + 1, 0);
        b[31] = 1;   // keep it off the ed25519 curve's trivial values
        out.push(new PublicKey(b).toBase58());
    }
    return out;
}

test('the signer fan-out is CAPPED, and an empty member vector is not a multisig', async () => {
    const many = bulkSigners(vaults.MAX_VAULT_SIGNERS + 1);
    const rpc = await startRpc({
        multisigs: {
            [TWO_OF_TWO]: multisigB64(many, 2),
            [REAL_MULTISIG_ADDRESS]: multisigB64([], 1),
        },
    });
    const sql = sgtSql();
    try {
        const over = await vaults.registerClanVault(sql, CLAN_ID, TWO_OF_TWO, 0, { rpcUrl: rpc.url });
        assert.equal(over.code, vaults.VaultCode.TOO_MANY_SIGNERS);
        assert.equal(over.detail.cap, vaults.MAX_VAULT_SIGNERS);
        assert.equal(sql.matching(SGT_WRITE_RE).length, 0, 'refused before ANY RPC fan-out');
        assert.equal(rpc.count('getTokenAccountsByOwnerV2'), 0);

        const none = await vaults.registerClanVault(
            sql, CLAN_ID, REAL_MULTISIG_ADDRESS, 0, { rpcUrl: rpc.url });
        assert.equal(none.code, vaults.VaultCode.NOT_A_MULTISIG);
        assert.equal(none.detail.reason, 'no_members');
    } finally { await rpc.close(); }
});

// ═══════════════════════════════════════════════════════════════════════════
// 7. THE COLLECTIVE VIGIL
// ═══════════════════════════════════════════════════════════════════════════

function vaultRow(over = {}) {
    return Object.assign({
        clan_id: CLAN_ID,
        vault_address: REAL_VAULT_0,
        multisig_address: REAL_MULTISIG_ADDRESS,
        vault_index: 0,
        signer_wallets: [SEEKER_A, SEEKER_B],
        threshold: 2,
        verified_at: 'T1',
        first_seen_staked_at: 'T0',
        tenure_seconds: 3600,
        signer_count: 2,
        verified_signer_count: 2,
    }, over);
}

test('hardware_backed is TRUE only when EVERY signer is verified — and never vacuously', () => {
    assert.equal(vaults.shapeVaultRow(vaultRow()).hardwareBacked, true);
    assert.equal(vaults.shapeVaultRow(vaultRow({ verified_signer_count: 1 })).hardwareBacked, false);
    assert.equal(vaults.shapeVaultRow(vaultRow({ verified_signer_count: 0 })).hardwareBacked, false);
    // ⛔ THE VACUOUS-TRUTH GUARD: 0 === 0 is true, and without the signerCount > 0 test a
    // signer-less row would read hardware-backed.
    assert.equal(vaults.shapeVaultRow(
        vaultRow({ signer_wallets: [], signer_count: 0, verified_signer_count: 0 })
    ).hardwareBacked, false);
    // JSONB may arrive parsed or as text; both must shape identically.
    assert.deepEqual(vaults.shapeVaultRow(
        vaultRow({ signer_wallets: JSON.stringify([SEEKER_A, SEEKER_B]) })).signerWallets,
        [SEEKER_A, SEEKER_B]);
});

test('the vault row query lets POSTGRES compute the tenure and COUNT the verified signers',
    async () => {
        const sql = recordingSql([{ match: VAULT_ROW_RE, rows: [vaultRow()] }]);
        const row = await vaults.readClanVaultRow(sql, CLAN_ID);
        assert.equal(row.tenureSeconds, 3600);
        assert.equal(row.hardwareBacked, true);

        const q = sql.matching(VAULT_ROW_RE)[0].text;
        assert.match(q, /EXTRACT\(EPOCH\s+FROM\s*\(\s*NOW\(\)\s*-\s*v\.first_seen_staked_at/i,
            'tenure is measured by Postgres, never by a serverless Date.now()');
        assert.match(q, /jsonb_array_elements_text\(v\.signer_wallets\)/i);
        assert.match(q, /wi\.sgt_verified_at\s+IS\s+NOT\s+NULL/i,
            'hardware_backed is DERIVED live, never stored');
        assert.match(q, /WHERE\s+v\.clan_id\s*=\s*\?/i, 'scoped to ONE clan, as a parameter');
    });

test('the collective weight is the VAULT\'s percent x the VAULT\'s tenure', async () => {
    // 100 staked + 100 liquid = 200 total => 0.5, x 3600 s of tenure = 1800.
    const rpc = await startRpc({
        stakes: { [REAL_VAULT_0]: { shares: 100n * SKR_UNIT } },
        balances: { [REAL_VAULT_0]: [100n * SKR_UNIT] },
    });
    const sql = recordingSql([{ match: VAULT_ROW_RE, rows: [vaultRow()] }]);
    try {
        const r = await vaults.readCollectiveVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
        assert.equal(r.vault.percentStaked, 0.5);
        assert.equal(r.vault.tenureSeconds, 3600);
        assert.equal(r.vault.collectiveVigilWeight, 1800);
        assert.equal(r.degraded, false);
        assert.equal(r.vault.hardwareBacked, true);
    } finally { await rpc.close(); }
});

test('a vault that has never staked reads a REAL zero, not a degraded one', async () => {
    // ⚠ AND THIS IS THE STATE EVERY EXISTING MAINNET VAULT IS IN TODAY. Whether the SKR
    // staking program accepts a PDA as its `user` (a Squads invoke_signed CPI) is NOT
    // proven by this ticket. The read path is correct either way: the day a vault does
    // stake, the number appears with no code change.
    const rpc = await startRpc({
        stakes: { [REAL_VAULT_0]: 'missing' },
        balances: { [REAL_VAULT_0]: [] },
    });
    const sql = recordingSql([{ match: VAULT_ROW_RE, rows: [vaultRow()] }]);
    try {
        const r = await vaults.readCollectiveVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
        assert.equal(r.degraded, false, 'NO_STAKE is an answer, not a fault');
        assert.equal(r.vault.percentStaked, 0);
        assert.equal(r.vault.collectiveVigilWeight, 0);
        assert.equal(sql.matching(VAULT_STAMP_RE).length, 0,
            'and a zero stake never begins the vault\'s tenure');
    } finally { await rpc.close(); }
});

test('the vault\'s tenure begins on the FIRST positive observation, with tenure 0 right then',
    async () => {
        // WO-1852's "first observation, not chain history" ruling, applied to a vault. Using
        // verified_at instead would bank tenure for the gap between registering an empty
        // vault and funding it.
        const rpc = await startRpc({
            stakes: { [REAL_VAULT_0]: { shares: 50n * SKR_UNIT } },
            balances: { [REAL_VAULT_0]: [50n * SKR_UNIT] },
        });
        const sql = recordingSql([
            { match: VAULT_ROW_RE, rows: [vaultRow({ first_seen_staked_at: null, tenure_seconds: 0 })] },
            { match: VAULT_STAMP_RE, rows: [{ first_seen_staked_at: 'NOW' }] },
        ]);
        try {
            const r = await vaults.readCollectiveVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
            assert.equal(r.vault.vigilBegan, true);
            assert.equal(r.vault.tenureSeconds, 0, 'it genuinely IS zero at the instant it begins');
            assert.equal(r.vault.collectiveVigilWeight, 0);
            const stamp = sql.matching(VAULT_STAMP_RE)[0];
            assert.match(stamp.text, /first_seen_staked_at\s+IS\s+NULL/i,
                'idempotent by its WHERE clause: a tenure that has begun can never move');
        } finally { await rpc.close(); }
    });

test('an unreadable chain degrades the collective to 0 + a flag, never a throw or a 500',
    async () => {
        const rpc = await startRpc({ fail: 'all' });
        const sql = recordingSql([{ match: VAULT_ROW_RE, rows: [vaultRow()] }]);
        try {
            const r = await vaults.readCollectiveVigil(sql, CLAN_ID, { rpcUrl: rpc.url });
            assert.equal(r.degraded, true);
            assert.equal(r.vault.collectiveVigilWeight, 0);
            assert.equal(r.vault.percentStaked, 0);
        } finally { await rpc.close(); }
    });

test('a MISSING clan_vaults table degrades — it does not take the Vigil endpoint down',
    async () => {
        // The deploy-order failure touchClanRate's fail-open note describes: code landing
        // before migration 0036.
        const missing = new Error('relation "clan_vaults" does not exist');
        missing.code = '42P01';
        const sql = recordingSql([{ match: VAULT_ROW_RE, throws: missing }]);
        const r = await vaults.readCollectiveVigil(sql, CLAN_ID, {});
        assert.equal(r.vault, null);
        assert.equal(r.degraded, true);
        assert.equal(r.reason, 'vault_row_unavailable');
    });

test('a clan with NO vault is the ordinary case and is not an error', async () => {
    const sql = recordingSql([{ match: VAULT_ROW_RE, rows: [] }]);
    const r = await vaults.readCollectiveVigil(sql, CLAN_ID, {});
    assert.equal(r.vault, null);
    assert.equal(r.degraded, false);

    const wire = vigilLib.readCollective ? await vigilLib.readCollective(sql, CLAN_ID, {}) : null;
    assert.equal(wire.vault, null);
    assert.equal(wire.collectiveVigilWeight, 0);
    // ⛔ NO VAULT IS NOT HARDWARE-BACKED. A truthy default would hand the status to every
    // clan in the game.
    assert.equal(wire.hardwareBacked, false);
});

// ═══════════════════════════════════════════════════════════════════════════
// 8. THE WIRE SHAPE — additive, and it does not disturb WO-1852's
// ═══════════════════════════════════════════════════════════════════════════

test('toWire adds top-level fields and leaves the MEMBER object byte-identical', () => {
    const wire = vigilLib.toWire({
        members: [{ wallet: SEEKER_A, role: 'leader', percentStaked: 0.5, tenureSeconds: 10,
            vigilContribution: 5, firstSeenStakedAt: 'T0', vigilBegan: false, degraded: false }],
        vigilWeight: 5, degraded: false, memberCount: 1,
        vault: vaults.shapeVaultRow(vaultRow()),
        collectiveVigilWeight: 1800, hardwareBacked: true, collectiveDegraded: false,
    });

    // ⛔ THE MEMBER KEY SET IS PINNED BY test/clan-vigil.test.js's own deepEqual. A SIGNER
    // is not a MEMBER — the two sets overlap by accident at best — so vault status must not
    // hang off a roster row.
    assert.deepEqual(Object.keys(wire.members[0]).sort(),
        ['degraded', 'percent_staked', 'role', 'tenure_seconds', 'vigil_contribution', 'wallet'].sort());

    // WO-1852's own fields, untouched.
    assert.equal(wire.vigil_weight, 5);
    assert.equal(wire.member_count, 1);
    assert.equal(wire.degraded, false);

    // BOTH spellings, from ONE source expression each, so they cannot drift. snake_case is
    // this endpoint's convention; camelCase is what WO-1854's acceptance criterion quotes
    // literally ("returns `hardwareBacked: true`"). The precedent is skr-staking's
    // totalRaw/balanceRaw pair.
    assert.equal(wire.hardware_backed, true);
    assert.equal(wire.hardwareBacked, true);
    assert.equal(wire.collective_vigil_weight, 1800);
    assert.equal(wire.collectiveVigilWeight, 1800);
    assert.equal(wire.collective_degraded, false);

    // ⛔ NO sgt_mint ANYWHERE ON THE WIRE. A mint is a DEVICE identifier — one per physical
    // phone — so publishing it even to a clanmate hands out a stable cross-wallet
    // fingerprint for a person.
    assert.ok(!JSON.stringify(wire).includes('sgt_mint'));
    assert.ok(!JSON.stringify(wire).includes(SGT_MINT_A));

    assert.equal(wire.vault.vault_address, REAL_VAULT_0);
    assert.equal(wire.vault.multisig_address, REAL_MULTISIG_ADDRESS);
    assert.equal(wire.vault.threshold, 2);
    assert.equal(wire.vault.signer_count, 2);
    assert.equal(wire.vault.verified_signer_count, 2);
});

test('with no vault the wire says so, and the two weights are NEVER added together', () => {
    const wire = vigilLib.toWire({
        members: [], vigilWeight: 42, degraded: false, memberCount: 0,
        vault: null, collectiveVigilWeight: 0, hardwareBacked: false, collectiveDegraded: false,
    });
    assert.equal(wire.vault, null);
    assert.equal(wire.hardware_backed, false);
    assert.equal(wire.collective_vigil_weight, 0);
    // ⛔ TWO NUMBERS, REPORTED SEPARATELY, FOREVER. `vigil_weight` is what the MEMBERS hold
    // individually; `collective_vigil_weight` is what the VAULT holds jointly. Summing them
    // would collapse the one distinction the whole feature exists to make.
    assert.equal(wire.vigil_weight, 42);
    assert.notEqual(wire.vigil_weight, wire.vigil_weight + wire.collective_vigil_weight - 42);
});

// ═══════════════════════════════════════════════════════════════════════════
// 9. THE ENDPOINTS
// ═══════════════════════════════════════════════════════════════════════════

const DRIVER_ID = require.resolve('@neondatabase/serverless');
const FRESH = [VIGIL_ROUTE, REGISTER_ROUTE, CLAN_HTTP, CLAN_LIB, CLAN_VIGIL, VAULTS_PATH];

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

function loadRoute(routePath, sqlFn) {
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = { neon() { return sqlFn; } };
    for (const p of FRESH) delete require.cache[require.resolve(p)];
    const handler = require(routePath);
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    for (const p of FRESH) delete require.cache[require.resolve(p)];
    return handler;
}

async function callRoute(routePath, opts = {}) {
    const sqlFn = opts.sql || recordingSql();
    const headers = Object.assign({ 'x-session': 'a'.repeat(44) }, opts.headers || {});
    const req = {
        method: opts.method || 'GET', headers: headers,
        query: opts.query || {}, body: opts.body,
        readableEnded: opts.body !== undefined, complete: opts.body !== undefined,
    };
    const prev = {
        db: process.env.DATABASE_URL,
        main: process.env.SOLANA_MAINNET_RPC_URL,
        helius: process.env.HELIUS_RPC_URL,
    };
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    // ⛔ CLEARED, NOT MERELY OVERWRITTEN, AND THIS IS A REAL HAZARD NOT A TIDINESS RULE.
    //    A developer or a CI job with .env.local sourced has HELIUS_RPC_URL in its
    //    environment. Any route test that does NOT pass an rpcUrl would then reach the REAL
    //    Helius endpoint — a shared, BILLED key (see the work order's FLAG 8) — on every
    //    run. "Zero mainnet at test time" has to be ENFORCED here, not assumed of the
    //    runner's environment.
    delete process.env.SOLANA_MAINNET_RPC_URL;
    delete process.env.HELIUS_RPC_URL;
    if (opts.rpcUrl) {
        process.env.SOLANA_MAINNET_RPC_URL = opts.rpcUrl;
        process.env.HELIUS_RPC_URL = opts.rpcUrl;
    }
    const handler = loadRoute(routePath, sqlFn);
    const res = fakeRes();
    try {
        await handler(req, res);
    } finally {
        restore('DATABASE_URL', prev.db);
        restore('SOLANA_MAINNET_RPC_URL', prev.main);
        restore('HELIUS_RPC_URL', prev.helius);
    }
    return { out: res.out, sql: sqlFn };
}

function restore(name, value) {
    if (value === undefined) delete process.env[name];
    else process.env[name] = value;
}

/** A route-level sql that authenticates the wallet rail and answers membership. */
function authedSql(role, extra = []) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: SEEKER_A, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity[\s\S]*first_seen_at/i,
          rows: [{ first_seen_at: 'T0', last_seen_at: 'T0', first_seen_staked_at: 'T0' }] },
        ...extra,
        { match: /FROM\s+clan_members\s+m[\s\S]*JOIN\s+clans/i, rows: [{
            clan_id: CLAN_ID, code: 'ABCD12', name: 'Ember Wardens', tag: 'EMBR',
            join_policy: 'invite', created_at: 'T0', role: role, joined_at: 'T0', member_count: 2,
        }] },
    ]);
}

test('ACCEPTANCE 6: GET /api/clan/vigil reports hardwareBacked for a registered vault',
    async () => {
        const rpc = await startRpc({
            stakes: { [SEEKER_A]: { shares: 100n * SKR_UNIT },
                      [REAL_VAULT_0]: { shares: 300n * SKR_UNIT } },
            balances: { [SEEKER_A]: [100n * SKR_UNIT], [REAL_VAULT_0]: [100n * SKR_UNIT] },
        });
        try {
            const { out } = await callRoute(VIGIL_ROUTE, {
                rpcUrl: rpc.url,
                query: { playerId: SEEKER_A },
                sql: authedSql('leader', [
                    { match: ROSTER_RE, rows: [{ wallet: SEEKER_A, role: 'leader',
                        first_seen_staked_at: 'T0', tenure_seconds: 7200 }] },
                    { match: VAULT_ROW_RE, rows: [vaultRow()] },
                ]),
            });
            assert.equal(out.statusCode, 200);
            assert.equal(out.body.ok, true);
            assert.equal(out.body.hardwareBacked, true, 'the acceptance criterion\'s literal name');
            assert.equal(out.body.hardware_backed, true);
            // The vault holds 300 staked of 400 total = 0.75, x 3600 s = 2700.
            assert.equal(out.body.collective_vigil_weight, 2700);
            assert.equal(out.body.collectiveVigilWeight, 2700);
            // ⛔ AND THE PER-MEMBER NUMBER IS UNCHANGED AND SEPARATE: 0.5 x 7200 = 3600.
            assert.equal(out.body.vigil_weight, 3600);
            assert.equal(out.body.vault.verified_signer_count, 2);
            assert.equal(out.body.collective_degraded, false);
        } finally { await rpc.close(); }
    });

test('GET /api/clan/vigil is UNCHANGED for a clan with no vault', async () => {
    const rpc = await startRpc({
        stakes: { [SEEKER_A]: { shares: 100n * SKR_UNIT } },
        balances: { [SEEKER_A]: [100n * SKR_UNIT] },
    });
    try {
        const { out } = await callRoute(VIGIL_ROUTE, {
            rpcUrl: rpc.url, query: { playerId: SEEKER_A },
            sql: authedSql('leader', [
                { match: ROSTER_RE, rows: [{ wallet: SEEKER_A, role: 'leader',
                    first_seen_staked_at: 'T0', tenure_seconds: 7200 }] },
                { match: VAULT_ROW_RE, rows: [] },
            ]),
        });
        assert.equal(out.statusCode, 200);
        assert.equal(out.body.vigil_weight, 3600, 'WO-1852\'s answer, byte for byte');
        assert.equal(out.body.vault, null);
        assert.equal(out.body.hardware_backed, false);
        assert.equal(out.body.collective_vigil_weight, 0);
    } finally { await rpc.close(); }
});

test('POST /api/clan/vault/register refuses a non-Leader with 403, before any chain read',
    async () => {
        const sql = authedSql('officer');
        const { out } = await callRoute(REGISTER_ROUTE, {
            method: 'POST',
            body: { playerId: SEEKER_A, vaultAddress: REAL_MULTISIG_ADDRESS },
            sql: sql,
        });
        assert.equal(out.statusCode, 403);
        assert.equal(out.body.code, vaults.VaultCode.NOT_LEADER);
        assert.equal(sql.matching(VAULT_INSERT_RE).length, 0);
        // And the body carries no wallet at all on this refusal — only the two SGT
        // refusals the work order names may do that.
        assert.equal(out.body.signer_wallet, undefined);
    });

test('POST /api/clan/vault/register is a 404 for a caller in no clan', async () => {
    const { out } = await callRoute(REGISTER_ROUTE, {
        method: 'POST',
        body: { playerId: SEEKER_A, vaultAddress: REAL_MULTISIG_ADDRESS },
        sql: recordingSql([
            { match: /FROM\s+auth_sessions/i, rows: [{ wallet: SEEKER_A, revoked: false, expired: false }] },
            { match: /INSERT\s+INTO\s+wallet_identity[\s\S]*first_seen_at/i, rows: [{}] },
        ]),
    });
    assert.equal(out.statusCode, 404);
    assert.equal(out.body.code, ClanCode.NOT_IN_CLAN);
});

test('POST /api/clan/vault/register rejects a badly-shaped address as a 400', async () => {
    const { out } = await callRoute(REGISTER_ROUTE, {
        method: 'POST',
        body: { playerId: SEEKER_A, vaultAddress: 'not-an-address' },
        sql: authedSql('leader'),
    });
    assert.equal(out.statusCode, 400);
    assert.equal(out.body.code, vaults.VaultCode.BAD_ADDRESS);
});

test('POST /api/clan/vault/register refuses GET, and answers the preflight', async () => {
    const { out } = await callRoute(REGISTER_ROUTE, {
        method: 'GET', query: { playerId: SEEKER_A }, sql: authedSql('leader'),
    });
    assert.equal(out.statusCode, 400);
    assert.equal(out.body.code, AuthCode.METHOD_NOT_ALLOWED);

    const pre = await callRoute(REGISTER_ROUTE, { method: 'OPTIONS' });
    assert.equal(pre.out.statusCode, 204);
    assert.match(String(pre.out.headers['access-control-allow-methods']), /POST/);
});

test('the register route carries EXACTLY the one permitted line of player-facing copy',
    async () => {
        // ⛔ THE LEGAL / COPY GATE IS BINDING. The work order permits one line for
        // hardware-backed status — "The ancestors remember what you built together." —
        // and forbids any other new public-facing copy without the owner's explicit
        // review. This test is the guard: adding a second sentence fails it.
        const src = require('node:fs').readFileSync(REGISTER_ROUTE, 'utf8');
        const strings = src.match(/message:\s*'([^']*)'/g) || [];
        assert.equal(strings.length, 1, 'exactly one player-facing message, no more');
        assert.ok(src.includes("'The ancestors remember what you built together.'"),
            'and it is the approved line, verbatim');
        // ⛔ AND NO PLAYER-FACING STRING ANYWHERE IN THE FEATURE MAY IMPLY AN INVESTMENT, A
        //    RETURN OR A LEGAL STATUS. The scan is over STRING LITERALS ONLY, with comments
        //    stripped first — the comments are where this very rule is written down, so a
        //    naive whole-file grep would fail on the guard's own explanation and would then
        //    be "fixed" by deleting the explanation. That is the failure mode, so the test
        //    is built not to have it.
        const literals = quotedStrings(stripComments(src))
            .concat(quotedStrings(stripComments(require('node:fs').readFileSync(VAULTS_PATH, 'utf8'))))
            .concat(quotedStrings(stripComments(require('node:fs').readFileSync(GT_PATH, 'utf8'))))
            .join('\n');
        for (const banned of ['investment', 'securities', 'security', 'yield',
            'profit', 'APY', 'dividend', 'guaranteed']) {
            assert.ok(!new RegExp('\\b' + banned + '\\b', 'i').test(literals),
                '⛔ the word "' + banned + '" appears in a STRING in this feature — '
                + 'docs/SKR_VISION_RECONCILIATION_2026-09-11.md forbids that claim, and any '
                + 'new legal-adjacent copy needs the owner\'s explicit review first');
        }
    });

/** Blank out // and /* comments so a scan sees code, not commentary. */
function stripComments(src) {
    return String(src)
        .replace(/\/\*[\s\S]*?\*\//g, ' ')
        .replace(/(^|[^:])\/\/[^\n]*/g, '$1');
}

/** Every single- or double-quoted literal in the (comment-free) source. */
function quotedStrings(src) {
    const out = [];
    const re = /'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"/g;
    let m;
    while ((m = re.exec(src)) !== null) out.push(m[1] !== undefined ? m[1] : m[2]);
    return out;
}
