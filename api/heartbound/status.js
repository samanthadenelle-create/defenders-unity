// =============================================================================
// api/heartbound/status.js — Vercel Serverless Function
// -----------------------------------------------------------------------------
// GET /api/heartbound/status?playerId=<wallet>[&refresh=1]
//
// WO-1674 (HEART-001). The ONE place anything of value learns how much SKR a
// player has natively staked. Owner ruling 2026-09-10 13:10: backend only; an
// RPC outage serves the last-known verified state within a bounded grace
// window; Heartbound is a Seeker-artifact feature.
//
// ⛔ THE SECURITY PROPERTY, AND IT IS STRUCTURAL RATHER THAN CHECKED.
//
//   Spec :162-168 — the client may request status; the client may NOT submit
//   `stakedSkr = 500000` "or any equivalent staking value"; the server resolves
//   the wallet from the authenticated player identity.
//
//   This handler REFUSES ANY METHOD BUT GET, so there is no body to trust, and
//   it reads exactly TWO query parameters: `playerId` (which must survive
//   authenticate(), i.e. be proven by an ed25519 signature over a single-use
//   nonce) and `refresh` (a boolean). The stake is then derived from
//   `auth.identity` — the PROVEN wallet — and NEVER from anything the request
//   said about itself. There is no parameter, under any name, through which an
//   amount, a tier, a share count or an account address can enter; the amount
//   comes from api/_lib/skr-staking.js reading mainnet.
//
//   ⚠ NOTE THE SUBTLETY: it resolves from auth.identity, not from the playerId
//   query parameter, even though on the wallet rail they are the same string.
//   Reading the query parameter would be correct today and would silently stop
//   being correct the moment any rail lets a request name an id it merely
//   proved access to. Use the proven value.
//
// ⛔ WHY authenticate() AND NOT authenticateGranting(). This route GRANTS
//   NOTHING — it reports what the chain says. api/_lib/wallet-auth.js:42-44:
//   "ROUTES THAT GRANT VALUE CALL authenticateGranting(), NOT authenticate()."
//   Heartbound routes that later pay something out must call the granting gate;
//   this one is a read, exactly as api/game/load.js is. It is also the reason a
//   guest can reach it at all — and get WALLET_NOT_LINKED, which is the honest
//   answer for a rail that has no wallet and no way to attach one
//   (api/_lib/wallet-auth.js:27-29: "THE WALLET REMAINS THE SOLE IDENTITY ON
//   THE SEEKER/APK ARTIFACT"), and the confirmation of the Seeker-only ruling.
//
// REFRESH RULES (spec :155-160). The client decides WHEN to ask (login, screen
//   open on a stale cache, wallet-link change, background processing, manual
//   request); this route decides whether that ask reaches the chain:
//     • cache younger than 5 minutes  -> served from the row, no RPC;
//     • `refresh=1` inside the 60-second manual cooldown -> served from the row,
//       no RPC, and it says so (`refreshThrottled: true`) rather than pretending;
//     • otherwise -> one chain read, then store.
//   "Do not hammer RPC on every UI render" (spec :161) is enforced here, in the
//   server, because a client-side rule is a client-side promise.
//
// Status codes: 200 | 400 | 401 | 500
// Driver: @neondatabase/serverless
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const { AuthCode, authenticate, isWalletId } = require('../_lib/wallet-auth');
const { applyCors, newRef, quietFail } = require('../_lib/http');
const { logAuthReject, logApiEvent } = require('../_lib/audit');
const skr = require('../_lib/skr-staking');

/** TIMESTAMPTZ -> unix SECONDS, tolerating both shapes the Neon driver returns. */
function toUnixSeconds(ts) {
    if (ts == null) return null;
    const ms = ts instanceof Date ? ts.getTime() : Date.parse(String(ts));
    return Number.isFinite(ms) ? Math.floor(ms / 1000) : null;
}

