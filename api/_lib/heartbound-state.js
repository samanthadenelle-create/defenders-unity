'use strict';

// =============================================================================
// heartbound-state.js — the ONE data-access seam for the heartbound_state table.
// WO-1675 (HEART-002). Pure module: it takes the `sql` tagged template as an
// argument and opens no connection of its own, exactly as api/_lib/patronage.js
// does (`readLifetimePatronage(sql, wallet)`, patronage.js:69).
// -----------------------------------------------------------------------------
// ⛔ WHY THIS IS A TABLE AND NOT A SAVE FIELD. The save blob is CLIENT-AUTHORED —
//    api/game/save.js:70-72 calls its own guards "anti-grief / anti-corruption
//    ceilings, NOT a server-authoritative economy", and :410-421 records that the
//    simulated systems reach the backend only inside the opaque blob, "a RECORD,
//    NOT A CONTROL". A pulse-payment ledger stored there could be rewritten by a
//    replayed blob. So Heartbound is backend-owned, and
//    `SaveSchema.CurrentVersion` DOES NOT BUMP for this feature. A bump would be
//    the defect, not the deliverable (WO-1675 §0b).
//
// ⛔ THE PLAYER ID IS THE WALLET. api/schema.sql:60 declares
//    `player_id TEXT PRIMARY KEY -- BoundWallet address`, and
//    api/_lib/wallet-auth.js:27-29 records the standing ruling that the wallet is
//    the sole identity on the Seeker artifact. RULED 2026-09-10: ONE WALLET = ONE
//    REALM, no re-binding, NO LINKAGE TABLE. Therefore:
//      • the spec's separate `walletAddress` field is DELIBERATELY ABSENT — it
//        would be a second copy of the primary key, which is the duplicated-state
//        failure CLAUDE.md §2/§5/§8/§16 each carry a scar from;
//      • the spec's entire "Wallet Change" section (HEART-002 spec :235-247) is
//        DROPPED, not implemented. A different wallet is a different player with a
//        different row. There is no migration path and there is deliberately no
//        function here that could move a row between two player ids.
//
// ⛔ FAILING VERIFICATION NEVER OVERWRITES A VERIFIED FIELD (Q2 ruling, 2026-09-10:
//    "fail to last-known verified state", never to zero). That is why there are TWO
//    write paths and they touch DISJOINT column sets:
//      recordVerifiedStake       → the verified columns + status
//      recordVerificationFailure → the attempt/staleness columns + status ONLY
//    A single "write everything you know" upsert is how a transient RPC error
//    becomes a zeroed stake and a broken streak.
//
// ⚠ THIS MODULE OWNS NO MATHEMATICS. The resonance curve, the tier ladder and the
//   pulse arithmetic belong to WO-1676/1677/1679. `readHeartboundStatus` takes an
//   INJECTED `resolveTier` so it can answer `nextTierAt` without holding a copy of
//   the ladder — the client must never derive it (Q-CONFIG sub-question), and
//   neither must this file.
//
// ⚠ AND IT OWNS NO SECOND BIGINT CONTRACT EITHER. `toBigInt` is IMPORTED from
//   api/_lib/heartbound-resonance.js (WO-1676, landed at `cde1c1f63`) — it is the
//   ONE rule for what a chain-sourced u128 may arrive as, and this file adds only
//   the two constraints that belong to the COLUMN rather than to the arithmetic:
//   non-negative, and inside u128. Two coercion rules over one currency is the
//   duplicated-state failure CLAUDE.md §2/§5/§8/§16 each carry a scar from.
//   (History, recorded because it nearly produced exactly that: this module was
//   authored at `c96030b5c`, where `ls api/_lib | grep -i heart` matched nothing and
//   `git log --all` on that path returned no commits. The resonance module landed
//   one commit later; the duplicate helpers written in the meantime are deleted.)
// =============================================================================

/** The six Heartbound statuses, verbatim from HEART-002 spec :213-220. */
const HEARTBOUND_STATUSES = Object.freeze([
    'DORMANT',
    'ACTIVE',
    'STALE',
    'UNSTAKING',
    'DISCONNECTED',
    'SUSPENDED',
]);

// ⛔ THE COERCION RULE IS IMPORTED, NOT RE-STATED. `toBigInt` is heartbound-resonance's
//    boundary layer (`heartbound-resonance.js:95`): BigInt passes; a decimal string
//    passes; a Number passes ONLY if it is a safe integer, and an unsafe or fractional
//    one is REFUSED rather than silently rounded — "a u128 that arrived as a lossy
//    double is not a number this module can honour". That is the whole precision
//    argument, written once, in the module that also does the arithmetic.
const { toBigInt } = require('./heartbound-resonance');

