'use strict';

// =============================================================================
// WO-1833 - a promo code that carries a DISCOUNT (SPOTLIGHT30).
// -----------------------------------------------------------------------------
// Owner, 2026-09-17: "if i set up promocode spotlight30 as a promo code can you
// make it make everything 30% off?", duration "flat 12:00 - 11:59 CST".
//
// These are BEHAVIOURAL tests: they drive the REAL api/promo/redeem.js handler with
// a faked Neon driver and a stubbed auth result. The same reasoning
// test/promo.owner-bypass.test.js states for doing it this way applies with full
// force here - a source-grep would pass for a window gate wired ANYWHERE in the
// file, including after the atomic claim, which is the one place it must not be:
// a code refused AFTER the claim is a code burned for nothing, and there is no
// un-burn (UNIQUE(code, player_id)).
//
// ⛔ WHY A SEPARATE FILE RATHER THAN MORE CASES IN promo.owner-bypass.test.js: that
// suite hijacks require.cache to stub the driver and is scoped to the WO-1533 owner
// ruling. node --test runs each file in its own process, so two files may each own
// their stubs; mixing two rulings' fixtures in one is how a fixture change breaks an
// unrelated proof.
//
// The three rulings under test, each of which was got wrong once while this ticket
// was being written:
//   1. the window is a FIXED CALENDAR EVENT, never redemption + 48h;
//   2. redeeming outside it is refused EXPIRED and does NOT consume the code;
//   3. a DISCOUNT-ONLY code (0 crystals, 0 coins) must NOT hit the zero-reward
//      backstop - that refusal would make the owner's actual ask unshippable.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');

const WALLET = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const GUEST = 'guest-local-' + 'b'.repeat(64);

// ── Stub the modules redeem.js DESTRUCTURES AT REQUIRE TIME ──────────────────
const walletAuth = require('../api/_lib/wallet-auth');
const audit = require('../api/_lib/audit');
const ipBudget = require('../api/_lib/ip-budget');

let CURRENT_SQL = null;
let CURRENT_AUTH = null;
const AUDIT_EVENTS = [];

// The driver's `neon` export is getter-only, so replace the whole cached module.
const neonPath = require.resolve('@neondatabase/serverless');
require.cache[neonPath] = {
    id: neonPath, filename: neonPath, loaded: true, exports: { neon: () => CURRENT_SQL },
};

walletAuth.authenticatePromoRedeem = async () => CURRENT_AUTH;
audit.hashIp = () => 'deadbeefcafe';
audit.logAuthReject = async () => {};
audit.logApiEvent = async (sql, identity, eventName, properties) => {
    AUDIT_EVENTS.push({ identity, eventName, properties });
};
ipBudget.reserveIpBudget = async () => ({ ok: true, grants: 1 });

process.env.DATABASE_URL = process.env.DATABASE_URL || 'postgres://fake/fake';

const handler = require('../api/promo/redeem.js');
const promoDiscount = require('../api/_lib/promo-discount');

// ── Fixtures ─────────────────────────────────────────────────────────────────

const NOW = Date.now();
const HOUR = 3_600_000;

/**
 * SPOTLIGHT30's shape: a DISCOUNT-ONLY code. Zero crystals, zero coins - the exact
 * row the pre-WO-1833 zero-reward backstop would have refused forever.
 */
function discountCode(over) {
    return Object.assign({
        code: 'SPOTLIGHT30',
        reward_crystals: 0,
        reward_coins: 0,
        message: '30% off everything until 11:59pm CST',
        active: true,
        max_redemptions: null,
        per_player_limit: null,
        expires_at: null,
        bound_wallet: null,
        reward_pack_sku: null,
        tier1_pack_sku: null,
        tier1_limit: null,
        tier2_pack_sku: null,
        tier2_reward_crystals: null,
        tier2_reward_coins: null,
        redemption_count: 0,
        discount_bps: 3000,
        discount_starts_at: new Date(NOW - 2 * HOUR).toISOString(),
        discount_ends_at: new Date(NOW + 30 * HOUR).toISOString(),
    }, over || {});
}

/**
 * A tagged-template stand-in for the Neon driver. `claims` records every atomic
 * claim so a test can prove the code was NOT consumed by a refusal.
 */