/**
 * NUMERIC(39,0) -> BigInt, preserving NULL AS NULL.
 *
 * ⛔ NULL IS A MEANING, NOT A MISSING VALUE (the same rule api/game/load.js
 * spells out at :47-68 for schema_version). NULL says: this wallet has never
 * been successfully verified. Coercing it to 0n here would recreate, in one
 * line, the exact confusion the migration's nullable columns exist to prevent.
 * The Neon driver hands a NUMERIC back as a string, which is the right shape:
 * a u128 cannot survive a round trip through Number.
 */
function toBigIntOrNull(v) {
    if (v === null || v === undefined) return null;
    try {
        return BigInt(String(v));
    } catch (_) {
        return null;
    }
}

/** A stored row -> the shape resolveServedState and the wire both expect. */
function rowToSnapshot(row) {
    if (!row) return null;
    return {
        walletAddress: row.wallet_address,
        guardianPool: row.guardian_pool,
        userStakeAddress: row.user_stake_address,
        sharesRaw: toBigIntOrNull(row.shares_raw),
        sharePriceRaw: toBigIntOrNull(row.share_price_raw),
        activeStakedRaw: toBigIntOrNull(row.active_staked_raw),
        unstakingRaw: toBigIntOrNull(row.unstaking_raw),
        unstakeTimestamp: row.unstake_timestamp != null ? BigInt(row.unstake_timestamp) : null,
        cooldownSeconds: row.cooldown_seconds != null ? BigInt(row.cooldown_seconds) : null,
        unstakingReady: !!row.unstaking_ready,
        sourceSlot: row.source_slot != null ? Number(row.source_slot) : null,
        verificationStatus: row.verification_status,
        errorCode: row.error_code,
        verifiedAtSeconds: toUnixSeconds(row.verified_at),
        lastAttemptSeconds: toUnixSeconds(row.last_attempt_at),
    };
}

/** BigInt|null -> a decimal STRING for SQL, preserving NULL. */
function sqlNum(v) {
    return v === null || v === undefined ? null : String(v);
}

/**
 * Persist a verification attempt.
 *
 * ⛔ verified_at ADVANCES ONLY ON A SUCCESS, and the amount columns are only
 * overwritten on a success. A failed attempt moves last_attempt_at and records
 * error_code, and LEAVES THE LAST-KNOWN AMOUNTS EXACTLY WHERE THEY WERE — that
 * is the whole mechanism behind the owner's grace-window ruling. Writing the
 * failure's nulls over a good row would delete the very state the ruling says
 * to serve.
 */
async function persist(sql, playerId, fresh, succeeded) {
    if (succeeded) {
        await sql`
            INSERT INTO skr_stake_snapshots (
                player_id, wallet_address, guardian_pool, user_stake_address,
                shares_raw, share_price_raw, active_staked_raw, unstaking_raw,
                unstake_timestamp, cooldown_seconds, unstaking_ready, source_slot,
                verification_status, error_code, verified_at, last_attempt_at, updated_at)
            VALUES (
                ${playerId}, ${fresh.walletAddress}, ${fresh.guardianPool}, ${fresh.userStakeAddress},
                ${sqlNum(fresh.sharesRaw)}, ${sqlNum(fresh.sharePriceRaw)},
                ${sqlNum(fresh.activeStakedRaw)}, ${sqlNum(fresh.unstakingRaw)},
                ${sqlNum(fresh.unstakeTimestamp)}, ${sqlNum(fresh.cooldownSeconds)},
                ${!!fresh.unstakingReady}, ${fresh.sourceSlot},
                ${fresh.verificationStatus}, ${fresh.errorCode}, NOW(), NOW(), NOW())
            ON CONFLICT (player_id) DO UPDATE SET
                wallet_address      = EXCLUDED.wallet_address,
                guardian_pool       = EXCLUDED.guardian_pool,
                user_stake_address  = EXCLUDED.user_stake_address,
                shares_raw          = EXCLUDED.shares_raw,
                share_price_raw     = EXCLUDED.share_price_raw,
                active_staked_raw   = EXCLUDED.active_staked_raw,
                unstaking_raw       = EXCLUDED.unstaking_raw,
                unstake_timestamp   = EXCLUDED.unstake_timestamp,
                cooldown_seconds    = EXCLUDED.cooldown_seconds,
                unstaking_ready     = EXCLUDED.unstaking_ready,
                source_slot         = EXCLUDED.source_slot,
                verification_status = EXCLUDED.verification_status,
                error_code          = EXCLUDED.error_code,
                verified_at         = NOW(),
                last_attempt_at     = NOW(),
                updated_at          = NOW()
        `;
        return;
    }

    // FAILURE: touch the attempt clock and the error, never the amounts and
    // never verified_at. A first-ever attempt that fails inserts a row with NULL
    // amounts and NULL verified_at, which reads back as "never verified" — the
    // honest state, and the one the grace window correctly refuses to extend.
    await sql`
        INSERT INTO skr_stake_snapshots (
            player_id, wallet_address, guardian_pool, user_stake_address,
            verification_status, error_code, verified_at, last_attempt_at, updated_at)
        VALUES (
            ${playerId}, ${fresh.walletAddress}, ${fresh.guardianPool}, ${fresh.userStakeAddress},
            ${fresh.verificationStatus}, ${fresh.errorCode}, NULL, NOW(), NOW())
        ON CONFLICT (player_id) DO UPDATE SET
            user_stake_address = COALESCE(EXCLUDED.user_stake_address, skr_stake_snapshots.user_stake_address),
            error_code         = EXCLUDED.error_code,
            last_attempt_at    = NOW(),
            updated_at         = NOW()
    `;
}