/**
 * u128 ceiling — the on-chain width of a raw SKR amount, and the reason the columns
 * are NUMERIC(39,0) rather than BIGINT (u128 max ≈ 3.4e38; BIGINT tops out ≈ 9.2e18).
 *
 * ⚠ KEPT HERE DELIBERATELY, and it is the only piece not imported: resonance has no
 *   equivalent, because a CEILING is a property of the column this row is stored in,
 *   not of the curve. Same for the non-negative check below — u128 is unsigned, so a
 *   negative BigInt is a caller error that `toBigInt` has no reason to catch (it takes
 *   chain reads, which cannot be negative) but a NUMERIC column would happily store.
 */
const MAX_RAW_AMOUNT = (1n << 128n) - 1n;

/**
 * The storage half of the contract: coerce with the SHARED rule, then apply the two
 * column constraints, then narrow to the canonical digit string that goes on the wire
 * and into NUMERIC(39,0). Leading zeros are normalised by the BigInt round-trip.
 */
function toRawAmountText(value, label = 'raw amount') {
    // Resonance's own error already says exactly what was wrong and why (unsafe
    // double, non-integer, wrong type), so it is left to propagate unwrapped —
    // re-phrasing it here would copy the message as well as the rule.
    const parsed = toBigInt(value, label);
    if (parsed < 0n) throw new RangeError(`${label} must not be negative`);
    if (parsed > MAX_RAW_AMOUNT) throw new RangeError(`${label} exceeds u128`);
    return parsed.toString();
}

/** Read a NUMERIC(39,0) column back as a bigint. Postgres hands it over as text. */
function rawAmountFromRow(value, label = 'raw amount') {
    if (value == null) return 0n;
    return BigInt(toRawAmountText(String(value), label));
}

/** Non-negative 32-bit integer counter/tier guard. INTEGER columns, so the ceiling is real. */
function toCount(value, label) {
    if (typeof value === 'bigint') value = Number(value);
    if (!Number.isSafeInteger(value) || value < 0 || value > 2147483647)
        throw new RangeError(`${label} must be an integer in [0, 2147483647]: ${value}`);
    return value;
}

function requirePlayerId(playerId) {
    if (typeof playerId !== 'string' || playerId.trim() === '')
        throw new TypeError('playerId (the wallet) is required');
    return playerId;
}

function requireSql(sql) {
    if (typeof sql !== 'function') throw new TypeError('sql tagged-template function is required');
    return sql;
}

function requireStatus(status) {
    if (!HEARTBOUND_STATUSES.includes(status))
        throw new RangeError(`unknown Heartbound status: ${status}`);
    return status;
}

function msOrNull(value) {
    if (value == null) return null;
    const ms = value instanceof Date ? value.getTime() : Date.parse(String(value));
    return Number.isFinite(ms) ? ms : null;
}

/**
 * The LAST-KNOWN VERIFIED SNAPSHOT (Q2 ruling: fail to last-known, never to zero).
 *
 * Every field here was written by a SUCCESSFUL verification and is never touched by
 * a failure path, so it stays truthful across an RPC outage. The grace-window inputs
 * ride alongside it: `lastVerifiedAtMs` is the clock the window is measured from,
 * `staleSinceMs` is when the first consecutive failure was seen, and
 * `lastVerificationCode` is why.
 *
 * ⚠ THE WINDOW LENGTH IS NOT HARDCODED HERE. It is a backend-only knob (Q-CONFIG
 *   ruling: Heartbound knobs live as Command Center SERVER-ONLY rows). Pass
 *   `graceWindowMs` and this returns `withinGraceWindow`; omit it and that field is
 *   `null`, which is the honest answer — a window this module was not told the
 *   length of has not expired and has not held.
 */