function makeSql(state) {
    const calls = [];
    const claims = [];
    const fn = (strings, ...values) => {
        const text = strings.join(' ? ');
        calls.push({ text, values });

        if (/SELECT code, reward_crystals/.test(text) && /FROM promo_codes/.test(text)) {
            // ⚠ THE UNMIGRATED-DATABASE RAIL. When the statement names the discount
            // columns and this fixture says they do not exist, raise the REAL SQLSTATE
            // Postgres raises, so the handler's fallback cascade is exercised rather
            // than described.
            if (state.discountColumnsMissing && /discount_bps/.test(text)) {
                const err = new Error('column "discount_bps" does not exist');
                err.code = '42703';
                return Promise.reject(err);
            }
            if (!state.promo) return Promise.resolve([]);
            const row = Object.assign({}, state.promo);
            if (state.discountColumnsMissing) {
                delete row.discount_bps;
                delete row.discount_starts_at;
                delete row.discount_ends_at;
            }
            return Promise.resolve([row]);
        }
        if (/SELECT 1 FROM promo_redemptions/.test(text)) {
            return Promise.resolve(state.alreadyRedeemedThisCode ? [{ '?column?': 1 }] : []);
        }
        if (/COUNT\(\*\)::int AS n FROM promo_redemptions/.test(text)) {
            return Promise.resolve([{ n: state.globalRedemptions || 0 }]);
        }
        if (/COUNT\(DISTINCT code\)::int/.test(text)) {
            return Promise.resolve([{ n: state.distinctCodesForPlayer || 0 }]);
        }
        if (/SELECT redemption_count FROM promo_codes/.test(text)) {
            return Promise.resolve([{ redemption_count: state.globalRedemptions || 0 }]);
        }
        if (/WITH claimed/.test(text)) {
            claims.push({ text, values });
            // A concurrent redeem already took it: UNIQUE(code, player_id).
            if (state.uniqueViolation) {
                const err = new Error('duplicate key value violates unique constraint');
                err.code = '23505';
                return Promise.reject(err);
            }
            const max = state.promo.max_redemptions;
            const capBlocks = max != null && Number(state.globalRedemptions || 0) >= Number(max);
            if (capBlocks) return Promise.resolve([]);
            state.globalRedemptions = (state.globalRedemptions || 0) + 1;
            return Promise.resolve([{
                crystals: state.promo.reward_crystals,
                coins: state.promo.reward_coins,
            }]);
        }
        return Promise.resolve([]);
    };
    fn.calls = calls;
    fn.claims = claims;
    return fn;
}

function makeRes() {
    return {
        statusCode: 0, body: null, headers: {},
        setHeader(k, v) { this.headers[k] = v; },
        status(code) { this.statusCode = code; return this; },
        json(payload) { this.body = payload; return this; },
        end() { return this; },
    };
}

async function redeem(opts) {
    const body = JSON.stringify({ playerId: opts.playerId, code: opts.code || 'SPOTLIGHT30' });
    const req = { method: 'POST', headers: {}, body: body, readableEnded: true, complete: true };
    const sql = makeSql(opts.state);
    CURRENT_SQL = sql;
    CURRENT_AUTH = {
        ok: true,
        mode: opts.unproven ? 'guest' : 'wallet',
        identity: opts.playerId,
        unproven: opts.unproven === true,
    };
    AUDIT_EVENTS.length = 0;
    const res = makeRes();
    await handler(req, res);
    return { res, sql };
}

// ── 1. THE OWNER'S ACTUAL ASK: A DISCOUNT-ONLY CODE ──────────────────────────

test('⛔ a DISCOUNT-ONLY code redeems — the zero-reward backstop does not eat it', async () => {
    // Before WO-1833 this row (0 crystals, 0 coins) was refused REWARD_UNAVAILABLE by
    // the structural backstop, forever, while logging that the operator had
    // mis-authored it. That refusal would have made the owner's ask unshippable.
    const state = { promo: discountCode(), globalRedemptions: 0 };
    const { res, sql } = await redeem({ playerId: WALLET, state: state });

    assert.equal(res.statusCode, 200);
    assert.equal(res.body.success, true,
        'refused with ' + (res.body && res.body.error) + ' - a live discount window IS a reward');
    assert.equal(res.body.discount.bps, 3000);
    assert.equal(res.body.discount.label, '30% off');
    assert.equal(res.body.discount.endsAt, state.promo.discount_ends_at);
    // The one thing the SHIPPED APK can actually render.
    assert.match(res.body.message, /30% off/);
    // Exactly ONE claim statement - the ledger row IS the discount record, so there
    // is no second write to lose (WO-1833 D1).
    assert.equal(sql.claims.length, 1);
    assert.equal(AUDIT_EVENTS.filter(e => e.eventName === 'promo_discount_redeem').length, 1,
        'a discount grant must be as auditable as a currency grant');
});