/** The wire shape. Raw u128s travel as STRINGS; only display values are decimal. */
function toWire(s, extra) {
    const base = {
        walletAddress: s.walletAddress || null,
        guardianPool: s.guardianPool || null,
        userStakeAddress: s.userStakeAddress || null,
        sharesRaw: s.sharesRaw === null || s.sharesRaw === undefined ? null : String(s.sharesRaw),
        sharePriceRaw: s.sharePriceRaw === null || s.sharePriceRaw === undefined ? null : String(s.sharePriceRaw),
        activeStakedRaw: s.activeStakedRaw === null || s.activeStakedRaw === undefined ? null : String(s.activeStakedRaw),
        unstakingRaw: s.unstakingRaw === null || s.unstakingRaw === undefined ? null : String(s.unstakingRaw),
        unstakeTimestamp: s.unstakeTimestamp === null || s.unstakeTimestamp === undefined ? null : Number(s.unstakeTimestamp),
        cooldownSeconds: s.cooldownSeconds === null || s.cooldownSeconds === undefined ? null : Number(s.cooldownSeconds),
        unstakingReady: !!s.unstakingReady,
        sourceSlot: s.sourceSlot === null || s.sourceSlot === undefined ? null : Number(s.sourceSlot),
        verificationStatus: s.verificationStatus,
        errorCode: s.errorCode || null,
        skrDecimals: skr.SKR_DECIMALS,
        // Display convenience, computed ONCE and at the edge (spec :178). Null
        // stays null: there is no display value for an amount we never read.
        activeStakedDisplay: s.activeStakedRaw === null || s.activeStakedRaw === undefined
            ? null : skr.toDisplaySkr(s.activeStakedRaw),
        unstakingDisplay: s.unstakingRaw === null || s.unstakingRaw === undefined
            ? null : skr.toDisplaySkr(s.unstakingRaw),
    };
    return Object.assign(base, extra || {});
}