function toSnapshot(row, { nowMs = Date.now(), graceWindowMs = null } = {}) {
    if (!row) return null;
    const lastVerifiedAtMs = msOrNull(row.last_verified_at_utc);
    const graceExpiresAtMs =
        (graceWindowMs != null && lastVerifiedAtMs != null) ? lastVerifiedAtMs + graceWindowMs : null;
    return Object.freeze({
        playerId: row.player_id,
        status: row.status,
        activatedAtMs: msOrNull(row.activated_at_utc),
        lastVerifiedAtMs,
        lastActualStake: rawAmountFromRow(row.last_actual_stake, 'last_actual_stake').toString(),
        effectiveResonatingStake:
            rawAmountFromRow(row.effective_resonating_stake, 'effective_resonating_stake').toString(),
        lastVerificationAttemptAtMs: msOrNull(row.last_verification_attempt_at_utc),
        lastVerificationCode: row.last_verification_code == null ? null : String(row.last_verification_code),
        staleSinceMs: msOrNull(row.stale_since_utc),
        graceWindowMs: graceWindowMs == null ? null : graceWindowMs,
        graceExpiresAtMs,
        withinGraceWindow: graceExpiresAtMs == null ? null : nowMs <= graceExpiresAtMs,
        continuousPulseCount: Number(row.continuous_pulse_count || 0),
        totalLifetimePulses: Number(row.total_lifetime_pulses || 0),
        lastGlobalPulseId: row.last_global_pulse_id == null ? null : String(row.last_global_pulse_id),
        lastPlayerPulseId: row.last_player_pulse_id == null ? null : String(row.last_player_pulse_id),
        // TEXT on the wire, deliberately: WO-1676 owns the scale of this number and
        // a JS float would decide it here by accident.
        resonanceScore: row.resonance_score == null ? '0' : String(row.resonance_score),
        resonanceTier: Number(row.resonance_tier || 0),
        highestLifetimeTier: Number(row.highest_lifetime_tier || 0),
        currentTreeResonanceStage: Number(row.current_tree_resonance_stage || 0),
        pendingEchoEvents: Object.freeze(Array.isArray(row.pending_echo_events) ? [...row.pending_echo_events] : []),
        lastEchoEventId: row.last_echo_event_id == null ? null : String(row.last_echo_event_id),
        version: Number(row.version || 0),
    });
}

/** Read one player's row, or null. Never throws on absence — DORMANT is "no row". */
async function readHeartboundState(sql, playerId, options = {}) {
    requireSql(sql);
    requirePlayerId(playerId);
    const rows = await sql`
        SELECT player_id, status, activated_at_utc, last_verified_at_utc,
               last_actual_stake::text            AS last_actual_stake,
               effective_resonating_stake::text   AS effective_resonating_stake,
               last_verification_attempt_at_utc, last_verification_code, stale_since_utc,
               continuous_pulse_count, total_lifetime_pulses,
               last_global_pulse_id, last_player_pulse_id,
               resonance_score::text              AS resonance_score,
               resonance_tier, highest_lifetime_tier, current_tree_resonance_stage,
               pending_echo_events, last_echo_event_id, version
        FROM heartbound_state
        WHERE player_id = ${playerId}`;
    return toSnapshot(rows && rows[0] ? rows[0] : null, options);
}

/**
 * ACTIVATION (spec :222-233). Idempotent by the PRIMARY KEY: a second call for the
 * same wallet returns the ORIGINAL row and does NOT re-stamp activation.
 *
 * ⛔ `activated_at_utc` IS SET ONCE, FOR EVER. `DO UPDATE SET activated_at_utc =
 *    heartbound_state.activated_at_utc` re-asserts the STORED value, so a
 *    reconnecting wallet cannot move its own activation floor. That floor is what
 *    spec :242 — "Do NOT retroactively award historical Elarion pulses" — is
 *    enforced with: every pulse query in WO-1677 filters on it. If it could move
 *    forward the player loses pulses; if it could move BACKWARD the player could
 *    claim the entire history of the chain, which is the reason this is written as
 *    a self-assignment rather than left out of the UPDATE list (leaving it out is
 *    correct too, but says nothing to the next reader).
 */
async function activateHeartbound(sql, playerId, { status = 'ACTIVE' } = {}) {
    requireSql(sql);
    requirePlayerId(playerId);
    requireStatus(status);
    const rows = await sql`
        INSERT INTO heartbound_state (player_id, status)
        VALUES (${playerId}, ${status})
        ON CONFLICT (player_id) DO UPDATE
            SET activated_at_utc = heartbound_state.activated_at_utc
        RETURNING player_id, status, activated_at_utc, last_verified_at_utc,
                  last_actual_stake::text            AS last_actual_stake,
                  effective_resonating_stake::text   AS effective_resonating_stake,
                  last_verification_attempt_at_utc, last_verification_code, stale_since_utc,
                  continuous_pulse_count, total_lifetime_pulses,
                  last_global_pulse_id, last_player_pulse_id,
                  resonance_score::text              AS resonance_score,
                  resonance_tier, highest_lifetime_tier, current_tree_resonance_stage,
                  pending_echo_events, last_echo_event_id, version`;
    return toSnapshot(rows && rows[0] ? rows[0] : null);
}