test('⛔ the expiry the player gets is the SHARED window end, NOT redemption + 48h', async () => {
    // THE CORRECTION THAT FORCED A REDESIGN (owner: "flat 12:00 - 11:59 CST"). Two
    // players who redeem hours apart must be handed the SAME end time.
    const promo = discountCode();
    const first = await redeem({ playerId: WALLET, state: { promo: promo } });
    const second = await redeem({ playerId: GUEST, state: { promo: promo }, unproven: true });

    assert.equal(first.res.body.discount.endsAt, second.res.body.discount.endsAt,
        'a per-player rolling window is exactly what this ruling forbids');
    assert.equal(first.res.body.discount.endsAt, promo.discount_ends_at,
        'the end time is AUTHORED on the row and echoed, never computed');

    // A player redeeming with 20 minutes left gets 20 minutes, not a fresh window.
    const nearlyOver = discountCode({ discount_ends_at: new Date(Date.now() + 20 * 60_000).toISOString() });
    const late = await redeem({ playerId: WALLET, state: { promo: nearlyOver } });
    assert.equal(late.res.body.success, true);
    assert.ok(Date.parse(late.res.body.discount.endsAt) - Date.now() < HOUR,
        'a late redeemer gets WHAT IS LEFT of the shared window');
});

// ── 2. THE WINDOW GATE, AND THAT IT COSTS THE PLAYER NOTHING ─────────────────

test('outside the window: EXPIRED, and ⛔ the code is NOT consumed', async () => {
    for (const [label, over] of [
        ['before it opens', { discount_starts_at: new Date(NOW + 4 * HOUR).toISOString() }],
        ['after it closes', { discount_ends_at: new Date(NOW - HOUR).toISOString() }],
        ['no end authored', { discount_ends_at: null }],
    ]) {
        const state = { promo: discountCode(over), globalRedemptions: 0 };
        const { res, sql } = await redeem({ playerId: WALLET, state: state });
        assert.equal(res.statusCode, 200, label);
        assert.equal(res.body.success, false, label);
        assert.equal(res.body.error, 'EXPIRED', label + ': must reuse the EXISTING error key');
        // ⛔ THE HALF THAT MATTERS MORE THAN THE ERROR STRING. A refusal is retryable; a
        // burn is not. If the gate ever moves below the claim, this is what goes red.
        assert.equal(sql.claims.length, 0, label + ': the code was CONSUMED by a refusal');
        assert.equal(state.globalRedemptions, 0, label);
    }
});

test('a plain GRANT code is untouched by the window gate', async () => {
    const grant = discountCode({ code: 'LINK01', reward_crystals: 500, discount_bps: null,
        discount_starts_at: null, discount_ends_at: null });
    const { res } = await redeem({ playerId: WALLET, code: 'LINK01', state: { promo: grant } });
    assert.equal(res.body.success, true);
    assert.equal(res.body.reward.crystals, 500);
    assert.equal(res.body.discount, null, 'null, never a 0% discount some formatter prints');
});

test('an UNMIGRATED database degrades to plain-grant behaviour instead of 500ing every code', async () => {
    // ⛔ THE OUTAGE THIS PREVENTS. Migration 0028 is applied by a human running
    // run-migrations.mjs, and a DEPLOY CAN BEAT THEM TO IT. Naming a column that does
    // not exist raises 42703 on the FIRST statement of every redemption, which would
    // turn an optional new feature into a total outage of the promo rail.
    const state = { promo: discountCode({ reward_crystals: 250 }), discountColumnsMissing: true };
    const { res, sql } = await redeem({ playerId: WALLET, state: state });
    assert.equal(res.statusCode, 200, 'the rail must not 500 because a migration is pending');
    assert.equal(res.body.success, true);
    assert.equal(res.body.reward.crystals, 250);
    assert.equal(res.body.discount, null, 'no discount can be honoured, and it says so by being null');
    assert.equal(sql.claims.length, 1);

    // A DISCOUNT-ONLY code on an unmigrated database reads as granting nothing and is
    // refused UNBURNED by the structural backstop - the correct, recoverable answer.
    const only = { promo: discountCode(), discountColumnsMissing: true };
    const refused = await redeem({ playerId: WALLET, state: only });
    assert.equal(refused.res.body.success, false);
    assert.equal(refused.res.body.error, 'REWARD_UNAVAILABLE');
    assert.equal(refused.sql.claims.length, 0, 'and it is NOT consumed, so it works after the migration');
});