module.exports = async (req, res) => {
    if (applyCors(req, res, 'GET, OPTIONS')) return;

    const ref = newRef();

    // GET ONLY. This is the first half of "the client may NOT submit a staking
    // value": with no body accepted, there is nothing to submit.
    if (req.method !== 'GET') {
        return quietFail(res, 400, AuthCode.METHOD_NOT_ALLOWED, ref);
    }

    const query = req.query || {};
    const playerId = query.playerId;
    if (!playerId) {
        return quietFail(res, 400, AuthCode.PLAYER_ID_MISSING, ref);
    }
    const wantsRefresh = query.refresh === '1' || query.refresh === 'true';

    let sql;
    try {
        sql = neon(process.env.DATABASE_URL);
    } catch (err) {
        console.error('[heartbound/status] DB init error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    // ── AUTH GATE — no body, so the wallet rail signs the literal "load" tag,
    //    exactly as api/game/load.js:122 does. Unchanged rail, added caller.
    let auth;
    try {
        auth = await authenticate(sql, req, null, playerId);
    } catch (err) {
        console.error('[heartbound/status] Auth check error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!auth.ok) {
        await logAuthReject(sql, req, {
            code: auth.code, ref, identity: auth.identity, mode: auth.mode, detail: auth.detail,
        });
        const status = (auth.code === AuthCode.PLAYER_ID_BAD_SHAPE ||
                        auth.code === AuthCode.WALLET_MALFORMED) ? 400 : 401;
        return quietFail(res, status, auth.code, ref);
    }

    // THE WALLET IS THE PROVEN IDENTITY, never the query parameter. See the header.
    const identity = auth.identity;
    const nowSeconds = Math.floor(Date.now() / 1000);

    // A rail with no wallet cannot be Heartbound, and says so plainly rather
    // than by an error. Confirms the Seeker-only ruling at the wire.
    if (auth.mode !== 'wallet' || !isWalletId(identity)) {
        await logApiEvent(sql, identity, 'skr_verification_failed', {
            ref, mode: auth.mode, status: skr.VerificationStatus.WALLET_NOT_LINKED,
        });
        return res.status(200).json({
            ok: true,
            success: true,
            serverNowMs: Date.now(),
            mode: auth.mode,
            snapshot: toWire({
                walletAddress: null,
                guardianPool: skr.GUARDIAN_POOL,
                userStakeAddress: null,
                sharesRaw: null, sharePriceRaw: null, activeStakedRaw: null,
                unstakingRaw: null, unstakeTimestamp: null, cooldownSeconds: null,
                unstakingReady: false, sourceSlot: null,
                verificationStatus: skr.VerificationStatus.WALLET_NOT_LINKED,
                errorCode: 'no_wallet_on_rail',
            }, { verifiedAtMs: null, ageSeconds: null, fromCache: false, refreshThrottled: false }),
            ref,
        });
    }

    // ── The stored snapshot IS the cache (there is no KV in this project) ──
    let stored = null;
    try {
        const rows = await sql`
            SELECT * FROM skr_stake_snapshots WHERE player_id = ${identity} LIMIT 1
        `;
        stored = rows.length ? rowToSnapshot(rows[0]) : null;
    } catch (err) {
        // A missing table must not 500 an otherwise-answerable read: fall
        // through to a live verification and report it. Same degrade-don't-deny
        // posture api/_lib/wallet-auth.js:700-706 takes for guest_rate_limit.
        console.warn('[heartbound/status] snapshot read failed:', err.message);
    }

    const cacheFresh = stored && skr.isCacheFresh(stored.verifiedAtSeconds, nowSeconds);
    const cooldownBlocks = wantsRefresh &&
        !skr.manualRefreshAllowed(stored && stored.lastAttemptSeconds, nowSeconds);

    // Serve from the row when the cache is fresh and nobody asked for a refresh,
    // or when a manual refresh is inside its 60-second cooldown.
    if (stored && ((cacheFresh && !wantsRefresh) || cooldownBlocks)) {
        const age = stored.verifiedAtSeconds ? nowSeconds - stored.verifiedAtSeconds : null;
        return res.status(200).json({
            ok: true,
            success: true,
            serverNowMs: Date.now(),
            mode: auth.mode,
            snapshot: toWire(stored, {
                verifiedAtMs: stored.verifiedAtSeconds ? stored.verifiedAtSeconds * 1000 : null,
                ageSeconds: age,
                fromCache: true,
                refreshThrottled: !!cooldownBlocks,
            }),
            ref,
        });
    }

    // ── Read the chain ──
    let fresh;
    try {
        fresh = await skr.verifyStake(identity, { nowSeconds: nowSeconds });
    } catch (err) {
        console.error('[heartbound/status] verifier threw:', err);
        fresh = {
            walletAddress: identity,
            guardianPool: skr.GUARDIAN_POOL,
            userStakeAddress: null,
            sharesRaw: null, sharePriceRaw: null, activeStakedRaw: null,
            unstakingRaw: null, unstakeTimestamp: null, cooldownSeconds: null,
            unstakingReady: false, sourceSlot: null,
            verificationStatus: skr.VerificationStatus.INVALID_RESPONSE,
            errorCode: 'verifier_threw',
            rpcLatencyMs: null,
        };
    }

    const succeeded = fresh.verificationStatus === skr.VerificationStatus.VERIFIED ||
                      fresh.verificationStatus === skr.VerificationStatus.NO_STAKE;

    try {
        await persist(sql, identity, fresh, succeeded);
    } catch (err) {
        // A persistence failure must not turn a good chain read into an error
        // for the player. Report the read; the row simply did not advance.
        console.warn('[heartbound/status] snapshot persist failed:', err.message);
    }

    const resolution = skr.resolveServedState(fresh, stored, nowSeconds);
    const served = resolution.served;

    await logApiEvent(sql, identity, succeeded ? 'skr_verification_success' : 'skr_verification_failed', {
        ref,
        status: fresh.verificationStatus,
        servedStatus: served.verificationStatus,
        errorCode: fresh.errorCode,
        rpcLatencyMs: fresh.rpcLatencyMs,
        fromCache: resolution.fromCache,
        graceExpired: resolution.graceExpired,
        ageSeconds: resolution.ageSeconds,
        // ⛔ NO WALLET ADDRESS AND NO EXACT AMOUNT IN GENERAL ANALYTICS
        //    (spec :1027: "Do not put unnecessary wallet addresses into general
        //    analytics"). logApiEvent already keys the row by identity; a second
        //    copy inside the properties blob is the leak. The BUCKET is what the
        //    balance questions at spec :1041-1052 actually need.
        stakeBucket: bucketOf(served.activeStakedRaw),
    });

    const verifiedAtSeconds = resolution.fromCache
        ? (stored ? stored.verifiedAtSeconds : null)
        : (succeeded ? nowSeconds : (stored ? stored.verifiedAtSeconds : null));

    return res.status(200).json({
        ok: true,
        success: true,
        // The authoritative-clock handshake (api/game/save.js:733-755). HEART-010's
        // "clock manipulation has no effect" rides on this being present.
        serverNowMs: Date.now(),
        mode: auth.mode,
        snapshot: toWire(served, {
            verifiedAtMs: verifiedAtSeconds ? verifiedAtSeconds * 1000 : null,
            ageSeconds: resolution.ageSeconds,
            fromCache: resolution.fromCache,
            refreshThrottled: false,
            graceExpired: resolution.graceExpired,
            graceWindowSeconds: skr.staleGraceSeconds(),
        }),
        ref,
    });
};

/**
 * Coarse magnitude bucket for analytics. Never the exact amount: HEART-011's
 * questions are distributional ("which tiers contain most users", "are whales
 * gaining disproportionate advantage") and an exact staked balance in an
 * analytics table is a financial detail about a named player that no question
 * on that list needs.
 */
function bucketOf(raw) {
    if (raw === null || raw === undefined) return 'unknown';
    let v;
    try { v = BigInt(raw); } catch (_) { return 'unknown'; }
    const skrWhole = v / skr.SKR_BASE_UNITS;
    if (skrWhole <= 0n) return '0';
    if (skrWhole < 100n) return '1-99';
    if (skrWhole < 1000n) return '100-999';
    if (skrWhole < 10000n) return '1k-9k';
    if (skrWhole < 100000n) return '10k-99k';
    if (skrWhole < 1000000n) return '100k-999k';
    return '1m+';
}