/**
 * A SUCCESSFUL verification. This is the ONLY path that writes the verified
 * columns, and it clears the staleness columns because the outage is over.
 *
 * ⛔ `highest_lifetime_tier` USES GREATEST AND CAN THEREFORE NEVER FALL — the
 *    monotonic half of acceptance criterion 3. `resonance_tier` is written flat and
 *    MAY fall, which is the point: the current bond can weaken, the lifetime high
 *    water mark cannot. Doing this in SQL rather than in JS means a caller that
 *    reaches this statement without reading first still cannot lower it — the same
 *    two-defence shape `GREATEST(player_data.reset_epoch, EXCLUDED.reset_epoch)`
 *    uses (migration 0023).
 *
 * ⛔ `version` IS THE ROW'S OWN GENERATION, NOT SaveSchema.CurrentVersion. It is
 *    incremented server-side on every accepted write so a later optimistic-
 *    concurrency check has something to compare. Nothing about the save schema
 *    changes for this feature.
 */
async function recordVerifiedStake(sql, playerId, {
    lastActualStake,
    effectiveResonatingStake,
    resonanceScore = '0',
    resonanceTier = 0,
    currentTreeResonanceStage = 0,
    status = 'ACTIVE',
    verifiedAtUtc = null,
} = {}) {
    requireSql(sql);
    requirePlayerId(playerId);
    requireStatus(status);
    const actual = toRawAmountText(lastActualStake, 'lastActualStake');
    const effective = toRawAmountText(effectiveResonatingStake, 'effectiveResonatingStake');
    const tier = toCount(resonanceTier, 'resonanceTier');
    const stage = toCount(currentTreeResonanceStage, 'currentTreeResonanceStage');
    const score = String(resonanceScore);
    if (!/^\d+(\.\d+)?$/.test(score))
        throw new TypeError(`resonanceScore must be a non-negative decimal string: ${score}`);

    const rows = await sql`
        INSERT INTO heartbound_state (
            player_id, status, last_verified_at_utc,
            last_actual_stake, effective_resonating_stake,
            resonance_score, resonance_tier, highest_lifetime_tier,
            current_tree_resonance_stage, updated_at)
        VALUES (
            ${playerId}, ${status}, COALESCE(${verifiedAtUtc}::timestamptz, NOW()),
            ${actual}::numeric, ${effective}::numeric,
            ${score}::numeric, ${tier}, ${tier},
            ${stage}, NOW())
        ON CONFLICT (player_id) DO UPDATE SET
            status                       = EXCLUDED.status,
            last_verified_at_utc         = EXCLUDED.last_verified_at_utc,
            last_actual_stake            = EXCLUDED.last_actual_stake,
            effective_resonating_stake   = EXCLUDED.effective_resonating_stake,
            resonance_score              = EXCLUDED.resonance_score,
            resonance_tier               = EXCLUDED.resonance_tier,
            highest_lifetime_tier        = GREATEST(heartbound_state.highest_lifetime_tier,
                                                    EXCLUDED.highest_lifetime_tier),
            current_tree_resonance_stage = EXCLUDED.current_tree_resonance_stage,
            last_verification_code       = NULL,
            stale_since_utc              = NULL,
            activated_at_utc             = heartbound_state.activated_at_utc,
            version                      = heartbound_state.version + 1,
            updated_at                   = NOW()
        RETURNING player_id, status, activated_at_utc, last_verified_at_utc,
                  last_actual_stake::text            AS last_actual_stake,
                  effective_resonating_stake::text   AS effective_resonating_stake,
                  last_verification_attempt_at_utc, last_verification_code, stale_since_utc,
                  continuous_pulse_count, total_lifetime_pulses,
                  last_global_pulse_id, last_player_pulse_id,
                  resonance_score::text              AS resonance_score,
                  resonance_tier, highest_lifetime_tier, current_tree_resonance_stage,
                  pending_echo_events, last_echo_event_id, version`;
    return toSnapshot(rows && rows[0] ? rows[0] : null);
}

/**
 * A FAILED verification (RPC_UNAVAILABLE and friends).
 *
 * ⛔ THIS STATEMENT NAMES NOT ONE VERIFIED COLUMN. It cannot zero a stake, cannot
 *    move a tier and cannot touch the activation floor, because those column names
 *    do not appear in it — which is a structural guarantee rather than a promise in
 *    a comment. Today's client fails CLOSED (stake 0 on an RPC error,
 *    NativeSkrStakeQuery.cs:87-94) and that is correct for a perk; it is WRONG for
 *    a streak, so the 2026-09-10 ruling for Heartbound is FAIL TO LAST-KNOWN
 *    VERIFIED STATE.
 *
 * ⛔ IT ALSO DOES NOT INSERT. A player who has never verified has no row and must
 *    not get one from a failure — a row created by an outage would claim an
 *    activation timestamp on no evidence, which is migration 0022's `DEFAULT 10`
 *    mistake in a new costume. Zero rows updated is the honest outcome and the
 *    caller is told so.
 *
 * `stale_since_utc` is stamped only on the FIRST consecutive failure (COALESCE
 * keeps the existing value), so the grace window is measured from when the outage
 * began, not from the latest retry — otherwise a client that retries forever never
 * leaves the window.
 */