// ── 3. THE ATOMIC CLAIM IS UNCHANGED ─────────────────────────────────────────

test('⛔ the atomic claim still behaves for a discount-carrying code: no double grant', async () => {
    // A lost UNIQUE(code, player_id) race answers ALREADY_REDEEMED, idempotently - the
    // WO-1440 machinery, reused rather than paralleled. A discount code must not have
    // found its own way around it.
    const race = { promo: discountCode(), uniqueViolation: true };
    const lost = await redeem({ playerId: WALLET, state: race });
    assert.equal(lost.res.body.success, false);
    assert.equal(lost.res.body.error, 'ALREADY_REDEEMED');
    assert.equal(lost.res.body.discount, undefined, 'a refusal grants no discount');

    // The cheap early-out still refuses a second redemption by the same player.
    const twice = await redeem({ playerId: WALLET,
        state: { promo: discountCode(), alreadyRedeemedThisCode: true } });
    assert.equal(twice.res.body.error, 'ALREADY_REDEEMED');
    assert.equal(twice.sql.claims.length, 0);

    // And the global cap predicate still owns the cap.
    const capped = await redeem({ playerId: WALLET,
        state: { promo: discountCode({ max_redemptions: 5 }), globalRedemptions: 5 } });
    assert.equal(capped.res.body.error, 'ALREADY_REDEEMED');

    // ⛔ ONE STATEMENT, STILL. The claim carries the INSERT, and nothing follows it -
    // D1's whole argument is that a second write would not be in the same transaction.
    const ok = await redeem({ playerId: WALLET, state: { promo: discountCode() } });
    assert.equal(ok.res.body.success, true);
    assert.equal(ok.sql.claims.length, 1);
    assert.match(ok.sql.claims[0].text, /INSERT INTO promo_redemptions/,
        'the claim and the ledger insert are ONE statement');
});

test('the gate order is proven, not assumed: a bad code never reaches the window check', async () => {
    // An INACTIVE code, an EXPIRED code and a code bound to someone else all still
    // answer their own refusals - the new gate did not reorder the cheap ones.
    const inactive = await redeem({ playerId: WALLET, state: { promo: discountCode({ active: false }) } });
    assert.equal(inactive.res.body.error, 'INVALID_CODE');

    const expired = await redeem({ playerId: WALLET,
        state: { promo: discountCode({ expires_at: new Date(NOW - HOUR).toISOString() }) } });
    assert.equal(expired.res.body.error, 'EXPIRED');

    const bound = await redeem({ playerId: WALLET,
        state: { promo: discountCode({ bound_wallet: 'SomeoneElse111111111111111111111111111111' }) } });
    assert.equal(bound.res.body.error, 'INVALID_CODE',
        'a private code stays indistinguishable from a nonexistent one');

    const missing = await redeem({ playerId: WALLET, state: { promo: null } });
    assert.equal(missing.res.body.error, 'INVALID_CODE');
});

// ── 4. THE LIBRARY THE QUOTE PATH SHARES WITH THIS HANDLER ───────────────────

test('the quote path and the redeem path judge the SAME window through the SAME library', async () => {
    // One library, two callers: "is this discount live" must have ONE answer, or the
    // till and the redemption disagree about a promise made to a player.
    const row = discountCode();
    assert.equal(promoDiscount.resolveRedeemWindow(row, NOW).ok, true);
    assert.equal(promoDiscount.pickActiveDiscount([row], NOW).bps, 3000);

    // readActivePromoDiscount FAILS TO NO DISCOUNT - it may never throw into a quote.
    const throwing = () => { throw Object.assign(new Error('nope'), { code: '42703' }); };
    assert.equal(await promoDiscount.readActivePromoDiscount(throwing, WALLET), null);
    assert.equal(await promoDiscount.readActivePromoDiscount(null, WALLET), null);
    assert.equal(await promoDiscount.readActivePromoDiscount(async () => [row], ''), null,
        'no playerId, no personal discount - that is the public shelf');
    assert.equal((await promoDiscount.readActivePromoDiscount(async () => [row], WALLET)).bps, 3000);
});