async function recordVerificationFailure(sql, playerId, { code, status = 'STALE', attemptedAtUtc = null } = {}) {
    requireSql(sql);
    requirePlayerId(playerId);
    requireStatus(status);
    if (typeof code !== 'string' || code.trim() === '')
        throw new TypeError('a verification failure code is required');

    const rows = await sql`
        UPDATE heartbound_state SET
            status                           = ${status},
            last_verification_attempt_at_utc = COALESCE(${attemptedAtUtc}::timestamptz, NOW()),
            last_verification_code           = ${code},
            stale_since_utc                  = COALESCE(heartbound_state.stale_since_utc,
                                                        ${attemptedAtUtc}::timestamptz, NOW()),
            updated_at                       = NOW()
        WHERE player_id = ${playerId}
        RETURNING player_id, status, activated_at_utc, last_verified_at_utc,
                  last_actual_stake::text            AS last_actual_stake,
                  effective_resonating_stake::text   AS effective_resonating_stake,
                  last_verification_attempt_at_utc, last_verification_code, stale_since_utc,
                  continuous_pulse_count, total_lifetime_pulses,
                  last_global_pulse_id, last_player_pulse_id,
                  resonance_score::text              AS resonance_score,
                  resonance_tier, highest_lifetime_tier, current_tree_resonance_stage,
                  pending_echo_events, last_echo_event_id, version`;
    return toSnapshot(rows && rows[0] ? rows[0] : null);
}

/**
 * THE STATUS READ SHAPE — what GET /api/heartbound/status returns.
 *
 * ⛔ `nextTierAt` IS SERVED, NEVER DERIVED ON THE CLIENT. The tier thresholds are
 *    backend-only configuration (Q-CONFIG ruling: Command Center server-only rows),
 *    so a client that computed "how far to the next tier" would be holding a second
 *    copy of the ladder — and a copy of live state in a second place is the exact
 *    failure CLAUDE.md §2/§5/§8/§16 each record a scar from. The panel renders this
 *    number; it does not calculate it.
 *
 * ⚠ AND THIS MODULE DOES NOT HOLD THE LADDER EITHER. `resolveTier` is INJECTED by
 *   the caller (WO-1676/1679 own it). With no resolver, `nextTierAt` is `null` —
 *   the honest answer for "nobody has told me the thresholds", not a guess.
 *
 * `serverNowMs` rides along because the server owns the clock (save.js:753-755;
 * the client's rule is TimeSource.NowUnixMs(), never DateTime.UtcNow).
 */
async function readHeartboundStatus(sql, playerId, {
    resolveTier = null,
    nowMs = Date.now(),
    graceWindowMs = null,
} = {}) {
    const snapshot = await readHeartboundState(sql, playerId, { nowMs, graceWindowMs });
    if (!snapshot) {
        return Object.freeze({
            playerId: requirePlayerId(playerId),
            status: 'DORMANT',
            exists: false,
            nextTierAt: null,
            serverNowMs: nowMs,
            state: null,
        });
    }
    let nextTierAt = null;
    if (typeof resolveTier === 'function') {
        const resolved = resolveTier({
            effectiveResonatingStake: snapshot.effectiveResonatingStake,
            resonanceScore: snapshot.resonanceScore,
            resonanceTier: snapshot.resonanceTier,
        });
        // A resolver that cannot answer (top tier, or thresholds not loaded) says so
        // with null. It is never inferred from the tier number.
        nextTierAt = resolved && resolved.nextTierAt != null ? String(resolved.nextTierAt) : null;
    }
    return Object.freeze({
        playerId: snapshot.playerId,
        status: snapshot.status,
        exists: true,
        nextTierAt,
        serverNowMs: nowMs,
        state: snapshot,
    });
}

module.exports = {
    HEARTBOUND_STATUSES,
    MAX_RAW_AMOUNT,
    toRawAmountText,
    rawAmountFromRow,
    toSnapshot,
    readHeartboundState,
    readHeartboundStatus,
    activateHeartbound,
    recordVerifiedStake,
    recordVerificationFailure,
};
